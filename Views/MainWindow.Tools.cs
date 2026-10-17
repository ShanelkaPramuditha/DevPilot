using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DevPilot.Models;
using DevPilot.Navigation;
using DevPilot.Services.Platform;
using DevPilot.Services.Tools;

namespace DevPilot.Views;

/// <summary>
/// The "Dev Tools" page: install, update and remove tools (Git, Docker, Node.js, ...) on
/// Windows with winget, or inside a WSL distribution with its package manager.
/// </summary>
public partial class MainWindow
{
    /// <summary>The target "Windows"; other targets are distribution names.</summary>
    private const string WindowsTarget = "";

    /// <summary>Output lines kept in the panel; older lines are dropped.</summary>
    private const int MaxOutputLines = 2000;

    private readonly ObservableCollection<ToolRow> _toolRows = [];
    private readonly List<string> _toolOutput = [];
    private DistroToolInfo? _toolDistroInfo;
    private Action? _cancelToolOperation;
    private bool _toolsLoaded;

    /// <summary>Set while the target list is rebuilt, so that does not trigger a refresh.</summary>
    private bool _changingToolTargets;

    private string ToolTarget => (ToolTargetBox.SelectedItem as ComboBoxItem)?.Tag as string ?? WindowsTarget;

    private string ToolTargetName => ToolTarget == WindowsTarget ? "Windows" : ToolTarget;

    private ToolRow? SelectedToolRow => ToolList.SelectedItem as ToolRow;

    private void InitializeTools()
    {
        ToolList.ItemsSource = _toolRows;
        UpdateToolTargets();
    }

    /// <summary>Called when the page is opened: the first time, read what is installed.</summary>
    private async Task OnToolsPageOpenedAsync()
    {
        UpdateToolTargets();
        if (!_toolsLoaded && _cancelToolOperation is null)
        {
            await RefreshToolsAsync();
        }
    }

    /// <summary>Windows plus every installed distribution, keeping the current choice.</summary>
    private void UpdateToolTargets()
    {
        string current = ToolTargetBox.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : WindowsTarget;
        var targets = new List<ComboBoxItem> { new() { Content = "Windows (winget)", Tag = WindowsTarget } };
        targets.AddRange(_installed.Select(d => new ComboBoxItem { Content = $"{d.Name} (WSL)", Tag = d.Name }));

        bool same = ToolTargetBox.Items.Count == targets.Count
            && ToolTargetBox.Items.OfType<ComboBoxItem>().Select(i => i.Tag as string).SequenceEqual(targets.Select(t => t.Tag as string));
        if (same)
        {
            return;
        }

        _changingToolTargets = true;
        ToolTargetBox.Items.Clear();
        foreach (var item in targets)
        {
            ToolTargetBox.Items.Add(item);
        }

        ToolTargetBox.SelectedItem = targets.FirstOrDefault(t => t.Tag as string == current) ?? targets[0];
        _changingToolTargets = false;
    }

