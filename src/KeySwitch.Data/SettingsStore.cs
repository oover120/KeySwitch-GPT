using System.Text.Json;
using KeySwitch.Core;
namespace KeySwitch.Data;

public sealed class SettingsStore
{
    private readonly object gate = new();
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public string? LoadWarning { get; private set; }
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public SettingsStore(string? directory = null) => DirectoryPath = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeySwitch");
    public Settings Load()
    {
        lock (gate)
        {
            if (!File.Exists(FilePath)) return new();
            try { var result = JsonSerializer.Deserialize<Settings>(File.ReadAllBytes(FilePath), Options) ?? throw new InvalidDataException(); result.Validate(); return result; }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
            { LoadWarning = "Настройки не прочитаны. Исходный файл сохранён; проверьте settings.json."; return new Settings { Enabled = false }; }
        }
    }
    public void Save(Settings settings)
    {
        lock (gate)
        {
            settings.Validate(); Directory.CreateDirectory(DirectoryPath);
            var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { JsonSerializer.Serialize(file, settings, Options); file.Flush(true); }
                File.Move(temporary, FilePath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public static Settings Clone(Settings settings) => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings, Options), Options)!;
    public static void Learn(Settings settings, string word, string from, string to, bool rejected, IEnumerable<LanguageProfile> profiles)
    {
        if (PasswordHeuristic.IsSensitive(word) || word.Length > 64 || ExclusionPolicy.ValidateWord(word, profiles) is not null) return;
        var key = from + ">" + to + ":" + word.ToLowerInvariant();
        if (settings.Learning.Count >= 2000 && !settings.Learning.ContainsKey(key)) return;
        if (!settings.Learning.TryGetValue(key, out var counter)) settings.Learning[key] = counter = new();
        if (rejected) counter.Rejected++; else counter.Accepted++;
        if (counter.Rejected >= 3 && !ExclusionPolicy.IsExcepted(word, from, to, settings.Words)) settings.Words.Add(new(word.ToLowerInvariant(), from + ">" + to));
    }
}
