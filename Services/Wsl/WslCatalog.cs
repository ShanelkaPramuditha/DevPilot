using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevPilot.Models;
using Microsoft.Win32;

namespace DevPilot.Services.Wsl;

/// <summary>
/// Reads the WSL distribution catalog (the same file "wsl --list --online"
/// uses) and derives the metadata shown in the "Available" list.
/// </summary>
public static partial class WslCatalog
{
    private const string DefaultCatalogUrl =
        "https://raw.githubusercontent.com/microsoft/WSL/master/distributions/DistributionInfo.json";

    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly ConcurrentDictionary<string, long?> DownloadSizes = new();
    private static readonly SemaphoreSlim SizeRequests = new(6);

    /// <summary>
    /// Publishers by catalog group or distribution name prefix.
    /// </summary>
    private static readonly (string Prefix, string Publisher)[] Publishers =
    [
        ("Ubuntu", "Canonical"),
        ("openSUSE", "openSUSE Project"),
        ("SUSE", "SUSE"),
        ("RLCPlus", "CIQ"),
        ("kali", "OffSec"),
        ("Debian", "Debian Project"),
        ("AlmaLinux", "AlmaLinux OS Foundation"),
        ("archlinux", "Arch Linux"),
        ("Fedora", "Fedora Project"),
        ("eLxr", "eLxr Project"),
        ("OracleLinux", "Oracle"),
    ];

    /// <summary>
    /// Downloads the catalog. Entries are keyed by distribution name;
    /// modern (.wsl image) entries take precedence over legacy (Store) ones.
    /// </summary>
    public static async Task<Dictionary<string, CatalogEntry>> GetEntriesAsync()
    {
        string json = await Http.GetStringAsync(CatalogUrl());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var entries = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);

        // The distribution "wsl --install" picks when none is named.
        string? defaultName = GetString(root, "Default");

        if (root.TryGetProperty("ModernDistributions", out var modern))
        {
            foreach (var group in modern.EnumerateObject())
            {
                foreach (var item in group.Value.EnumerateArray())
                {
                    string name = GetString(item, "Name") ?? "";
                    string friendly = GetString(item, "FriendlyName") ?? name;
                    string? amd64 = GetUrl(item, "Amd64Url");
                    string? arm64 = GetUrl(item, "Arm64Url");

                    entries.TryAdd(name, new CatalogEntry(
                        Name: name,
                        FriendlyName: friendly,
                        Version: GetVersion(name, friendly, amd64 ?? arm64),
                        Publisher: GetPublisher(group.Name, name),
                        Amd64: amd64 is not null,
                        Arm64: arm64 is not null,
                        Amd64Url: amd64,
                        Arm64Url: arm64,
                        Support: GetSupport(name, friendly),
                        IsRecommended: name.Equals(defaultName, StringComparison.OrdinalIgnoreCase)));
                }
            }
        }

        if (root.TryGetProperty("Distributions", out var legacy))
        {
            foreach (var item in legacy.EnumerateArray())
            {
                string name = GetString(item, "Name") ?? "";
                string friendly = GetString(item, "FriendlyName") ?? name;
                string? amd64 = GetString(item, "Amd64PackageUrl");
                string? arm64 = GetString(item, "Arm64PackageUrl");
                string family = GetString(item, "PackageFamilyName") ?? "";

                entries.TryAdd(name, new CatalogEntry(
                    Name: name,
                    FriendlyName: friendly,
                    Version: GetVersion(name, friendly, null),
                    Publisher: GetPublisher(name, name, family),
                    Amd64: GetBool(item, "Amd64"),
                    Arm64: GetBool(item, "Arm64"),
                    Amd64Url: amd64,
                    Arm64Url: arm64,
                    Support: GetSupport(name, friendly),
                    IsRecommended: false));
            }
        }

