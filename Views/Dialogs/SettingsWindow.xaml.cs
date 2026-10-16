using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DevPilot.Core;
using DevPilot.Models;
using DevPilot.Services.Wsl;
using Microsoft.Win32;

namespace DevPilot.Views.Dialogs;

/// <summary>
/// Edits a distribution's /etc/wsl.conf, the shared %UserProfile%\.wslconfig
/// (resource limits, networking) and the distribution's virtual disk.
/// </summary>
public partial class SettingsWindow : Window
{
    private sealed record Editor(SettingDefinition Definition, bool IsGlobal, Func<string?> Get, Action<string?> Set);

    private readonly List<Editor> _editors = [];
    private readonly bool _openGlobal;
    private WslDistro? _distro;
    private IniDocument _distroDoc = IniDocument.Parse("");
    private IniDocument _globalDoc = IniDocument.Parse("");
    private string _distroSaved = "";
    private string _globalSaved = "";
    private bool _distroLoaded;
    private bool? _rawIsGlobal;
    private TabItem? _currentTab;
    private bool _busy;
    private bool _closeConfirmed;

    /// <param name="distro">Distribution to configure, or null for the shared WSL settings only.</param>
    /// <param name="openGlobal">Start on the shared WSL settings tab.</param>
    public SettingsWindow(WslDistro? distro, bool openGlobal = false)
    {
        InitializeComponent();
        _distro = distro;
        _openGlobal = openGlobal || distro is null;

        if (distro is null)
        {
            Title = "WSL Settings";
            DistroTab.Visibility = Visibility.Collapsed;
            DiskTab.Visibility = Visibility.Collapsed;
        }
        else
        {
            Title = $"Settings - {distro.Name}";
            DistroNote.Text =
                $"These settings are saved in /etc/wsl.conf inside {distro.Name} and only affect this distribution. " +
                "They take effect after the distribution restarts. Leave a value on Default to let WSL decide.";

            if (distro.Version != "2" || distro.VhdPath is null)
            {
                DiskTab.Visibility = Visibility.Collapsed;
            }
        }
    }

    private bool HasDistro => _distro is not null;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _globalDoc = IniDocument.Parse(SafeReadGlobal());
        _globalSaved = _globalDoc.ToString();
        BuildForm(GlobalForm, WslSettings.GlobalSettings, isGlobal: true, users: null);
        LoadForms();

        FileSelector.Items.Clear();
        if (_distro is { } d)
        {
            FileSelector.Items.Add(new ComboBoxItem { Content = $"/etc/wsl.conf  ({d.Name})", Tag = false });
        }

        FileSelector.Items.Add(new ComboBoxItem { Content = @"%UserProfile%\.wslconfig  (all distributions)", Tag = true });

        Tabs.SelectedItem = _openGlobal ? GlobalTab : DistroTab;
        _currentTab = Tabs.SelectedItem as TabItem;

        if (_distro is { } distro)
        {
            UpdateDiskInfo();
            await RunBusyAsync($"Reading settings from {distro.Name}...", async () =>
            {
                var usersTask = WslSettings.GetUsersAsync(distro.Name);
                string text = await WslSettings.ReadWslConfAsync(distro.Name);
                var (users, current) = await usersTask;

                _distroDoc = IniDocument.Parse(text);
                _distroSaved = _distroDoc.ToString();
                _distroLoaded = true;

                BuildForm(DistroForm, WslSettings.DistroSettings, isGlobal: false, users,
                    currentUser: current);
                LoadForms();
            });

            if (!_distroLoaded)
            {
                var error = new TextBlock
                {
                    Text = $"The settings of {distro.Name} could not be read.",
                    TextWrapping = TextWrapping.Wrap
                };
                error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Error");
                DistroForm.Children.Add(error);
            }
        }

