using DevPilot.Models;
using DevPilot.Services.Tools;

namespace DevPilot.Tests.Services;

public class WslToolServiceTests
{
    private static DevTool Tool(string id) => ToolCatalog.Tools.Single(t => t.Id == id);

    [Fact]
    public void ParseStatus_ReadsTheDistributionAndInstalledTools()
    {
        string[] output =
        [
            "pm|apt-get",
            "os|Ubuntu 26.04.1 LTS",
            "systemd|yes",
            "tool|git|/usr/bin/git|git version 2.53.0",
            "tool|python|/usr/bin/python3.14|Python 3.14.4",
            "tool|docker|/mnt/wsl/docker-desktop/cli-tools/usr/bin/docker|Docker version 28.1.1, build 4eba377",
            "tool|node||",
        ];

        var (info, rows) = WslToolService.ParseStatus(output, ToolCatalog.Tools, "dev");

        Assert.Equal(new DistroToolInfo("apt-get", "Ubuntu 26.04.1 LTS", true, "dev"), info);

        var git = rows.Single(r => r.Tool.Id == "git");
        Assert.True(git.Installed);
        Assert.Equal("2.53.0", git.Version);
        Assert.Equal("Install", rows.Single(r => r.Tool.Id == "node").ActionText);

        // Docker linked in by Docker Desktop is shown, but left to Docker Desktop.
        var docker = rows.Single(r => r.Tool.Id == "docker");
        Assert.True(docker.Installed);
        Assert.NotNull(docker.ManagedElsewhere);
        Assert.False(docker.CanUninstall);

        // Windows-only tools explain themselves.
        var vscode = rows.Single(r => r.Tool.Id == "vscode");
        Assert.False(vscode.Supported);
        Assert.Contains("Windows", vscode.Note);
    }

    [Fact]
    public void ParseStatus_ToolsNotPackagedForTheDistributionAreUnavailable()
    {
        var (_, rows) = WslToolService.ParseStatus(["pm|zypper", "os|openSUSE Tumbleweed", "systemd|no"], ToolCatalog.Tools, "dev");

        var dotnet = rows.Single(r => r.Tool.Id == "dotnet");
        Assert.False(dotnet.Supported);
        Assert.Contains("zypper", dotnet.Note);
        Assert.True(rows.Single(r => r.Tool.Id == "git").Supported);
    }

    [Fact]
    public void ParseStatus_WithoutAPackageManager()
    {
        var (info, rows) = WslToolService.ParseStatus(["pm|none", "os|Alpine Linux"], ToolCatalog.Tools, "root");

        Assert.Equal("none", info.PackageManager);
        Assert.All(rows, r => Assert.False(r.Supported));
    }

    [Theory]
    [InlineData("git version 2.53.0", "2.53.0")]
    [InlineData("go version go1.24.1 linux/amd64", "1.24.1")]
    [InlineData("openjdk version \"21.0.8\" 2025-07-15", "21.0.8")]
    [InlineData("v22.17.0", "22.17.0")]
    [InlineData("command not found", null)]
    public void ExtractVersion_FindsTheVersionNumber(string line, string? expected)
    {
        Assert.Equal(expected, WslToolService.ExtractVersion(line));
    }

    [Fact]
    public void BuildActionScript_InstallWithAptRunsUnattended()
    {
        string script = WslToolService.BuildActionScript(Tool("docker"), ToolAction.Install, "apt-get");

        Assert.StartsWith("set -e\nexport DEBIAN_FRONTEND=noninteractive\napt-get update\napt-get install -y docker.io\n", script);
        // Compose is optional: older releases do not have the package.
        Assert.Contains("apt-get install -y docker-compose-v2 || echo", script);
        // Post-install: docker group for the default user ($1) and the service.
        Assert.Contains("usermod -aG docker \"$1\"", script);
        Assert.Contains("systemctl enable --now docker", script);
        Assert.EndsWith("echo \"Done.\"\n", script);
    }

    [Theory]
    [InlineData("dnf", ToolAction.Install, "dnf install -y git")]
    [InlineData("pacman", ToolAction.Install, "pacman -Sy --noconfirm --needed git")]
    [InlineData("zypper", ToolAction.Update, "zypper --non-interactive update git")]
    [InlineData("apt-get", ToolAction.Update, "apt-get install -y --only-upgrade git")]
    [InlineData("apt-get", ToolAction.Uninstall, "apt-get remove -y git")]
    public void BuildActionScript_UsesThePackageManagersCommands(string packageManager, ToolAction action, string expected)
    {
        Assert.Contains(expected + "\n", WslToolService.BuildActionScript(Tool("git"), action, packageManager));
    }

    [Fact]
    public void BuildActionScript_RefusesToolsNotPackagedForTheDistribution()
    {
        Assert.Throws<InvalidOperationException>(() => WslToolService.BuildActionScript(Tool("dotnet"), ToolAction.Install, "zypper"));
        Assert.Throws<InvalidOperationException>(() => WslToolService.BuildActionScript(Tool("vscode"), ToolAction.Install, "apt-get"));
    }

    [Fact]
    public void Catalog_EveryWslToolHasAStatusCheck()
    {
        string script = WslToolService.BuildStatusScript(ToolCatalog.Tools);

        foreach (var tool in ToolCatalog.Tools.Where(t => t.Wsl is not null))
        {
            Assert.Contains($"check {tool.Id} {tool.Wsl!.Command} ", script);
        }
    }
}
