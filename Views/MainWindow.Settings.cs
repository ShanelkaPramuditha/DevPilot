using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DevPilot.Services;
using DevPilot.Services.Platform;
using DevPilot.Services.Updates;
using DevPilot.Theming;

namespace DevPilot.Views;

/// <summary>
/// The "Settings" page: startup, minimize and close behaviour, notifications, theme,
/// remote access and updates. Every change is saved and applied at once.
/// </summary>
public partial class MainWindow
{
    /// <summary>Set while the page shows the saved values, so that does not count as a change.</summary>
    private bool _loadingSettings;

    /// <summary>
    /// The page to open on: the chosen one, or the one used last. Picked after the first
    /// refresh, so the page loads with the distributions already known.
    /// </summary>
    private TabItem? StartTab()
    {
        string? start = App.Preferences.StartPage ?? App.Preferences.LastPage;
        return DistroTabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Name == start && t != SettingsTab);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => DistroTabs.SelectedItem = SettingsTab;

    /// <summary>Remembers the open page for "Open on page: the page I used last".</summary>
    private void RememberPage()
    {
        if (DistroTabs.SelectedItem is TabItem tab && tab != SettingsTab && App.Preferences.LastPage != tab.Name)
        {
            App.Preferences.LastPage = tab.Name;
            App.Preferences.Save();
        }
    }

    /// <summary>Shows the saved values (they can also change from the menu, the sidebar or the Remote Access page).</summary>
    private void LoadSettingsPage()
    {
        var prefs = App.Preferences;
        _loadingSettings = true;
        try
        {
            SettingsStartWithWindowsBox.IsChecked = StartupRegistration.IsEnabled;
            SettingsStartupWindowBox.IsEnabled = StartupRegistration.IsEnabled;
            Select(SettingsStartupWindowBox, prefs.EffectiveStartupWindow.ToString());

            SettingsStartPageBox.Items.Clear();
            SettingsStartPageBox.Items.Add(new ComboBoxItem { Content = "The page I used last", Tag = "" });
            foreach (var tab in DistroTabs.Items.OfType<TabItem>().Where(t => t != SettingsTab))
            {
                SettingsStartPageBox.Items.Add(new ComboBoxItem { Content = tab.Header, Tag = tab.Name });
            }

            Select(SettingsStartPageBox, prefs.StartPage ?? "");

            Select(SettingsMinimizeBox, prefs.MinimizeToTray ? "NotificationArea" : "Taskbar");
            Select(SettingsCloseBox, prefs.CloseToTray ? "NotificationArea" : "Exit");
            SettingsNotifyBox.IsChecked = prefs.NotifyWhenDone;

            Select(SettingsThemeBox, ThemeManager.Current.ToString());
            SettingsSidebarBox.IsChecked = prefs.SidebarCollapsed;

            SettingsKeepAwakeBox.IsChecked = prefs.RemoteKeepAwake;

            SettingsVersionText.Text = $"DevPilot {UpdateService.CurrentVersion} ({UpdateService.Architecture})" +
                (_availableUpdate is { } update ? $" · version {update.Version} is available: click Check Now to install it" : "");
            SettingsVersionText.SetResourceReference(TextBlock.ForegroundProperty, _availableUpdate is null ? "Brush.TextSecondary" : "Brush.Accent");
            SettingsAutoUpdateBox.IsChecked = prefs.CheckForUpdatesAutomatically;
            bool preRelease = UpdateService.CurrentVersion.IsPreRelease;
            SettingsPreReleaseBox.IsChecked = prefs.IncludePreReleases || preRelease;
            SettingsPreReleaseBox.IsEnabled = !preRelease;
            SettingsPreReleaseBox.ToolTip = preRelease ? "Always on while a pre-release is installed." : null;

            SettingsPathText.Text = $"Saved in {AppPreferences.FolderPath}";
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == tag) ?? box.Items[0];

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    /// <summary>Saves a change unless the page is only showing the saved values.</summary>
    private void ChangeSetting(Action<AppPreferences> change)
    {
        if (_loadingSettings || !IsLoaded)
        {
            return;
        }

        change(App.Preferences);
        App.Preferences.Save();
    }

    #region Startup

    private void SettingsStartWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings)
        {
            return;
        }

