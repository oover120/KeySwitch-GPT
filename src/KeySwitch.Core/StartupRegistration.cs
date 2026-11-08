namespace KeySwitch.Core;

public sealed record StartupRegistration(bool Registered, string? Executable, bool FileExists, bool MatchesCurrent)
{
    public bool Ready => Registered && FileExists && MatchesCurrent;

    public static StartupRegistration Inspect(string? command, string currentExecutable, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(command)) return new(false, null, false, false);
        var value = command.Trim();
        string path;
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            if (end < 0) return new(true, null, false, false);
            path = value[1..end];
        }
        else
        {
            var end = value.IndexOfAny([' ', '\t']);
            path = end < 0 ? value : value[..end];
        }
        return new(true, path, fileExists(path), path.Equals(currentExecutable, StringComparison.OrdinalIgnoreCase));
    }

    // Re-register even when the checkbox did not change: the app may have moved.
    public static string CommandFor(string executable) => "\"" + executable + "\"";
}
