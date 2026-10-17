using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevPilot.Models;
using DevPilot.Services.Platform;

namespace DevPilot.Services.Tools;

/// <summary>
/// Installs and updates tools on Windows with winget (the Windows Package Manager).
/// </summary>
public static class WingetService
{
    private static readonly string[] CommonArguments = ["--accept-source-agreements", "--disable-interactivity"];

    /// <summary>winget.exe, or null when the App Installer (which provides winget) is missing.</summary>
    public static string? WingetExe
    {
        get
        {
            string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");
            if (File.Exists(apps))
            {
                return apps;
            }

            return Environment.GetEnvironmentVariable("PATH")?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(dir => Path.Combine(dir, "winget.exe"))
                .FirstOrDefault(File.Exists);
        }
    }

    public const string MissingMessage =
        "winget (the Windows Package Manager) was not found. Install or update \"App Installer\" from the Microsoft Store.";

    /// <summary>The state of every Windows tool: installed version, and newer version if any.</summary>
    public static async Task<List<ToolRow>> GetStatusAsync(IEnumerable<DevTool> tools, CancellationToken cancellationToken = default)
    {
        string winget = WingetExe ?? throw new InvalidOperationException(MissingMessage);

        // "winget export" lists installed packages with exact ids and versions as JSON.
        string file = Path.Combine(Path.GetTempPath(), $"devpilot-winget-{Environment.ProcessId}.json");
        try
        {
            await ProcessRunner.RunAsync(winget, ["export", "--output", file, "--include-versions", .. CommonArguments], _ => { }, cancellationToken);
            var installed = File.Exists(file) ? ParseExport(await File.ReadAllTextAsync(file, cancellationToken)) : [];

            var lines = new List<string>();
            await ProcessRunner.RunAsync(winget, ["upgrade", "--include-unknown", .. CommonArguments], lines.Add, cancellationToken);
            var upgrades = ParseUpgradeTable(lines);

            return tools.Select(tool => BuildRow(tool, installed, upgrades)).ToList();
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static ToolRow BuildRow(DevTool tool, IReadOnlyDictionary<string, string> installed, IReadOnlyDictionary<string, string> upgrades)
    {
        if (tool.Windows is not { } package)
        {
            return new ToolRow(tool) { Supported = false, Note = "Not offered for Windows; install it inside a WSL distribution." };
        }

        var match = installed.Keys
            .Where(id => Regex.IsMatch(id, package.InstalledPattern, RegexOptions.IgnoreCase))
            .OrderByDescending(id => id.Equals(package.InstallId, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(id => id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return new ToolRow(tool)
        {
            Supported = true,
            Installed = match is not null,
            InstalledPackageId = match,
            Version = match is null ? null : installed[match] is { Length: > 0 } version ? version : null,
            UpdateVersion = match is null ? null : FindUpgrade(upgrades, match)
        };
    }

    /// <summary>The upgrade for a package; ids winget shortened ("Microsoft.VisualStud…") match by prefix.</summary>
    private static string? FindUpgrade(IReadOnlyDictionary<string, string> upgrades, string packageId)
    {
        if (upgrades.TryGetValue(packageId, out string? version))
        {
            return version;
        }

        return upgrades
            .Where(u => u.Key.EndsWith('…') && packageId.StartsWith(u.Key[..^1], StringComparison.OrdinalIgnoreCase))
            .Select(u => u.Value)
            .FirstOrDefault();
    }

    /// <summary>Package id → version, from "winget export --include-versions".</summary>
    public static Dictionary<string, string> ParseExport(string json)
    {
        var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Sources", out var sources))
        {
            return packages;
        }

        foreach (var source in sources.EnumerateArray())
        {
            if (!source.TryGetProperty("Packages", out var list))
            {
                continue;
            }

            foreach (var package in list.EnumerateArray())
            {
                if (package.TryGetProperty("PackageIdentifier", out var id) && id.GetString() is { Length: > 0 } name)
                {
                    packages[name] = package.TryGetProperty("Version", out var version) ? version.GetString() ?? "" : "";
                }
            }
        }

        return packages;
    }

    /// <summary>
    /// Package id → available version, from the table "winget upgrade" prints. Columns are
    /// found from the header ("Name  Id  Version  Available  Source"); ids winget shortened
    /// keep their "…".
    /// </summary>
    public static Dictionary<string, string> ParseUpgradeTable(IEnumerable<string> lines)
    {
        var upgrades = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int idColumn = -1, versionColumn = -1, availableColumn = -1, sourceColumn = -1;
        bool inTable = false;

        foreach (string line in lines)
        {
            if (!inTable)
            {
                var header = Regex.Match(line, @"^(?<name>\S.*?)\s+(?<id>Id)\s+(?<version>Version)\s+(?<available>Available)(\s+(?<source>Source))?\s*$");
                if (header.Success)
                {
                    idColumn = header.Groups["id"].Index;
                    versionColumn = header.Groups["version"].Index;
                    availableColumn = header.Groups["available"].Index;
                    sourceColumn = header.Groups["source"].Success ? header.Groups["source"].Index : -1;
                }
                else if (idColumn >= 0 && line.StartsWith("---", StringComparison.Ordinal))
                {
                    inTable = true;
                }

                continue;
            }

            // The table ends at the first line that is too short, such as "9 upgrades available."
            if (line.Length <= availableColumn)
            {
                break;
            }

            string id = Column(line, idColumn, versionColumn);
            string available = Column(line, availableColumn, sourceColumn > 0 ? sourceColumn : line.Length);
            if (id.Length > 0 && available.Length > 0)
            {
                upgrades[id] = available;
            }
        }

        return upgrades;
    }

    public static IEnumerable<string> InstallArguments(string packageId) =>
        ["install", "--id", packageId, "--exact", "--source", "winget", "--silent", "--accept-package-agreements", .. CommonArguments];

    public static IEnumerable<string> UpgradeArguments(string packageId) =>
        ["upgrade", "--id", packageId, "--exact", "--silent", "--accept-package-agreements", .. CommonArguments];

    public static IEnumerable<string> UninstallArguments(string packageId) =>
        ["uninstall", "--id", packageId, "--exact", "--silent", .. CommonArguments];

    private static string Column(string line, int start, int end)
    {
        if (start >= line.Length)
        {
            return "";
        }

        return line[start..Math.Min(end, line.Length)].Trim();
    }
}
