using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows.Automation;
using KeySwitch.Core;

namespace KeySwitch.Hooks;

internal enum PasswordStatus { Pending, No, Yes, Unknown, Error, Timeout }

internal static class PasswordProbe
{
    internal static PasswordStatus Classify(object? value) => value switch
    {
        true => PasswordStatus.Yes,
        false => PasswordStatus.No,
        _ => PasswordStatus.Unknown
    };

    internal static bool IsPassword(PasswordStatus status) => status == PasswordStatus.Yes;

    internal static PasswordStatus Read()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            return Classify(element?.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, ignoreDefaultValue: true));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return PasswordStatus.Error;
        }
    }
}

internal readonly record struct FocusIdentity(nint Window, nint Control, uint ProcessId)
{
    internal static FocusIdentity Capture()
    {
        var window = Native.GetForegroundWindow();
        var focus = Native.Focus(window);
        Native.GetWindowThreadProcessId(focus == 0 ? window : focus, out var processId);
        return new(window, focus, processId);
    }
}

internal readonly record struct FocusNotification(uint EventId, nint Window, int ObjectId, int ChildId);

internal sealed class FocusTracker
{
    private FocusIdentity identity;
    private FocusNotification? accessibleFocus;

    internal bool Observe(FocusIdentity next, FocusNotification? notification = null, bool force = false)
    {
        if (notification is { EventId: not (3 or 0x8005) }) return false;
        if (identity != next)
        {
            identity = next;
            accessibleFocus = notification is { EventId: 0x8005 } ? notification : null;
            return true;
        }

        if (notification is { EventId: 0x8005 } && notification != accessibleFocus)
        {
            accessibleFocus = notification;
            return true;
        }
        return force;
    }
}

public sealed record WindowContext(nint Window, nint Focus, long Generation, string ProcessPath,
    bool IsFullScreen, bool IsElevated, bool Ready, string FocusClass = "", string RootClass = "",
    string? ContextError = null, int? ContextWin32Error = null)
{
    internal FocusIdentity Identity { get; init; }
    internal PasswordStatus Password { get; init; } = PasswordStatus.Pending;
    public bool IsPassword => PasswordProbe.IsPassword(Password);
    public string PasswordCheck => Password.ToString().ToLowerInvariant();
}

public sealed class WindowMonitor : IDisposable
{
    private readonly Channel<(WindowContext Context, bool AfterClick)> requests = Channel.CreateBounded<(WindowContext, bool)>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    private readonly object snapshotGate = new();
    private readonly FocusTracker tracker = new();
    private readonly Native.WinEventProc callback;
    private readonly List<nint> hooks = [];
    private volatile WindowContext current = new(0, 0, 0, "", false, false, false);
    private int uiaInFlight;

    public WindowContext Current => current;
    public event Action<string>? Changed;
    public event Action<WindowContext>? Observed;

    public WindowMonitor()
    {
        callback = OnEvent;
        // No title/name-change hook. Only foreground and actual accessibility focus events.
        hooks.Add(Native.SetWinEventHook(3, 3, 0, callback, 0, 0, 0));
        hooks.Add(Native.SetWinEventHook(0x8005, 0x8005, 0, callback, 0, 0, 0));
        if (hooks.Any(h => h == 0))
        {
            Dispose();
            throw new InvalidOperationException("WinEvent hook failed");
        }
        _ = Task.Run(WorkAsync);
        RefreshFocus();
    }

    private void OnEvent(nint hook, uint eventId, nint hwnd, int obj, int child, uint thread, uint time)
    {
        var identity = FocusIdentity.Capture();
        if (eventId == 0x8005 && (obj is not (0 or -4) ||
            (hwnd != identity.Window && hwnd != identity.Control && Native.GetAncestor(hwnd, 2) != identity.Window))) return;

        var notification = new FocusNotification(eventId, hwnd, obj, child);
        if (tracker.Observe(identity, notification)) BeginFocus(identity, afterClick: false);
    }

    // Called on the hook thread. Poll native focus on keys to cover a delayed WinEvent.
    internal WindowContext RefreshFocus(bool afterClick = false, bool force = false)
    {
        var identity = FocusIdentity.Capture();
        if (tracker.Observe(identity, force: afterClick || force)) BeginFocus(identity, afterClick);
        return current;
    }

