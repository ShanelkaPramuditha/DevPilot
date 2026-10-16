using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using DevPilot.Core;
using DevPilot.Models;
using DevPilot.Services.Updates;

namespace DevPilot.Views.Dialogs;

/// <summary>
/// Shows an available update with its release notes, and downloads, verifies
/// and installs it.
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly ReleaseInfo _release;
    private readonly InstallKind _installKind = UpdateService.DetectInstallKind();
    /// <summary>Cancels the running download; null when nothing is downloading.</summary>
    private Action? _cancelDownload;
    private bool _installing;

    public UpdateWindow(ReleaseInfo release)
    {
        InitializeComponent();
        _release = release;

        TitleText.Text = $"DevPilot {release.Version} is available";
        SubtitleText.Text = $"You have version {UpdateService.CurrentVersion}" +
            (release.PublishedAt is { } published ? $"  ·  released {published.LocalDateTime.ToString("d MMMM yyyy", CultureInfo.CurrentCulture)}" : "");
        PreReleaseBadge.Visibility = release.IsPreRelease ? Visibility.Visible : Visibility.Collapsed;

        string notes = UpdateService.CleanNotes(release.Notes);
        NotesText.Text = notes.Length > 0 ? notes : "No release notes were published for this version.";

        var preferences = App.Preferences;
        AutoCheckBox.IsChecked = preferences.CheckForUpdatesAutomatically;
        PreReleaseBox.IsChecked = preferences.IncludePreReleases || UpdateService.CurrentVersion.IsPreRelease;
        PreReleaseBox.IsEnabled = !UpdateService.CurrentVersion.IsPreRelease;

        if (_installKind == InstallKind.Portable || UpdateService.FindInstaller(release, UpdateService.Architecture) is null)
        {
            PortableNote.Visibility = Visibility.Visible;
            UpdateButton.Content = "Download";
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (PortableNote.Visibility == Visibility.Visible)
        {
            OpenInBrowser(_release.PageUrl);
            Close();
            return;
        }

        using var download = new CancellationTokenSource();
        _cancelDownload = download.Cancel;
        SetDownloading(true);

        try
        {
            var progress = new Progress<(long Received, long Total)>(p =>
            {
                DownloadProgress.Value = p.Total > 0 ? 100.0 * p.Received / p.Total : 0;
                ProgressText.Text = p.Total > 0
                    ? $"Downloading {Format.Size(p.Received)} of {Format.Size(p.Total)}..."
                    : $"Downloading {Format.Size(p.Received)}...";
            });

            string installer = await UpdateService.DownloadInstallerAsync(_release, progress, download.Token);

            ProgressText.Text = "Download verified. Starting the installer...";
            UpdateService.StartInstaller(installer, _installKind);

            // The installer replaces the files and starts DevPilot again when it is done.
            _installing = true;
            SavePreferences();
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            SetDownloading(false);
            ProgressPanel.Visibility = Visibility.Collapsed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user declined the admin prompt for an all-users installation.
            SetDownloading(false);
            ProgressText.Text = "The update was cancelled. Administrator rights are needed because DevPilot is installed for all users.";
        }
        catch (Exception ex)
        {
            SetDownloading(false);
            ProgressText.Text = $"The update failed: {ex.Message}";
        }
        finally
        {
            _cancelDownload = null;
        }
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        App.Preferences.SkippedVersion = _release.Version.ToString();
        Close();
    }

    private void ViewOnGitHub_Click(object sender, RoutedEventArgs e) => OpenInBrowser(_release.PageUrl);

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        // Cancels a running download; otherwise just closes.
        if (_cancelDownload is { } cancel)
        {
            cancel();
            return;
        }

        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _cancelDownload?.Invoke();
        if (!_installing)
        {
            SavePreferences();
        }
    }

    private void SetDownloading(bool downloading)
    {
        ProgressPanel.Visibility = Visibility.Visible;
        UpdateButton.IsEnabled = !downloading;
        SkipButton.IsEnabled = !downloading;
        GitHubButton.IsEnabled = !downloading;
        LaterButton.Content = downloading ? "Cancel" : "Later";
        if (downloading)
        {
            DownloadProgress.Value = 0;
            ProgressText.Text = "Starting the download...";
        }
    }

    private void SavePreferences()
    {
        var preferences = App.Preferences;
        preferences.CheckForUpdatesAutomatically = AutoCheckBox.IsChecked == true;
        if (PreReleaseBox.IsEnabled)
        {
            preferences.IncludePreReleases = PreReleaseBox.IsChecked == true;
        }

        preferences.Save();
    }

    private void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open {url}: {ex.Message}", "DevPilot", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
