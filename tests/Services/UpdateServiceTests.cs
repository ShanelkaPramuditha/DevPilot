using DevPilot.Core;
using DevPilot.Services.Updates;

namespace DevPilot.Tests.Services;

public class UpdateServiceTests
{
    // Shaped like GitHub's "list releases" response (newest first), trimmed to the fields DevPilot reads.
    private const string ReleasesJson = """
        [
          { "tag_name": "v1.6.0-beta.1", "name": "DevPilot 1.6.0-beta.1", "draft": false, "prerelease": true,
            "body": "beta", "html_url": "https://github.com/me/DevPilot/releases/tag/v1.6.0-beta.1",
            "published_at": "2026-10-10T08:00:00Z", "assets": [] },
          { "tag_name": "v1.7.0", "name": "", "draft": true, "prerelease": false,
            "body": "draft", "html_url": "", "published_at": null, "assets": [] },
          { "tag_name": "v1.5.0", "name": "DevPilot 1.5.0", "draft": false, "prerelease": false,
            "body": "## Download\n| a | b |\n\n## New features\n* Add **updates** by @me in [#7](https://github.com/me/DevPilot/pull/7)\n",
            "html_url": "https://github.com/me/DevPilot/releases/tag/v1.5.0",
            "published_at": "2026-10-01T08:00:00Z",
            "assets": [
              { "name": "DevPilot-1.5.0-win-arm64-setup.exe", "browser_download_url": "https://example/arm64-setup.exe", "size": 50000000 },
              { "name": "DevPilot-1.5.0-win-x64-setup.exe", "browser_download_url": "https://example/x64-setup.exe", "size": 48000000 },
              { "name": "DevPilot-1.5.0-win-x64-portable.zip", "browser_download_url": "https://example/x64.zip", "size": 61000000 },
              { "name": "SHA256SUMS.txt", "browser_download_url": "https://example/SHA256SUMS.txt", "size": 400 }
            ] },
          { "tag_name": "nightly", "name": "Nightly", "draft": false, "prerelease": true,
            "body": "", "html_url": "", "published_at": null, "assets": [] },
          { "tag_name": "v1.4.2", "name": "DevPilot 1.4.2", "draft": false, "prerelease": false,
            "body": "", "html_url": "", "published_at": "2026-09-01T08:00:00Z", "assets": [] }
        ]
        """;

    private static SemanticVersion V(string text) => SemanticVersion.TryParse(text)!;

    [Fact]
    public void StaticState_LoadsWithoutErrors()
    {
        // Regression: the HTTP client used to be created before CurrentVersion was set,
        // which made every use of UpdateService throw TypeInitializationException.
        var exception = Record.Exception(() =>
        {
            _ = UpdateService.CurrentVersion;
            _ = UpdateService.IsConfigured;
            _ = UpdateService.Architecture;
        });

        Assert.Null(exception);
    }

    [Fact]
    public void ParseReleases_SkipsDraftsAndTagsThatAreNotVersions()
    {
        var releases = UpdateService.ParseReleases(ReleasesJson);

        Assert.Equal(["v1.6.0-beta.1", "v1.5.0", "v1.4.2"], releases.Select(r => r.Tag));
        Assert.Equal(4, releases[1].Assets.Count);
        Assert.True(releases[0].IsPreRelease);
    }

    [Theory]
    [InlineData("1.4.2", false, "1.5.0")]          // newest stable release
    [InlineData("1.4.2", true, "1.6.0-beta.1")]    // pre-releases when asked for
    [InlineData("1.5.0", false, null)]             // up to date
    [InlineData("1.6.0", true, null)]              // newer than anything published
    [InlineData("1.0.0", false, "1.5.0")]          // jumps straight to the newest
    public void SelectUpdate_PicksTheNewestNewerRelease(string current, bool includePreReleases, string? expected)
    {
        var update = UpdateService.SelectUpdate(UpdateService.ParseReleases(ReleasesJson), V(current), includePreReleases);

        Assert.Equal(expected, update?.Version.ToString());
    }

    [Theory]
    [InlineData("x64", "DevPilot-1.5.0-win-x64-setup.exe")]
    [InlineData("arm64", "DevPilot-1.5.0-win-arm64-setup.exe")]
    public void FindInstaller_PicksTheSetupForTheArchitecture(string architecture, string expected)
    {
        var release = UpdateService.ParseReleases(ReleasesJson).Single(r => r.Tag == "v1.5.0");

        Assert.Equal(expected, UpdateService.FindInstaller(release, architecture)?.Name);
    }

    [Fact]
    public void FindHash_ReadsBothSha256sumFormats()
    {
        string hashA = new('a', 64);
        string hashB = new('B', 64);
        string sums = $"{hashA}  DevPilot-1.5.0-win-x64-setup.exe\n{hashB} *DevPilot-1.5.0-win-arm64-setup.exe\n";

        Assert.Equal(hashA, UpdateService.FindHash(sums, "DevPilot-1.5.0-win-x64-setup.exe"));
        Assert.Equal(hashB.ToLowerInvariant(), UpdateService.FindHash(sums, "DevPilot-1.5.0-win-arm64-setup.exe"));
        Assert.Null(UpdateService.FindHash(sums, "DevPilot-1.5.0-win-x64-portable.zip"));
    }

    [Fact]
    public void CleanNotes_DropsTheDownloadTableAndMarkdown()
    {
        var release = UpdateService.ParseReleases(ReleasesJson).Single(r => r.Tag == "v1.5.0");

        Assert.Equal("New features\n• Add updates by @me in #7", UpdateService.CleanNotes(release.Notes));
    }
}
