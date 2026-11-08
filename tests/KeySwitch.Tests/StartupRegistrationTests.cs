using KeySwitch.Core;
using Xunit;

namespace KeySwitch.Tests;

public sealed class StartupRegistrationTests
{
    [Fact]
    public void MissingOldCopyIsNotReportedAsReady()
    {
        var state = StartupRegistration.Inspect("\"C:\\Old\\KeySwitch.exe\"", @"C:\New\KeySwitch.exe", _ => false);
        Assert.True(state.Registered);
        Assert.False(state.FileExists);
        Assert.False(state.Ready);
    }

    [Fact]
    public void ExistingDifferentCopyStillNeedsRepair()
    {
        var state = StartupRegistration.Inspect("\"C:\\Old\\KeySwitch.exe\"", @"C:\New\KeySwitch.exe", _ => true);
        Assert.True(state.FileExists);
        Assert.False(state.MatchesCurrent);
        Assert.False(state.Ready);
    }

    [Fact]
    public void CurrentQuotedPathWithSpacesRoundTrips()
    {
        const string path = @"C:\Program Files\KeySwitch\KeySwitch.exe";
        var state = StartupRegistration.Inspect(StartupRegistration.CommandFor(path), path.ToLowerInvariant(), p => p == path);
        Assert.Equal(path, state.Executable);
        Assert.True(state.Ready);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("\"C:\\Unclosed.exe", true)]
    public void AbsentOrMalformedEntryIsNotReady(string? command, bool registered)
    {
        var state = StartupRegistration.Inspect(command, @"C:\KeySwitch.exe", _ => true);
        Assert.Equal(registered, state.Registered);
        Assert.False(state.Ready);
    }
}
