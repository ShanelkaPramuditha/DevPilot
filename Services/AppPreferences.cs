using System.IO;
using System.Text.Json;
using DevPilot.Theming;

namespace DevPilot.Services;

/// <summary>
/// User preferences saved between runs in %LocalAppData%\DevPilot\settings.json.
/// </summary>
public sealed class AppPreferences
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }

    public bool SidebarCollapsed { get; set; }

    /// <summary>Closing the window keeps DevPilot running in the notification area.</summary>
    public bool CloseToTray { get; set; }

    /// <summary>Check GitHub for a newer version when DevPilot starts (at most every few hours).</summary>
    public bool CheckForUpdatesAutomatically { get; set; } = true;

    /// <summary>Also offer pre-releases (beta, rc). Always on while a pre-release is installed.</summary>
    public bool IncludePreReleases { get; set; }

    public DateTimeOffset? LastUpdateCheck { get; set; }

    /// <summary>A version the user chose to skip; automatic checks do not offer it again.</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>SSH config file chosen by the user; null means the default %UserProfile%\.ssh\config.</summary>
    public string? SshConfigPath { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevPilot", "settings.json");

    public static AppPreferences Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(FilePath), JsonOptions) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Preferences are a convenience; failing to save them is not an error.
        }
    }
}
