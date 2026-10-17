using System.Text;
using System.Text.RegularExpressions;
using DevPilot.Models;
using DevPilot.Services.Platform;
using DevPilot.Services.Wsl;

namespace DevPilot.Services.Tools;

/// <summary>
/// Installs, updates and removes tools inside a WSL distribution with its own package
/// manager (apt-get, dnf, pacman or zypper). Commands run as root through wsl.exe, so no
/// password is asked.
/// </summary>
public static partial class WslToolService
{
    public static readonly IReadOnlyList<string> SupportedPackageManagers = ["apt-get", "dnf", "pacman", "zypper"];

    /// <summary>Reads the package manager and the installed tools of a distribution.</summary>
    public static async Task<(DistroToolInfo Info, List<ToolRow> Rows)> GetStatusAsync(string distro, IReadOnlyList<DevTool> tools, CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        int exit = await ProcessRunner.RunAsync(WslService.WslPath, ["--distribution", distro, "--user", "root", "--exec", "sh", "-s"],
            lines.Add, cancellationToken, BuildStatusScript(tools), Utf8Output);
        if (exit != 0 && lines.Count == 0)
        {
            throw new InvalidOperationException($"Could not read {distro} (wsl.exe exited with code {exit}).");
        }

        // The default user (for docker group membership) is whoever wsl.exe logs in as.
        var userLines = new List<string>();
        await ProcessRunner.RunAsync(WslService.WslPath, ["--distribution", distro, "--exec", "id", "-un"], userLines.Add, cancellationToken, environment: Utf8Output);

        return ParseStatus(lines, tools, userLines.FirstOrDefault() ?? "root");
    }

    /// <summary>Prints "pm|...", "os|...", "systemd|yes|no" and "tool|id|resolved path|version line" lines.</summary>
    public static string BuildStatusScript(IEnumerable<DevTool> tools)
    {
        var script = new StringBuilder("""
            . /etc/os-release 2>/dev/null
            pm=none
            for c in apt-get dnf pacman zypper; do
                if command -v "$c" >/dev/null 2>&1; then pm=$c; break; fi
            done
            echo "pm|$pm"
            echo "os|${PRETTY_NAME:-Linux}"
            echo "systemd|$([ -d /run/systemd/system ] && echo yes || echo no)"
            check() {
                id=$1; cmd=$2; shift 2
                p=$(command -v "$cmd" 2>/dev/null)
                if [ -n "$p" ]; then
                    v=$("$cmd" "$@" 2>&1 | head -n 1)
                    echo "tool|$id|$(readlink -f "$p")|$v"
                else
                    echo "tool|$id||"
                fi
            }

            """);

        foreach (var tool in tools.Where(t => t.Wsl is not null))
        {
            script.Append(CultureInvariant($"check {tool.Id} {tool.Wsl!.Command} {tool.Wsl.VersionArguments}\n"));
        }

        return script.ToString();
    }

    public static (DistroToolInfo Info, List<ToolRow> Rows) ParseStatus(IEnumerable<string> lines, IReadOnlyList<DevTool> tools, string defaultUser)
    {
        string pm = "none", os = "Linux";
        bool systemd = false;
        var found = new Dictionary<string, (string Path, string Version)>();

        foreach (string line in lines)
        {
            string[] parts = line.Split('|', 4);
            switch (parts[0])
            {
                case "pm" when parts.Length > 1:
                    pm = parts[1].Trim();
                    break;
                case "os" when parts.Length > 1:
                    os = parts[1].Trim();
                    break;
                case "systemd" when parts.Length > 1:
                    systemd = parts[1].Trim() == "yes";
                    break;
                case "tool" when parts.Length == 4 && parts[2].Length > 0:
                    found[parts[1]] = (parts[2], parts[3]);
                    break;
            }
        }

        var info = new DistroToolInfo(pm, os, systemd, defaultUser);
        var rows = tools.Select(tool =>
        {
            if (tool.Wsl is not { } package)
            {
                return new ToolRow(tool) { Supported = false, Note = tool.WslNote };
            }

            if (!package.Packages.ContainsKey(pm))
            {
                return new ToolRow(tool)
                {
                    Supported = false,
                    Note = pm == "none"
                        ? "This distribution has no supported package manager (apt, dnf, pacman or zypper)."
                        : $"Not packaged for {pm} distributions."
                };
            }

            if (!found.TryGetValue(tool.Id, out var installed))
            {
                return new ToolRow(tool) { Supported = true, Note = tool.WslNote };
            }

            // Docker Desktop's WSL integration links its own docker into the distribution.
            string? elsewhere = installed.Path.Contains("docker-desktop", StringComparison.OrdinalIgnoreCase)
                ? "Provided by Docker Desktop's WSL integration. Manage it in Docker Desktop."
                : null;

            return new ToolRow(tool)
            {
                Supported = true,
                Installed = true,
                Version = ExtractVersion(installed.Version),
                ManagedElsewhere = elsewhere,
                Note = tool.WslNote
            };
        }).ToList();

        return (info, rows);
    }

