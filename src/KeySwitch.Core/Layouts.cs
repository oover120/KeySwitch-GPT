namespace KeySwitch.Core;

public sealed record PhysicalKey(int VirtualKey, int ScanCode, bool Shift, bool CapsLock);

/// <summary>Layouts share physical positions; language-specific characters are data.</summary>
public sealed class KeyboardLayout(string id, string klid, string normal, string shifted)
{
    public string Id { get; } = id;
    public string Klid { get; } = klid;
    public string Normal { get; } = normal;
    public string Shifted { get; } = shifted;
    private static readonly int[] VirtualKeys = [0xC0,
        49,
        50,
        51,
        52,
        53,
        54,
        55,
        56,
        57,
        48,
        0xBD,
        0xBB,
        81,
        87,
        69,
        82,
        84,
        89,
        85,
        73,
        79,
        80,
        0xDB,
        0xDD,
        0xDC,
        65,
        83,
        68,
        70,
        71,
        72,
        74,
        75,
        76,
        0xBA,
        0xDE,
        90,
        88,
        67,
        86,
        66,
        78,
        77,
        0xBC,
        0xBE,
        0xBF];
    public char? Translate(PhysicalKey key)
    {
        var index = Array.IndexOf(VirtualKeys, key.VirtualKey);
        if (index < 0) return null;
        var shift = key.Shift ^ (key.CapsLock && char.IsLetter(Normal[index]));
        return (shift ? Shifted : Normal)[index];
    }
    public PhysicalKey? PhysicalKeyFor(char character)
    {
        var index=Normal.IndexOf(character);
        if(index>=0) return new(VirtualKeys[index],0,false,false);
        index=Shifted.IndexOf(character);
        return index>=0 ? new(VirtualKeys[index],0,true,false) : null;
    }
    public string ConvertTo(string text, KeyboardLayout target)
    {
        if (Normal.Length != target.Normal.Length || Shifted.Length != target.Shifted.Length)
            throw new ArgumentException("Layouts must use the same physical positions.");
        return string.Create(text.Length, (text, target), (span, state) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                var index = Normal.IndexOf(state.text[i]);
                if (index >= 0) span[i] = state.target.Normal[index];
                else { index = Shifted.IndexOf(state.text[i]); span[i] = index >= 0 ? state.target.Shifted[index] : state.text[i]; }
            }
        });
    }
}

public static class Layouts
{
    public static readonly KeyboardLayout English = new("en", "00000409",
        "`1234567890-=qwertyuiop[]\\asdfghjkl;'zxcvbnm,./",
        "~!@#$%^&*()_+QWERTYUIOP{}|ASDFGHJKL:\"ZXCVBNM<>?");
    public static readonly KeyboardLayout Russian = new("ru", "00000419",
        "ё1234567890-=йцукенгшщзхъ\\фывапролджэячсмитьбю.",
        "Ё!\"№;%:?*()_+ЙЦУКЕНГШЩЗХЪ/ФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,");
    public static KeyboardLayout? FromLanguageId(int langId) => (langId & 0xFFFF) switch
    { 0x409 => English, 0x419 => Russian, _ => null };
}
