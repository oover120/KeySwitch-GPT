namespace KeySwitch.App;
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var suffix = Environment.UserName;
        var settingsEvent = "Local\\KeySwitch-Settings-" + suffix;
        var debugEvent = "Local\\KeySwitch-Debug-" + suffix;
        using var single = new Mutex(true, "Local\\KeySwitch-" + suffix, out var first);
        if (!first)
        {
            if (args.Contains("--settings")) Signal(settingsEvent);
            if (args.Contains("--debug")) Signal(debugEvent);
            return 0;
        }
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        try
        {
            using var tray = new TrayApplication(app, args.Contains("--smoke") || args.Contains("--preview"), args.Contains("--debug"));
            using var showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, settingsEvent);
            using var enableDebug = new EventWaitHandle(false, EventResetMode.AutoReset, debugEvent);
            var registration = ThreadPool.RegisterWaitForSingleObject(showSettings, (_, _) => app.Dispatcher.BeginInvoke(() => tray.ShowSettings(0)), null, Timeout.Infinite, false);
            var debugRegistration = ThreadPool.RegisterWaitForSingleObject(enableDebug, (_, _) => app.Dispatcher.BeginInvoke(tray.EnableDebug), null, Timeout.Infinite, false);
            if (args.Contains("--settings"))
            {
                tray.ShowSettings(0);
            }
            if (args.Contains("--smoke")) { var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) }; t.Tick += (_, _) => app.Shutdown(); t.Start(); }
            try { app.Run(); }
            finally { registration.Unregister(null); debugRegistration.Unregister(null); }
            return 0;
        }
        catch
        {
            if (!args.Contains("--smoke")) System.Windows.MessageBox.Show("KeySwitch не запустился. Проверьте доступ к данным и наличие .NET Desktop Runtime 8 при запуске из исходников.", "KeySwitch");
            return 1;
        }
    }

    private static void Signal(string name)
    {
        try { using var existing = EventWaitHandle.OpenExisting(name); existing.Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
        catch (UnauthorizedAccessException) { }
    }
}
