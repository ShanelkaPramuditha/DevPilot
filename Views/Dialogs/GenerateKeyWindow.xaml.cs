using System.IO;
using System.Windows;
using System.Windows.Controls;
using DevPilot.Services.Ssh;

namespace DevPilot.Views.Dialogs;

/// <summary>Asks for the settings of a new SSH key pair.</summary>
public partial class GenerateKeyWindow : Window
{
    public GenerateKeyWindow()
    {
        InitializeComponent();
        CommentBox.Text = $"{Environment.UserName.ToLowerInvariant()}@{Environment.MachineName.ToLowerInvariant()}";
        SuggestName();
        Loaded += (_, _) => NameBox.Focus();
    }

    public string KeyPath { get; private set; } = "";

    public string KeyType => (TypeBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "ed25519";

    public string Comment => CommentBox.Text.Trim();

    public string Passphrase => PassphraseBox.Password;

    public bool AddToAgent => AddToAgentBox.IsChecked == true;

    private void TypeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            SuggestName();
        }
    }

    /// <summary>id_ed25519, or id_ed25519_2 when that file exists already.</summary>
    private void SuggestName()
    {
        string baseName = $"id_{KeyType}";
        string name = baseName;
        for (int i = 2; File.Exists(Path.Combine(SshKeyService.SshFolder, name)); i++)
        {
            name = $"{baseName}_{i}";
        }

        NameBox.Text = name;
        PathHint.Text = $"Saved in {SshKeyService.SshFolder}. Keys named id_ed25519 or id_rsa are used by ssh automatically.";
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        string? error =
            name.Length == 0 ? "Enter a file name." :
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? "The file name contains characters Windows does not allow." :
            File.Exists(Path.Combine(SshKeyService.SshFolder, name)) || File.Exists(Path.Combine(SshKeyService.SshFolder, name + ".pub"))
                ? $"{name} already exists. Choose another name." :
            PassphraseBox.Password != ConfirmBox.Password ? "The passphrases do not match." :
            PassphraseBox.Password.Length is > 0 and < 5 ? "Use at least 5 characters for the passphrase, or leave it empty." :
            Comment.Contains('"', StringComparison.Ordinal) ? "The comment cannot contain quotes." :
            null;

        if (error is not null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        KeyPath = Path.Combine(SshKeyService.SshFolder, name);
        DialogResult = true;
    }
}
