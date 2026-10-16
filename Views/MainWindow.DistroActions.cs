using System.Diagnostics;
using System.IO;
using System.Windows;
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
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (name.Any(char.IsWhiteSpace))
        {
            ShowError("The distribution name cannot contain spaces.");
            return;
        }

        if (_installed.Any(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowError($"A distribution named {name} already exists.");
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
