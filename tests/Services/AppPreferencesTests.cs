using System.Text.Json;
using System.Text.Json.Serialization;
using DevPilot.Services;
using DevPilot.Theming;

namespace DevPilot.Tests.Services;

public class AppPreferencesTests
{
    [Theory]
    [InlineData(true, StartupWindowMode.NotificationArea)]
    [InlineData(false, StartupWindowMode.Taskbar)]
    public void EffectiveStartupWindow_FollowsTheTrayOptionUntilChosen(bool closeToTray, StartupWindowMode expected)
    {
        var prefs = new AppPreferences { CloseToTray = closeToTray };
        Assert.Equal(expected, prefs.EffectiveStartupWindow);

        prefs.StartupWindow = StartupWindowMode.Window;
        Assert.Equal(StartupWindowMode.Window, prefs.EffectiveStartupWindow);
    }

    [Fact]
    public void ResetToDefaults_KeepsSavedData()
    {
        var prefs = new AppPreferences
        {
            Theme = AppTheme.Dark,
            CloseToTray = true,
            MinimizeToTray = true,
            StartupWindow = StartupWindowMode.Window,
            StartPage = "UsageTab",
            NotifyWhenDone = false,
            RemoteKeepAwake = true,
            CheckForUpdatesAutomatically = false,
            SshConfigPath = @"D:\ssh\config",
            RemoteKeepRunning = ["Ubuntu"],
            WindowWidth = 1200
        };

        prefs.ResetToDefaults();

        Assert.Equal(AppTheme.System, prefs.Theme);
        Assert.False(prefs.CloseToTray);
        Assert.False(prefs.MinimizeToTray);
        Assert.Null(prefs.StartupWindow);
        Assert.Null(prefs.StartPage);
        Assert.True(prefs.NotifyWhenDone);
        Assert.False(prefs.RemoteKeepAwake);
        Assert.True(prefs.CheckForUpdatesAutomatically);
        Assert.Equal(@"D:\ssh\config", prefs.SshConfigPath);
        Assert.Equal(["Ubuntu"], prefs.RemoteKeepRunning);
        Assert.Equal(1200, prefs.WindowWidth);
    }

    [Fact]
    public void Json_OldSettingsFileGetsDefaultsForNewOptions()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var prefs = JsonSerializer.Deserialize<AppPreferences>("""{ "CloseToTray": true, "Theme": "Dark" }""", options)!;

        Assert.True(prefs.NotifyWhenDone);
        Assert.False(prefs.MinimizeToTray);
        Assert.Null(prefs.StartupWindow);
        Assert.Equal(StartupWindowMode.NotificationArea, prefs.EffectiveStartupWindow);
        Assert.DoesNotContain("EffectiveStartupWindow", JsonSerializer.Serialize(prefs, options), StringComparison.Ordinal);
    }
}
