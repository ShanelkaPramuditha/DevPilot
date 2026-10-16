using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Diagnostics;

namespace DevModeManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }


    private async void StartLocal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "--exec bash -lc \"exec bash\"",
                UseShellExecute = false
            };

            Process.Start(startInfo);

            MessageBox.Show(
                "WSL has been started.",
                "Local Development",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to start WSL: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void StopWsl_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will stop ALL WSL distributions and their running processes. Continue?",
            "Stop WSL",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "--shutdown",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);

            if (process == null)
                throw new InvalidOperationException("Could not start wsl.exe.");

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"WSL shutdown failed with exit code {process.ExitCode}.");

            MessageBox.Show(
                "WSL has been shut down.",
                "Local Development",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to stop WSL: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void CheckStatus_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "--list --running --quiet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);

            if (process == null)
                throw new InvalidOperationException("Could not start wsl.exe.");

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new InvalidOperationException(error);

            // WSL may return UTF-16 output. Remove null characters
            // to make the distribution list easier to process.
            output = output.Replace("\0", "").Trim();

            string message = string.IsNullOrWhiteSpace(output)
                ? "WSL is stopped. No distributions are running."
                : $"Running WSL distributions:\n{output}";

            MessageBox.Show(
                message,
                "WSL Status",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to check WSL status: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

}