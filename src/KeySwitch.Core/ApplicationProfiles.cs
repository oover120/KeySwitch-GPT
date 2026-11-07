namespace KeySwitch.Core;

public static class ApplicationProfiles
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "vivaldi.exe",
        "opera.exe", "browser.exe", "chromium.exe", "waterfox.exe", "librewolf.exe"
    };

    public static bool IsBrowser(string path) => Browsers.Contains(ExecutableName(path));

    public static bool IsGame(string path, bool exclusiveFullscreen, IEnumerable<string>? games = null)
    {
        if (IsBrowser(path)) return false;
        return exclusiveFullscreen || games?.Contains(ExecutableName(path), StringComparer.OrdinalIgnoreCase) == true
            || path.Split(['\\', '/']).Any(part => part.Equals("Games", StringComparison.OrdinalIgnoreCase));
    }

    public static bool NeedsInputPause(string path, string rootClass) => IsBrowser(path)
        || rootClass.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal);

    public static string ExecutableName(string path) => path[(Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/')) + 1)..];
}
