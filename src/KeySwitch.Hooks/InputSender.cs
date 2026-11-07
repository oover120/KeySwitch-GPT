using System.Diagnostics;
using System.Runtime.InteropServices;
using KeySwitch.Core;
namespace KeySwitch.Hooks;

public static class InputSender
{
    public static string? CurrentLanguage()
    {
        var window = Native.GetForegroundWindow();
        var focus = Native.Focus(window);
        return Layouts.FromLanguageId((int)LayoutHandle(focus == 0 ? window : focus))?.Id;
    }

    private static nint LayoutHandle(nint window)
    {
        var thread = Native.GetWindowThreadProcessId(window, out _);
        return thread == 0 ? 0 : Native.GetKeyboardLayout(thread);
    }

    public static bool SwitchLayout(WindowContext context, KeyboardLayout layout) => RequestLayout(context, layout).Confirmed;

    public static LayoutReport RequestLayout(WindowContext context, KeyboardLayout layout)
    {
        bool HasFocus() => Native.GetForegroundWindow() == context.Window && Native.Focus(context.Window) == context.Focus;
        if (context.Focus == 0 || !HasFocus()) return new(false, 0, false, false, 0);
        if (Layouts.FromLanguageId((int)LayoutHandle(context.Focus))?.Id == layout.Id)
            return new(true, 0, false, false, 0);

        var handle = Native.LoadKeyboardLayout(layout.Klid, 0);
        if (handle == 0) return new(false, 0, false, false, Marshal.GetLastPInvokeError());
        var root = Native.GetAncestor(context.Focus, 2);
        if (root == 0) root = context.Window;
        bool postedFocus = false, postedRoot = false;
        var error = 0;
        var attempts = 0;
        while (attempts < 2 && HasFocus())
        {
            attempts++;
            var focusResult = Native.PostMessage(context.Focus, 0x50, 0, handle);
            if (!focusResult) error = Marshal.GetLastPInvokeError();
            var rootResult = root == context.Focus ? focusResult : Native.PostMessage(root, 0x50, 0, handle);
            if (!rootResult) error = Marshal.GetLastPInvokeError();
            postedFocus |= focusResult;
            postedRoot |= rootResult;

            var started = Stopwatch.GetTimestamp();
            do
            {
                if (!HasFocus()) return new(false, attempts, postedFocus, postedRoot, error);
                if (LayoutHandle(context.Focus) == handle) return new(true, attempts, postedFocus, postedRoot, 0);
                Thread.Sleep(2);
            } while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < 30);
        }
        return new(HasFocus() && LayoutHandle(context.Focus) == handle, attempts, postedFocus, postedRoot, error);
    }
    public static InputReport Replace(int erase, string text, string delimiter)
    {
        // Physical modifiers can be down even though the application has not yet received them.
        var modifiers = new ushort[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C }.Where(v => (Native.GetAsyncKeyState(v) & 0x8000) != 0).ToArray();
        return Native.Send(BuildReplacement(erase, text, delimiter, modifiers));
    }

    internal static InputReport ReplaceWithPause(int erase, string text, string delimiter, Func<bool> canContinue)
    {
        // SendInput has no delayed-event facility. The two marked batches retain normal modifier state.
        var first = Replace(erase, "", "");
        Thread.Sleep(10);
        if (!canContinue()) return first with { Interrupted = true };
        try
        {
            var second = Replace(0, text, delimiter);
            return Combine(first, second);
        }
        catch (InputInjectionException ex)
        {
            throw new InputInjectionException(Combine(first, ex.Report));
        }
    }

    private static InputReport Combine(InputReport first, InputReport second) =>
        new(first.Requested + second.Requested, first.Sent + second.Sent, second.Win32Error, 2);
    internal static IReadOnlyCollection<Native.Input> BuildReplacement(int erase, string text, string delimiter, ushort[] modifiers)
    {
        if (erase < 0 || erase > 256) throw new ArgumentOutOfRangeException(nameof(erase));
        var input = new List<Native.Input>();
        foreach (var key in modifiers) input.Add(Native.Key(key, flags: 2));
        for (var i = 0; i < erase; i++) Native.Tap(input, 8);
        foreach (var character in text)
        { input.Add(Native.Key(0, character, 4)); input.Add(Native.Key(0, character, 6)); }
        foreach (var character in delimiter)
        {
            if (character == '\n') Native.Tap(input, 13);
            else if (character == '\t') Native.Tap(input, 9);
            else { input.Add(Native.Key(0, character, 4)); input.Add(Native.Key(0, character, 6)); }
        }
        foreach (var key in modifiers) input.Add(Native.Key(key));
        return input;
    }
    internal static void Replay(IEnumerable<Native.KeyboardEvent> events) => Native.Send(events.Select(e => Native.Key((ushort)e.Vk, (ushort)e.Scan,
        ((e.Flags & 0x80) != 0 ? 2u : 0u) | ((e.Flags & 1) != 0 ? 1u : 0u))).ToArray());
    public static void Chord(ushort key)
    {
        var modifiers = new ushort[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C }.Where(v => (Native.GetAsyncKeyState(v) & 0x8000) != 0).ToArray();
        var inputs = modifiers.Select(v => Native.Key(v, flags: 2)).ToList();
        inputs.Add(Native.Key(0x11)); Native.Tap(inputs, key); inputs.Add(Native.Key(0x11, flags: 2));
        foreach (var modifier in modifiers) inputs.Add(Native.Key(modifier)); Native.Send(inputs);
    }
    public static bool IsCurrentProcessElevated => Native.IsElevated((uint)Environment.ProcessId);
}
