using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DevPilot.Core;
using DevPilot.Models;
using DevPilot.Services.Platform;
using DevPilot.Services.Wsl;

namespace DevPilot.Views;

/// <summary>
/// The "Usage" tab: health findings, live resource usage of the WSL VM, a
/// memory breakdown, and per-distribution details (top processes, ports).
/// </summary>
public partial class MainWindow
{
    /// <summary>Samples kept for the charts (10 minutes at the default 5 seconds).</summary>
    private const int HistoryLength = 120;

    private readonly ObservableCollection<DistroUsage> _usage = [];
    private readonly List<(DateTime Time, double Value)> _cpuHistory = [];
    private readonly List<(DateTime Time, double Value)> _memoryHistory = [];
    private readonly DispatcherTimer _usageTimer = new();
    private bool _usageRefreshing;
    private VmUsage? _lastVm;
    private string? _detailsDistro;

    private enum Severity { Good, Info, Warning }

    private void InitializeUsage()
    {
        UsageList.ItemsSource = _usage;
        _usageTimer.Tick += async (_, _) => await RefreshUsageAsync();
        UpdateUsageTimer();
        ShowVmOff("Open this tab to start measuring.");
    }

    private bool IsUsageTabOpen => DistroTabs.SelectedItem == UsageTab;

    private async void DistroTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selection changes in lists inside the tabs bubble up to here too.
        if (e.Source != DistroTabs)
        {
            return;
        }

        SyncNavSelection();

        // Pick up changes made to the SSH config outside DevPilot.
        if (DistroTabs.SelectedItem == SshTab)
        {
            LoadSshConfig();
        }

        if (DistroTabs.SelectedItem == ToolsTab)
        {
            await OnToolsPageOpenedAsync();
        }

