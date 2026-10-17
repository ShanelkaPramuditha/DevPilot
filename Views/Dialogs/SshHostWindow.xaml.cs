using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DevPilot.Models;
using Microsoft.Win32;

namespace DevPilot.Views.Dialogs;

/// <summary>
/// Adds or edits one Host block of the SSH config. Only the fields shown are changed;
/// the block's other options are kept.
/// </summary>
public partial class SshHostWindow : Window
{
    private readonly IReadOnlySet<string> _otherHostNames;

    /// <param name="host">The host to edit or copy, or null for a new host.</param>
    /// <param name="otherHostNames">Names used by the other hosts, to prevent duplicates.</param>
    /// <param name="title">Window title, for example "Add Host".</param>
    public SshHostWindow(SshHost? host, IEnumerable<string> otherHostNames, string title)
    {
        InitializeComponent();
        Title = title;
        _otherHostNames = otherHostNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (host is not null)
        {
            PatternsBox.Text = host.Patterns;
            HostNameBox.Text = host.HostName ?? "";
            UserBox.Text = host.User ?? "";
            PortBox.Text = host.Port ?? "";
            IdentityFileBox.Text = host.IdentityFile ?? "";
            ProxyJumpBox.Text = host.ProxyJump ?? "";
            ForwardAgentBox.SelectedItem = ForwardAgentBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals(i.Tag as string, host.ForwardAgent, StringComparison.OrdinalIgnoreCase))
                ?? ForwardAgentBox.Items[0];

            if (host.OtherOptions.Count > 0)
            {
                OtherOptionsPanel.Visibility = Visibility.Visible;
                OtherOptionsText.Text = string.Join("\n", host.OtherOptions.Select(o => $"{o.Key} {o.Value}"));
            }
        }

        Loaded += (_, _) =>
        {
            PatternsBox.Focus();
            PatternsBox.SelectAll();
        };
    }

    /// <summary>The values to save, set when the dialog closes with Save.</summary>
    public SshHostEdit? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string? error = Validate();
        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        static string? Value(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

        Result = new SshHostEdit(
            Patterns: string.Join(' ', PatternsBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
            HostName: Value(HostNameBox),
            User: Value(UserBox),
            Port: Value(PortBox),
            IdentityFile: Value(IdentityFileBox),
            ProxyJump: Value(ProxyJumpBox),
            ForwardAgent: (ForwardAgentBox.SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } forward ? forward : null);
        DialogResult = true;
    }

    private string? Validate()
    {
        string[] names = PatternsBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0)
        {
            return "Enter a host name to connect with.";
        }

        // Config lines cannot contain line breaks, and quotes or # would change their meaning.
        foreach (var box in new[] { PatternsBox, HostNameBox, UserBox, PortBox, IdentityFileBox, ProxyJumpBox })
        {
            if (box.Text.IndexOfAny(['\r', '\n', '"', '#']) >= 0)
            {
                return "Values cannot contain line breaks, quotes (\") or #.";
            }
        }

        if (HostNameBox.Text.Trim().Contains(' ', StringComparison.Ordinal) || UserBox.Text.Trim().Contains(' ', StringComparison.Ordinal))
        {
            return "Address and user cannot contain spaces.";
        }

        if (PortBox.Text.Trim().Length > 0
            && (!int.TryParse(PortBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535))
        {
            return "Port must be a number from 1 to 65535.";
        }

        string? taken = names.FirstOrDefault(_otherHostNames.Contains);
        return taken is null ? null : $"Another host is already called \"{taken}\". SSH would only use the first one.";
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        string sshFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        var dialog = new OpenFileDialog
        {
            Title = "Choose the private key",
            Filter = "Private keys|*.*",
            InitialDirectory = Directory.Exists(sshFolder) ? sshFolder : null
        };

        if (dialog.ShowDialog(this) == true)
        {
            // Keys under the user's .ssh folder are written as ~/.ssh/<name>, like ssh-keygen suggests.
            string path = dialog.FileName;
            IdentityFileBox.Text = path.StartsWith(sshFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? "~/.ssh/" + path[(sshFolder.Length + 1)..].Replace('\\', '/')
                : path;
        }
    }
}
