using DevPilot.Services.Wsl;

namespace DevPilot.Tests.Services;

public class WslUpdateCheckTests
{
    [Theory]
    [InlineData("3.0.3", "3.0.3.0")]
    [InlineData("v2.6.1", "2.6.1.0")]
    [InlineData("3.0.3.0", "3.0.3.0")]
    [InlineData("2.6", "2.6.0.0")]
    public void ParseVersion_PadsToFourParts(string text, string expected) =>
        Assert.Equal(Version.Parse(expected), WslUpdateCheck.ParseVersion(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("3")]
    [InlineData("3.0.3-preview")]
    [InlineData("1.2.3.4.5")]
    public void ParseVersion_RejectsOtherText(string? text) => Assert.Null(WslUpdateCheck.ParseVersion(text));

    [Fact]
    public void ParseInstalledVersion_FromTheDetailsLine() =>
        Assert.Equal(new Version(3, 0, 3, 0),
            WslUpdateCheck.ParseInstalledVersion("WSL version: 3.0.3.0   ·   Kernel version: 6.18.20.1   ·   VM memory: Up to 7.9 GB"));

    [Theory]
    [InlineData("3.0.3.0", "3.0.3", false)]
    [InlineData("3.0.3.0", "3.0.4", true)]
    [InlineData("2.6.1.0", "3.0.3", true)]
    [InlineData("3.1.0.0", "3.0.3", false)]
    public void IsUpdateAvailable_ComparesWholeVersions(string installed, string latest, bool expected) =>
        Assert.Equal(expected, WslUpdateCheck.IsUpdateAvailable(WslUpdateCheck.ParseVersion(installed), WslUpdateCheck.ParseVersion(latest)));

    [Fact]
    public void IsUpdateAvailable_UnknownVersionsAreNotUpdates()
    {
        Assert.False(WslUpdateCheck.IsUpdateAvailable(null, new Version(3, 0, 4, 0)));
        Assert.False(WslUpdateCheck.IsUpdateAvailable(new Version(3, 0, 3, 0), null));
    }
}
