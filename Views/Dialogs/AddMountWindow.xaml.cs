using System.IO;
using System.Windows;
using System.Windows.Controls;
using DevPilot.Models;
using DevPilot.Services.Wsl;
using Microsoft.Win32;

namespace DevPilot.Views.Dialogs;

/// <summary>
/// Asks for a Windows folder and the Linux folder to mount it at.
/// </summary>
public partial class AddMountWindow : Window
{
    private readonly DistroUser _user;
    private string _suggestedLinuxPath = "";

    public AddMountWindow(DistroUser user)
    {
        InitializeComponent();
        _user = user;
    }

    public string WindowsPath { get; private set; } = "";

    public string LinuxPath { get; private set; } = "";

    public bool IsPersistent => PersistentBox.IsChecked == true;

    public bool IsReadOnly => ReadOnlyBox.IsChecked == true;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the Windows folder to mount" };
        if (Directory.Exists(WindowsPathBox.Text))
        {
            dialog.InitialDirectory = WindowsPathBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            WindowsPathBox.Text = dialog.FolderName;
        }
    }

    /// <summary>
    /// Suggests ~/folder-name until the user types their own Linux path.
    /// </summary>
    private void WindowsPathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (LinuxPathBox.Text.Length > 0 && LinuxPathBox.Text != _suggestedLinuxPath)
        {
            return;
        }

        string name = Path.GetFileName(WindowsPathBox.Text.Trim().TrimEnd('\\', '/'));
        _suggestedLinuxPath = string.IsNullOrEmpty(name) || name.Contains(':')
            ? ""
            : "~/" + name.Replace(' ', '-');
        LinuxPathBox.Text = _suggestedLinuxPath;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string windowsPath = WindowsPathBox.Text.Trim();
        if (windowsPath.Length == 0 || !Directory.Exists(windowsPath))
        {
            ShowError("Choose a Windows folder that exists.");
            return;
        }

        string? error = WslMounts.ValidateLinuxPath(LinuxPathBox.Text, _user, out string linuxPath);
        if (error is not null)
        {
            ShowError(error);
            return;
        }

        WindowsPath = Path.GetFullPath(windowsPath);
        LinuxPath = linuxPath;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
