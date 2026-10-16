using DevPilot.Models;
using DevPilot.Services.Wsl;

namespace DevPilot.Tests.Services;

public class WslMountsTests
{
    private static readonly DistroUser User = new("1000", "1000", "/home/dev");

    [Theory]
    [InlineData("~/projects", "/home/dev/projects")]
    [InlineData("~/My Project/", "/home/dev/My Project")]
    [InlineData("/srv/www/site", "/srv/www/site")]
    [InlineData("/home/dev//code", "/home/dev/code")]
    public void ValidateLinuxPath_AcceptsAndNormalisesSafePaths(string input, string expected)
    {
        string? error = WslMounts.ValidateLinuxPath(input, User, out string path);

        Assert.Null(error);
        Assert.Equal(expected, path);
    }

    [Theory]
    [InlineData("projects")]          // not absolute
    [InlineData("/")]                 // system folders
    [InlineData("/etc")]
    [InlineData("/mnt")]
    [InlineData("/mnt/c")]            // drive mounted by WSL
    [InlineData("~")]                 // the home folder itself
    [InlineData("/proc/self")]
    [InlineData("/home/dev/../etc")]  // parent references
    public void ValidateLinuxPath_RejectsUnsafePaths(string input)
    {
        Assert.NotNull(WslMounts.ValidateLinuxPath(input, User, out _));
    }
}