    private async void ToolTarget_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_changingToolTargets && IsLoaded)
        {
            await RefreshToolsAsync();
        }
    }

    private async void ToolsRefresh_Click(object sender, RoutedEventArgs e) => await RefreshToolsAsync();

    /// <summary>Reads which tools are installed on the selected target.</summary>
    private async Task RefreshToolsAsync()
    {
        string target = ToolTarget;
        _toolRows.Clear();
        _toolsLoaded = true;
        ShowToolPlaceholder(target == WindowsTarget
            ? "Checking installed tools with winget..."
            : $"Checking installed tools in {target}... (this starts the distribution)");
        ToolTargetHint.Text = "";
        SetToolControlsEnabled(false);

        try
        {
            List<ToolRow> rows;
            if (target == WindowsTarget)
            {
                rows = await WingetService.GetStatusAsync(ToolCatalog.Tools);
                _toolDistroInfo = null;
                ToolTargetHint.Text = "Installs with winget, the Windows Package Manager. Some installers ask for admin rights.";
            }
            else
            {
                var (info, distroRows) = await WslToolService.GetStatusAsync(target, ToolCatalog.Tools);
                rows = distroRows;
                _toolDistroInfo = info;
                ToolTargetHint.Text = info.PackageManager == "none"
                    ? $"{info.OsName} · no supported package manager"
                    : $"{info.OsName} · installs with {info.PackageManager.Replace("-get", "", StringComparison.Ordinal)} as root" +
                      (info.HasSystemd ? " · systemd" : "");
            }

            // Ignore the result if the user switched targets meanwhile.
            if (target != ToolTarget)
            {
                return;
            }

            foreach (var row in rows)
            {
                _toolRows.Add(row);
            }

            ToolPlaceholder.Visibility = Visibility.Collapsed;
            int updates = rows.Count(r => r.UpdateVersion is not null);
            PageInfo.SetBadge(ToolsTab, updates == 0 ? "" : updates.ToString(System.Globalization.CultureInfo.CurrentCulture));
        }
        catch (Exception ex)
        {
            ShowToolPlaceholder($"Could not read the installed tools on {ToolTargetName}.\n\n{ex.Message}");
        }
        finally
        {
            SetToolControlsEnabled(true);
        }
    }

    private void ShowToolPlaceholder(string text)
    {
        ToolPlaceholder.Text = text;
        ToolPlaceholder.Visibility = Visibility.Visible;
    }

    private void SetToolControlsEnabled(bool enabled)
    {
        ToolTargetBox.IsEnabled = enabled;
        ToolsRefreshButton.IsEnabled = enabled;
        ToolList.IsEnabled = enabled;
    }

    #region Actions

    private async void ToolAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ToolRow row })
        {
            await RunToolActionAsync(row, row.Installed ? ToolAction.Update : ToolAction.Install);
        }
    }

    private async void ToolInstall_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedToolRow is { } row)
        {
            await RunToolActionAsync(row, ToolAction.Install);
        }
    }

    private async void ToolUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedToolRow is { } row)
        {
            await RunToolActionAsync(row, ToolAction.Update);
        }
    }

    private async void ToolUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedToolRow is { } row)
        {
            await RunToolActionAsync(row, ToolAction.Uninstall);
        }
    }

    private void ToolWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedToolRow is { } row)
        {
            Process.Start(new ProcessStartInfo { FileName = row.Tool.Website, UseShellExecute = true });
        }
    }

    private void ToolMenu_Opened(object sender, RoutedEventArgs e)
    {
        var row = SelectedToolRow;
        if (row is null)
        {
            ToolMenu.IsOpen = false;
            return;
        }

        ToolInstallItem.Visibility = row.Installed ? Visibility.Collapsed : Visibility.Visible;
        ToolInstallItem.IsEnabled = row.CanAct;
        ToolUpdateItem.Visibility = row.Installed ? Visibility.Visible : Visibility.Collapsed;
        ToolUpdateItem.IsEnabled = row.CanUpdate;
        ToolUpdateItem.Header = row.UpdateVersion is { } version ? $"_Update to {version}" : "_Update";
        ToolUninstallItem.IsEnabled = row.CanUninstall;
    }

    private void ToolCancel_Click(object sender, RoutedEventArgs e) => _cancelToolOperation?.Invoke();

    private void ToolHideOutput_Click(object sender, RoutedEventArgs e) => ToolOutputPanel.Visibility = Visibility.Collapsed;

    /// <summary>Runs an install, update or uninstall, showing its output live, then refreshes the list.</summary>
    private async Task RunToolActionAsync(ToolRow row, ToolAction action)
    {
        if (_cancelToolOperation is not null)
        {
            return;
        }

        string target = ToolTarget;
        string where = target == WindowsTarget ? "Windows" : target;
        if (!ConfirmToolAction(row, action, where))
        {
            return;
        }

        string verb = action switch { ToolAction.Install => "Installing", ToolAction.Update => "Updating", _ => "Uninstalling" };
        StartToolOutput($"{verb} {row.Name} on {where}...");

        using var cancellation = new CancellationTokenSource();
        _cancelToolOperation = cancellation.Cancel;
        SetToolControlsEnabled(false);

        try
        {
            int exitCode;
            if (target == WindowsTarget)
            {
                string winget = WingetService.WingetExe ?? throw new InvalidOperationException(WingetService.MissingMessage);
                string packageId = row.InstalledPackageId ?? row.Tool.Windows!.InstallId;
                var arguments = action switch
                {
                    ToolAction.Install => WingetService.InstallArguments(row.Tool.Windows!.InstallId),
                    ToolAction.Update => WingetService.UpgradeArguments(packageId),
                    _ => WingetService.UninstallArguments(packageId)
                };
                AppendToolOutput($"> winget {string.Join(' ', arguments)}");
                exitCode = await ProcessRunner.RunAsync(winget, arguments, AppendToolOutput, cancellation.Token);
            }
            else
            {
                var info = _toolDistroInfo ?? throw new InvalidOperationException("Refresh the list first.");
                string script = WslToolService.BuildActionScript(row.Tool, action, info.PackageManager);
                foreach (string line in script.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    AppendToolOutput($"# {line}");
                }

                exitCode = await WslToolService.RunAsync(target, script, info.DefaultUser, AppendToolOutput, cancellation.Token);
            }

            string done = action switch { ToolAction.Install => "installed", ToolAction.Update => "updated", _ => "uninstalled" };
            if (exitCode == 0)
            {
                ToolOperationText.Text = $"{row.Name} was {done} on {where}.";
                if (row.Tool.Id == "docker" && target != WindowsTarget && action == ToolAction.Install)
                {
                    AppendToolOutput($"Restart {target} (right-click it on the Installed page → Restart) so your user can run docker without sudo.");
                }
            }
            else
            {
                ToolOperationText.Text = $"{row.Name} was not {done} (exit code {exitCode}). See the output below.";
            }
        }
        catch (OperationCanceledException)
        {
            ToolOperationText.Text = "Cancelled.";
            AppendToolOutput("Cancelled.");
        }
        catch (Exception ex)
        {
            ToolOperationText.Text = $"Failed: {ex.Message}";
            AppendToolOutput(ex.Message);
        }
        finally
        {
            _cancelToolOperation = null;
            NotifyIfInBackground(row.Name, ToolOperationText.Text);
            ToolProgress.Visibility = Visibility.Collapsed;
            ToolCancelButton.Visibility = Visibility.Collapsed;
            ToolHideOutputButton.Visibility = Visibility.Visible;
            SetToolControlsEnabled(true);
        }

        await RefreshToolsAsync();
    }

    private bool ConfirmToolAction(ToolRow row, ToolAction action, string where)
    {
        string? question = action switch
        {
            ToolAction.Uninstall => $"Uninstall {row.Name} from {where}?",
            ToolAction.Install when row.Tool.Id == "docker" && where != "Windows" =>
                $"Install Docker Engine in {where}?\n\n{row.Tool.WslNote}",
            _ => null
        };

        return question is null
            || MessageBox.Show(this, question, $"{row.Name}", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private void StartToolOutput(string title)
    {
        _toolOutput.Clear();
        ToolOutputBox.Clear();
        ToolOperationText.Text = title;
        ToolProgress.Visibility = Visibility.Visible;
        ToolCancelButton.Visibility = Visibility.Visible;
        ToolHideOutputButton.Visibility = Visibility.Collapsed;
        ToolOutputPanel.Visibility = Visibility.Visible;
    }

    /// <summary>Adds a line to the output panel (callable from any thread).</summary>
    private void AppendToolOutput(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendToolOutput(line));
            return;
        }

        _toolOutput.Add(line);
        if (_toolOutput.Count > MaxOutputLines)
        {
            _toolOutput.RemoveRange(0, _toolOutput.Count - MaxOutputLines);
            ToolOutputBox.Text = string.Join(Environment.NewLine, _toolOutput) + Environment.NewLine;
        }
        else
        {
            ToolOutputBox.AppendText(line + Environment.NewLine);
        }

        ToolOutputBox.ScrollToEnd();
    }

    #endregion
}
