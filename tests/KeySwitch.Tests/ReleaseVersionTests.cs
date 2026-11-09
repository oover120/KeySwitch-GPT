using KeySwitch.Core;
using Xunit;

namespace KeySwitch.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v1.0.5", true)]
    [InlineData("1.0.4", false)]
    [InlineData("v1.0.3", false)]
    [InlineData("v1.0.5-beta", false)]
    [InlineData("invalid", false)]
    [InlineData(null, false)]
    public void OnlyNewerStableReleaseIsOffered(string? tag, bool expected) =>
        Assert.Equal(expected, ReleaseVersion.IsNewer(tag, new Version(1, 0, 4, 0)));
}
