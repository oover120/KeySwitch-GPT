namespace KeySwitch.Core;

public enum ApplicationMode { Disabled, ManualOnly, Full }
public sealed record ApplicationRule(string Pattern, bool IsFolder, ApplicationMode Mode);
public sealed record WordException(string Word, string Direction);
public static class ExclusionPolicy
{
    public static ApplicationRule? RuleFor(string executablePath, IEnumerable<ApplicationRule> rules)
    {
        var path = executablePath.Replace('/', '\\');
        var name = ApplicationProfiles.ExecutableName(path);
        return rules.FirstOrDefault(r => r.IsFolder
            ? path.StartsWith(r.Pattern.TrimEnd('\\', '/') + "\\", StringComparison.OrdinalIgnoreCase)
            : name.Equals(r.Pattern, StringComparison.OrdinalIgnoreCase));
    }

    public static ApplicationMode Resolve(string executablePath, bool exclusiveFullscreen, IEnumerable<ApplicationRule> rules,
        IEnumerable<string>? games = null) => RuleFor(executablePath, rules)?.Mode
        ?? (ApplicationProfiles.IsGame(executablePath, exclusiveFullscreen, games) ? ApplicationMode.Disabled : ApplicationMode.Full);
    public static bool IsExcepted(string word, string source, string target, IEnumerable<WordException> exceptions) =>
        exceptions.Any(e => e.Word.Equals(word, StringComparison.OrdinalIgnoreCase) && (e.Direction == "*" || e.Direction == source + ">" + target));
    public static string? ValidateWord(string word, IEnumerable<LanguageProfile> profiles)
    {
        if (word.Length < 2 || !word.All(char.IsLetter)) return "Введите слово из двух или более букв.";
        if (profiles.Any(p => p.IsProtected(word)))
            return "Это одно из самых частых слов. Исключение для него нарушит распознавание языка.";
        return null;
    }
    public static List<ApplicationRule> Defaults() => new[] { "Code.exe","Cursor.exe","devenv.exe","idea64.exe","pycharm64.exe",
        "WindowsTerminal.exe","OpenConsole.exe","cmd.exe","powershell.exe","pwsh.exe","mstsc.exe","msrdc.exe","vmconnect.exe" }
        .Select(p => new ApplicationRule(p, false, ApplicationMode.ManualOnly)).ToList();
}

public sealed class LearningCounter
{
    public int Rejected { get; set; }
    public int Accepted { get; set; }
}

public static class TextCorrections
{
    public static string Apply(string word, bool typos, bool capitals, bool yo, IReadOnlyDictionary<string, string> replacements,
        IReadOnlyDictionary<string, string> unambiguousYo)
    {
        var tail = word.Length;
        while (tail > 0 && !char.IsLetter(word[tail - 1])) tail--;
        var stem = word[..tail]; var suffix = word[tail..];
        if (capitals && stem.Length > 2 && char.IsUpper(stem[0]) && char.IsUpper(stem[1]) && stem.Skip(2).All(char.IsLower))
            stem = stem[..1] + char.ToLowerInvariant(stem[1]) + stem[2..];
        if (typos && replacements.TryGetValue(stem.ToLowerInvariant(), out var replacement)) stem = MatchCase(stem, replacement);
        if (yo && unambiguousYo.TryGetValue(stem.ToLowerInvariant(), out var withYo)) stem = MatchCase(stem, withYo);
        return stem + suffix;
    }
    private static string MatchCase(string original, string replacement) => original.All(char.IsUpper)
        ? replacement.ToUpperInvariant() : char.IsUpper(original[0]) ? char.ToUpperInvariant(replacement[0]) + replacement[1..] : replacement;
}
