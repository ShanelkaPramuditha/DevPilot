using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using DevPilot.Models;
using DevPilot.Navigation;
using DevPilot.Services.Ssh;
using DevPilot.Views.Dialogs;
using Microsoft.VisualBasic.FileIO;

namespace DevPilot.Views;

/// <summary>
/// The "SSH Keys" page: key pairs in %UserProfile%\.ssh, generating keys, copying public
/// keys, and ssh-agent.
/// </summary>
public partial class MainWindow
{
    private bool _agentRunning;

    private SshKey? SelectedKey => KeyList.SelectedItem as SshKey;

    private async Task LoadKeysAsync()
    {
        string? selected = SelectedKey?.Name;
        var keys = SshKeyService.FindKeys(SshKeyService.SshFolder);

        HashSet<string>? loaded = null;
        try
        {
            loaded = await SshKeyService.GetAgentFingerprintsAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Agent status is informational.
        }

        _agentRunning = loaded is not null;
        if (loaded is not null)
        {
            keys = keys.Select(k => k with { InAgent = k.Fingerprint is not null && loaded.Contains(k.Fingerprint) }).ToList();
        }

        KeyList.ItemsSource = keys;
        KeyList.SelectedItem = keys.FirstOrDefault(k => k.Name == selected);
        KeysEmptyPanel.Visibility = keys.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        KeyList.Visibility = keys.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PageInfo.SetBadge(KeysTab, keys.Count == 0 ? "" : keys.Count.ToString(System.Globalization.CultureInfo.CurrentCulture));

        AgentDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, _agentRunning ? "Brush.Success" : "Brush.TextMuted");
        AgentText.Text = _agentRunning
            ? $"ssh-agent is running · {loaded!.Count} key(s) loaded"
            : "ssh-agent is not running";
        AgentHint.Text = _agentRunning
            ? "Keys in the agent are used without typing their passphrase again."
            : "Turn it on to type each key's passphrase only once per sign-in.";
        AgentButton.Visibility = _agentRunning ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void KeyGenerate_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new GenerateKeyWindow { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            KeysStatusText.Text = "Generating the key...";
            await SshKeyService.GenerateAsync(dialog.KeyPath, dialog.KeyType, dialog.Comment, dialog.Passphrase);
            await LoadKeysAsync();

            Clipboard.SetText((await File.ReadAllTextAsync(dialog.KeyPath + ".pub")).Trim());
            KeysStatusText.Text = $"Created {Path.GetFileName(dialog.KeyPath)}. Its public key is on the clipboard: paste it into GitHub " +
                "(Settings → SSH and GPG keys) or a server's ~/.ssh/authorized_keys.";

            if (dialog.AddToAgent)
            {
                if (_agentRunning)
                {
                    SshKeyService.AddToAgent(dialog.KeyPath);
                }
                else
                {
                    KeysStatusText.Text += " Turn on ssh-agent to add the key to it.";
                }
            }
        }
        catch (Exception ex)
        {
            KeysStatusText.Text = "";
            ShowError(ex.Message);
        }
    }

    private async void KeyCopy_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedKey is not { PublicPath: { } publicPath } key)
        {
            return;
        }

        Clipboard.SetText((await File.ReadAllTextAsync(publicPath)).Trim());
        KeysStatusText.Text = $"Copied the public key of {key.Name}. Paste it into GitHub (Settings → SSH and GPG keys) or a server's ~/.ssh/authorized_keys.";
    }

    private void KeyList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => KeyCopy_Click(sender, e);

    private async void KeyAddToAgent_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedKey is not { PrivatePath: { } privatePath })
        {
            return;
        }

        try
        {
            SshKeyService.AddToAgent(privatePath);
            KeysStatusText.Text = "Adding the key in a terminal window (it asks for the passphrase, if the key has one)...";
            await Task.Delay(TimeSpan.FromSeconds(4));
            await LoadKeysAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void KeyCreatePublic_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedKey is not { PrivatePath: { } privatePath })
        {
            return;
        }

        try
        {
            await SshKeyService.CreatePublicKeyAsync(privatePath);
            await LoadKeysAsync();
            KeysStatusText.Text = $"Created {Path.GetFileName(privatePath)}.pub.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void KeyShow_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedKey is { } key && (key.PrivatePath ?? key.PublicPath) is { } path)
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true });
        }
    }

    private void KeysOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SshKeyService.SshFolder);
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{SshKeyService.SshFolder}\"", UseShellExecute = true });
    }

    private async void KeyDelete_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedKey is not { } key)
        {
            return;
        }

        var answer = MessageBox.Show(this,
            $"Delete the key {key.Name}?\n\nBoth files go to the Recycle Bin. Servers and services where this key was added will no longer accept it from this PC.",
            "Delete SSH Key", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            foreach (string? path in new[] { key.PrivatePath, key.PublicPath })
            {
                if (path is not null && File.Exists(path))
                {
                    FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }
            }

            await LoadKeysAsync();
            KeysStatusText.Text = $"Moved {key.Name} to the Recycle Bin.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void AgentEnable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SshKeyService.EnableAgentAsync();
            await LoadKeysAsync();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            KeysStatusText.Text = "Cancelled: turning on ssh-agent needs administrator rights.";
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void KeyMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (SelectedKey is not { } key)
        {
            KeyMenu.IsOpen = false;
            return;
        }

        KeyCopyItem.IsEnabled = key.PublicPath is not null;
        KeyAgentItem.IsEnabled = key.PrivatePath is not null && _agentRunning && !key.InAgent;
        KeyAgentItem.Header = key.InAgent ? "Already in ssh-agent" : "_Add to ssh-agent";
        KeyCreatePublicItem.Visibility = key.PublicPath is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