    /// <summary>"git version 2.53.0" → "2.53.0"; "go version go1.24.1 linux/amd64" → "1.24.1".</summary>
    public static string? ExtractVersion(string versionLine)
    {
        var match = VersionNumber().Match(versionLine);
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// The shell script for an action. Runs as root and never prompts; $1 is the
    /// distribution's default user (used by post-install steps).
    /// </summary>
    public static string BuildActionScript(DevTool tool, ToolAction action, string packageManager)
    {
        var package = tool.Wsl ?? throw new InvalidOperationException($"{tool.Name} is not offered for WSL.");
        string packages = package.Packages.TryGetValue(packageManager, out string? names)
            ? names
            : throw new InvalidOperationException($"{tool.Name} is not packaged for {packageManager}.");
        string? optional = package.OptionalPackages?.GetValueOrDefault(packageManager);

        var script = new StringBuilder("set -e\n");
        string refresh = packageManager switch
        {
            "apt-get" => "export DEBIAN_FRONTEND=noninteractive\napt-get update\n",
            _ => ""
        };

        switch (action)
        {
            case ToolAction.Install:
                script.Append(refresh);
                script.Append(CultureInvariant($"{InstallCommand(packageManager)} {packages}\n"));
                if (optional is not null)
                {
                    // Optional extras: not every release has them, so failures are not fatal.
                    script.Append(CultureInvariant($"{InstallCommand(packageManager)} {optional} || echo \"Skipped optional packages: {optional}\"\n"));
                }

                if (package.PostInstall is not null)
                {
                    script.Append(package.PostInstall.ReplaceLineEndings("\n").TrimEnd('\n')).Append('\n');
                }

                break;

            case ToolAction.Update:
                script.Append(refresh);
                script.Append(CultureInvariant($"{UpdateCommand(packageManager)} {packages}\n"));
                break;

            case ToolAction.Uninstall:
                script.Append(refresh.Replace("apt-get update\n", "", StringComparison.Ordinal));
                script.Append(CultureInvariant($"{RemoveCommand(packageManager)} {packages}{(optional is null ? "" : " " + optional)}\n"));
                break;
        }

        script.Append("echo \"Done.\"\n");
        return script.ToString();
    }

    /// <summary>Runs an action script, streaming its output.</summary>
    public static Task<int> RunAsync(string distro, string script, string defaultUser, Action<string> onLine, CancellationToken cancellationToken) =>
        ProcessRunner.RunAsync(WslService.WslPath, ["--distribution", distro, "--user", "root", "--exec", "sh", "-s", "--", defaultUser],
            onLine, cancellationToken, script, Utf8Output);

    private static string InstallCommand(string pm) => pm switch
    {
        "apt-get" => "apt-get install -y",
        "dnf" => "dnf install -y",
        "pacman" => "pacman -Sy --noconfirm --needed",
        "zypper" => "zypper --non-interactive install",
        _ => throw new InvalidOperationException($"Unsupported package manager: {pm}")
    };

    private static string UpdateCommand(string pm) => pm switch
    {
        "apt-get" => "apt-get install -y --only-upgrade",
        "dnf" => "dnf upgrade -y",
        "pacman" => "pacman -Sy --noconfirm",
        "zypper" => "zypper --non-interactive update",
        _ => throw new InvalidOperationException($"Unsupported package manager: {pm}")
    };

    private static string RemoveCommand(string pm) => pm switch
    {
        "apt-get" => "apt-get remove -y",
        "dnf" => "dnf remove -y",
        "pacman" => "pacman -R --noconfirm",
        "zypper" => "zypper --non-interactive remove",
        _ => throw new InvalidOperationException($"Unsupported package manager: {pm}")
    };

    private static readonly Dictionary<string, string> Utf8Output = new() { ["WSL_UTF8"] = "1" };

    private static string CultureInvariant(FormattableString text) => FormattableString.Invariant(text);

    [GeneratedRegex(@"\d+(\.\d+)+")]
    private static partial Regex VersionNumber();
}