        if (DistroTabs.SelectedItem == KeysTab)
        {
            await LoadKeysAsync();
        }
        UpdateUsageTimer();
        if (IsUsageTabOpen)
        {
            await RefreshUsageAsync();
        }
    }

    private async void RefreshUsage_Click(object sender, RoutedEventArgs e) => await RefreshUsageAsync();

    private void AutoRefresh_Changed(object sender, RoutedEventArgs e) => UpdateUsageTimer();

    private void RefreshInterval_Changed(object sender, SelectionChangedEventArgs e) => UpdateUsageTimer();

    /// <summary>
    /// Measures only while the Usage tab is visible, so the app does not keep
    /// calling into WSL in the background.
    /// </summary>
    private void UpdateUsageTimer()
    {
        // Called during InitializeComponent before every control exists.
        if (AutoRefreshBox is null || RefreshIntervalBox is null || DistroTabs is null)
        {
            return;
        }

        int seconds = RefreshIntervalBox.SelectedItem is ComboBoxItem { Tag: string tag }
            && int.TryParse(tag, out int value) ? value : 5;
        _usageTimer.Interval = TimeSpan.FromSeconds(seconds);
        _usageTimer.IsEnabled = IsUsageTabOpen && AutoRefreshBox.IsChecked == true;
    }

    #region Refresh

    private async Task RefreshUsageAsync()
    {
        if (_usageRefreshing || !_wslInstalled)
        {
            return;
        }

        _usageRefreshing = true;
        RefreshUsageButton.IsEnabled = false;
        try
        {
            var distros = await WslService.GetInstalledAsync();
            var vmTask = UsageStats.GetVmUsageAsync(distros);
            var rowsTask = Task.WhenAll(distros.Select(UsageStats.GetDistroUsageAsync));

            VmUsage? vm = await vmTask;
            DistroUsage[] rows = await rowsTask;
            _lastVm = vm;

            ShowDistroUsage(rows, vm);
            if (vm is null)
            {
                ShowVmOff("The WSL 2 VM is not running. Start a distribution to see live usage.");
            }
            else
            {
                ShowVmUsage(vm);
            }

            ShowInsights(vm, rows, distros);
            await ShowDetailsAsync();
            UsageUpdatedText.Text = $"Updated {DateTime.Now:T}";
        }
        catch (Exception ex)
        {
            UsageUpdatedText.Text = $"Could not read usage: {ex.Message}";
        }
        finally
        {
            _usageRefreshing = false;
            RefreshUsageButton.IsEnabled = true;
        }
    }

    private void ShowVmUsage(VmUsage vm)
    {
        var now = DateTime.Now;
        AddSample(_cpuHistory, now, vm.CpuPercent);
        AddSample(_memoryHistory, now, vm.MemoryUsed);

        UsageTitleText.Text = $"WSL 2 virtual machine · running for {FormatDuration(vm.Uptime)}";

        CpuTile.Update($"{vm.CpuPercent:0}%", $"{vm.Cores} cores · load {vm.LoadAverage}", vm.CpuPercent);
        MemoryTile.Update(Format.Size(vm.MemoryUsed), $"of {Format.Size(vm.MemoryTotal)} ({vm.MemoryPercent:0}%)", vm.MemoryPercent);
        if (vm.SwapTotal > 0)
        {
            SwapTile.Update(vm.SwapUsed > 0 ? Format.Size(vm.SwapUsed) : "0 B",
                $"of {Format.Size(vm.SwapTotal)} ({vm.SwapPercent:0}%)", vm.SwapPercent);
        }
        else
        {
            SwapTile.Update("Off", "No swap is configured");
        }

        UptimeTile.Update(FormatDuration(vm.Uptime), "Since the WSL 2 VM started");

        CpuChart.Update(_cpuHistory, 100, v => $"{v:0}%");
        MemoryChart.Update(_memoryHistory, vm.MemoryTotal, v => FormatCompactSize((long)v));

        ShowMemoryBreakdown(vm);
    }

    private void ShowVmOff(string message)
    {
        _cpuHistory.Clear();
        _memoryHistory.Clear();
        UsageTitleText.Text = "WSL 2 virtual machine · not running";
        UsageUpdatedText.Text = message;
        CpuTile.Clear("—");
        MemoryTile.Clear("—");
        SwapTile.Clear("—");
        UptimeTile.Clear("Not running");
        CpuChart.Clear();
        MemoryChart.Clear();

        ProgramsColumn.Width = new GridLength(0, GridUnitType.Star);
        CacheColumn.Width = new GridLength(0, GridUnitType.Star);
        FreeColumn.Width = new GridLength(1, GridUnitType.Star);
        ProgramsText.Text = CacheText.Text = FreeText.Text = "—";
        MemoryLimitText.Text = "Start a distribution to see how memory is used.";
        MemoryExplainText.Text = "";
        FreeCacheButton.IsEnabled = false;
    }

    /// <summary>
    /// Programs + cache + free, with the Windows view of the VM for comparison.
    /// </summary>
    private void ShowMemoryBreakdown(VmUsage vm)
    {
        double total = Math.Max(1, vm.MemoryTotal);
        ProgramsColumn.Width = new GridLength(vm.MemoryPrograms / total, GridUnitType.Star);
        CacheColumn.Width = new GridLength(vm.MemoryCache / total, GridUnitType.Star);
        FreeColumn.Width = new GridLength(vm.MemoryFree / total, GridUnitType.Star);

        ProgramsText.Text = Format.Size(vm.MemoryPrograms);
        CacheText.Text = Format.Size(vm.MemoryCache);
        FreeText.Text = Format.Size(vm.MemoryFree);
        ProgramsSegment.ToolTip = $"Programs: {Format.Size(vm.MemoryPrograms)} ({100 * vm.MemoryPrograms / total:0}%)";
        CacheSegment.ToolTip = $"Cache: {Format.Size(vm.MemoryCache)} ({100 * vm.MemoryCache / total:0}%)";
        FreeSegment.ToolTip = $"Free: {Format.Size(vm.MemoryFree)} ({100 * vm.MemoryFree / total:0}%)";

        MemoryLimitText.Text = $"Limit {Format.Size(vm.MemoryTotal)} ({SystemInfo.WslMemoryText()})";

        long? windows = UsageStats.GetWindowsVmMemory();
        MemoryExplainText.Text =
            "Cache is file data Linux keeps to work faster; it is freed when programs need it, but Windows cannot use it meanwhile." +
            (windows is { } w ? $" Windows currently sees {Format.Size(w)} for the VM (vmmemWSL in Task Manager)." : "");
        FreeCacheButton.IsEnabled = vm.MemoryCache > 0;
    }

    private void ShowDistroUsage(IEnumerable<DistroUsage> rows, VmUsage? vm)
    {
        string? selected = (UsageList.SelectedItem as DistroUsage)?.Name ?? _detailsDistro;

        _usage.Clear();
        foreach (var row in rows)
        {
            double share = vm is { MemoryTotal: > 0 } && row.MemoryBytes is { } bytes ? 100.0 * bytes / vm.MemoryTotal : 0;
            _usage.Add(row with { MemoryShare = share });
        }

        // Keep the selection; otherwise pick the first running distribution.
        UsageList.SelectedItem = _usage.FirstOrDefault(u => u.Name == selected)
            ?? _usage.FirstOrDefault(u => u.IsRunning)
            ?? _usage.FirstOrDefault();

        int running = _usage.Count(u => u.IsRunning);
        long diskFiles = _usage.Sum(u => u.Distro.DiskBytes ?? 0);
        var parts = new List<string>
        {
            $"{running} of {_usage.Count} running",
            $"{_usage.Sum(u => u.Processes ?? 0):N0} processes",
        };
        if (diskFiles > 0)
        {
            parts.Add($"{Format.Size(diskFiles)} of disk files");
        }

        parts.Add(HostFreeText());
        DistrosSummaryText.Text = string.Join("  ·  ", parts);
    }

    /// <summary>
    /// Free space on the Windows drives that hold the virtual disks, e.g. "120 GB free on C:".
    /// </summary>
    private string HostFreeText()
    {
        var drives = _usage
            .Where(u => u.HostDrive is not null && u.HostFree is not null)
            .GroupBy(u => u.HostDrive!, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{Format.Size(g.First().HostFree)} free on {g.Key}")
            .ToList();

        return drives.Count > 0 ? string.Join(", ", drives) : "free space unknown";
    }

    #endregion

    #region Details of the selected distribution

    private async void UsageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_usageRefreshing && UsageList.SelectedItem is DistroUsage row && row.Name != _detailsDistro)
        {
            await ShowDetailsAsync();
        }
    }

    private async Task ShowDetailsAsync()
    {
        if (UsageList.SelectedItem is not DistroUsage row)
        {
            DetailsTitle.Text = "Inside the distribution";
            DetailsHint.Text = "Select a distribution above.";
            SetDetails(null, "", "");
            return;
        }

        _detailsDistro = row.Name;
        DetailsTitle.Text = $"Inside {row.Name}";

        if (!row.IsRunning)
        {
            DetailsHint.Text = $"{row.Name} is stopped. Start it to see its processes and ports.";
            SetDetails(null, "Not running.", "Not running.");
            return;
        }

        try
        {
            var details = await UsageStats.GetDetailsAsync(row.Name);
            if (_detailsDistro != row.Name)
            {
                return;
            }

            DetailsHint.Text = details.Ports.Any(p => p.CanOpenInBrowser)
                ? "Web servers listening here can be opened from Windows at localhost."
                : "Programs using the most memory, and the network ports they listen on.";
            SetDetails(details,
                "Process list is not available (ps is not installed).",
                "No ports are listening, or ss is not installed.");
        }
        catch (Exception ex)
        {
            DetailsHint.Text = $"Could not read details: {ex.Message}";
            SetDetails(null, "", "");
        }
    }

    private void SetDetails(DistroDetails? details, string noProcesses, string noPorts)
    {
        var processes = details?.Processes ?? [];
        var ports = details?.Ports ?? [];

        ProcessList.ItemsSource = processes;
        ProcessList.Visibility = processes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProcessEmptyText.Text = noProcesses;
        ProcessEmptyText.Visibility = processes.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        PortList.ItemsSource = ports;
        PortList.Visibility = ports.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PortEmptyText.Text = noPorts;
        PortEmptyText.Visibility = ports.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OpenPort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Documents.Hyperlink { Tag: string url })
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError($"Failed to open {url}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Lists inside the page pass the mouse wheel on, so the page scrolls
    /// instead of stopping at the list.
    /// </summary>
    private void NestedList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        e.Handled = true;
        UsageScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender
        });
    }

    #endregion

    #region Health

    /// <summary>
    /// Turns the figures into plain-language findings, each with a fix where
    /// there is one. Shows a single "healthy" line when nothing needs attention.
    /// </summary>
    private void ShowInsights(VmUsage? vm, DistroUsage[] rows, List<WslDistro> distros)
    {
        var insights = new List<(Severity Severity, string Text, string? Action, Func<Task>? Run)>();

        if (distros.Count == 0)
        {
            insights.Add((Severity.Info, "No distributions are installed yet.", "Browse Available",
                () => { DistroTabs.SelectedItem = AvailableTab; return Task.CompletedTask; }
            ));
        }
        else if (vm is null)
        {
            var start = distros.FirstOrDefault(d => d.IsDefault) ?? distros[0];
            insights.Add((Severity.Info, "The WSL 2 VM is not running, so there is no live usage to show.", $"Start {start.Name}",
                () => RunBusyAsync($"Starting {start.Name}...", async () =>
                {
                    await WslService.StartInBackgroundAsync(start.Name);
                    await RefreshInstalledAsync();
                })));
        }

        if (vm is not null)
        {
            // CPU: judge the last minute, not a single sample.
            var recent = _cpuHistory.Where(s => s.Time > DateTime.Now.AddMinutes(-1)).Select(s => s.Value).ToList();
            double cpuAverage = recent.Count > 0 ? recent.Average() : vm.CpuPercent;
            if (recent.Count >= 3 && cpuAverage >= 85)
            {
                insights.Add((Severity.Warning,
                    $"The CPU has been busy for the last minute (average {cpuAverage:0}%). Check Top processes below.", null, null));
            }

            if (vm.MemoryPercent >= 85)
            {
                insights.Add((Severity.Warning,
                    $"Memory is {vm.MemoryPercent:0}% full ({Format.Size(vm.MemoryUsed)} of {Format.Size(vm.MemoryTotal)}). " +
                    "Programs may slow down or be stopped. You can raise the memory limit.",
                    "WSL Settings", () => ShowSettingsAsync(null)));
            }

            if (vm.SwapTotal > 0 && vm.SwapPercent >= 25)
            {
                insights.Add((Severity.Warning,
                    $"Linux is swapping ({Format.Size(vm.SwapUsed)} of swap in use), which is much slower than memory.",
                    "WSL Settings", () => ShowSettingsAsync(null)));
            }

            if (vm.MemoryCache >= 1L << 30 && vm.MemoryCache >= vm.MemoryTotal / 4)
            {
                insights.Add((Severity.Info,
                    $"Linux is keeping {Format.Size(vm.MemoryCache)} of file cache. Windows cannot use this memory until it is freed.",
                    "Free Cached Memory", FreeCacheAsync));
            }
        }

        foreach (var row in rows.Where(r => r.HostFree is { } free && free < 10L << 30).GroupBy(r => r.HostDrive))
        {
            var first = row.First();
            insights.Add((Severity.Warning,
                $"Only {Format.Size(first.HostFree)} is free on {first.HostDrive}. Distributions stop working when the drive is full.",
                null, null));
        }

        foreach (var row in rows.Where(r => r.DiskPercent >= 90))
        {
            insights.Add((Severity.Warning, $"{row.Name} has used {row.DiskPercent:0}% of the space it can use.", null, null));
        }

        if (!insights.Any(i => i.Severity == Severity.Warning) && vm is not null)
        {
            insights.Insert(0, (Severity.Good,
                $"Everything looks healthy. CPU {vm.CpuPercent:0}%, memory {vm.MemoryPercent:0}% used, {HostFreeText()}", null, null));
        }

        InsightsPanel.Children.Clear();
        foreach (var insight in insights.OrderByDescending(i => i.Severity))
        {
            InsightsPanel.Children.Add(CreateInsightRow(insight.Severity, insight.Text, insight.Action, insight.Run));
        }
    }

    private static DockPanel CreateInsightRow(Severity severity, string text, string? action, Func<Task>? run)
    {
        // An icon and a word carry the severity, never colour alone.
        var (glyph, brush, label) = severity switch
        {
            Severity.Warning => ("", "Brush.WarningText", "Attention"),
            Severity.Info => ("", "Brush.Accent", "Tip"),
            _ => ("", "Brush.Success", "Healthy")
        };

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };

        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 16,
            Margin = new Thickness(0, 1, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = label
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, brush);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);

        if (action is not null && run is not null)
        {
            var button = new Button { Content = action, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            button.Click += async (_, _) => await run();
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
        }

        row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private async void FreeCache_Click(object sender, RoutedEventArgs e) => await FreeCacheAsync();

    private async Task FreeCacheAsync()
    {
        if (_lastVm is not { } vm)
        {
            return;
        }

        FreeCacheButton.IsEnabled = false;
        try
        {
            await UsageStats.DropCachesAsync(vm.HostDistro);
            UsageUpdatedText.Text = $"Freed cached memory at {DateTime.Now:T}.";
        }
        catch (Exception ex)
        {
            ShowError($"Could not free cached memory: {ex.Message}");
        }

        await RefreshUsageAsync();
    }

    #endregion

    private static void AddSample(List<(DateTime Time, double Value)> history, DateTime time, double value)
    {
        history.Add((time, value));
        if (history.Count > HistoryLength)
        {
            history.RemoveAt(0);
        }
    }

    /// <summary>Short sizes for chart axes: "3.9 GB", "512 MB".</summary>
    private static string FormatCompactSize(long bytes) => bytes <= 0 ? "0" : Format.Size(bytes);

    private static string FormatDuration(TimeSpan time) => time switch
    {
        { TotalDays: >= 1 } => $"{(int)time.TotalDays} d {time.Hours} h",
        { TotalHours: >= 1 } => $"{(int)time.TotalHours} h {time.Minutes} min",
        { TotalMinutes: >= 1 } => $"{(int)time.TotalMinutes} min",
        _ => $"{time.Seconds.ToString(CultureInfo.CurrentCulture)} s"
    };
}
