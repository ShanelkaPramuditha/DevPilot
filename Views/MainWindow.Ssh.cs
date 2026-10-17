using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using DevPilot.Models;
using DevPilot.Navigation;
using DevPilot.Services.Platform;
using DevPilot.Services.Ssh;
using DevPilot.Views.Dialogs;
using Microsoft.Win32;

namespace DevPilot.Views;

/// <summary>
/// The "SSH" page: the hosts in the SSH client config file (by default
/// %UserProfile%\.ssh\config, or a file the user chose), with connect, test and edit.
/// </summary>
public partial class MainWindow
{
    private readonly ObservableCollection<SshHost> _sshHosts = [];

    private static string SshConfigPath => SshService.ResolveConfigPath(App.Preferences.SshConfigPath);

    private SshHost? SelectedSshHost => SshHostList.SelectedItem as SshHost;

    private void InitializeSsh()
    {
        SshHostList.ItemsSource = _sshHosts;
        CollectionViewSource.GetDefaultView(_sshHosts).Filter = MatchesSshSearch;
        LoadSshConfig();
    }

    /// <summary>Reads the config file again (it may have been edited elsewhere) and shows its hosts.</summary>
    private void LoadSshConfig()
    {
        string path = SshConfigPath;
        bool isDefault = SshService.IsDefaultConfig(path);
        string? selected = SelectedSshHost?.Patterns;

        SshPathText.Text = path;
        SshDefaultButton.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
        _sshHosts.Clear();

        if (!File.Exists(path))
        {
            SshPathHint.Text = isDefault ? "Default SSH config file — not created yet" : "Chosen file — not found";
            SshOpenFileButton.IsEnabled = false;
            ShowSshEmpty(
                isDefault ? "No SSH config file yet" : "The chosen SSH config file was not found",
                $"DevPilot looked for {path}. Create it, or choose the config file you use.",
                showFileActions: true);
            PageInfo.SetBadge(SshTab, "");
            return;
        }

        SshConfigDocument document;
        try
        {
            document = SshService.Load(path);
        }
        catch (Exception ex)
        {
            SshPathHint.Text = "Could not be read";
            ShowSshEmpty("The SSH config file could not be read", ex.Message, showFileActions: true);
            return;
        }

        foreach (var host in document.GetHosts())
        {
            _sshHosts.Add(host);
        }

        SshOpenFileButton.IsEnabled = true;
        SshHostList.SelectedItem = _sshHosts.FirstOrDefault(h => h.Patterns == selected);

        var hint = new List<string>
        {
            isDefault ? "Default SSH config file" : "Chosen file (DevPilot passes it to ssh with -F)",
            _sshHosts.Count == 1 ? "1 host" : $"{_sshHosts.Count.ToString(CultureInfo.CurrentCulture)} hosts"
        };
        if (document.Includes.Count > 0)
        {
            hint.Add($"also includes {string.Join(", ", document.Includes)} (not listed here)");
        }

        SshPathHint.Text = string.Join("  ·  ", hint);
        PageInfo.SetBadge(SshTab, _sshHosts.Count == 0 ? "" : _sshHosts.Count.ToString(CultureInfo.CurrentCulture));

        if (_sshHosts.Count == 0)
        {
            ShowSshEmpty("No hosts yet", "Add a host to connect with a short name, like ssh web, instead of typing addresses and options.", showFileActions: false);
        }
        else
        {
            SshEmptyPanel.Visibility = Visibility.Collapsed;
            SshHostList.Visibility = Visibility.Visible;
        }
    }

    private void ShowSshEmpty(string title, string text, bool showFileActions)
    {
        SshEmptyTitle.Text = title;
        SshEmptyText.Text = text;
        SshMissingActions.Visibility = showFileActions ? Visibility.Visible : Visibility.Collapsed;
        SshEmptyPanel.Visibility = Visibility.Visible;
        SshHostList.Visibility = Visibility.Collapsed;
    }

    #region Config file

    private void SshReload_Click(object sender, RoutedEventArgs e)
    {
        LoadSshConfig();
        SshStatusText.Text = $"Reloaded at {DateTime.Now:T}.";
    }

