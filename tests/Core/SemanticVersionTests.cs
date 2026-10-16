using DevPilot.Core;

namespace DevPilot.Tests.Core;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.5.0", 1, 5, 0, "")]
    [InlineData("v1.5.0", 1, 5, 0, "")]
    [InlineData("1.5.0-beta.2", 1, 5, 0, "beta.2")]
    [InlineData("1.5.0+abc123", 1, 5, 0, "")]
    [InlineData("v10.20.30-rc.1+build.5", 10, 20, 30, "rc.1")]
    public void TryParse_ReadsVersions(string text, int major, int minor, int patch, string preRelease)
    {
        Assert.Equal(new SemanticVersion(major, minor, patch, preRelease), SemanticVersion.TryParse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.5")]
    [InlineData("1.5.0.1")]
    [InlineData("release-1.5.0")]
    public void TryParse_RejectsNonVersions(string? text)
    {
        Assert.Null(SemanticVersion.TryParse(text));
    }

    [Theory]
    [InlineData("1.0.0", "2.0.0")]
    [InlineData("1.4.9", "1.5.0")]
    [InlineData("1.5.0", "1.5.1")]
    [InlineData("1.5.0-alpha.1", "1.5.0-beta.1")]
    [InlineData("1.5.0-beta.2", "1.5.0-beta.10")]   // numbers compare as numbers
    [InlineData("1.5.0-beta.10", "1.5.0-rc.1")]
    [InlineData("1.5.0-rc.1", "1.5.0")]             // a release is newer than its pre-releases
    [InlineData("1.5.0-1", "1.5.0-alpha")]          // numbers sort before words
    [InlineData("1.5.0-beta", "1.5.0-beta.1")]      // more labels sort later
    public void CompareTo_OrdersBySemanticVersioning(string older, string newer)
    {
        var a = SemanticVersion.TryParse(older)!;
        var b = SemanticVersion.TryParse(newer)!;

        Assert.True(a < b, $"{older} should be older than {newer}");
        Assert.True(b > a);
    }

    [Fact]
    public void ToString_LeavesOutBuildMetadata()
    {
        Assert.Equal("1.5.0-beta.2", SemanticVersion.TryParse("v1.5.0-beta.2+abc")!.ToString());
    }
}