        bool on = SettingsStartWithWindowsBox.IsChecked == true;
        try
        {
            if (on)
            {
                StartupRegistration.Enable();
            }
            else
            {
                StartupRegistration.Disable();
            }
        }
        catch (Exception ex)
        {
            ShowError($"Could not change the startup setting: {ex.Message}");
        }

        LoadSettingsPage();
    }

    private void SettingsStartupWindow_Changed(object sender, SelectionChangedEventArgs e) => ChangeSetting(p =>
    {
        if (Enum.TryParse(SelectedTag(SettingsStartupWindowBox), out StartupWindowMode mode))
        {
            p.StartupWindow = mode;
        }
    });

    private void SettingsStartPage_Changed(object sender, SelectionChangedEventArgs e) => ChangeSetting(p =>
        p.StartPage = SelectedTag(SettingsStartPageBox) is { Length: > 0 } page ? page : null);

    #endregion

    #region Window and notification area

    private void SettingsMinimize_Changed(object sender, SelectionChangedEventArgs e)
    {
        ChangeSetting(p => p.MinimizeToTray = SelectedTag(SettingsMinimizeBox) == "NotificationArea");
        UpdateTrayIcon();
    }

    private void SettingsClose_Changed(object sender, SelectionChangedEventArgs e)
    {
        ChangeSetting(p => p.CloseToTray = SelectedTag(SettingsCloseBox) == "NotificationArea");
        UpdateTrayIcon();
    }

    private void SettingsNotify_Changed(object sender, RoutedEventArgs e) =>
        ChangeSetting(p => p.NotifyWhenDone = SettingsNotifyBox.IsChecked == true);

    #endregion

    #region Appearance, remote access and updates

    private void SettingsTheme_Changed(object sender, SelectionChangedEventArgs e) => ChangeSetting(p =>
    {
        if (Enum.TryParse(SelectedTag(SettingsThemeBox), out AppTheme theme))
        {
            ThemeManager.Apply(theme);
            p.Theme = theme;
        }
    });

    private void SettingsSidebar_Changed(object sender, RoutedEventArgs e) => ChangeSetting(p =>
    {
        p.SidebarCollapsed = SettingsSidebarBox.IsChecked == true;
        IsSidebarExpanded = !p.SidebarCollapsed && ActualWidth >= AutoCollapseWidth;
    });

    private void SettingsKeepAwake_Changed(object sender, RoutedEventArgs e)
    {
        ChangeSetting(p => p.RemoteKeepAwake = SettingsKeepAwakeBox.IsChecked == true);
        SyncKeepAwakeBox();
    }

    /// <summary>The same option is on the Remote Access page.</summary>
    private void SyncKeepAwakeBox()
    {
        _loadingSettings = true;
        RemoteKeepAwakeBox.IsChecked = App.Preferences.RemoteKeepAwake;
        _loadingSettings = false;
        UpdateKeepAwake();
    }

    private void SettingsUpdates_Changed(object sender, RoutedEventArgs e) => ChangeSetting(p =>
    {
        p.CheckForUpdatesAutomatically = SettingsAutoUpdateBox.IsChecked == true;
        if (!UpdateService.CurrentVersion.IsPreRelease)
        {
            p.IncludePreReleases = SettingsPreReleaseBox.IsChecked == true;
        }
    });

    #endregion

    #region Settings file

    private void SettingsOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppPreferences.FolderPath);
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{AppPreferences.FolderPath}\"", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError($"Could not open the folder: {ex.Message}");
        }
    }

    private void SettingsReset_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Put every option on this page back to its default?\n\n" +
            "DevPilot stops starting with Windows. Your SSH config path, distributions kept running for remote access and window size are kept.",
            "Reset Settings", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        App.Preferences.ResetToDefaults();
        App.Preferences.Save();
        try
        {
            StartupRegistration.Disable();
        }
        catch (Exception ex)
        {
            ShowError($"Could not change the startup setting: {ex.Message}");
        }

        ThemeManager.Apply(App.Preferences.Theme);
        IsSidebarExpanded = ActualWidth >= AutoCollapseWidth;
        UpdateTrayIcon();
        SyncKeepAwakeBox();
        LoadSettingsPage();
    }

    #endregion
}
