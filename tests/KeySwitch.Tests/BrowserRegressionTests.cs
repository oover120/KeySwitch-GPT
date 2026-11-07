using KeySwitch.Core;
using KeySwitch.Hooks;
using Xunit;

namespace KeySwitch.Tests;

public sealed class BrowserRegressionTests
{
    [Theory]
    [InlineData("error")]
    [InlineData("timeout")]
    [InlineData("unknown")]
    public void UiaFailureDoesNotTurnAnEditableContextIntoAPassword(string result)
    {
        var context = new WindowContext(1, 2, 3, "chrome.exe", false, false, true)
        {
            Password = Enum.Parse<PasswordStatus>(result, ignoreCase: true)
        };

        Assert.True(context.Ready);
        Assert.False(context.IsPassword);
        Assert.Equal(result, context.PasswordCheck);
    }

    [Fact]
    public void OnlyExplicitTrueMarksTheFieldAsAPassword()
    {
        Assert.True(PasswordProbe.IsPassword(PasswordProbe.Classify(true)));
        Assert.False(PasswordProbe.IsPassword(PasswordProbe.Classify(false)));
        Assert.False(PasswordProbe.IsPassword(PasswordProbe.Classify(null)));
        Assert.False(PasswordProbe.IsPassword(PasswordProbe.Classify(new object())));
    }

    [Theory]
    [InlineData("chrome.exe")]
    [InlineData("msedge.exe")]
    [InlineData("firefox.exe")]
    public void MaximizedBrowsersAreNeverClassifiedAsGames(string exe)
    {
        var path = @"C:\Games\Browser\" + exe;
        Assert.False(ApplicationProfiles.IsGame(path, exclusiveFullscreen: true, games: [exe]));
        Assert.Equal(ApplicationMode.Full, ExclusionPolicy.Resolve(path, true, []));
        Assert.Equal(ApplicationMode.Disabled, ExclusionPolicy.Resolve(path, true, [new(exe, false, ApplicationMode.Disabled)]));
    }

    [Fact]
    public void GameListAndGamesFolderStillExcludeNonBrowsers()
    {
        Assert.True(ApplicationProfiles.IsGame(@"C:\Tools\racer.exe", false, ["RACER.EXE"]));
        Assert.True(ApplicationProfiles.IsGame(@"C:\Games\racer.exe", false));
        Assert.False(ApplicationProfiles.IsGame(@"C:\OtherGames\racer.exe", false));
    }

    [Fact]
    public void TitleChangeAndDuplicateFocusNotificationKeepTheWordBuffer()
    {
        var tracker = new FocusTracker();
        var identity = new FocusIdentity(1, 2, 42);
        var focus = new FocusNotification(0x8005, 2, -4, 7);
        Assert.True(tracker.Observe(identity, focus));
        var buffer = new WordBuffer();
        buffer.Add('g', new(0x47, 0, false, false), "en");

        var titleChanged = new FocusNotification(0x800c, 1, 0, 0);
        if (tracker.Observe(identity, titleChanged)) buffer.Reset();
        if (tracker.Observe(identity, focus)) buffer.Reset();

        Assert.Equal("g", buffer.Text);
        Assert.True(tracker.Observe(identity, focus with { ChildId = 8 }));
        Assert.True(tracker.Observe(identity with { Control = 3 }));
    }
}