        StatusText.Text = "Ready";
    }

    #region Form

    /// <summary>
    /// Creates one row per setting: its name and description on the left,
    /// the editor on the right.
    /// </summary>
    private void BuildForm(StackPanel panel, IEnumerable<SettingDefinition> definitions, bool isGlobal,
        IReadOnlyList<string>? users, string? currentUser = null)
    {
        panel.Children.Clear();
        _editors.RemoveAll(e => e.IsGlobal == isGlobal);

        foreach (var group in definitions.GroupBy(d => d.Group))
        {
            panel.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 14, 0, 6)
            });

            foreach (var definition in group)
            {
                var definitionForRow = definition;
                if (!isGlobal && $"{definition.Section}.{definition.Key}" == WslSettings.DefaultUserKey && users is not null)
                {
                    definitionForRow = definition with
                    {
                        Choices = users,
                        DefaultText = currentUser is null ? definition.DefaultText : $"current: {currentUser}"
                    };
                }

                var (control, editor) = CreateEditor(definitionForRow, isGlobal);
                _editors.Add(editor);
                panel.Children.Add(CreateRow(definitionForRow, control));
            }
        }
    }

    private static Grid CreateRow(SettingDefinition definition, FrameworkElement control)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });

        var text = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock { Text = definition.Label, FontWeight = FontWeights.SemiBold });
        var description = new TextBlock { Text = definition.Description, TextWrapping = TextWrapping.Wrap };
        description.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        text.Children.Add(description);

        control.VerticalAlignment = VerticalAlignment.Center;
        control.ToolTip = $"{definition.Section}.{definition.Key}";
        Grid.SetColumn(control, 1);
        row.Children.Add(text);
        row.Children.Add(control);
        return row;
    }

    private static (FrameworkElement Control, Editor Editor) CreateEditor(SettingDefinition definition, bool isGlobal)
    {
        if (definition.Kind is SettingKind.Bool or SettingKind.Choice)
        {
            var combo = new ComboBox { Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center };
            combo.Items.Add(new ComboBoxItem { Content = $"Default ({definition.DefaultText})", Tag = null });

            if (definition.Kind == SettingKind.Bool)
            {
                combo.Items.Add(new ComboBoxItem { Content = "On", Tag = "true" });
                combo.Items.Add(new ComboBoxItem { Content = "Off", Tag = "false" });
            }
            else
            {
                foreach (string choice in definition.Choices ?? [])
                {
                    combo.Items.Add(new ComboBoxItem { Content = choice, Tag = choice });
                }
            }

            string? Get() => (combo.SelectedItem as ComboBoxItem)?.Tag as string;

            void Set(string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    combo.SelectedIndex = 0;
                    return;
                }

                var match = combo.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(i => i.Tag is string tag && tag.Equals(value, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    // Keep values this app does not know about.
                    match = new ComboBoxItem { Content = value, Tag = value };
                    combo.Items.Add(match);
                }

                combo.SelectedItem = match;
            }

            combo.SelectedIndex = 0;
            return (combo, new Editor(definition, isGlobal, Get, Set));
        }

        // Text, size and number: a text box with the default shown while empty.
        var box = new TextBox { Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center };
        var hint = new TextBlock
        {
            Text = $"Default: {definition.DefaultText}",
            Margin = new Thickness(9, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var container = new Grid();
        container.Children.Add(box);
        container.Children.Add(hint);

        return (container, new Editor(definition, isGlobal, () => box.Text.Trim(), value => box.Text = value ?? ""));
    }

    private void LoadForms()
    {
        foreach (var editor in _editors)
        {
            var doc = editor.IsGlobal ? _globalDoc : _distroDoc;
            editor.Set(doc.Get(editor.Definition.Section, editor.Definition.Key));
        }
    }

    /// <summary>
    /// Copies the form values into the documents. Returns the problems found;
    /// values with problems are left unchanged.
    /// </summary>
    private List<string> ApplyForms()
    {
        var problems = new List<string>();

        foreach (var editor in _editors)
        {
            if (!editor.IsGlobal && !_distroLoaded)
            {
                continue;
            }

            var definition = editor.Definition;
            string? value = editor.Get();

            if (!string.IsNullOrWhiteSpace(value))
            {
                switch (definition.Kind)
                {
                    case SettingKind.Size when value.Trim() == "0":
                        value = "0";
                        break;
                    case SettingKind.Size:
                        string? size = WslSettings.NormalizeSize(value);
                        if (size is null)
                        {
                            problems.Add($"{definition.Label}: enter a size such as 4GB or 512MB.");
                            continue;
                        }

                        value = size;
                        break;
                    case SettingKind.Number when !int.TryParse(value, out int number) || number < 0:
                        problems.Add($"{definition.Label}: enter a whole number.");
                        continue;
                }
            }

            var doc = editor.IsGlobal ? _globalDoc : _distroDoc;
            doc.Set(definition.Section, definition.Key, value);
        }

        return problems;
    }

    #endregion

    #region Config file text

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Tabs || Tabs.SelectedItem is not TabItem selected || selected == _currentTab)
        {
            return;
        }

        // Leaving the text editor: its text becomes the source of truth.
        if (_currentTab == FilesTab)
        {
            CommitRaw();
            LoadForms();
        }

        if (selected == FilesTab)
        {
            ShowProblems(ApplyForms());
            if (FileSelector.SelectedIndex < 0)
            {
                FileSelector.SelectedIndex = _openGlobal || !HasDistro ? FileSelector.Items.Count - 1 : 0;
            }
            else
            {
                ShowRaw();
            }
        }

        _currentTab = selected;
    }

    private void FileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitRaw();
        ShowRaw();
    }

    private void ShowRaw()
    {
        if (FileSelector.SelectedItem is not ComboBoxItem { Tag: bool isGlobal })
        {
            return;
        }

        bool unavailable = !isGlobal && !_distroLoaded;
        _rawIsGlobal = unavailable ? null : isGlobal;
        RawBox.IsEnabled = !unavailable;
        RawBox.Text = unavailable ? "" : (isGlobal ? _globalDoc : _distroDoc).ToString();
        FileHint.Text = isGlobal
            ? File.Exists(WslSettings.GlobalConfigPath)
                ? $"{WslSettings.GlobalConfigPath}. Shared by all WSL 2 distributions."
                : $"{WslSettings.GlobalConfigPath} does not exist yet. It is created when you save."
            : unavailable ? "Could not be read." : "Inside the distribution. Saved as root.";
    }

    private void CommitRaw()
    {
        if (_rawIsGlobal is not { } isGlobal)
        {
            return;
        }

        var doc = IniDocument.Parse(RawBox.Text);
        if (isGlobal)
        {
            _globalDoc = doc;
        }
        else
        {
            _distroDoc = doc;
        }
    }

    #endregion

    #region Save

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync(restart: false);

    private async void SaveRestart_Click(object sender, RoutedEventArgs e) => await SaveAsync(restart: true);

    /// <summary>Brings the documents up to date with whatever is being edited.</summary>
    private bool PrepareSave()
    {
        if (Tabs.SelectedItem == FilesTab)
        {
            CommitRaw();
            LoadForms();
            return true;
        }

        var problems = ApplyForms();
        ShowProblems(problems);
        return problems.Count == 0;
    }

    private bool DistroChanged => _distroLoaded && _distroDoc.ToString() != _distroSaved;

    private bool GlobalChanged => _globalDoc.ToString() != _globalSaved;

    private async Task<bool> SaveAsync(bool restart)
    {
        if (_busy || !PrepareSave())
        {
            return false;
        }

        bool distroChanged = DistroChanged;
        bool globalChanged = GlobalChanged;

        if (!distroChanged && !globalChanged && !restart)
        {
            StatusText.Text = "No changes to save.";
            return true;
        }

        if (restart && globalChanged)
        {
            var answer = MessageBox.Show(this,
                "The shared WSL settings changed. Applying them shuts down WSL, which stops ALL running distributions.\n\nContinue?",
                "Restart WSL", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        bool saved = false;
        await RunBusyAsync("Saving...", async () =>
        {
            if (distroChanged && _distro is { } distro)
            {
                string text = _distroDoc.ToString();
                await WslSettings.WriteWslConfAsync(distro.Name, text);
                _distroSaved = text;
            }

            if (globalChanged)
            {
                string text = _globalDoc.ToString();
                WslSettings.WriteGlobalConfig(text);
                _globalSaved = text;
            }

            saved = true;

            if (!restart)
            {
                StatusText.Text = (distroChanged, globalChanged) switch
                {
                    (true, true) => "Saved. Shut down WSL to apply the changes.",
                    (false, true) => "Saved. Shut down WSL to apply the changes.",
                    (true, false) => $"Saved. Restart {_distro?.Name} to apply the changes.",
                    _ => "Saved."
                };
                return;
            }

            if (globalChanged)
            {
                StatusText.Text = "Shutting down WSL...";
                await WslService.RunCheckedAsync("--shutdown");
            }
            else if (_distro is { } d)
            {
                StatusText.Text = $"Stopping {d.Name}...";
                await WslService.RunCheckedAsync("--terminate", d.Name);
            }

            if (_distro is { } toStart)
            {
                StatusText.Text = $"Starting {toStart.Name}...";
                await WslService.StartInBackgroundAsync(toStart.Name);
            }

            StatusText.Text = globalChanged ? "Saved and WSL restarted." : $"Saved and {_distro?.Name} restarted.";
        });

        if (!saved)
        {
            StatusText.Text = "The changes were not saved.";
        }

        return saved;
    }

    private void ShowProblems(List<string> problems)
    {
        if (problems.Count > 0)
        {
            StatusText.Text = "Please fix: " + string.Join(" ", problems);
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Error");
        }
        else
        {
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        if (_busy)
        {
            e.Cancel = true;
            return;
        }

        // Compare without showing validation messages.
        if (Tabs.SelectedItem == FilesTab)
        {
            CommitRaw();
        }
        else
        {
            ApplyForms();
        }

        if (!DistroChanged && !GlobalChanged)
        {
            return;
        }

        e.Cancel = true;
        var answer = MessageBox.Show(this, "Save your changes before closing?", "Unsaved Changes",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !await SaveAsync(restart: false)))
        {
            return;
        }

        _closeConfirmed = true;
        Close();
    }

    #endregion

    #region Disk

    private void UpdateDiskInfo()
    {
        if (_distro is not { VhdPath: { } vhd } distro)
        {
            return;
        }

        LocationBox.Text = distro.Location ?? Path.GetDirectoryName(vhd) ?? "";

        bool sparse = false;
        try
        {
            var info = new FileInfo(vhd);
            sparse = info.Exists && info.Attributes.HasFlag(FileAttributes.SparseFile);
            DiskSizeText.Text = info.Exists ? $"{vhd}  ·  {Format.Size(info.Length)}" : vhd;
        }
        catch (Exception ex)
        {
            DiskSizeText.Text = $"{vhd} ({ex.Message})";
        }

        SparseStatusText.Text = sparse ? "On: freed space is given back to Windows." : "Off";
        SparseButton.Content = sparse ? "Turn Off" : "Turn On";
        SparseButton.Tag = !sparse;
    }

    private async Task RefreshDistroAsync()
    {
        if (_distro is null)
        {
            return;
        }

        var distros = await WslService.GetInstalledAsync();
        _distro = distros.FirstOrDefault(d => d.Name == _distro.Name) ?? _distro;
        UpdateDiskInfo();
    }

    /// <summary>Disk changes need the distribution stopped.</summary>
    private bool ConfirmStop(string action)
    {
        return MessageBox.Show(this,
            $"{action}\n\n{_distro!.Name} will be stopped first. Programs running in it will be closed. Continue?",
            "Change Disk", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private async void Sparse_Click(object sender, RoutedEventArgs e)
    {
        if (_distro is not { } distro || SparseButton.Tag is not bool enable)
        {
            return;
        }

        if (!ConfirmStop(enable ? "Turn on sparse disk for this distribution?" : "Turn off sparse disk for this distribution?"))
        {
            return;
        }

        string value = enable ? "true" : "false";
        await RunBusyAsync("Changing the disk...", async () =>
        {
            await WslService.RunCheckedAsync("--terminate", distro.Name);
            var result = await WslService.RunAsync("--manage", distro.Name, "--set-sparse", value);

            // Some WSL versions only allow sparse disks when explicitly forced.
            if (!result.Succeeded && result.Output.Contains("allow-unsafe", StringComparison.OrdinalIgnoreCase))
            {
                var answer = MessageBox.Show(this,
                    $"WSL reports:\n\n{result.Output}\n\nForce it anyway? Export a backup first if the data is important.",
                    "Sparse Disk", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }

                result = await WslService.RunAsync("--manage", distro.Name, "--set-sparse", value, "--allow-unsafe");
            }

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Output);
            }

            await RefreshDistroAsync();
            StatusText.Text = enable ? "Sparse disk turned on." : "Sparse disk turned off.";
        });
    }

    private async void Resize_Click(object sender, RoutedEventArgs e)
    {
        if (_distro is not { } distro)
        {
            return;
        }

        string? size = WslSettings.NormalizeSize(ResizeBox.Text);
        if (size is null)
        {
            ShowProblems([$"Maximum disk size: enter a size such as 256GB."]);
            return;
        }

        if (!ConfirmStop($"Set the maximum disk size of {distro.Name} to {size}?"))
        {
            return;
        }

        await RunBusyAsync($"Resizing the disk to {size}...", async () =>
        {
            await WslService.RunCheckedAsync("--terminate", distro.Name);
            await WslService.RunCheckedAsync("--manage", distro.Name, "--resize", size);
            await RefreshDistroAsync();
            StatusText.Text = $"The maximum disk size is now {size}.";
        });
    }

    private async void Move_Click(object sender, RoutedEventArgs e)
    {
        if (_distro is not { } distro)
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = $"Choose where to move {distro.Name}" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string target = Path.Combine(dialog.FolderName, distro.Name);
        if (!ConfirmStop($"Move {distro.Name} to {target}?\n\nThis copies the whole disk file and can take a while."))
        {
            return;
        }

        await RunBusyAsync($"Moving {distro.Name}... this can take several minutes.", async () =>
        {
            Directory.CreateDirectory(target);
            await WslService.RunCheckedAsync("--terminate", distro.Name);
            await WslService.RunCheckedAsync("--manage", distro.Name, "--move", target);
            await RefreshDistroAsync();
            StatusText.Text = $"{distro.Name} was moved to {target}.";
        });
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(LocationBox.Text))
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{LocationBox.Text}\"", UseShellExecute = true });
        }
    }

    #endregion

    private async Task RunBusyAsync(string message, Func<Task> action)
    {
        _busy = true;
        StatusText.Text = message;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        BusyProgress.Visibility = Visibility.Visible;
        Tabs.IsEnabled = false;
        SaveButton.IsEnabled = false;
        SaveRestartButton.IsEnabled = false;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed.";
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            BusyProgress.Visibility = Visibility.Collapsed;
            Tabs.IsEnabled = true;
            SaveButton.IsEnabled = true;
            SaveRestartButton.IsEnabled = true;
        }
    }

    private static string SafeReadGlobal()
    {
        try
        {
            return WslSettings.ReadGlobalConfig();
        }
        catch
        {
            return "";
        }
    }
}
