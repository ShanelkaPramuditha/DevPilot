using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using DevPilot.Services.Updates;

namespace DevPilot.Services.Wsl;

/// <summary>
/// Finds out whether a newer WSL exists: the latest release of microsoft/WSL on GitHub
/// (what "wsl --update" installs) against the installed "wsl --version".
/// </summary>
public static class WslUpdateCheck
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/microsoft/WSL/releases/latest";

    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"DevPilot/{UpdateService.CurrentVersion}");
        return client;
    });

    /// <summary>The latest stable WSL version, or null when GitHub cannot be reached.</summary>
    public static async Task<Version?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.Value.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.TryGetProperty("tag_name", out var tag) ? ParseVersion(tag.GetString()) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// "2.6.1", "v2.6.1" or "3.0.3.0" as a four-part version, so "3.0.3" and "3.0.3.0" compare as equal.
    /// </summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Trim().TrimStart('v', 'V').Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return null;
        }

        var numbers = new int[4];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    /// <summary>The "WSL version: 3.0.3.0" line of "wsl --version" (in the details DevPilot shows).</summary>
    public static Version? ParseInstalledVersion(string details) =>
        details.Split(["   ·   ", "\n"], StringSplitOptions.TrimEntries)
            .Where(p => p.StartsWith("WSL version:", StringComparison.OrdinalIgnoreCase))
            .Select(p => ParseVersion(p.Split(':', 2)[1]))
            .FirstOrDefault();

    public static bool IsUpdateAvailable(Version? installed, Version? latest) =>
        installed is not null && latest is not null && latest > installed;
}