    private void BeginFocus(FocusIdentity identity, bool afterClick)
    {
        WindowContext next;
        string reason;
        lock (snapshotGate)
        {
            var previous = current;
            reason = afterClick ? "mouse-click"
                : previous.Window != identity.Window || previous.Identity.ProcessId != identity.ProcessId ? "window-changed"
                : "focus-changed";
            next = identity == previous.Identity && previous.Ready
                ? previous with { Generation = previous.Generation + 1, Password = PasswordStatus.Pending }
                : new WindowContext(identity.Window, identity.Control, previous.Generation + 1, "", false, false, false) { Identity = identity };
            current = next;
        }
        requests.Writer.TryWrite((next, afterClick));
        Changed?.Invoke(reason);
    }

    public bool Matches(WindowContext context) => context.Ready && current.Generation == context.Generation
        && context.Identity == FocusIdentity.Capture();

    internal bool CanEdit(WindowContext context)
    {
        var snapshot = current;
        return snapshot.Generation == context.Generation && snapshot.Ready && !snapshot.IsPassword
            && snapshot.Password != PasswordStatus.Pending && Matches(context);
    }

    private bool Publish(WindowContext snapshot, bool observed)
    {
        lock (snapshotGate)
        {
            if (snapshot.Generation != current.Generation) return false;
            current = snapshot;
        }
        if (observed) Observed?.Invoke(snapshot);
        return true;
    }

    private async Task WorkAsync()
    {
        await foreach (var (context, afterClick) in requests.Reader.ReadAllAsync())
        {
            if (context.Generation != current.Generation || context.Identity.ProcessId is 0
                || context.Identity.ProcessId == Environment.ProcessId) continue;

            var path = Native.ProcessImage(context.Identity.ProcessId, out var error);
            var metadata = context with
            {
                ProcessPath = path,
                FocusClass = Native.ClassName(context.Focus == 0 ? context.Window : context.Focus),
                RootClass = Native.ClassName(context.Window),
                IsElevated = Native.IsElevated(context.Identity.ProcessId),
                IsFullScreen = IsExclusiveFullscreen(context.Window, path),
                Ready = context.Focus != 0 && path.Length > 0,
                ContextError = error == 0 ? null : "process-image",
                ContextWin32Error = error == 0 ? null : error
            };
            if (!Publish(metadata, observed: !metadata.Ready) || !metadata.Ready) continue;

            // A mouse-down hook runs before the control receives the click.
            if (afterClick) await Task.Delay(10);
            if (context.Generation != current.Generation) continue;
            if (Interlocked.CompareExchange(ref uiaInFlight, 1, 0) != 0)
            {
                Publish(metadata with { Password = PasswordStatus.Timeout }, observed: true);
                continue;
            }

            var probe = Task.Run(() =>
            {
                try { return PasswordProbe.Read(); }
                finally { Interlocked.Exchange(ref uiaInFlight, 0); }
            });
            try
            {
                var result = await probe.WaitAsync(TimeSpan.FromMilliseconds(50));
                Publish(metadata with { Password = result }, observed: true);
            }
            catch (TimeoutException)
            {
                Publish(metadata with { Password = PasswordStatus.Timeout }, observed: true);
                _ = ObserveLatePasswordAsync(probe, metadata);
            }
        }
    }

    private async Task ObserveLatePasswordAsync(Task<PasswordStatus> probe, WindowContext metadata)
    {
        // A late explicit positive still protects the same field. Never reuse it after a focus change.
        if (await probe == PasswordStatus.Yes && metadata.Identity == FocusIdentity.Capture())
            Publish(metadata with { Password = PasswordStatus.Yes }, observed: true);
    }

    private static bool IsExclusiveFullscreen(nint window, string processPath)
    {
        if (ApplicationProfiles.IsBrowser(processPath) || Native.IsZoomed(window)
            || Native.SHQueryUserNotificationState(out var state) != 0 || state != 3) return false;

        // QUNS_RUNNING_D3D_FULL_SCREEN is system-wide. Check that the active window covers its monitor too.
        var monitor = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
        return Native.GetWindowRect(window, out var r) && Native.GetMonitorInfo(Native.MonitorFromWindow(window, 2), ref monitor)
            && r.Left <= monitor.Monitor.Left && r.Top <= monitor.Monitor.Top && r.Right >= monitor.Monitor.Right && r.Bottom >= monitor.Monitor.Bottom;
    }

    public void Dispose()
    {
        foreach (var hook in hooks.Where(h => h != 0)) Native.UnhookWinEvent(hook);
        requests.Writer.TryComplete();
    }
}