    private void SshCreateFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SshService.Save(SshConfigPath, SshConfigDocument.Parse("# SSH client configuration. Hosts are added below.\n"));
            LoadSshConfig();
            SshStatusText.Text = $"Created {SshConfigPath}.";
        }
        catch (Exception ex)
        {
            ShowError($"Could not create {SshConfigPath}: {ex.Message}");
        }
    }

    private void SshChangeFile_Click(object sender, RoutedEventArgs e)
    {
        string current = SshConfigPath;
        string? folder = Path.GetDirectoryName(current);
        var dialog = new OpenFileDialog
        {
            Title = "Choose your SSH config file",
            Filter = "SSH config files|*.*",
            CheckFileExists = true,
            InitialDirectory = folder is not null && Directory.Exists(folder) ? folder : null
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        UseSshConfig(dialog.FileName);
        SshStatusText.Text = $"Now managing {dialog.FileName}.";
    }

    private void SshUseDefault_Click(object sender, RoutedEventArgs e)
    {
        UseSshConfig(null);
        SshStatusText.Text = "Now managing the default SSH config file.";
    }

    /// <summary>Remembers the chosen file; the default path is stored as "no choice".</summary>
    private void UseSshConfig(string? path)
    {
        App.Preferences.SshConfigPath = path is null || SshService.IsDefaultConfig(path) ? null : path;
        App.Preferences.Save();
        LoadSshConfig();
    }

    private void SshOpenFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (VsCode.IsInstalled)
            {
                VsCode.OpenFile(SshConfigPath);
            }
            else
            {
                Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{SshConfigPath}\"", UseShellExecute = true });
            }

            SshStatusText.Text = "Opened in the editor. Click Reload (right-click menu) after saving changes there.";
        }
        catch (Exception ex)
        {
            ShowError($"Could not open {SshConfigPath}: {ex.Message}");
        }
    }

    #endregion

    #region Hosts

    private void SshMenu_Opened(object sender, RoutedEventArgs e)
    {
        var host = SelectedSshHost;
        var visibility = host is null ? Visibility.Collapsed : Visibility.Visible;
        foreach (var item in new FrameworkElement[]
        {
            SshConnectItem, SshTestItem, SshCopyItem, SshEditSeparator,
            SshEditItem, SshDuplicateItem, SshDeleteItem, SshGeneralSeparator
        })
        {
            item.Visibility = visibility;
        }

        bool canConnect = host?.CanConnect == true;
        SshConnectItem.IsEnabled = canConnect && SshService.SshExe is not null;
        SshTestItem.IsEnabled = canConnect && SshService.SshExe is not null;
        SshCopyItem.IsEnabled = canConnect;

        // Match blocks use conditions rather than names; edit those in the file itself.
        SshEditItem.IsEnabled = host is { IsMatchBlock: false };
        SshDuplicateItem.IsEnabled = host is { IsMatchBlock: false };
    }

    private void SshHostList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedSshHost is { CanConnect: true })
        {
            SshConnect_Click(sender, e);
        }
    }

    private void SshConnect_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSshHost is not { Alias: { } alias })
        {
            return;
        }

        try
        {
            SshService.Connect(alias, SshConfigPath);
            SshStatusText.Text = $"Opened an SSH session to {alias} in a new window.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void SshTest_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSshHost is not { Alias: { } alias })
        {
            return;
        }

        SshStatusText.Text = $"Testing the connection to {alias}...";
        try
        {
            var (success, message) = await SshService.TestAsync(alias, SshConfigPath);
            SshStatusText.Text = success ? $"{alias}: {message}" : $"{alias}: the test failed.";
            if (!success)
            {
                MessageBox.Show(this,
                    $"Could not log in to {alias} without a prompt:\n\n{message}\n\n" +
                    "The test only uses your keys and ssh-agent, so hosts that ask for a password, or that you " +
                    "have never connected to (new host key), fail here. Use Connect to answer those prompts.",
                    "Test Connection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            SshStatusText.Text = "";
            ShowError(ex.Message);
        }
    }

    private void SshCopyCommand_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSshHost is not { Alias: { } alias })
        {
            return;
        }

        string command = SshService.CommandFor(alias, SshConfigPath);
        Clipboard.SetText(command);
        SshStatusText.Text = $"Copied: {command}";
    }

    private void SshAddHost_Click(object sender, RoutedEventArgs e) => EditSshHost(null, "Add Host", copy: false);

    private void SshEditHost_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSshHost is { } host)
        {
            EditSshHost(host, $"Edit {host.Patterns}", copy: false);
        }
    }

    private void SshDuplicateHost_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSshHost is { } host)
        {
            EditSshHost(host with { Patterns = $"{host.Alias ?? host.Patterns}-copy" }, $"Duplicate {host.Patterns}", copy: true);
        }
    }

    /// <summary>Shows the host dialog, then adds or updates the host in the file.</summary>
    private void EditSshHost(SshHost? host, string title, bool copy)
    {
        bool isUpdate = host is not null && !copy;
        var otherNames = _sshHosts
            .Where(h => !h.IsMatchBlock && !(isUpdate && h.BlockIndex == host!.BlockIndex))
            .SelectMany(h => h.Patterns.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var dialog = new SshHostWindow(host, otherNames, title) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } edit)
        {
            return;
        }

        ChangeSshConfig(document =>
        {
            if (isUpdate)
            {
                document.UpdateHost(FindCurrentBlock(document, host!), edit);
            }
            else
            {
                document.AddHost(edit);
            }
        }, isUpdate ? $"Saved {edit.Patterns}." : $"Added {edit.Patterns}.");
    }

    private void SshDeleteHost_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSshHost is not { } host)
        {
            return;
        }

        var answer = MessageBox.Show(this,
            $"Delete {host.KindText.ToLowerInvariant()} \"{host.Patterns}\" from the SSH config?\n\n" +
            "Its options and the comment lines directly above it are removed. The previous file is kept as config.devpilot.bak.",
            "Delete Host", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        ChangeSshConfig(document => document.RemoveHost(FindCurrentBlock(document, host)), $"Deleted {host.Patterns}.");
    }

    /// <summary>
    /// Applies a change to the file as it is on disk now (it may have been edited elsewhere
    /// since it was shown), saves it with a backup, and reloads the list.
    /// </summary>
    private void ChangeSshConfig(Action<SshConfigDocument> change, string done)
    {
        string path = SshConfigPath;
        try
        {
            var document = SshService.Load(path);
            change(document);
            SshService.Save(path, document);
            LoadSshConfig();
            SshStatusText.Text = File.Exists(path + ".devpilot.bak")
                ? $"{done} The previous version is kept as {Path.GetFileName(path)}.devpilot.bak."
                : done;
        }
        catch (Exception ex)
        {
            LoadSshConfig();
            ShowError(ex.Message);
        }
    }

    /// <summary>Finds the host's block in a freshly loaded file, by position and then by name.</summary>
    private static int FindCurrentBlock(SshConfigDocument document, SshHost host)
    {
        var hosts = document.GetHosts();
        if (host.BlockIndex < hosts.Count && hosts[host.BlockIndex].Patterns == host.Patterns)
        {
            return host.BlockIndex;
        }

        var matches = hosts.Where(h => h.Patterns == host.Patterns).ToList();
        return matches.Count == 1
            ? matches[0].BlockIndex
            : throw new InvalidOperationException(
                $"The SSH config file was changed outside DevPilot and \"{host.Patterns}\" could not be found reliably. The list has been reloaded; please try again.");
    }

    private void SshSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        SshSearchHint.Visibility = SshSearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CollectionViewSource.GetDefaultView(_sshHosts).Refresh();
    }

    private bool MatchesSshSearch(object item)
    {
        string search = SshSearchBox.Text.Trim();
        return search.Length == 0 || item is not SshHost host
            || new[] { host.Patterns, host.HostName, host.User, host.ProxyJump }
                .Any(v => v?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
    }

    #endregion
}
