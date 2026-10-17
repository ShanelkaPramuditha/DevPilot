using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using DevPilot.Core;
using DevPilot.Models;
using DevPilot.Navigation;
using DevPilot.Services.Updates;
using DevPilot.Views.Dialogs;

namespace DevPilot.Views;

/// <summary>
/// Checking for a newer DevPilot: automatically at startup and every few hours while it
/// runs (a banner, and a notification when DevPilot is in the background), and from
/// "Check for Updates..." in the "more" menu or on the Settings page.
/// </summary>
public partial class MainWindow
{
    /// <summary>Automatic checks run at most this often.</summary>
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(12);

    /// <summary>Wakes up hourly; a check runs when one is due, so DevPilot left running for days still finds updates.</summary>
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(1) };

    /// <summary>The version the user was already told about, so the notification is not repeated.</summary>
    private string? _notifiedVersion;

    private void InitializeUpdates()
    {
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesInBackgroundAsync();
        _updateTimer.Start();
    }

    private ReleaseInfo? _availableUpdate;

    private static bool IncludePreReleases => App.Preferences.IncludePreReleases;

    /// <summary>
    /// Quietly checks at startup. Problems (offline, GitHub unavailable) are ignored:
    /// the user did not ask, so there is nothing to report.
    /// </summary>
    private async Task CheckForUpdatesInBackgroundAsync()
    {
        var preferences = App.Preferences;
        if (!UpdateService.IsConfigured || !preferences.CheckForUpdatesAutomatically || _availableUpdate is not null)
        {
            return;
        }

        // Not due yet, unless the last check found an update: look it up again to show it.
        bool due = preferences.LastUpdateCheck is not { } last || DateTimeOffset.Now - last >= AutomaticCheckInterval;
        bool knownUpdate = SemanticVersion.TryParse(preferences.AvailableVersion) is { } known
            && known > UpdateService.CurrentVersion && preferences.AvailableVersion != preferences.SkippedVersion;
        if (!due && !knownUpdate)
        {
            return;
        }

        try
        {
            var update = await UpdateService.CheckAsync(IncludePreReleases);
            preferences.LastUpdateCheck = DateTimeOffset.Now;
            preferences.AvailableVersion = update?.Version.ToString();
            preferences.Save();

            if (update is not null && update.Version.ToString() != preferences.SkippedVersion)
            {
                ShowUpdateBanner(update);
                if (_notifiedVersion != update.Version.ToString())
                {
                    _notifiedVersion = update.Version.ToString();
                    NotifyIfInBackground("Update available", $"DevPilot {update.Version} is available. Open DevPilot to update.");
                }
            }
        }
        catch
        {
            // Try again at the next start.
        }
    }

    /// <summary>The startup check, for when DevPilot starts hidden in the notification area.</summary>
    public Task CheckForUpdatesWhileHiddenAsync() => CheckForUpdatesInBackgroundAsync();

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (!UpdateService.IsConfigured)
        {
            MessageBox.Show(this,
                "This build of DevPilot does not know where its releases are published, so it cannot check for updates.\n\n" +
                "Builds made by the release workflow can. For local builds, set RepositoryUrl in Directory.Build.props.",
                "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string previous = BusyText.Text;
        BusyText.Text = "Checking for updates...";
        try
        {
            var update = await UpdateService.CheckAsync(IncludePreReleases);
            App.Preferences.LastUpdateCheck = DateTimeOffset.Now;
            App.Preferences.AvailableVersion = update?.Version.ToString();
            App.Preferences.Save();

            if (update is null)
            {
                HideUpdateBanner();
                MessageBox.Show(this, $"You're up to date.\n\nDevPilot {UpdateService.CurrentVersion} is the latest version.",
                    "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // A manual check always shows the update, even a skipped version.
            ShowUpdateBanner(update);
            ShowUpdateWindow(update);
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show(this,
                $"Could not reach GitHub to check for updates. Check your internet connection and try again.\n\n{ex.Message}",
                "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not check for updates.\n\n{ex.Message}",
                "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BusyText.Text = previous;
        }
    }

    private void UpdateBannerOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is { } update)
        {
            ShowUpdateWindow(update);
        }
    }

    private void UpdateBannerDismiss_Click(object sender, RoutedEventArgs e) => HideUpdateBanner();

    private void ShowUpdateBanner(ReleaseInfo update)
    {
        _availableUpdate = update;
        UpdateBannerText.Text = $"DevPilot {update.Version} is available. You have {UpdateService.CurrentVersion}.";
        UpdateBanner.Visibility = Visibility.Visible;
        PageInfo.SetBadge(SettingsTab, "1");
    }

    private void HideUpdateBanner()
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
        PageInfo.SetBadge(SettingsTab, "");
    }

    private void ShowUpdateWindow(ReleaseInfo update)
    {
        new UpdateWindow(update) { Owner = this }.ShowDialog();

        // Skipping the version also hides its banner.
        if (App.Preferences.SkippedVersion == update.Version.ToString())
        {
            HideUpdateBanner();
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        string install = UpdateService.DetectInstallKind() switch
        {
            InstallKind.CurrentUser => "installed for the current user",
            InstallKind.AllUsers => "installed for all users",
            _ => "portable or development build"
        };

        var answer = MessageBox.Show(this,
            $"DevPilot {UpdateService.CurrentVersion} ({UpdateService.Architecture}, {install})\n\n" +
            "Manage WSL distributions and developer tools on Windows.\n\n" +
            "Copyright © 2026 Shanelka Pramuditha" +
            (UpdateService.IsConfigured ? "\n\nOpen the releases page on GitHub?" : ""),
            "About DevPilot",
            UpdateService.IsConfigured ? MessageBoxButton.YesNo : MessageBoxButton.OK,
            MessageBoxImage.Information);

        if (answer == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo { FileName = UpdateService.ReleasesPageUrl, UseShellExecute = true });
        }
    }
}
