using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DevPilot.Models;
using DevPilot.Services.Platform;
using DevPilot.Services.Wsl;

namespace DevPilot.Views.Dialogs;

/// <summary>
/// Lists, adds and removes Windows folders mounted into one distribution.
/// </summary>
public partial class MountsWindow : Window
{
    private readonly string _distro;
    private readonly bool _hasVsCode = VsCode.IsInstalled;
    private DistroUser? _user;

    public MountsWindow(string distro)
    {
        InitializeComponent();
        _distro = distro;
        Title = $"Folder Mounts - {distro}";
        TitleText.Text = $"Windows folders mounted in {distro}";
        HintText.Text = _hasVsCode
            ? "Right-click a mount for actions. Double-click to open it in VS Code."
            : "Right-click a mount for actions. Double-click to open the Windows folder.";
    }

    private FolderMount? SelectedMount => MountList.SelectedItem as FolderMount;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync($"Starting {_distro} and reading its mounts...", RefreshAsync);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Refreshing...", RefreshAsync);
    }

    private async Task RefreshAsync()
    {
        string? selected = SelectedMount?.LinuxPath;

        _user ??= await WslMounts.GetDefaultUserAsync(_distro);
        var mounts = await WslMounts.GetMountsAsync(_distro);

        MountList.ItemsSource = mounts;
        MountList.SelectedItem = mounts.FirstOrDefault(m => m.LinuxPath == selected);
        Placeholder.Visibility = mounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_user is null)
        {
            return;
        }

        var dialog = new AddMountWindow(_user) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunBusyAsync($"Mounting {dialog.WindowsPath}...", async () =>
        {
            await WslMounts.AddAsync(_distro, dialog.WindowsPath, dialog.LinuxPath, dialog.IsPersistent, dialog.IsReadOnly, _user);
            await RefreshAsync();
            MountList.SelectedItem = (MountList.ItemsSource as List<FolderMount>)?
                .FirstOrDefault(m => m.LinuxPath == dialog.LinuxPath);
        });
    }

    private async void Mount_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMount is not { } mount)
        {
            return;
        }

        await RunBusyAsync($"Mounting {mount.LinuxPath}...", async () =>
        {
            await WslMounts.MountAsync(_distro, mount.LinuxPath);
            await RefreshAsync();
        });
    }

    private async void Unmount_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMount is not { } mount)
        {
            return;
        }

        await RunBusyAsync($"Unmounting {mount.LinuxPath}...", async () =>
        {
            await WslMounts.UnmountAsync(_distro, mount.LinuxPath);
            await RefreshAsync();
        });
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMount is not { } mount)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Remove the mount at {mount.LinuxPath}?\n\n" +
            $"It will be unmounted{(mount.IsPersistent ? " and removed from /etc/fstab" : "")}. " +
            $"The files in {mount.WindowsPath} are not deleted.",
            "Remove Mount",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync($"Removing {mount.LinuxPath}...", async () =>
        {
            await WslMounts.RemoveAsync(_distro, mount.LinuxPath);
            await RefreshAsync();
        });
    }

    private void OpenWindows_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMount is { } mount)
        {
            OpenInExplorer(mount.WindowsPath);
        }
    }

    // A mounted Windows folder cannot be opened through \\wsl.localhost
    // (Windows denies access to it there), and it holds the same files as
    // the Windows folder anyway, so these open Linux tools at the mount instead.
    private void OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMount is not { } mount)
        {
            return;
        }

        try
        {
            WslService.OpenTerminal(_distro, "--cd", mount.LinuxPath);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to open a terminal: {ex.Message}");
        }
    }

    private void OpenVsCode_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedMount is not { } mount)
        {
            return;
        }

        try
        {
            VsCode.OpenInWsl(_distro, mount.LinuxPath);
        }
        catch (Exception ex)
        {
            ShowError($"Failed to open VS Code: {ex.Message}");
        }
    }

    /// <summary>
    /// Double-click opens the mount in VS Code when it is installed and the
    /// folder is mounted, otherwise the Windows folder.
    /// </summary>
    private void MountList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectedMount is not { } mount)
        {
            return;
        }

        if (mount.IsMounted && _hasVsCode)
        {
            OpenVsCode_Click(sender, e);
        }
        else
        {
            OpenWindows_Click(sender, e);
        }
    }

    private void MountList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void MountMenu_Opened(object sender, RoutedEventArgs e) => UpdateButtons();

    /// <summary>
    /// Right-clicking a row selects it; right-clicking empty space clears the
    /// selection so the menu only offers Add and Refresh.
    /// </summary>
    private void MountList_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null and not ListViewItem)
        {
            element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        if (element is ListViewItem item)
        {
            item.IsSelected = true;
            item.Focus();
        }
        else
        {
            MountList.SelectedItem = null;
        }
    }

    private void UpdateButtons()
    {
        var mount = SelectedMount;
        bool hasMount = mount is not null;
        bool mounted = mount is { IsMounted: true };

        AddButton.IsEnabled = _user is not null;
        AddItem.IsEnabled = _user is not null;

        // Mount actions only show for a right-clicked mount.
        var visibility = hasMount ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in new FrameworkElement[]
        {
            OpenTerminalItem, OpenWindowsItem, MountSeparator, MountItem, UnmountItem,
            RemoveSeparator, RemoveItem, GeneralSeparator
        })
        {
            item.Visibility = visibility;
        }

        OpenVsCodeItem.Visibility = hasMount && _hasVsCode ? Visibility.Visible : Visibility.Collapsed;
        OpenVsCodeItem.IsEnabled = mounted;
        OpenTerminalItem.IsEnabled = mounted;
        MountItem.IsEnabled = mount is { IsMounted: false, IsPersistent: true };
        UnmountItem.IsEnabled = mounted;

        // The double-click action is shown in bold.
        bool vsCodeIsDefault = mounted && _hasVsCode;
        OpenVsCodeItem.FontWeight = vsCodeIsDefault ? FontWeights.SemiBold : FontWeights.Normal;
        OpenWindowsItem.FontWeight = vsCodeIsDefault ? FontWeights.Normal : FontWeights.SemiBold;
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);

    private async Task RunBusyAsync(string message, Func<Task> action)
    {
        BusyText.Text = message;
        BusyProgress.Visibility = Visibility.Visible;
        ActionsPanel.IsEnabled = false;
        MountList.IsEnabled = false;
        MountMenu.IsOpen = false;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BusyText.Text = "Ready";
            BusyProgress.Visibility = Visibility.Hidden;
            ActionsPanel.IsEnabled = true;
            MountList.IsEnabled = true;
            UpdateButtons();
        }
    }

    private void OpenInExplorer(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"{path} could not be found.");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
