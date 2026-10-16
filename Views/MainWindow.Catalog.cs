using System.Windows;
using DevPilot.Services.Wsl;

namespace DevPilot.Views;

/// <summary>
/// Installing distributions from the "Available" page.
/// </summary>
public partial class MainWindow
{
    private void InstallDistro_Click(object sender, RoutedEventArgs e) => InstallSelectedOnlineDistro();

    private void ShowInstalled_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedOnlineDistro is not { } online)
        {
            return;
        }

        DistroTabs.SelectedItem = InstalledTab;
        InstalledList.SelectedItem = _installed.FirstOrDefault(
            d => d.Name.Equals(online.Name, StringComparison.OrdinalIgnoreCase));
        InstalledList.Focus();
    }

    private void OnlineList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        InstallSelectedOnlineDistro();

    private void InstallSelectedOnlineDistro()
    {
        var distro = SelectedOnlineDistro;
        if (distro is null || distro.Status != "Available" || _busy)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Install {distro.FriendlyName}?\n\n" +
            "A console window will open to download the distribution. When it finishes you will be asked " +
            "to create a Linux username and password. Type 'exit' to close the window when you are done.",
            "Install Distribution",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var process = WslService.StartInConsole($"--install -d {distro.Name}");
            BusyText.Text = $"Installing {distro.FriendlyName} in the console window...";
            _ = RefreshWhenExitedAsync(process);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to install {distro.FriendlyName}: {ex.Message}");
        }
    }
}
