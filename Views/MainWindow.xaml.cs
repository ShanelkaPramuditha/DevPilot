using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DevPilot.Models;
using DevPilot.Navigation;
using DevPilot.Services.Platform;
using DevPilot.Services.Wsl;

namespace DevPilot.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const int ErrorCancelled = 1223;

    private readonly ObservableCollection<WslDistro> _installed = [];
    private readonly ObservableCollection<OnlineDistro> _online = [];
    private bool _wslInstalled;
    private bool _busy;
    private bool _catalogLoaded;
    private bool _tabChosen;

    public MainWindow()
    {
        InitializeComponent();

        InstalledList.ItemsSource = _installed;
        OnlineList.ItemsSource = _online;
        CollectionViewSource.GetDefaultView(_online).Filter = MatchesSearch;
        InitializeUsage();
        InitializeSidebar();
        RestoreWindowSize();
    }

    private WslDistro? SelectedDistro => InstalledList.SelectedItem as WslDistro;

    private OnlineDistro? SelectedOnlineDistro => OnlineList.SelectedItem as OnlineDistro;

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Checking WSL...", RefreshAllAsync);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _catalogLoaded = false;
        await RunBusyAsync("Refreshing...", RefreshAllAsync);
    }

    #region State and refresh

    /// <summary>
    /// Runs an action with the UI disabled and a busy indicator shown.
    /// Errors are reported in a message box.
    /// </summary>
    private async Task RunBusyAsync(string message, Func<Task> action)
    {
        _busy = true;
        BusyText.Text = message;
        BusyProgress.Visibility = Visibility.Visible;
        HeaderPanel.IsEnabled = false;
        DistroTabs.IsEnabled = false;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _busy = false;
            BusyText.Text = "Ready";
            BusyProgress.Visibility = Visibility.Hidden;
            HeaderPanel.IsEnabled = true;
            DistroTabs.IsEnabled = _wslInstalled;
            UpdateButtons();
        }
    }

    private async Task RefreshAllAsync()
    {
        _wslInstalled = await WslService.IsInstalledAsync();

        InstallWslButton.Visibility = _wslInstalled ? Visibility.Collapsed : Visibility.Visible;
        SetStatusDot(_wslInstalled ? "Brush.TextMuted" : "Brush.Critical");

        if (!_wslInstalled)
        {
            WslStatusText.Text = "WSL is not installed";
            WslDetailsText.Text = "Click Install WSL to get started.";
            StatusCard.ToolTip = "WSL was not found on this system. Install it to manage Linux distributions.";
            _installed.Clear();
            _online.Clear();
            _catalogLoaded = false;
            InstalledPlaceholder.Visibility = Visibility.Collapsed;
            ShowOnlinePlaceholder("Install WSL first.");
            UpdateTabHeaders();
            return;
        }

        string details = await GetWslDetailsAsync();
        StatusCard.ToolTip = details.Replace("   ·   ", "\n");
        WslDetailsText.Text = CompactDetails(details);

        await RefreshInstalledAsync();
        await RefreshCatalogAsync();

        // Start on the "Available" tab when there is nothing installed yet.
        if (!_tabChosen)
        {
            _tabChosen = true;
            DistroTabs.SelectedItem = _installed.Count == 0 ? AvailableTab : InstalledTab;
        }
    }

    /// <summary>
    /// One-line summary of the WSL and kernel versions plus the VM memory limit.
    /// </summary>
    private static async Task<string> GetWslDetailsAsync()
    {
        var parts = new List<string>();
        try
        {
            var result = await WslService.RunAsync(TimeSpan.FromSeconds(15), "--version");
            if (result.Succeeded)
            {
                // "WSL version: 2.x" and "Kernel version: 6.x" are the first two lines.
                parts.AddRange(result.Output
                    .Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .Take(2));
            }
        }
        catch
        {
            // Older inbox WSL does not support --version.
        }

        parts.Add($"VM memory: {SystemInfo.WslMemoryText()}");
        return string.Join("   ·   ", parts);
    }

    /// <summary>"WSL version: 3.0.3.0 · Kernel version: ... · VM memory: Up to 7.9 GB" to "WSL 3.0.3.0 · VM up to 7.9 GB".</summary>
    private static string CompactDetails(string details)
    {
        var parts = details.Split("   ·   ");
        string? wsl = parts.FirstOrDefault(p => p.StartsWith("WSL version:", StringComparison.OrdinalIgnoreCase));
        string memory = $"VM {SystemInfo.WslMemoryText().Replace("Up to", "up to")}";
        return wsl is null ? memory : $"WSL {wsl.Split(':', 2)[1].Trim()} · {memory}";
    }

    private async Task RefreshInstalledAsync()
    {
        string? selectedName = SelectedDistro?.Name;

        var distros = await WslService.GetInstalledAsync();
        _installed.Clear();
        foreach (var distro in distros)
        {
            _installed.Add(distro);
        }

        InstalledList.SelectedItem = _installed.FirstOrDefault(d => d.Name == selectedName)
            ?? _installed.FirstOrDefault(d => d.IsDefault);
        InstalledPlaceholder.Visibility = _installed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        int running = _installed.Count(d => d.IsRunning);
        SetStatusDot(running > 0 ? "Brush.Success" : "Brush.TextMuted");
        WslStatusText.Text = _installed.Count == 0
            ? "No distributions yet"
            : running == 0 ? "WSL is idle" : $"{running} of {_installed.Count} running";

        // Keep the status column of the catalog in sync.
        UpdateOnlineStatuses();
        UpdateTabHeaders();
        UpdateButtons();
    }

    /// <summary>
    /// Loads the catalog. "wsl --list --online" decides which distributions
    /// are offered; the catalog file adds the metadata for each one.
    /// </summary>
    private async Task RefreshCatalogAsync()
    {
        if (_catalogLoaded)
        {
            return;
        }

        ShowOnlinePlaceholder("Loading available distributions...");
        _online.Clear();

        var namesTask = WslService.GetOnlineAsync();
        var catalogTask = WslCatalog.GetEntriesAsync();

        List<(string Name, string FriendlyName)>? names = null;
        Dictionary<string, CatalogEntry>? catalog = null;
        string? error = null;

        try
        {
            names = await namesTask;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        try
        {
            catalog = await catalogTask;
        }
        catch (Exception ex)
        {
            error ??= ex.Message;
        }

        IEnumerable<CatalogEntry> entries = names is not null
            ? names.Select(n => catalog?.GetValueOrDefault(n.Name) ?? WslCatalog.Fallback(n.Name, n.FriendlyName))
            : catalog?.Values ?? Enumerable.Empty<CatalogEntry>();

        string ram = SystemInfo.WslMemoryText();
        foreach (var entry in entries)
        {
            _online.Add(new OnlineDistro(entry, ram));
        }

        if (_online.Count == 0)
        {
            ShowOnlinePlaceholder(
                "Could not load the list of distributions. Check your internet connection and click Refresh." +
                (error is null ? "" : $"\n\n{error}"));
            UpdateTabHeaders();
            return;
        }

        _catalogLoaded = true;
        OnlinePlaceholder.Visibility = Visibility.Collapsed;
        CatalogFootnote.Text =
            "Default RAM is the limit of the WSL 2 VM, shared by all running distributions. " +
            "Click a column header to sort, double-click a row to install." +
            (catalog is null ? "\nDetails could not be loaded from the WSL catalog, so some columns are empty." : "");

        UpdateOnlineStatuses();
        UpdateTabHeaders();
        _ = LoadDownloadSizesAsync();
    }

    /// <summary>
    /// Fills in download sizes in the background; the list stays usable meanwhile.
    /// </summary>
    private async Task LoadDownloadSizesAsync()
    {
        var pending = _online.Where(d => d.DownloadUrl is not null && d.DownloadBytes is null).ToList();
        foreach (var distro in pending)
        {
            distro.DownloadSizeLoading = true;
        }

        await Task.WhenAll(pending.Select(async distro =>
        {
            distro.DownloadBytes = await WslCatalog.GetDownloadSizeAsync(distro.DownloadUrl!);
            distro.DownloadSizeLoading = false;
        }));
    }

    private void UpdateOnlineStatuses()
    {
        var installed = _installed.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var distro in _online)
        {
            distro.Status = installed.TryGetValue(distro.Name, out var match)
                ? match.IsRunning ? "Running" : "Installed"
                : distro.IsSupportedOnThisPc ? "Available" : "Not for this PC";
        }

        UpdateButtons();
    }

    private void UpdateTabHeaders()
    {
        PageInfo.SetBadge(InstalledTab, _installed.Count.ToString(CultureInfo.CurrentCulture));
        PageInfo.SetBadge(AvailableTab, _online.Count == 0 ? "" : _online.Count.ToString(CultureInfo.CurrentCulture));
    }

    private void ShowOnlinePlaceholder(string text)
    {
        OnlinePlaceholder.Text = text;
        OnlinePlaceholder.Visibility = Visibility.Visible;
    }

    private void UpdateButtons()
    {
        var distro = SelectedDistro;
        bool hasDistro = distro is not null;

        bool running = distro?.IsRunning == true;

        // Distribution actions only show when a distribution was right-clicked;
        // on empty space the menu offers just Import.
        var visibility = hasDistro ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in new FrameworkElement[]
        {
            OpenTerminalItem, OpenVsCodeItem, OpenFilesItem, FolderMountsItem, SettingsItem, LifecycleSeparator,
            StartDistroItem, RestartDistroItem, StopDistroItem, SettingsSeparator,
            SetDefaultItem, ConvertVersionItem, BackupSeparator, ExportItem,
            UnregisterSeparator, UnregisterItem
        })
        {
            item.Visibility = visibility;
        }

        OpenVsCodeItem.Visibility = hasDistro && VsCode.IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        StartDistroItem.IsEnabled = hasDistro && !running;
        RestartDistroItem.IsEnabled = running;
        StopDistroItem.IsEnabled = running;
        SetDefaultItem.IsEnabled = hasDistro && !distro!.IsDefault;
        ConvertVersionItem.Header = distro?.Version == "2" ? "Convert to WSL 1" : "Convert to WSL 2";

        var online = SelectedOnlineDistro;
        InstallDistroButton.IsEnabled = online is { Status: "Available" };
        InstallDistroItem.IsEnabled = online is { Status: "Available" };
        InstallDistroItem.Visibility = online is null ? Visibility.Collapsed : Visibility.Visible;
        ShowInstalledItem.Visibility = online?.IsInstalled == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InstalledMenu_Opened(object sender, RoutedEventArgs e) => UpdateButtons();

    private void OnlineMenu_Opened(object sender, RoutedEventArgs e)
    {
        UpdateButtons();

        // Nothing to offer when empty space was right-clicked.
        if (SelectedOnlineDistro is null)
        {
            OnlineMenu.IsOpen = false;
        }
    }

    /// <summary>
    /// Right-clicking a row selects it; right-clicking empty space clears the
    /// selection so the menu does not act on a row that was not clicked.
    /// </summary>
    private void List_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not ListView list)
        {
            return;
        }

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
            list.SelectedItem = null;
        }
    }

    private void InstalledList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void OnlineList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CollectionViewSource.GetDefaultView(_online).Refresh();
    }

    private bool MatchesSearch(object item)
    {
        string search = SearchBox.Text.Trim();
        if (search.Length == 0 || item is not OnlineDistro distro)
        {
            return true;
        }

        return new[] { distro.Name, distro.FriendlyName, distro.Publisher, distro.Version, distro.Support, distro.Status }
            .Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Sorts a list by the clicked column. The header's Tag names the property
    /// to sort by; clicking the same column again reverses the order.
    /// </summary>
    private void ListHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Tag: string property } || sender is not ListView list)
        {
            return;
        }

        var view = CollectionViewSource.GetDefaultView(list.ItemsSource);
        var direction = view.SortDescriptions.FirstOrDefault() is { PropertyName: var current, Direction: ListSortDirection.Ascending }
            && current == property
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(property, direction));
    }

    /// <summary>
    /// Refreshes the installed list once a console window closes, without
    /// blocking the UI while the user works in that window.
    /// </summary>
    private async Task RefreshWhenExitedAsync(Process process)
    {
        using (process)
        {
            await process.WaitForExitAsync();
        }

        await RefreshIfIdleAsync();
    }

    private async Task RefreshAfterDelayAsync(TimeSpan delay)
    {
        await Task.Delay(delay);
        await RefreshIfIdleAsync();
    }

    private async Task RefreshIfIdleAsync()
    {
        if (!_busy && _wslInstalled)
        {
            await RunBusyAsync("Refreshing...", RefreshInstalledAsync);
        }
    }

    #endregion

    #region Helpers

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>
    /// Shows a small dialog asking for a line of text.
    /// Returns null when the user cancels.
    /// </summary>
    private string? PromptForText(string title, string message, string defaultValue)
    {
        var input = new TextBox { Text = defaultValue, Margin = new Thickness(0, 8, 0, 12), Padding = new Thickness(4) };
        var ok = new Button { Content = "OK", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = message });
        panel.Children.Add(input);
        panel.Children.Add(buttons);

        var dialog = new Window
        {
            Title = title,
            Owner = this,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = panel,
            Resources = Resources
        };

        ok.Click += (_, _) => dialog.DialogResult = true;
        input.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    #endregion
}
