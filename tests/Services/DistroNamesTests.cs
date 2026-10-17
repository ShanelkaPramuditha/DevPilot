using DevPilot.Services.Wsl;

namespace DevPilot.Tests.Services;

public class DistroNamesTests
{
    private static readonly string[] Existing = ["Ubuntu-26.04", "Debian"];

    [Theory]
    [InlineData("Ubuntu-dev")]
    [InlineData("my_distro.2")]
    public void Validate_AcceptsWslNames(string name)
    {
        Assert.Null(DistroNames.Validate(name, Existing));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("my distro")]
    [InlineData("ubuntu/dev")]
    [InlineData("debian")]          // names are compared without case
    public void Validate_RejectsInvalidOrTakenNames(string name)
    {
        Assert.NotNull(DistroNames.Validate(name, Existing));
    }

    [Fact]
    public void SuggestCopyName_FindsAFreeName()
    {
        Assert.Equal("Debian-copy", DistroNames.SuggestCopyName("Debian", Existing));
        Assert.Equal("Debian-copy-3", DistroNames.SuggestCopyName("Debian", [.. Existing, "Debian-copy", "debian-copy-2"]));
    }
}
