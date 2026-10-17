using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DevPilot.Controls;
using DevPilot.Core;
using DevPilot.Models;
using DevPilot.Services.Platform;
using DevPilot.Services.Remote;
using DevPilot.Services.Ssh;
using DevPilot.Services.Wsl;

namespace DevPilot.Views;

/// <summary>
/// The "Remote Access" page: an SSH server in a distribution, letting computers on the same
/// network reach it through Windows, and Tailscale for computers on other networks.
/// </summary>
public partial class MainWindow
{
    private RemoteAccessStatus? _remoteStatus;
    private string _remoteUser = "";
    private bool _remoteBusy;
    private bool _changingRemoteDistros;
    private OutputLog? _remoteOutput;

    private string? RemoteDistro => (RemoteDistroBox.SelectedItem as ComboBoxItem)?.Tag as string;

    /// <summary>Checks for open remote sessions while "keep this PC awake" is on.</summary>
    private readonly DispatcherTimer _remoteSessionTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _checkingRemoteSessions;

    private void InitializeRemote()
    {
        _remoteSessionTimer.Tick += async (_, _) => await CheckRemoteSessionsAsync();
        RemoteKeepAwakeBox.IsChecked = App.Preferences.RemoteKeepAwake;
        UpdateKeepAwake();
    }

    private void RemoteKeepAwake_Changed(object sender, RoutedEventArgs e)
    {
        App.Preferences.RemoteKeepAwake = RemoteKeepAwakeBox.IsChecked == true;
        App.Preferences.Save();
        UpdateKeepAwake();
    }

    private void UpdateKeepAwake()
    {
        if (App.Preferences.RemoteKeepAwake)
        {
            _remoteSessionTimer.Start();
            _ = CheckRemoteSessionsAsync();
        }
        else
        {
            _remoteSessionTimer.Stop();
            SleepBlocker.KeepAwake(false);
            ShowAwakeStatus("", "Brush.TextMuted");
        }
    }

    /// <summary>
    /// Counts SSH and Tailscale SSH sessions in the running distributions (stopped ones are
    /// not started for this) and keeps Windows awake while there is at least one.
    /// </summary>
    private async Task CheckRemoteSessionsAsync()
    {
        if (_checkingRemoteSessions || !_wslInstalled)
        {
            return;
        }

        _checkingRemoteSessions = true;
        try
        {
            var running = (await WslService.GetInstalledAsync())
                .Where(d => d.IsRunning && !d.Name.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var counts = await Task.WhenAll(running.Select(async d =>
            {
                try
                {
                    return (d.Name, Count: await RemoteAccessService.CountSessionsAsync(d.Name));
                }
                catch (Exception)
                {
                    return (d.Name, Count: 0);
                }
            }));

            if (!App.Preferences.RemoteKeepAwake)
            {
                return;
            }

            int total = counts.Sum(c => c.Count);
            SleepBlocker.KeepAwake(total > 0);
            if (total > 0)
            {
                string where = string.Join(", ", counts.Where(c => c.Count > 0).Select(c => c.Name));
                ShowAwakeStatus($"{total} remote session{(total == 1 ? "" : "s")} in {where} · this PC stays awake", "Brush.Success");
            }
            else
            {
                ShowAwakeStatus("No one is connected · Windows sleeps as usual", "Brush.TextMuted");
            }
        }
        catch (Exception)
        {
            // WSL not answering: allow sleep rather than keeping the PC awake for nothing.
            SleepBlocker.KeepAwake(false);
        }
        finally
        {
            _checkingRemoteSessions = false;
        }
    }

    private void ShowAwakeStatus(string text, string brush)
    {
        RemoteAwakeText.Text = text;
        RemoteAwakeDot.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        RemoteAwakeDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
    }

    private async Task OnRemotePageOpenedAsync()
    {
        UpdateRemoteDistros();
        await RefreshRemoteAsync();
    }

    private void UpdateRemoteDistros()
    {
        string? current = RemoteDistro ?? _installed.FirstOrDefault(d => d.IsDefault)?.Name;
        var names = _installed.Select(d => d.Name).ToList();
        if (RemoteDistroBox.Items.OfType<ComboBoxItem>().Select(i => i.Tag as string).SequenceEqual(names))
        {
            return;
        }

        _changingRemoteDistros = true;
        RemoteDistroBox.Items.Clear();
        foreach (string name in names)
        {
            RemoteDistroBox.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        }

        RemoteDistroBox.SelectedItem = RemoteDistroBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == current)
            ?? RemoteDistroBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        _changingRemoteDistros = false;
    }

