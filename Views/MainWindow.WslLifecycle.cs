using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using DevPilot.Services.Wsl;

namespace DevPilot.Views;

/// <summary>
/// Installing, updating and shutting down WSL itself.
/// </summary>
public partial class MainWindow
{
    private async void InstallWsl_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "WSL will be installed. Administrator rights are required and a restart may be needed afterwards.\n\n" +
            "Once WSL is installed you can choose which Linux distribution to install.\n\nContinue?",
            "Install WSL",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync("Installing WSL... follow the progress in the console window.", async () =>
        {
            try
            {
                if (File.Exists(WslService.WslPath))
                {
                    // Modern Windows: wsl.exe can install itself. The distribution
                    // is chosen afterwards from the "Available" tab.
                    using var process = WslService.StartInConsole("--install --no-distribution", elevated: true);
                    await process.WaitForExitAsync();
                }
                else
                {
                    // Older Windows 10 builds without wsl.exe: enable the
                    // Windows features directly.
                    using var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart" +
                                    " && dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart" +
                                    " & pause",
                        UseShellExecute = true,
                        Verb = "runas"
                    }) ?? throw new InvalidOperationException("Could not start the WSL installer.");
                    await process.WaitForExitAsync();
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                MessageBox.Show(
                    "Installation was cancelled. Administrator rights are required to install WSL.",
                    "Install WSL",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _catalogLoaded = false;
            await RefreshAllAsync();

            if (_wslInstalled)
            {
                MessageBox.Show(
                    "WSL has been installed. Choose a distribution on the 'Available' tab and click Install.",
                    "Install WSL",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                WslStatusText.Text = "Restart Windows to finish";
                WslDetailsText.Text = "The WSL installation needs a restart.";
                MessageBox.Show(
                    "The WSL installation has finished, but WSL is not available yet.\n\n" +
                    "Restart Windows to complete the installation, then open this app again.",
                    "Install WSL",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        });
    }

    private async void UpdateWsl_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Updating WSL... follow the progress in the console window.", async () =>
        {
            using var process = WslService.StartInConsole("--update", alwaysPause: true);
            await process.WaitForExitAsync();
            await RefreshAllAsync();
        });
    }

    private async void ShutdownWsl_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will stop ALL WSL distributions and their running processes. Continue?",
            "Shutdown WSL",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync("Shutting down WSL...", async () =>
        {
            await WslService.RunCheckedAsync("--shutdown");
            await RefreshInstalledAsync();
        });
    }
}
