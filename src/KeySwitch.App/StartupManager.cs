using System.Diagnostics;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.Win32;
namespace KeySwitch.App;

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string Executable => Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable");
    private static string TaskName => "KeySwitch-" + WindowsIdentity.GetCurrent().User!.Value;
    public static bool Enabled
    { get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue("KeySwitch") is string; } }
    public static void SetNormal(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue("KeySwitch", "\"" + Executable + "\""); else key.DeleteValue("KeySwitch", false);
    }
    public static async Task SetElevatedAsync(bool enabled)
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KeySwitch-task-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            var sid = WindowsIdentity.GetCurrent().User!.Value;
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var task = new XDocument(new XElement(ns + "Task", new XAttribute("version", "1.2"),
                new XElement(ns + "Triggers", new XElement(ns + "LogonTrigger", new XElement(ns + "Enabled", true), new XElement(ns + "UserId", sid))),
                new XElement(ns + "Principals", new XElement(ns + "Principal", new XAttribute("id", "Author"), new XElement(ns + "UserId", sid), new XElement(ns + "LogonType", "InteractiveToken"), new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings", new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"), new XElement(ns + "DisallowStartIfOnBatteries", false), new XElement(ns + "StopIfGoingOnBatteries", false), new XElement(ns + "ExecutionTimeLimit", "PT0S")),
                new XElement(ns + "Actions", new XAttribute("Context", "Author"), new XElement(ns + "Exec", new XElement(ns + "Command", Executable)))));
            task.Save(file);
            var args = enabled ? $"/Create /TN \"{TaskName}\" /XML \"{file}\" /F" : $"/Delete /TN \"{TaskName}\" /F";
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }) ?? throw new InvalidOperationException("Task scheduler unavailable");
            await process.WaitForExitAsync(); if (process.ExitCode != 0) throw new InvalidOperationException("Планировщик не изменил задачу. Требуется подтверждение UAC.");
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\KeySwitch"))
            { if (enabled) key.SetValue("ElevatedTaskName", TaskName); else key.DeleteValue("ElevatedTaskName", false); }
            if (enabled) SetNormal(false);
        }
        finally { if (System.IO.File.Exists(file)) System.IO.File.Delete(file); }
    }
}
