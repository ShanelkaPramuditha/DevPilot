using DevPilot.Models;
using DevPilot.Services.Tools;

namespace DevPilot.Tests.Services;

public class WingetServiceTests
{
    private static DevTool Tool(string id) => ToolCatalog.Tools.Single(t => t.Id == id);

    [Fact]
    public void ParseExport_ReadsIdsAndVersions()
    {
        const string json = """
            { "$schema": "x", "CreationDate": "2026-10-17",
              "Sources": [ { "Packages": [
                  { "PackageIdentifier": "Git.Git", "Version": "2.54.0" },
                  { "PackageIdentifier": "Python.Python.3.12", "Version": "3.12.3" },
                  { "PackageIdentifier": "Some.App" } ],
                "SourceDetails": { "Name": "winget" } } ],
              "WinGetVersion": "1.29" }
            """;

        var packages = WingetService.ParseExport(json);

        Assert.Equal("2.54.0", packages["git.git"]);   // ids are case-insensitive
        Assert.Equal("3.12.3", packages["Python.Python.3.12"]);
        Assert.Equal("", packages["Some.App"]);
    }

    [Fact]
    public void ParseUpgradeTable_ReadsColumnsByHeaderPosition()
    {
        string[] lines =
        [
            "Name                                              Id                                Version             Available           Source",
            "--------------------------------------------------------------------------------------------------------------------------------",
            "Git                                               Git.Git                           2.54.0              2.55.0.5            winget",
            "Microsoft Teams                                   Microsoft.Teams                   26058.713.4529.5861 26198.304.4946.9672 winget",
            "Visual Studio Code                                Microsoft.VisualStud…             1.142.0             1.143.0             winget",
            "3 upgrades available.",
        ];

        var upgrades = WingetService.ParseUpgradeTable(lines);

        Assert.Equal("2.55.0.5", upgrades["Git.Git"]);
        Assert.Equal("26198.304.4946.9672", upgrades["Microsoft.Teams"]);
        Assert.Equal("1.143.0", upgrades["Microsoft.VisualStud…"]);
        Assert.Equal(3, upgrades.Count);
    }

    [Fact]
    public void ParseUpgradeTable_WithNothingToUpgrade()
    {
        Assert.Empty(WingetService.ParseUpgradeTable(["No installed package found matching input criteria."]));
    }

    [Fact]
    public void BuildRow_RecognisesOtherEditionsOfATool()
    {
        var installed = new Dictionary<string, string> { ["Python.Python.3.12"] = "3.12.3", ["Python.Launcher"] = "3.13" };

        var row = WingetService.BuildRow(Tool("python"), installed, new Dictionary<string, string> { ["Python.Python.3.12"] = "3.12.10" });

        Assert.True(row.Installed);
        Assert.Equal("Python.Python.3.12", row.InstalledPackageId);
        Assert.Equal("3.12.3", row.Version);
        Assert.Equal("3.12.10", row.UpdateVersion);
        Assert.Equal("Update available", row.Status);
        Assert.Equal("Update to 3.12.10", row.ActionText);
    }

    [Fact]
    public void BuildRow_MatchesShortenedIdsInTheUpgradeTable()
    {
        var installed = new Dictionary<string, string> { ["Microsoft.VisualStudioCode"] = "1.142.0" };

        var row = WingetService.BuildRow(Tool("vscode"), installed, new Dictionary<string, string> { ["Microsoft.VisualStud…"] = "1.143.0" });

        Assert.Equal("1.143.0", row.UpdateVersion);
    }

    [Fact]
    public void BuildRow_DoesNotConfuseSimilarIds()
    {
        // An upgrade for the .NET 9 SDK must not be shown for the .NET 10 SDK.
        var installed = new Dictionary<string, string> { ["Microsoft.DotNet.SDK.10"] = "10.0.402" };

        var row = WingetService.BuildRow(Tool("dotnet"), installed, new Dictionary<string, string> { ["Microsoft.DotNet.SDK.9"] = "9.0.500" });

        Assert.True(row.Installed);
        Assert.Null(row.UpdateVersion);
        Assert.Equal("Installed", row.Status);
        Assert.False(row.CanAct);
    }

    [Fact]
    public void BuildRow_ToolsWithoutAWindowsPackageAreUnavailable()
    {
        var row = WingetService.BuildRow(Tool("build"), new Dictionary<string, string>(), new Dictionary<string, string>());

        Assert.False(row.Supported);
        Assert.Equal("Not available here", row.Status);
    }

    [Fact]
    public void Arguments_NeverPromptAndMatchExactly()
    {
        var install = WingetService.InstallArguments("Git.Git").ToList();

        Assert.Equal(["install", "--id", "Git.Git", "--exact"], install.Take(4));
        Assert.Contains("--silent", install);
        Assert.Contains("--accept-package-agreements", install);
        Assert.Contains("--disable-interactivity", install);
    }
}
