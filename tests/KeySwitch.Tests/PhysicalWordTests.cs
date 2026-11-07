using KeySwitch.Core;
using KeySwitch.Data;
using Xunit;

namespace KeySwitch.Tests;

public sealed class PhysicalWordTests
{
    [Theory]
    [InlineData(false, false, "ghbdtn", "привет")]
    [InlineData(true, false, "GHBDTN", "ПРИВЕТ")]
    [InlineData(false, true, "GHBDTN", "ПРИВЕТ")]
    [InlineData(true, true, "ghbdtn", "привет")]
    public void PhysicalSequenceReachesDetectorIntact(bool shift, bool caps, string input, string expected)
    {
        var detector = new LanguageDetector(ModelLoader.Load());
        var keys = new[] { (0x47, 0x22), (0x48, 0x23), (0x42, 0x30), (0x44, 0x20), (0x54, 0x14), (0x4E, 0x31) };
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buffer = new WordBuffer();
            foreach (var (vk, scan) in keys)
            {
                var key = new PhysicalKey(vk, scan, shift, caps);
                buffer.Add(Layouts.English.Translate(key)!.Value, key, "en");
            }
            Assert.Equal(input, buffer.Text);
            var evidence = new DetectionEvidence();
            Assert.Equal(expected, detector.Detect(buffer.Text, "en", evidence: evidence)?.Replacement);
            Assert.Equal(1d, evidence.ScoreB);
            Assert.Equal("accepted", evidence.Reason);
        }
    }
}