    private async void RemoteDistro_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_changingRemoteDistros && IsLoaded)
        {
            await RefreshRemoteAsync();
        }
    }

    private async void RemoteRefresh_Click(object sender, RoutedEventArgs e) => await RefreshRemoteAsync();

    /// <summary>Reads both sides: the distribution (SSH server, Tailscale) and Windows (port forward, firewall).</summary>
    private async Task RefreshRemoteAsync()
    {
        if (RemoteDistro is not { } distro)
        {
            RemoteSshText.Text = "Install a distribution first.";
            RemoteSteps.IsEnabled = false;
            return;
        }

        RemoteSteps.IsEnabled = false;
        _changingRemoteDistros = true;
        RemoteKeepRunningBox.IsChecked = App.Preferences.RemoteKeepRunning.Contains(distro, StringComparer.OrdinalIgnoreCase);
        _changingRemoteDistros = false;
        RemoteSshText.Text = RemoteLanText.Text = RemoteTsText.Text = "Checking...";

        try
        {
            var statusTask = RemoteAccessService.GetStatusAsync(distro);
            var userTask = WslService.RunAsync(TimeSpan.FromMinutes(1), "--distribution", distro, "--exec", "id", "-un");
            var forwardsTask = WindowsNetworkService.GetPortForwardsAsync();
            var firewallTask = WindowsNetworkService.HasFirewallRulesAsync();
            var categoriesTask = WindowsNetworkService.GetNetworkCategoriesAsync();

            _remoteStatus = await statusTask;
            _remoteUser = (await userTask).StandardOutput.Trim() is { Length: > 0 } user ? user : "root";
            var forwards = await forwardsTask;
            bool firewall = await firewallTask;
            var categories = await categoriesTask;

            if (distro != RemoteDistro)
            {
                return;
            }

            ShowSshStatus(_remoteStatus);
            await ShowLanStatusAsync(_remoteStatus, forwards, firewall, categories);
            ShowTailscaleStatus(_remoteStatus);
        }
        catch (Exception ex)
        {
            RemoteSshText.Text = $"Could not read {distro}: {ex.Message}";
            RemoteLanText.Text = RemoteTsText.Text = "—";
        }
        finally
        {
            RemoteSteps.IsEnabled = !_remoteBusy;
        }
    }

    private static bool IsMirroredNetworking =>
        string.Equals(IniDocument.Parse(SafeReadWslConfig()).Get("wsl2", "networkingMode"), "mirrored", StringComparison.OrdinalIgnoreCase);

    private static string SafeReadWslConfig()
    {
        try
        {
            return WslSettings.ReadGlobalConfig();
        }
        catch (IOException)
        {
            return "";
        }
    }

    /// <summary>The port other computers use: forwarded in NAT mode, direct in mirrored mode.</summary>
    private static int ExternalSshPort => IsMirroredNetworking ? RemoteAccessService.SshPort : RemoteAccessService.ForwardedPort;

    private void ShowSshStatus(RemoteAccessStatus status)
    {
        (string text, string brush) = status switch
        {
            { SshInstalled: false } => ("Not set up", "Brush.TextMuted"),
            { SshRunning: false } => ("Installed, but not running", "Brush.WarningText"),
            { SshPort: { } port } when port != RemoteAccessService.SshPort =>
                ($"Running on port {port}. Click Set Up SSH Server to switch to port {RemoteAccessService.SshPort}, which DevPilot uses.", "Brush.WarningText"),
            _ => ($"Running on port {RemoteAccessService.SshPort} · log in as {_remoteUser}", "Brush.Success")
        };

        RemoteSshText.Text = text;
        RemoteSshDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        RemoteSshButton.Content = status.SshInstalled ? "Repair and Restart" : "Set Up SSH Server";
        RemoteKeysButton.IsEnabled = status.SshInstalled;
    }

    /// <summary>Adapters on a Public network: Windows blocks DevPilot's firewall rule there.</summary>
    private List<string> _publicAdapters = [];

    /// <summary>
    /// Windows already lets other computers in, but the SSH server is missing or stopped: step 2's
    /// main button then runs step 1, which is easy to miss when the page is scrolled.
    /// </summary>
    private bool _lanNeedsSshServer;

    /// <summary>
    /// Shows whether other computers can connect, checking every link: the SSH server, the
    /// port forward and firewall rule, whether the server answers through Windows, and
    /// whether Windows treats the network as Public (which blocks the rule).
    /// </summary>
    private async Task ShowLanStatusAsync(RemoteAccessStatus status, List<PortForward> forwards, bool firewall, Dictionary<string, string> categories)
    {
        bool mirrored = IsMirroredNetworking;
        bool forwarded = forwards.Any(f => f.ListenPort == RemoteAccessService.ForwardedPort && f.ConnectPort == RemoteAccessService.SshPort);
        bool allowed = firewall && (mirrored || forwarded);
        int port = ExternalSshPort;

        var addresses = WindowsNetworkService.GetAddresses();
        _publicAdapters = addresses.Select(a => a.Adapter).Distinct()
            .Where(a => categories.GetValueOrDefault(a) is "Public").ToList();
        string probeHost = addresses.FirstOrDefault()?.Address ?? "127.0.0.1";
        string? banner = allowed && status.SshRunning
            ? await WindowsNetworkService.ProbeSshAsync(probeHost, port, TimeSpan.FromSeconds(3))
            : null;

        (string text, string brush) = (allowed, status) switch
        {
            (false, _) => ("Not allowed yet", "Brush.TextMuted"),
            (_, { SshInstalled: false }) => ("Windows lets other computers in, but there is no SSH server yet: click Set Up SSH Server", "Brush.WarningText"),
            (_, { SshRunning: false }) => ("Windows lets other computers in, but the SSH server is not running: click Start SSH Server", "Brush.WarningText"),
            _ when banner is null => ($"Allowed, but nothing answers on {probeHost}:{port}. Click Repair and Restart in step 1, or restart the distribution.", "Brush.WarningText"),
            _ when _publicAdapters.Count > 0 => ($"Answers on {probeHost}:{port}, but Windows blocks other computers on a Public network (see below)", "Brush.WarningText"),
            _ => ($"Ready: answers on {probeHost}:{port} ({banner})", "Brush.Success")
        };

        RemoteLanText.Text = text;
        RemoteLanDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        RemoteLanRemoveButton.Visibility = firewall || forwarded ? Visibility.Visible : Visibility.Collapsed;
        _lanNeedsSshServer = allowed && !status.SshRunning;
        RemoteLanButton.Content = _lanNeedsSshServer
            ? status.SshInstalled ? "Start SSH Server" : "Set Up SSH Server"
            : allowed ? "Allow Again" : "Allow from My Network";

        RemoteLanHint.Text = mirrored
            ? $"WSL uses mirrored networking: the distribution shares Windows' network, so a firewall rule for port {port} is enough."
            : $"WSL uses NAT networking (the default): Windows forwards port {port} to the distribution's SSH server.";

        RemotePublicNetworkNote.Visibility = _publicAdapters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RemotePublicNetworkText.Text = _publicAdapters.Count == 0 ? "" :
            $"Windows treats {string.Join(" and ", _publicAdapters)} as a Public network, so it blocks connections from other computers. " +
            "If this is your home or office network, make it private. On networks you don't trust (cafés, airports), leave it public and use Tailscale (step 3) instead.";

        var commands = addresses.Select(a => new ConnectionCommand($"ssh -p {port} {_remoteUser}@{a.Address}", a.Adapter)).ToList();
        commands.Add(new ConnectionCommand($"ssh -p {port} {_remoteUser}@{Environment.MachineName}", "by PC name"));
        RemoteLanCommands.ItemsSource = allowed ? commands : null;
    }

    private async void RemoteMakePrivate_Click(object sender, RoutedEventArgs e)
    {
        var adapters = _publicAdapters.ToList();
        if (adapters.Count == 0)
        {
            return;
        }

        var answer = MessageBox.Show(this,
            $"Make Windows treat {string.Join(" and ", adapters)} as a private network?\n\n" +
            "Do this only for networks you trust, such as your home or office: other devices on a private network can find this PC and use what it shares. " +
            "Windows asks for administrator rights.",
            "Make Network Private", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await RunRemoteStepAsync("Making the network private...", async log =>
        {
            await WindowsNetworkService.RunElevatedAsync(WindowsNetworkService.BuildMakePrivateScript(adapters));
            log("Done.");
            return $"{string.Join(" and ", adapters)} is now a private network.";
        });
    }

    private void ShowTailscaleStatus(RemoteAccessStatus status)
    {
        (string text, string brush) = status switch
        {
            { TailscaleInstalled: false } => ("Not set up", "Brush.TextMuted"),
            { TailscaleConnected: true } => ($"Connected as {status.TailscaleName ?? status.TailscaleAddress}", "Brush.Success"),
            _ => ($"Installed, not signed in ({status.TailscaleState ?? "stopped"})", "Brush.WarningText")
        };

        RemoteTsText.Text = text;
        RemoteTsDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);
        RemoteTailscaleButton.Content = status.TailscaleConnected ? "Sign In Again" : "Set Up Tailscale";

        var commands = new List<ConnectionCommand>();
        if (status.TailscaleConnected)
        {
            if (status.TailscaleName is { } name)
            {
                commands.Add(new($"ssh {_remoteUser}@{name.Split('.')[0]}", "Tailscale SSH (machine name)"));
            }

            if (status.TailscaleAddress is { } address)
            {
                commands.Add(new($"ssh {_remoteUser}@{address}", "Tailscale address"));
            }
        }

        RemoteTsCommands.ItemsSource = commands;
    }

    #region Actions

    private async void RemoteSetUpSsh_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteDistro is not { } distro || _remoteStatus is not { } status)
        {
            return;
        }

        await RunRemoteStepAsync($"Setting up the SSH server in {distro}...", async log =>
        {
            int exit = await RemoteAccessService.RunAsync(distro, RemoteAccessService.BuildSshSetupScript(status.PackageManager), [], log, CancellationToken.None);
            if (exit != 0)
            {
                return $"Setting up the SSH server failed (exit code {exit}). See the output below.";
            }

            // Without systemd nothing starts sshd when the distribution starts: use wsl.conf's boot command.
            if (!status.HasSystemd)
            {
                var conf = IniDocument.Parse(await WslSettings.ReadWslConfAsync(distro));
                string? command = conf.Get("boot", "command");
                if (command is null || !command.Contains("sshd", StringComparison.Ordinal))
                {
                    const string startSshd = "mkdir -p /run/sshd && /usr/sbin/sshd";
                    conf.Set("boot", "command", string.IsNullOrWhiteSpace(command) ? startSshd : $"{command}; {startSshd}");
                    await WslSettings.WriteWslConfAsync(distro, conf.ToString());
                    log("Added the SSH server to wsl.conf's boot command, so it starts with the distribution.");
                }
            }

            return $"The SSH server in {distro} is running on port {RemoteAccessService.SshPort}.";
        });
    }

    private async void RemoteAuthorizeKeys_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteDistro is not { } distro)
        {
            return;
        }

        var keys = SshKeyService.FindKeys(SshKeyService.SshFolder).Where(k => k.PublicPath is not null).ToList();
        if (keys.Count == 0)
        {
            ShowError("There are no public keys on this PC yet. Generate one on the SSH Keys page first.");
            return;
        }

        var answer = MessageBox.Show(this,
            $"Allow these keys to log in to {distro} as {_remoteUser} without a password?\n\n" + string.Join("\n", keys.Select(k => "• " + k.Name)),
            "Allow SSH Keys", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var publicKeys = new List<string>();
        foreach (var key in keys)
        {
            publicKeys.Add(await File.ReadAllTextAsync(key.PublicPath!));
        }

        await RunRemoteStepAsync($"Allowing your SSH keys in {distro}...", async log =>
        {
            int exit = await RemoteAccessService.RunAsync(distro, RemoteAccessService.AuthorizeKeysScript,
                [_remoteUser, RemoteAccessService.EncodeKeys(publicKeys)], log, CancellationToken.None);
            return exit == 0 ? $"Your SSH keys can log in to {distro} as {_remoteUser}." : $"Failed (exit code {exit}).";
        });
    }

    private async void RemoteLanAllow_Click(object sender, RoutedEventArgs e)
    {
        if (_lanNeedsSshServer)
        {
            RemoteSetUpSsh_Click(sender, e);
            return;
        }

        bool mirrored = IsMirroredNetworking;
        int port = ExternalSshPort;
        var answer = MessageBox.Show(this,
            $"Let other computers on your network connect to the SSH server?\n\n" +
            (mirrored
                ? $"DevPilot adds firewall rules allowing port {port}."
                : $"DevPilot adds a port forward (Windows port {port} → the distribution's port {RemoteAccessService.SshPort}) and a firewall rule for port {port}.") +
            "\n\nThe rules apply to private and work networks only, not public ones. Windows asks for administrator rights.",
            "Allow from My Network", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await RunRemoteStepAsync("Adding the port forward and firewall rules...", async log =>
        {
            await WindowsNetworkService.RunElevatedAsync(WindowsNetworkService.BuildAllowScript(mirrored, RemoteAccessService.SshPort, RemoteAccessService.ForwardedPort));
            log("Done.");
            return _remoteStatus is { SshRunning: true }
                ? $"Computers on your network can now connect on port {port}."
                : $"Windows now lets other computers in on port {port}. Next: set up the SSH server in the distribution.";
        });
    }

    private async void RemoteLanRemove_Click(object sender, RoutedEventArgs e)
    {
        await RunRemoteStepAsync("Removing the port forward and firewall rules...", async log =>
        {
            await WindowsNetworkService.RunElevatedAsync(WindowsNetworkService.BuildRemoveScript());
            log("Done.");
            return "Other computers can no longer connect through Windows.";
        });
    }

    private async void RemoteSetUpTailscale_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteDistro is not { } distro || _remoteStatus is not { } status)
        {
            return;
        }

        string hostName = RemoteAccessService.TailscaleHostName(distro, Environment.MachineName);
        await RunRemoteStepAsync($"Setting up Tailscale in {distro}: follow the terminal window to sign in...", async log =>
        {
            log($"A terminal window installs Tailscale and shows a sign-in link. This machine will be called \"{hostName}\".");
            using var terminal = RemoteAccessService.RunInTerminal(distro, RemoteAccessService.BuildTailscaleSetupScript(status.PackageManager), hostName);
            await terminal.WaitForExitAsync();
            return "Tailscale setup finished. The status below shows whether it is connected.";
        });
    }

    private void RemoteCopyCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string command })
        {
            Clipboard.SetText(command);
            BusyText.Text = $"Copied: {command}";
        }
    }

    private void RemoteHideOutput_Click(object sender, RoutedEventArgs e) => RemoteOutputPanel.Visibility = Visibility.Collapsed;

    private async void RemoteKeepRunning_Changed(object sender, RoutedEventArgs e)
    {
        if (_changingRemoteDistros || RemoteDistro is not { } distro)
        {
            return;
        }

        var list = App.Preferences.RemoteKeepRunning;
        list.RemoveAll(d => d.Equals(distro, StringComparison.OrdinalIgnoreCase));
        if (RemoteKeepRunningBox.IsChecked == true)
        {
            list.Add(distro);
            if (!_installed.Any(d => d.Name == distro && d.IsRunning))
            {
                try
                {
                    await WslService.StartInBackgroundAsync(distro);
                }
                catch (Exception ex)
                {
                    ShowError($"Could not start {distro}: {ex.Message}");
                }
            }
        }

        App.Preferences.Save();
    }

    /// <summary>Starts the distributions marked "keep running" (at startup, also when started with Windows).</summary>
    public static async Task StartKeptRunningDistrosAsync()
    {
        foreach (string distro in App.Preferences.RemoteKeepRunning.ToList())
        {
            try
            {
                var installed = await WslService.GetInstalledAsync();
                if (installed.FirstOrDefault(d => d.Name.Equals(distro, StringComparison.OrdinalIgnoreCase)) is { IsRunning: false })
                {
                    await WslService.StartInBackgroundAsync(distro);
                }
            }
            catch (Exception)
            {
                // Not fatal: the user can start it from the Installed page.
            }
        }
    }

    /// <summary>Runs one step with its output in the panel, then refreshes the status.</summary>
    private async Task RunRemoteStepAsync(string title, Func<Action<string>, Task<string>> step)
    {
        if (_remoteBusy)
        {
            return;
        }

        _remoteBusy = true;
        RemoteSteps.IsEnabled = false;
        _remoteOutput ??= new OutputLog(RemoteOutputBox);
        _remoteOutput.Clear();
        RemoteOperationText.Text = title;
        RemoteProgress.Visibility = Visibility.Visible;
        RemoteOutputPanel.Visibility = Visibility.Visible;

        var output = _remoteOutput;
        void Log(string line) => output.Append(line);

        try
        {
            RemoteOperationText.Text = await step(Log);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            RemoteOperationText.Text = "Cancelled: this step needs administrator rights.";
        }
        catch (Exception ex)
        {
            RemoteOperationText.Text = $"Failed: {ex.Message}";
            Log(ex.Message);
        }
        finally
        {
            _remoteBusy = false;
            output.Flush();
            RemoteProgress.Visibility = Visibility.Collapsed;
            NotifyIfInBackground("Remote access", RemoteOperationText.Text);
        }

        await RefreshRemoteAsync();
    }

    #endregion
}
