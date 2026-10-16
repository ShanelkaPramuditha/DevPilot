using DevPilot.Services.Wsl;

namespace DevPilot.Tests.Services;

public class WslSettingsTests
{
    [Theory]
    [InlineData("4", "4GB")]
    [InlineData("4gb", "4GB")]
    [InlineData("4 GB", "4GB")]
    [InlineData("512MB", "512MB")]
    [InlineData("512m", "512MB")]
    [InlineData("1.5GB", "1.5GB")]
    [InlineData("1TB", "1TB")]
    public void NormalizeSize_AcceptsCommonForms(string input, string expected)
    {
        Assert.Equal(expected, WslSettings.NormalizeSize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("four")]
    [InlineData("4 gigs")]
    [InlineData("-4GB")]
    public void NormalizeSize_RejectsInvalidSizes(string input)
    {
        Assert.Null(WslSettings.NormalizeSize(input));
    }
}
