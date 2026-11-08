using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Threading;
using KeySwitch.Core;
using KeySwitch.Data;
using KeySwitch.Hooks;
using Forms = System.Windows.Forms;
using Color = System.Drawing.Color;
namespace KeySwitch.App;

internal sealed class TrayApplication : IDisposable
{
    private readonly System.Windows.Application app;
    private readonly SettingsStore store;
    private readonly DiagnosticLog diagnostics;
    private bool debugFromCommandLine;
    private Settings settings;
    private readonly LanguageDetector detector;
    private readonly KeyboardEngine engine;
    private readonly Forms.NotifyIcon tray;
    private readonly Forms.ToolStripMenuItem enabledMenu, startupMenu;
    private readonly DispatcherTimer timer;
    private readonly ClipboardConverter clipboard = new();
    private MediaPlayer? player;
    private SettingsWindow? window;
    private string lastLabel = "";
    private Icon? currentIcon;
    public TrayApplication(System.Windows.Application app, bool smoke, bool debugFromCommandLine)
    {
        this.app = app; store = new SettingsStore(); settings = store.Load();
        this.debugFromCommandLine = debugFromCommandLine;
        diagnostics = new DiagnosticLog(store.DirectoryPath);
        diagnostics.Start();
        if (smoke) settings.Enabled = false;
        detector = new(ModelLoader.Load());
        engine = new(detector, Options());
        engine.Trace += diagnostics.Enqueue;
        tray = new Forms.NotifyIcon { Text = "KeySwitch", Visible = true, Icon = SystemIcons.Application };
        var menu = new Forms.ContextMenuStrip();
        enabledMenu = new Forms.ToolStripMenuItem("Автопереключение", null, (_, _) => Toggle()) { Checked = settings.Enabled };
        menu.Items.Add(enabledMenu);
        menu.Items.Add("Исключения", null, (_, _) => ShowSettings(1));
        menu.Items.Add("Настройки", null, (_, _) => ShowSettings(0));
        startupMenu = new Forms.ToolStripMenuItem("Автозапуск", null, (_, _) =>
        { try { StartupManager.SetNormal(!StartupManager.Enabled); startupMenu!.Checked = StartupManager.Enabled; } catch { Notify("Не удалось изменить автозапуск."); } });
        try { startupMenu.Checked = StartupManager.Enabled; } catch { }
        menu.Items.Add(startupMenu); menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => app.Shutdown()); tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowSettings(0);
        engine.TogglePause += () => app.Dispatcher.BeginInvoke(Toggle);
        engine.Status += message => app.Dispatcher.BeginInvoke(() => Notify(message));
        engine.Learning += (word, from, to, rejected) => app.Dispatcher.BeginInvoke(() =>
        {
            SettingsStore.Learn(settings, word, from, to, rejected, detector.Profiles);
            Save(); engine.Configure(Options(), false);
        });
        engine.Corrected += typo => app.Dispatcher.BeginInvoke(() => Play(typo));
        engine.ConvertSelection += context => app.Dispatcher.BeginInvoke(async () =>
        { try { var error = await clipboard.ConvertAsync(context, engine.Matches); if (error is not null) Notify(error); } finally { engine.CompleteSelection(); } });
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += (_, _) => RefreshIcon(); timer.Start(); RefreshIcon();
        if (store.LoadWarning is not null) Notify(store.LoadWarning);
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnPreferencesChanged;
        // Drop temporary model-loading arrays once, never force collections during typing.
        app.Dispatcher.BeginInvoke(() => { System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce; GC.Collect(2, GCCollectionMode.Forced, true, true); }, DispatcherPriority.ApplicationIdle);
    }
    private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs args) => engine.RefreshFocus();
    private void OnPreferencesChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs args) => app.Dispatcher.BeginInvoke(() => window?.ApplyTheme());
    private EngineOptions Options() => new(settings.Enabled, settings.ConfidenceThreshold, settings.EarlySwitch, settings.SingleShift,
        settings.Typos, settings.FixCapitals, settings.Yoficator, settings.SelectionKey, settings.SelectionModifiers, settings.PauseKey, settings.PauseModifiers,
        settings.Applications.ToArray(), settings.Words.ToArray(), new Dictionary<string, string>(settings.Replacements, StringComparer.OrdinalIgnoreCase), new Dictionary<string, string>(settings.YoWords, StringComparer.OrdinalIgnoreCase), Learned(), settings.Debug || debugFromCommandLine, settings.GameExecutables.ToArray());
    private Dictionary<string, string> Learned()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, counter) in settings.Learning)
        {
            if (counter.Accepted <= counter.Rejected || key.Length < 7 || key[2] != '>' || key[5] != ':') continue;
            result[key[..2] + ":" + key[6..]] = key[3..5];
        }
        return result;
    }
    private void Toggle() { settings.Enabled = !settings.Enabled; enabledMenu.Checked = settings.Enabled; Save(); engine.Configure(Options()); RefreshIcon(); }
    internal void EnableDebug()
    {
        debugFromCommandLine = true;
        engine.Configure(Options(), reset: false);
    }
    private void Save() { try { store.Save(settings); } catch { Notify("Настройки не сохранены. Проверьте доступ к папке KeySwitch."); } }
    internal void ShowSettings(int tab)
    {
        if (window is not null) { window.SelectTab(tab); window.Activate(); return; }
        window = new SettingsWindow(SettingsStore.Clone(settings), detector.Profiles, store.DirectoryPath, async updated =>
        {
            updated.Learning = settings.Learning; // retain learning that arrived while the dialog was open
            store.Save(updated); settings = updated;
            engine.Configure(Options()); enabledMenu.Checked = settings.Enabled;
            startupMenu.Checked = settings.ElevatedStartup || StartupManager.Enabled;
            await Task.CompletedTask;
        });
        window.Closed += (_, _) => window = null; window.SelectTab(tab); window.Show();
    }
    private void RefreshIcon()
    {
        var elevated = engine.CurrentWindow?.IsElevated == true && !InputSender.IsCurrentProcessElevated;
        var language = InputSender.CurrentLanguage()?.ToUpperInvariant() ?? "—";
        var label = !settings.Enabled ? "Ⅱ" : elevated ? "!" : language;
        tray.Text = "KeySwitch · " + (settings.Enabled ? language : "пауза") + (elevated ? " · окно администратора недоступно" : "");
        if (lastLabel == label) return; lastLabel = label;
        using var bitmap = new Bitmap(32, 32); using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(settings.Enabled ? Color.FromArgb(0, 103, 192) : Color.DimGray);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI", label.Length > 1 ? 12 : 17, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(label, font, System.Drawing.Brushes.White, new RectangleF(0, 0, 32, 32), format);
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); var next = (Icon)borrowed.Clone(); tray.Icon = next; currentIcon?.Dispose(); currentIcon = next; }
        finally { DestroyIcon(handle); }
    }
    private void Play(bool typo)
    {
        if (!settings.Sounds) return;
        var file = typo ? settings.TypoSound : settings.SwitchSound;
        if (!System.IO.File.Exists(file) || !file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return;
        try { player ??= new MediaPlayer(); player.Open(new Uri(System.IO.Path.GetFullPath(file))); player.Volume = settings.Volume; player.Play(); } catch { }
    }
    private void Notify(string text) => tray.ShowBalloonTip(4000, "KeySwitch", text, Forms.ToolTipIcon.Info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint handle);
    public void Dispose()
    {
        timer.Stop(); Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch; Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnPreferencesChanged;
        engine.Dispose(); diagnostics.Dispose(); tray.Visible = false; tray.Dispose(); currentIcon?.Dispose(); player?.Close();
    }
}
