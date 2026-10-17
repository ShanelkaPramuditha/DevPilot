using System.Diagnostics;
using System.IO;
using System.Windows;
using DevPilot.Core;
using DevPilot.Models;
using DevPilot.Services.Platform;
using DevPilot.Services.Wsl;
using DevPilot.Views.Dialogs;
using Microsoft.Win32;

namespace DevPilot.Views;

/// <summary>
/// Actions in the right-click menu of the "Installed" page.
/// </summary>
public partial class MainWindow
{
    private void InstalledList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectedDistro is not null)
        {
            OpenTerminal_Click(sender, e);
        }
    }

    private void OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDistro is not { } distro)
        {
            return;
        }

        try
        {
            var process = WslService.OpenTerminal(distro.Name, "--cd", "~");
            _ = RefreshWhenExitedAsync(process);

            // Give the distribution a moment to start, then show it as running.
            _ = RefreshAfterDelayAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            ShowError($"Failed to open {distro.Name}: {ex.Message}");
        }
    }

    private async void OpenVsCode_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDistro is not { } distro)
        {
            return;
        }

        await RunBusyAsync($"Opening {distro.Name} in VS Code...", async () =>
        {
            var user = await WslMounts.GetDefaultUserAsync(distro.Name);
            VsCode.OpenInWsl(distro.Name, user.Home);
            await RefreshInstalledAsync();
        });
    }

    private void OpenFiles_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDistro is not { } distro)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"\\\\wsl.localhost\\{distro.Name}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError($"Failed to open the files of {distro.Name}: {ex.Message}");
        }
    }

    private async void FolderMounts_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDistro is not { } distro)
        {
            return;
        }

        new MountsWindow(distro.Name) { Owner = this }.ShowDialog();

        // Reading the mounts starts the distribution, so show its new state.
        await RefreshIfIdleAsync();
    }

    private async void DistroSettings_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDistro is { } distro)
        {
            await ShowSettingsAsync(distro);
        }
    }

    private async void WslSettings_Click(object sender, RoutedEventArgs e) => await ShowSettingsAsync(null);

    private async Task ShowSettingsAsync(WslDistro? distro)
    {
        new SettingsWindow(distro) { Owner = this }.ShowDialog();

        // Settings can restart WSL, move a distribution or change the memory limit.
        if (!_busy)
        {
            await RunBusyAsync("Refreshing...", RefreshAllAsync);
        }
    }

    private async void StartDistro_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDistro is not { } distro)
        {
            return;
        }

        await RunBusyAsync($"Starting {distro.Name}...", async () =>
        {
            await WslService.StartInBackgroundAsync(distro.Name);
            await RefreshInstalledAsync();
        });
    }

    private async void RestartDistro_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDistro is not { } distro)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Restart {distro.Name}? Programs running in it will be closed.",
            "Restart Distribution",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync($"Restarting {distro.Name}...", async () =>
        {
            await WslService.RunCheckedAsync("--terminate", distro.Name);
            await WslService.StartInBackgroundAsync(distro.Name);
            await RefreshInstalledAsync();
        });
    }

    private async void StopDistro_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDistro is not { } distro)
        {
            return;
        }

        await RunBusyAsync($"Stopping {distro.Name}...", async () =>
        {
            await WslService.RunCheckedAsync("--terminate", distro.Name);
            await RefreshInstalledAsync();
        });
    }

    private async void SetDefault_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDistro is not { } distro)
        {
            return;
        }

        await RunBusyAsync($"Setting {distro.Name} as default...", async () =>
        {
            await WslService.RunCheckedAsync("--set-default", distro.Name);
            await RefreshInstalledAsync();
        });
    }

    private async void ConvertVersion_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDistro is not { } distro)
        {
            return;
        }

        string target = distro.Version == "2" ? "1" : "2";
        var result = MessageBox.Show(
            $"Convert {distro.Name} to WSL {target}?\n\nThis can take several minutes and the distribution will be stopped.",
            "Convert Distribution",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync($"Converting {distro.Name} to WSL {target}... this can take several minutes.", async () =>
        {
            await WslService.RunCheckedAsync("--set-version", distro.Name, target);
            await RefreshInstalledAsync();
        });
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDistro is not { } distro)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = $"Export {distro.Name}",
            FileName = $"{distro.Name}-{DateTime.Now:yyyyMMdd}.tar",
            Filter = "Tar archive (*.tar)|*.tar|Virtual disk (*.vhdx)|*.vhdx",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string file = dialog.FileName;
        bool asVhd = file.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase);

        await RunBusyAsync($"Exporting {distro.Name}... this can take several minutes.", async () =>
        {
            if (asVhd)
            {
                await WslService.RunCheckedAsync("--export", distro.Name, file, "--vhd");
            }
            else
            {
                await WslService.RunCheckedAsync("--export", distro.Name, file);
            }

            await RefreshInstalledAsync();
            MessageBox.Show(
                $"{distro.Name} was exported to:\n{file}",
                "Export",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var fileDialog = new OpenFileDialog
        {
            Title = "Choose a distribution to import",
            Filter = "Distribution archives (*.tar;*.tar.gz;*.vhdx)|*.tar;*.tar.gz;*.vhdx|All files (*.*)|*.*"
        };

        if (fileDialog.ShowDialog(this) != true)
        {
            return;
        }

        string file = fileDialog.FileName;
        string suggestedName = Path.GetFileName(file).Split('.')[0];

        string? name = PromptForText("Import Distribution", "Name for the new distribution:", suggestedName);
        if (name is null)
        {
            return;
        }

        if (DistroNames.Validate(name, _installed.Select(d => d.Name)) is { } nameError)
        {
            ShowError(nameError);
            return;
        }

        var folderDialog = new OpenFolderDialog
        {
            Title = "Choose where to store the distribution",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        if (folderDialog.ShowDialog(this) != true)
        {
            return;
        }

        string location = Path.Combine(folderDialog.FolderName, name);

        await RunBusyAsync($"Importing {name}... this can take several minutes.", async () =>
        {
            Directory.CreateDirectory(location);

            if (file.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase))
            {
                await WslService.RunCheckedAsync("--import", name, location, file, "--vhd");
            }
            else
            {
                await WslService.RunCheckedAsync("--import", name, location, file);
            }

            await RefreshInstalledAsync();
            MessageBox.Show(
                $"{name} was imported to:\n{location}",
                "Import",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
    }

    /// <summary>
    /// Copies a distribution under a new name: exports it to a temporary file and imports
    /// that next to the original. WSL 2 distributions are copied as a disk image (faster).
    /// </summary>
    private async void Clone_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDistro is not { } distro)
        {
            return;
        }

        var existing = _installed.Select(d => d.Name).ToList();
        string? name = PromptForText("Clone Distribution", $"Name for the copy of {distro.Name}:",
            DistroNames.SuggestCopyName(distro.Name, existing));
        if (name is null)
        {
            return;
        }

        if (DistroNames.Validate(name, existing) is { } nameError)
        {
            ShowError(nameError);
            return;
        }

        // Next to the original (for example %LocalAppData%\wsl\<name>); it can be moved later in Settings → Disk.
        string parent = Path.GetDirectoryName(distro.Location ?? "") is { Length: > 0 } folder
            ? folder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wsl");
        string location = Path.Combine(parent, name);
        bool asVhd = distro.Version == "2";
        string temp = Path.Combine(Path.GetTempPath(), $"devpilot-clone-{Guid.NewGuid():N}.{(asVhd ? "vhdx" : "tar")}");

        // The copy is written twice (temporary file, then the new distribution).
        if (distro.DiskBytes is { } size && new DriveInfo(Path.GetPathRoot(temp)!).AvailableFreeSpace < size * 2)
        {
            ShowError($"Not enough free disk space to clone {distro.Name}: about {Format.Size(size * 2)} is needed.");
            return;
        }

        var answer = MessageBox.Show(this,
            $"Clone {distro.Name} as {name}?\n\nThe copy is stored in {location}." +
            (distro.IsRunning ? $"\n\n{distro.Name} will be stopped while it is copied." : ""),
            "Clone Distribution", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync($"Cloning {distro.Name} as {name}... this can take several minutes.", async () =>
        {
            // Imported distributions log in as root unless wsl.conf says otherwise.
            string user = (await WslService.RunAsync(TimeSpan.FromMinutes(1), "--distribution", distro.Name, "--exec", "id", "-un")).StandardOutput.Trim();

            try
            {
                string[] export = asVhd ? ["--export", distro.Name, temp, "--vhd"] : ["--export", distro.Name, temp];
                string[] import = asVhd ? ["--import", name, location, temp, "--vhd"] : ["--import", name, location, temp];
                await WslService.RunCheckedAsync(export);
                Directory.CreateDirectory(location);
                await WslService.RunCheckedAsync(import);
            }
            finally
            {
                File.Delete(temp);
            }

            await KeepDefaultUserAsync(name, user);
            await RefreshInstalledAsync();
            BusyText.Text = $"{name} is a copy of {distro.Name}.";
            NotifyIfInBackground("Clone finished", $"{name} is a copy of {distro.Name}.");
        });
    }

    /// <summary>Makes a copied distribution log in as the same user as the original.</summary>
    private static async Task KeepDefaultUserAsync(string distro, string user)
    {
        if (user.Length == 0 || user == "root")
        {
            return;
        }

        string current = (await WslService.RunAsync(TimeSpan.FromMinutes(1), "--distribution", distro, "--exec", "id", "-un")).StandardOutput.Trim();
        if (current == user)
        {
            return;
        }

        var conf = IniDocument.Parse(await WslSettings.ReadWslConfAsync(distro));
        conf.Set("user", "default", user);
        await WslSettings.WriteWslConfAsync(distro, conf.ToString());
        await WslService.RunCheckedAsync("--terminate", distro);
    }

    private async void Unregister_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDistro is not { } distro)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Unregister {distro.Name}?\n\nThis permanently DELETES the distribution and all files inside it. " +
            "Export it first if you want to keep a backup.",
            "Unregister Distribution",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync($"Unregistering {distro.Name}...", async () =>
        {
            await WslService.RunCheckedAsync("--unregister", distro.Name);
            await RefreshInstalledAsync();
        });
    }
}
