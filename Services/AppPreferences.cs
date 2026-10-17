using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevPilot.Theming;

namespace DevPilot.Services;

/// <summary>How DevPilot appears when it starts with Windows.</summary>
public enum StartupWindowMode
{
    /// <summary>Only the notification area icon; the window opens from there.</summary>
    NotificationArea,

    /// <summary>A minimized window on the taskbar.</summary>
    Taskbar,

    /// <summary>The window opens normally.</summary>
    Window
}

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

    /// <summary>Minimizing the window hides it in the notification area instead of the taskbar.</summary>
    public bool MinimizeToTray { get; set; }

    /// <summary>How DevPilot appears when Windows starts it; null for settings saved before this option existed.</summary>
    public StartupWindowMode? StartupWindow { get; set; }

    /// <summary>The startup window mode, defaulting to the old behaviour: hidden when the tray icon is on.</summary>
    [JsonIgnore]
    public StartupWindowMode EffectiveStartupWindow =>
        StartupWindow ?? (CloseToTray ? StartupWindowMode.NotificationArea : StartupWindowMode.Taskbar);

    /// <summary>The page DevPilot opens on (a page name such as "UsageTab"); null opens the page used last.</summary>
    public string? StartPage { get; set; }

    /// <summary>The page that was open when DevPilot closed.</summary>
    public string? LastPage { get; set; }

    /// <summary>Show a notification (or flash the taskbar button) when long work finishes in the background.</summary>
    public bool NotifyWhenDone { get; set; } = true;

    /// <summary>Distributions DevPilot keeps running for remote access (also after starting with Windows).</summary>
    public List<string> RemoteKeepRunning { get; set; } = [];

    /// <summary>Keep Windows from sleeping while someone is connected to a distribution over SSH.</summary>
    public bool RemoteKeepAwake { get; set; }

    /// <summary>Check GitHub for a newer version when DevPilot starts (at most every few hours).</summary>
    public bool CheckForUpdatesAutomatically { get; set; } = true;

    /// <summary>Also offer pre-releases (beta, rc). Always on while a pre-release is installed.</summary>
    public bool IncludePreReleases { get; set; }

    public DateTimeOffset? LastUpdateCheck { get; set; }

    /// <summary>
    /// The newer version the last check found (null when up to date), so the next start shows
    /// it again even when the next check is not due yet.
    /// </summary>
    public string? AvailableVersion { get; set; }

    /// <summary>A version the user chose to skip; automatic checks do not offer it again.</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>SSH config file chosen by the user; null means the default %UserProfile%\.ssh\config.</summary>
    public string? SshConfigPath { get; set; }

    /// <summary>Puts the choices on the Settings page back to their defaults (not window size or saved data).</summary>
    public void ResetToDefaults()
    {
        var defaults = new AppPreferences();
        Theme = defaults.Theme;
        SidebarCollapsed = defaults.SidebarCollapsed;
        CloseToTray = defaults.CloseToTray;
        MinimizeToTray = defaults.MinimizeToTray;
        StartupWindow = defaults.StartupWindow;
        StartPage = defaults.StartPage;
        NotifyWhenDone = defaults.NotifyWhenDone;
        RemoteKeepAwake = defaults.RemoteKeepAwake;
        CheckForUpdatesAutomatically = defaults.CheckForUpdatesAutomatically;
        IncludePreReleases = defaults.IncludePreReleases;
    }

    public static string FolderPath => Path.GetDirectoryName(FilePath)!;

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
