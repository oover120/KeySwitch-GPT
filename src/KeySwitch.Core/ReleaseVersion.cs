namespace KeySwitch.Core;

public static class ReleaseVersion
{
    public static bool IsNewer(string? tag, Version current)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var value = tag.Trim();
        if (value.StartsWith('v')) value = value[1..];
        if (value.Contains('-') || !Version.TryParse(value, out var version) || version.Build < 0) return false;
        return version > new Version(current.Major, current.Minor, Math.Max(0, current.Build));
    }
}
