using System.Text;
namespace KeySwitch.Core;

public sealed class WordBuffer
{
    private readonly StringBuilder text = new();
    private readonly List<PhysicalKey> keys = [];
    private bool overflow;
    public const int Capacity = 64;
    public string Text => text.ToString();
    public IReadOnlyList<PhysicalKey> Keys => keys;
    public string? Language { get; private set; }
    public bool IsEmpty => text.Length == 0;
    public void Add(char character, PhysicalKey key, string language)
    {
        if (Language is not null && Language != language) Reset();
        Language = language;
        if (overflow) return;
        if (text.Length == Capacity) { text.Clear(); keys.Clear(); overflow = true; return; }
        text.Append(character); keys.Add(key);
    }
    public void Backspace()
    {
        if (overflow) return;
        if (text.Length > 0) { text.Length--; keys.RemoveAt(keys.Count - 1); }
    }
    public void Reset() { text.Clear(); keys.Clear(); overflow = false; Language = null; }
    public static bool IsBoundary(int vk) => vk is 0x20 or 0x0D or 0x09;
    public static bool Invalidates(int vk) => vk is 0x1B or >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x5B or 0x5C;
}

public static class PasswordHeuristic
{
    public static bool IsSensitive(string word) => word.Length > 6 && word.Any(char.IsLower)
        && word.Any(char.IsUpper) && word.Any(c => char.IsDigit(c) || !char.IsLetter(c));
}

public sealed record UndoRecord(string Original, string Replacement, string SourceLanguage,
    string TargetLanguage, string Delimiter, nint Window, long FocusGeneration);

public sealed class UndoState
{
    public UndoRecord? Last { get; private set; }
    public void Set(UndoRecord record) => Last = record;
    public void Invalidate() => Last = null;
    public UndoRecord? Take(nint window, long generation)
    {
        var result = Last; Last = null;
        return result?.Window == window && result.FocusGeneration == generation ? result : null;
    }
}

public sealed class ShiftGesture
{
    private long down = -1, previous = -1;
    private bool clean;
    public bool WasTap { get; private set; }
    public void OtherKey() { clean = false; previous = -1; }
    public void Down(long now) { if (down < 0) { down = now; clean = true; } }
    public bool Up(long now)
    {
        var tap = clean && down >= 0 && now - down < 250; WasTap = tap;
        down = -1; clean = false;
        if (!tap) { previous = -1; return false; }
        if (previous >= 0 && now - previous < 400) { previous = -1; return true; }
        previous = now; return false;
    }
}
