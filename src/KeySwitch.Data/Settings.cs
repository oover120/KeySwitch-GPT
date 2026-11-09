using KeySwitch.Core;
namespace KeySwitch.Data;

public sealed class Settings
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public bool Debug { get; set; }
    public bool PreferRussian { get; set; } = true;
    public bool CheckForUpdates { get; set; } = true;
    public double ConfidenceThreshold { get; set; } = LanguageDetector.DefaultThreshold;
    public bool EarlySwitch { get; set; }
    public bool ElevatedStartup { get; set; }
    public bool SingleShift { get; set; }
    public bool Typos { get; set; }
    public bool FixCapitals { get; set; } = true;
    public bool Yoficator { get; set; }
    public string Theme { get; set; } = "System";
    public bool Sounds { get; set; }
    public string SwitchSound { get; set; } = "";
    public string TypoSound { get; set; } = "";
    public double Volume { get; set; } = 0.4;
    public int SelectionKey { get; set; } = 0x20;
    public int SelectionModifiers { get; set; } = 6; // MOD_CONTROL | MOD_SHIFT
    public int PauseKey { get; set; } = 0x7B; // F12
    public int PauseModifiers { get; set; } = 6;
    public List<ApplicationRule> Applications { get; set; } = ExclusionPolicy.Defaults();
    public List<string> GameExecutables { get; set; } = [];
    public List<WordException> Words { get; set; } = [];
    public Dictionary<string, LearningCounter> Learning { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Replacements { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    { ["teh"] = "the", ["adn"] = "and", ["тоесть"] = "то есть", ["вообщем"] = "в общем" };
    // Only curated unambiguous forms; intentionally no все/всё.
    public Dictionary<string, string> YoWords { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    { ["береза"] = "берёза", ["березы"] = "берёзы", ["ежик"] = "ёжик", ["елочка"] = "ёлочка" };
    public void Validate()
    {
        if (!double.IsFinite(ConfidenceThreshold) || ConfidenceThreshold is < 0.85 or > 1) throw new InvalidDataException("Порог должен быть от 0.85 до 1.");
        if (!double.IsFinite(Volume) || Volume is < 0 or > 1) throw new InvalidDataException("Громкость должна быть от 0 до 1.");
        if (Theme is not ("System" or "Light" or "Dark")) throw new InvalidDataException("Неизвестная тема.");
        if (SelectionKey < 0x20 || SelectionKey > 0xFE || SelectionModifiers is not (6 or 3 or 10)) throw new InvalidDataException("Для выделения выберите Ctrl+Shift, Ctrl+Alt или Ctrl+Win.");
        if (PauseKey < 0x20 || PauseKey > 0xFE || PauseModifiers is not (6 or 3 or 10)) throw new InvalidDataException("Для паузы выберите Ctrl+Shift, Ctrl+Alt или Ctrl+Win.");
        if (PauseKey == SelectionKey && PauseModifiers == SelectionModifiers) throw new InvalidDataException("Сочетания для паузы и выделения должны отличаться.");
        if (Applications is null || Words is null || Learning is null || Replacements is null || YoWords is null) throw new InvalidDataException("Повреждена структура настроек.");
        if (GameExecutables is null || GameExecutables.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Некорректный список игр.");
        if(Applications.Any(r=>r is null || string.IsNullOrWhiteSpace(r.Pattern) || !Enum.IsDefined(r.Mode))) throw new InvalidDataException("Некорректное правило программы.");
        if(Words.Any(w=>w is null || string.IsNullOrWhiteSpace(w.Word) || w.Direction is not ("*" or "en>ru" or "ru>en"))) throw new InvalidDataException("Некорректное исключение слова.");
        if(Learning.Any(p=>p.Value is null || p.Value.Rejected<0 || p.Value.Accepted<0)) throw new InvalidDataException("Повреждены счётчики обучения.");
        if (Replacements.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Value) || x.Value.Length > 128)) throw new InvalidDataException("Недопустимое правило замены.");
        if(YoWords.Any(x=>string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Value) || x.Value.Length>128)) throw new InvalidDataException("Недопустимое правило ёфикатора.");
    }
}