        return entries;
    }

    /// <summary>
    /// Builds an entry for a distribution that "wsl --list --online" reports
    /// but the catalog file does not describe.
    /// </summary>
    public static CatalogEntry Fallback(string name, string friendlyName) => new(
        Name: name,
        FriendlyName: friendlyName,
        Version: GetVersion(name, friendlyName, null),
        Publisher: GetPublisher(name, name),
        Amd64: true,
        Arm64: false,
        Amd64Url: null,
        Arm64Url: null,
        Support: GetSupport(name, friendlyName),
        IsRecommended: false);

    /// <summary>
    /// Gets the size of a download without downloading it. Results are cached.
    /// </summary>
    public static async Task<long?> GetDownloadSizeAsync(string url)
    {
        if (DownloadSizes.TryGetValue(url, out long? cached))
        {
            return cached;
        }

        await SizeRequests.WaitAsync();
        try
        {
            long? size = await RequestSizeAsync(url);
            DownloadSizes[url] = size;
            return size;
        }
        finally
        {
            SizeRequests.Release();
        }
    }

    private static async Task<long?> RequestSizeAsync(string url)
    {
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await Http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength is > 0 and var length)
            {
                return length;
            }
        }
        catch
        {
            // Some servers reject HEAD; try a one-byte range request below.
        }

        try
        {
            using var get = new HttpRequestMessage(HttpMethod.Get, url);
            get.Headers.Range = new RangeHeaderValue(0, 0);
            using var response = await Http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead);
            return response.Content.Headers.ContentRange?.Length;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// WSL lets administrators point the catalog at another URL.
    /// </summary>
    private static string CatalogUrl()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Lxss");
            if (key?.GetValue("DistributionListUrl") is string url && Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                return url;
            }
        }
        catch
        {
            // Use the default catalog.
        }

        return DefaultCatalogUrl;
    }

    private static string GetVersion(string name, string friendlyName, string? url)
    {
        // "Ubuntu 24.04 LTS", "Fedora Linux 44", "SUSE Linux Enterprise 15 SP7"
        var match = FriendlyVersion().Match(friendlyName);
        if (match.Success)
        {
            return match.Value.Trim();
        }

        match = NameVersion().Match(name);
        if (match.Success)
        {
            return match.Groups[1].Value.Replace('_', '.');
        }

        // Unversioned entries such as "Ubuntu" or "Kali Linux Rolling":
        // take the release or snapshot from the image file name.
        if (url is not null)
        {
            string file = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Segments[^1] : url;

            match = UrlVersion().Match(file);
            if (match.Success)
            {
                return match.Value;
            }

            match = UrlSnapshot().Match(file);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return GetSupport(name, friendlyName) == "Rolling" ? "Rolling" : "Latest";
    }

    private static string GetPublisher(string group, string name, string? packageFamily = null)
    {
        foreach (var (prefix, publisher) in Publishers)
        {
            if (group.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return publisher;
            }
        }

        // Store packages: "CanonicalGroupLimited.Ubuntu_79rhkp1fndgsc"
        if (!string.IsNullOrEmpty(packageFamily) && packageFamily.Contains('.'))
        {
            return packageFamily.Split('.')[0];
        }

        return group;
    }

    private static string GetSupport(string name, string friendlyName)
    {
        string text = $"{name} {friendlyName}";

        bool Has(string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);

        if (Has("LTS") || name.Equals("Ubuntu", StringComparison.OrdinalIgnoreCase))
        {
            return "LTS";
        }

        if (Has("Rolling") || Has("Tumbleweed") || Has("archlinux") || Has("kali"))
        {
            return "Rolling";
        }

        if (Has("Kitten"))
        {
            return "Development";
        }

        if (Has("Enterprise") || Has("RLCPlus") || Has("Oracle") || Has("AlmaLinux"))
        {
            return "Enterprise LTS";
        }

        return "Stable";
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static string? GetUrl(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? GetString(value, "Url")
            : null;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DevPilot/1.0");
        return client;
    }

    [GeneratedRegex(@"\d+(\.\d+)*( SP\d+)?")]
    private static partial Regex FriendlyVersion();

    [GeneratedRegex(@"[-_](\d+([._]\d+)*)$")]
    private static partial Regex NameVersion();

    [GeneratedRegex(@"(?<![\d.])\d{2,4}\.\d+(\.\d+)?")]
    private static partial Regex UrlVersion();

    [GeneratedRegex(@"-(\d{8})\.")]
    private static partial Regex UrlSnapshot();
}
