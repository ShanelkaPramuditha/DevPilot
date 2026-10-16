using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevPilot.Core;
using DevPilot.Models;
using Microsoft.Win32;

namespace DevPilot.Services.Updates;

/// <summary>
/// Checks GitHub Releases for a newer DevPilot, downloads the setup for this PC's
/// architecture, verifies it against the release's SHA256SUMS.txt and starts it.
/// </summary>
public static partial class UpdateService
{
    /// <summary>The setup's AppId (installer/DevPilot.iss); Inno Setup registers the uninstaller under it.</summary>
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{47391CDA-BB77-4575-9FB8-506AA03AACCE}_is1";

    // Created on first use: its User-Agent needs CurrentVersion, which a field initialised
    // here would read before it is set (static initialisers run top to bottom).
    private static readonly Lazy<HttpClient> Http = new(CreateHttpClient);

    /// <summary>The running version, from the release tag the build was made from (1.0.0 for local builds).</summary>
    public static SemanticVersion CurrentVersion { get; } =
        SemanticVersion.TryParse(Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? new SemanticVersion(0, 0, 0, "");

    /// <summary>The GitHub repository releases come from, set at build time (RepositoryUrl in Directory.Build.props).</summary>
    public static string? Repository { get; } = ParseRepository(
        Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value);

    public static bool IsConfigured => Repository is not null;

    /// <summary>"x64" or "arm64": the architecture of the running app, which the update keeps.</summary>
    public static string Architecture => RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";

    public static string ReleasesPageUrl => $"https://github.com/{Repository}/releases";

    /// <summary>
    /// Returns the newest release that is newer than the running version, or null when up to date.
    /// Pre-releases are only offered when asked for, or when a pre-release is running already.
    /// </summary>
    public static async Task<ReleaseInfo?> CheckAsync(bool includePreReleases, CancellationToken cancellationToken = default)
    {
        if (Repository is null)
        {
            throw new InvalidOperationException("Update checks are not set up for this build (no repository URL).");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases?per_page=30");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Http.Value.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        return SelectUpdate(ParseReleases(json), CurrentVersion, includePreReleases || CurrentVersion.IsPreRelease);
    }

    public static ReleaseInfo? SelectUpdate(IEnumerable<ReleaseInfo> releases, SemanticVersion current, bool includePreReleases) =>
        releases
            .Where(r => includePreReleases || !r.IsPreRelease)
            .Where(r => r.Version > current)
            .OrderByDescending(r => r.Version)
            .FirstOrDefault();

    /// <summary>Reads the GitHub "list releases" response; drafts and tags that are not versions are skipped.</summary>
    public static List<ReleaseInfo> ParseReleases(string json)
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<ReleaseInfo>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (Bool(item, "draft"))
            {
                continue;
            }

            string tag = Text(item, "tag_name");
            if (SemanticVersion.TryParse(tag) is not { } version)
            {
                continue;
            }

            var assets = item.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray()
                    .Select(a => new ReleaseAsset(
                        Text(a, "name"),
                        Text(a, "browser_download_url"),
                        a.TryGetProperty("size", out var size) && size.TryGetInt64(out long bytes) ? bytes : 0))
                    .ToList()
                : [];

            releases.Add(new ReleaseInfo(
                version,
                tag,
                Text(item, "name") is { Length: > 0 } title ? title : $"DevPilot {version}",
                Text(item, "body"),
                Text(item, "html_url"),
                DateTimeOffset.TryParse(Text(item, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published)
                    ? published : null,
                Bool(item, "prerelease") || version.IsPreRelease,
                assets));
        }

        return releases;
    }

    /// <summary>The setup for this PC's architecture, for example DevPilot-1.5.0-win-x64-setup.exe.</summary>
    public static ReleaseAsset? FindInstaller(ReleaseInfo release, string architecture) =>
        release.Assets.FirstOrDefault(a => a.Name.EndsWith($"-win-{architecture}-setup.exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>Looks up a file's hash in a SHA256SUMS.txt ("&lt;hash&gt;  &lt;name&gt;" or "&lt;hash&gt; *&lt;name&gt;" per line).</summary>
    public static string? FindHash(string sums, string fileName)
    {
        foreach (string line in sums.Split('\n'))
        {
            var match = SumsLine().Match(line.Trim());
            if (match.Success && match.Groups[2].Value.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups[1].Value.ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// Downloads the setup and checks it against SHA256SUMS.txt from the same release.
    /// Returns the path of the verified file; throws if it cannot be verified.
    /// </summary>
    public static async Task<string> DownloadInstallerAsync(ReleaseInfo release, IProgress<(long Received, long Total)> progress, CancellationToken cancellationToken)
    {
        var installer = FindInstaller(release, Architecture)
            ?? throw new InvalidOperationException($"This release has no {Architecture} installer.");
        var sumsAsset = release.Assets.FirstOrDefault(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("This release has no SHA256SUMS.txt, so the download cannot be verified.");

        string sums = await Http.Value.GetStringAsync(sumsAsset.DownloadUrl, cancellationToken);
        string expected = FindHash(sums, installer.Name)
            ?? throw new InvalidOperationException($"SHA256SUMS.txt has no entry for {installer.Name}.");

        string folder = Path.Combine(Path.GetTempPath(), "DevPilot-update");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, installer.Name);

        using (var response = await Http.Value.GetAsync(installer.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? installer.Size;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(path);
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                progress.Report((received, total));
            }
        }

        string actual;
        await using (var file = File.OpenRead(path))
        {
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
        }

        if (actual != expected)
        {
            File.Delete(path);
            throw new InvalidOperationException("The downloaded installer does not match its published checksum and was deleted. Please try again.");
        }

        return path;
    }

    /// <summary>
    /// Runs the verified setup silently (it shows its progress) in the same install scope,
    /// and asks it to start DevPilot again when done. The caller should then exit.
    /// </summary>
    public static void StartInstaller(string installerPath, InstallKind kind)
    {
        string scope = kind == InstallKind.AllUsers ? "/ALLUSERS" : "/CURRENTUSER";
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = $"/SILENT /SUPPRESSMSGBOXES /NORESTART /LAUNCH {scope}",
            // Shell execute so Windows can ask for admin rights for an all-users installation.
            UseShellExecute = true
        });
    }

    /// <summary>
    /// Finds out whether this copy was installed by the setup (and for whom) or runs portable.
    /// </summary>
    public static InstallKind DetectInstallKind()
    {
        string running = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        foreach (var (hive, kind) in new[] { (Registry.CurrentUser, InstallKind.CurrentUser), (Registry.LocalMachine, InstallKind.AllUsers) })
        {
            try
            {
                using var key = hive.OpenSubKey(UninstallKey);
                if (key?.GetValue("InstallLocation") is string location
                    && Path.TrimEndingDirectorySeparator(location).Equals(running, StringComparison.OrdinalIgnoreCase))
                {
                    return kind;
                }
            }
            catch
            {
                // Unreadable registry: treat as not installed there.
            }
        }

        return InstallKind.Portable;
    }

    /// <summary>
    /// Turns the release notes (Markdown) into readable plain text for the update window.
    /// The download table at the top is left out: the app picks the right file itself.
    /// </summary>
    public static string CleanNotes(string markdown)
    {
        string text = markdown.Replace("\r\n", "\n");
        text = DownloadSection().Replace(text, "");
        text = Link().Replace(text, "$1");
        text = text.Replace("**", "").Replace("`", "");
        text = Heading().Replace(text, "");
        text = Bullet().Replace(text, "• ");
        text = ExtraBlankLines().Replace(text, "\n\n");
        return text.Trim();
    }

    private static string? ParseRepository(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var match = GitHubUrl().Match(url.Trim());
        return match.Success ? $"{match.Groups[1].Value}/{match.Groups[2].Value}" : null;
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool Bool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub's API requires a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"DevPilot/{CurrentVersion}");
        return client;
    }

    [GeneratedRegex(@"^https?://github\.com/([^/\s]+)/([^/\s]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubUrl();

    [GeneratedRegex(@"^([0-9a-fA-F]{64})\s+\*?(.+)$")]
    private static partial Regex SumsLine();

    [GeneratedRegex(@"(?ms)^## Download\b.*?(?=^## |\z)")]
    private static partial Regex DownloadSection();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?m)^#{1,6}\s*")]
    private static partial Regex Heading();

    [GeneratedRegex(@"(?m)^\s*[*-]\s+")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLines();
}
