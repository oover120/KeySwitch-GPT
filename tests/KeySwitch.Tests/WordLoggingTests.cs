using KeySwitch.Hooks;
using Xunit;

namespace KeySwitch.Tests;

public sealed class WordLoggingTests
{
    [Fact]
    public void AResetPreservesTheReasonAndLengthUntilOneBoundary()
    {
        var activity = new WordActivity();
        for (var i = 0; i < 6; i++) activity.Add();
        activity.ResetBuffer("arrow-key");

        Assert.Equal((6, "arrow-key", null), activity.Complete());
        Assert.Equal((0, null, null), activity.Complete());
    }

    [Fact]
    public void ThrottlingIgnoredKeysPreservesTheWordAndLatestReason()
    {
        var activity = new WordActivity();
        activity.Add();
        Assert.True(activity.Ignore("context-not-ready", 100));
        activity.Add();
        Assert.False(activity.Ignore("unicode-packet", 1099));
        Assert.Equal((2, null, "unicode-packet"), activity.Complete());
        Assert.True(activity.Ignore("unicode-packet", 1100));
    }

    [Fact]
    public void DeferredCompletionCannotPublishAWordTwice()
    {
        var trace = new WordTrace
        {
            Process = "chrome.exe", FocusClass = "Chrome_WidgetWin_1", PasswordCheck = "no",
            ProgramExcluded = false, GameOrFullscreen = false, ContextReady = true,
            WordLength = 6, BufferedLength = 6, FocusResets = 0, Deferred = true
        };

        Assert.Equal("INFO", trace.Level);
        Assert.True(trace.TryPublish());
        Assert.False(trace.TryPublish());
    }
}
