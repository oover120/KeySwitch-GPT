using KeySwitch.Core;
using System.IO;
using KeySwitch.Data;
using Xunit;
namespace KeySwitch.Tests;

public sealed class CoreTests
{
    private static readonly IReadOnlyList<LanguageProfile> Profiles = ModelLoader.Load();
    private static readonly LanguageDetector Detector = new(Profiles);
    [Theory]
    [InlineData("ghbdtn", "привет")]
    [InlineData("Ghbdtn", "Привет")]
    [InlineData("[jhjij", "хорошо")]
    [InlineData(";e;;fnm", "жужжать")]
    [InlineData("`", "ё")]
    public void PhysicalMapping(string en, string ru)
    { Assert.Equal(ru, Layouts.English.ConvertTo(en, Layouts.Russian)); Assert.Equal(en, Layouts.Russian.ConvertTo(ru, Layouts.English)); }
    [Fact]
    public void EveryPhysicalPositionRoundTrips()
    {
        Assert.Equal(Layouts.English.Normal, Layouts.Russian.ConvertTo(Layouts.English.ConvertTo(Layouts.English.Normal, Layouts.Russian), Layouts.English));
        Assert.Equal(Layouts.English.Shifted, Layouts.Russian.ConvertTo(Layouts.English.ConvertTo(Layouts.English.Shifted, Layouts.Russian), Layouts.English));
    }
    [Fact]
    public void CapsLockAppliesToRussianOemLetters()
    { Assert.Equal('Х', Layouts.Russian.Translate(new(0xDB, 26, false, true))); Assert.Equal('х', Layouts.Russian.Translate(new(0xDB, 26, true, true))); }
    [Theory]
    [InlineData(32)]
    [InlineData(13)]
    [InlineData(9)]
    public void Boundaries(int key) => Assert.True(WordBuffer.IsBoundary(key));
    [Theory]
    [InlineData(0xDB)]
    [InlineData(0xDD)]
    [InlineData(0xBA)]
    [InlineData(0xDE)]
    [InlineData(0xBC)]
    [InlineData(0xBE)]
    [InlineData(0xC0)]
    public void OemKeysAreNotBoundaries(int key) => Assert.False(WordBuffer.IsBoundary(key));
    [Fact]
    public void BufferOverflowCannotLeaveAnEditableSuffix()
    { var buffer = new WordBuffer(); for (var i = 0; i < 100; i++) buffer.Add('a', new(65, 30, false, false), "en"); Assert.True(buffer.IsEmpty); buffer.Backspace(); Assert.True(buffer.IsEmpty); buffer.Reset(); buffer.Add('b', new(66, 48, false, false), "en"); Assert.Equal("b", buffer.Text); }
    [Fact]
    public void LayoutChangeInvalidatesBuffer()
    { var b = new WordBuffer(); b.Add('a', new(65, 30, false, false), "en"); b.Add('ф', new(65, 30, false, false), "ru"); Assert.Equal("ф", b.Text); }
    public static IEnumerable<object[]> Corpus()
    {
        foreach (var profile in Profiles)
        {
            var other = Profiles.Single(p => p != profile);
            foreach (var word in profile.Frequencies.OrderByDescending(p => p.Value).Where(p => p.Key.Length >= 3).Take(600).Select(p => p.Key))
                yield return [word, profile.Layout.Id, profile.Layout.ConvertTo(word, other.Layout), other.Layout.Id];
        }
    }
    [Theory]
    [MemberData(nameof(Corpus))]
    public void CorrectWordsNeverSwitchAndWrongLayoutNeverProducesDifferentWord(string word, string language, string wrong, string wrongLanguage)
    {
        Assert.Null(Detector.Detect(word, language));
        var decision = Detector.Detect(wrong, wrongLanguage);
        if (decision is not null) Assert.Equal(word, decision.Replacement);
    }
    [Theory]
    [InlineData("ghbdtn", "привет")]
    [InlineData("ghbdtn,", "привет,")]
    [InlineData("[jhjij", "хорошо")]
    public void DetectsClassicErrorsAndPreservesPunctuation(string input, string expected) => Assert.Equal(expected, Detector.Detect(input, "en")?.Replacement);
    [Theory]
    [InlineData("hello")]
    [InlineData("abc_test")]
    [InlineData("https://example.com")]
    [InlineData("TestPass123!")]
    public void SafeInputRemains(string input) => Assert.Null(Detector.Detect(input, "en"));
    [Fact] public void ShortAmbiguousWordIsPreserved() => Assert.Null(Detector.Detect("ok", "en"));
    [Fact]
    public void ThresholdAndEarlyModeAreEnforced()
    { Assert.Null(Detector.Detect("ghbdtn", "en", 1)); Assert.Null(Detector.Detect("ghbd", "en", early: true)); }
    [Fact]
    public void PasswordGuard()
    { Assert.True(PasswordHeuristic.IsSensitive("Passwd12!")); Assert.False(PasswordHeuristic.IsSensitive("Привет")); }
    [Fact]
    public void ExplicitPositiveExampleCanDisambiguateKnownSource()
    { Assert.Null(Detector.Detect("key", "en")); Assert.Equal("лун", Detector.Detect("key", "en", learned: new Dictionary<string, string> { ["en:key"] = "ru" })?.Replacement); }
    [Fact]
    public void LongShiftOrShiftChordIsNotASingleTap()
    { var s = new ShiftGesture(); s.Down(0); s.Up(600); Assert.False(s.WasTap); s.Down(700); s.OtherKey(); s.Up(750); Assert.False(s.WasTap); }
    [Fact]
    public void UndoRequiresSameWindowAndGeneration()
    { var u = new UndoState(); u.Set(new("ghbdtn", "привет", "en", "ru", " ", 123, 9)); Assert.Null(u.Take(123, 10)); Assert.Null(u.Last); u.Set(new("a", "b", "en", "ru", "", 123, 9)); Assert.NotNull(u.Take(123, 9)); Assert.Null(u.Last); }
    [Fact]
    public void DoubleShiftRequiresTwoCleanTaps()
    { var s = new ShiftGesture(); s.Down(0); Assert.False(s.Up(50)); s.Down(100); Assert.True(s.Up(150)); s.Down(200); s.OtherKey(); Assert.False(s.Up(250)); s.Down(300); Assert.False(s.Up(350)); }
    [Fact]
    public void ApplicationPoliciesRespectFolderBoundaryAndOverrides()
    {
        var rules = new[] { new ApplicationRule(@"C:\Games\Allowed", true, ApplicationMode.Full) };
        Assert.Equal(ApplicationMode.Full, ExclusionPolicy.Resolve(@"C:\Games\Allowed\test.exe", true, rules));
        Assert.Equal(ApplicationMode.Disabled, ExclusionPolicy.Resolve(@"C:\Games\AllowedOther\test.exe", false, rules));
        Assert.Equal(ApplicationMode.ManualOnly, ExclusionPolicy.Resolve(@"C:\editor\Code.exe", false, ExclusionPolicy.Defaults()));
    }
    [Fact]
    public void ExceptionsAreDirectionalAndCommonWordsProtected()
    { Assert.NotNull(ExclusionPolicy.ValidateWord("я", Profiles)); Assert.NotNull(ExclusionPolicy.ValidateWord("the", Profiles)); Assert.True(ExclusionPolicy.IsExcepted("test", "en", "ru", [new("test", "en>ru")])); Assert.False(ExclusionPolicy.IsExcepted("test", "ru", "en", [new("test", "en>ru")])); }
    [Fact]
    public void RepeatedUndoLearnsOnlyExplicitWords()
    { var s = new Settings(); for (var i = 0; i < 3; i++) SettingsStore.Learn(s, "ghbdtn", "en", "ru", true, Profiles); Assert.Single(s.Words); SettingsStore.Learn(s, "MySecret12!", "en", "ru", true, Profiles); Assert.Single(s.Learning); }
    [Fact]
    public void OptionalCorrectionsRespectCaseAndAmbiguousYo()
    { var s = new Settings(); Assert.Equal("Привет", TextCorrections.Apply("ПРивет", false, true, false, s.Replacements, s.YoWords)); Assert.Equal("The", TextCorrections.Apply("Teh", true, false, false, s.Replacements, s.YoWords)); Assert.Equal("все", TextCorrections.Apply("все", false, false, true, s.Replacements, s.YoWords)); }
    [Fact]
    public void SettingsRoundTripAndMalformedFileIsPreserved()
    {
        var path = Path.Combine(Path.GetTempPath(), "KeySwitch-test-" + Guid.NewGuid().ToString("N"));
        try { var store = new SettingsStore(path); store.Save(new() { SingleShift = true }); Assert.True(store.Load().SingleShift); File.WriteAllText(store.FilePath, "bad json"); Assert.False(store.Load().Enabled); Assert.Equal("bad json", File.ReadAllText(store.FilePath)); }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
