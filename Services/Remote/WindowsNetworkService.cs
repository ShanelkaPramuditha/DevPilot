using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using DevPilot.Models;
using DevPilot.Services.Platform;

namespace DevPilot.Services.Remote;

/// <summary>
/// The Windows side of remote access: this PC's addresses, and letting other computers
/// on the network reach the distribution's SSH server (port forward and firewall rules).
/// </summary>
public static class WindowsNetworkService
{
    /// <summary>Firewall rules made by DevPilot start with this name, so they can be found and removed.</summary>
    public const string RulePrefix = "DevPilot remote access";

    /// <summary>The id Windows uses for WSL in Hyper-V firewall rules (mirrored networking mode).</summary>
    private const string WslVmCreatorId = "{40E0AC32-46A5-438A-A0B2-2B479E8F2E90}";

    /// <summary>
    /// IPv4 addresses other computers on the network can reach: connected Wi-Fi and Ethernet
    /// adapters. Loopback, the virtual adapters of WSL and Hyper-V, and VPN or proxy tunnels
    /// (their addresses are not reachable from the local network) are left out.
    /// </summary>
    public static List<NetworkAddress> GetAddresses()
    {
        var addresses = new List<NetworkAddress>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up
                || !IsLocalNetworkAdapter(adapter.NetworkInterfaceType)
                || adapter.Name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase)
                || adapter.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && IsReachableAddress(unicast.Address))
                {
                    addresses.Add(new NetworkAddress(unicast.Address.ToString(), adapter.Name));
                }
            }
        }

        return addresses;
    }

    private static bool IsLocalNetworkAdapter(NetworkInterfaceType type) =>
        type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Ethernet3Megabit or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.Wireless80211;

    /// <summary>
    /// Not link-local (169.254.x.x, no DHCP answer) and not 198.18.0.0/15, the range proxy
    /// and VPN tools use for their virtual adapters.
    /// </summary>
    public static bool IsReachableAddress(IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        return b.Length == 4 && !(b[0] == 169 && b[1] == 254) && !(b[0] == 198 && (b[1] & 0xFE) == 18);
    }

    /// <summary>
    /// The category of each connected network, by adapter name ("Wi-Fi" → "Public").
    /// Windows applies the firewall rules of a Public network, which block DevPilot's rule.
    /// </summary>
    public static async Task<Dictionary<string, string>> GetNetworkCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        await ProcessRunner.RunAsync("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command",
             "Get-NetConnectionProfile | ForEach-Object { $_.InterfaceAlias + '|' + $_.NetworkCategory }"],
            lines.Add, cancellationToken);
        return ParseNetworkCategories(lines);
    }

    public static Dictionary<string, string> ParseNetworkCategories(IEnumerable<string> lines) =>
        lines.Select(l => l.Split('|'))
            .Where(p => p.Length == 2 && p[0].Length > 0)
            .GroupBy(p => p[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>PowerShell that makes Windows treat these networks as private (trusted) networks.</summary>
    public static string BuildMakePrivateScript(IEnumerable<string> adapters) =>
        string.Join('\n', adapters.Select(a => $"Set-NetConnectionProfile -InterfaceAlias '{a.Replace("'", "''", StringComparison.Ordinal)}' -NetworkCategory Private"));

    /// <summary>
    /// Connects and reads the SSH server's greeting ("SSH-2.0-OpenSSH_10.2..."), or returns
    /// null when nothing answers within the timeout.
    /// </summary>
    public static async Task<string?> ProbeSshAsync(string host, int port, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cancellation.Token);
            using var reader = new StreamReader(client.GetStream());
            string? banner = await reader.ReadLineAsync(cancellation.Token);
            return banner?.StartsWith("SSH-", StringComparison.Ordinal) == true ? banner.Trim() : null;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>The port forwards Windows has ("netsh interface portproxy show v4tov4").</summary>
    public static async Task<List<PortForward>> GetPortForwardsAsync(CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        await ProcessRunner.RunAsync("netsh.exe", ["interface", "portproxy", "show", "v4tov4"], lines.Add, cancellationToken);
        return ParsePortForwards(lines);
    }

    /// <summary>Rows look like "0.0.0.0  2222  127.0.0.1  2022" (the headers are localised, so they are skipped by shape).</summary>
    public static List<PortForward> ParsePortForwards(IEnumerable<string> lines)
    {
        var forwards = new List<PortForward>();
        foreach (string line in lines)
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && int.TryParse(parts[1], out int listenPort) && int.TryParse(parts[3], out int connectPort))
            {
                forwards.Add(new PortForward(parts[0], listenPort, parts[2], connectPort));
            }
        }

        return forwards;
    }

    /// <summary>Whether DevPilot's firewall rules exist (readable without admin rights).</summary>
    public static async Task<bool> HasFirewallRulesAsync(CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        await ProcessRunner.RunAsync("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-Command",
             $"@(Get-NetFirewallRule -DisplayName '{RulePrefix}*' -ErrorAction SilentlyContinue).Count + @(Get-NetFirewallHyperVRule -Name 'DevPilot-*' -ErrorAction SilentlyContinue).Count"],
            lines.Add, cancellationToken);
        return int.TryParse(lines.LastOrDefault(), out int count) && count > 0;
    }

    /// <summary>
    /// PowerShell that lets other computers reach the distribution's SSH server.
    /// NAT mode (the default): Windows listens on <paramref name="forwardedPort"/> and forwards
    /// to the distribution through WSL's localhost relay, which keeps working when the
    /// distribution's IP address changes. Mirrored mode: the distribution uses Windows'
    /// network directly, so only firewall rules are needed.
    /// </summary>
    public static string BuildAllowScript(bool mirrored, int sshPort, int forwardedPort)
    {
        var script = new StringBuilder("$ErrorActionPreference = 'Stop'\n");
        script.Append(BuildRemoveScript()).Append('\n');

        if (mirrored)
        {
            script.Append(Invariant($"New-NetFirewallHyperVRule -Name 'DevPilot-SSH-{sshPort}' -DisplayName '{RulePrefix} (SSH {sshPort})' -Direction Inbound -VMCreatorId '{WslVmCreatorId}' -Protocol TCP -LocalPorts {sshPort} -Action Allow | Out-Null\n"));
            script.Append(Invariant($"New-NetFirewallRule -DisplayName '{RulePrefix} (SSH {sshPort})' -Direction Inbound -Action Allow -Protocol TCP -LocalPort {sshPort} -Profile Domain,Private | Out-Null\n"));
        }
        else
        {
            script.Append(Invariant($"netsh interface portproxy add v4tov4 listenaddress=0.0.0.0 listenport={forwardedPort} connectaddress=127.0.0.1 connectport={sshPort} | Out-Null\n"));
            script.Append(Invariant($"New-NetFirewallRule -DisplayName '{RulePrefix} (SSH {forwardedPort})' -Direction Inbound -Action Allow -Protocol TCP -LocalPort {forwardedPort} -Profile Domain,Private | Out-Null\n"));
        }

        return script.ToString();
    }

    /// <summary>PowerShell that removes everything <see cref="BuildAllowScript"/> adds.</summary>
    public static string BuildRemoveScript() =>
        $"""
        Get-NetFirewallRule -DisplayName '{RulePrefix}*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        Get-NetFirewallHyperVRule -Name 'DevPilot-*' -ErrorAction SilentlyContinue | Remove-NetFirewallHyperVRule
        netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport={RemoteAccessService.ForwardedPort} | Out-Null
        """;

    /// <summary>
    /// Runs PowerShell as administrator (Windows asks for permission). Errors are written to a
    /// log file the elevated process can reach, and thrown here.
    /// </summary>
    public static async Task RunElevatedAsync(string script)
    {
        string log = Path.Combine(Path.GetTempPath(), $"devpilot-remote-{Guid.NewGuid():N}.log");
        string wrapped = $"try {{\n{script}\n}} catch {{ $_.Exception.Message | Out-File -FilePath '{log}' -Encoding utf8; exit 1 }}";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped));

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("Could not start PowerShell.");
        await process.WaitForExitAsync();

        try
        {
            if (process.ExitCode != 0)
            {
                string message = File.Exists(log) ? (await File.ReadAllTextAsync(log)).Trim() : $"PowerShell exited with code {process.ExitCode}.";
                throw new InvalidOperationException(message);
            }
        }
        finally
        {
            File.Delete(log);
        }
    }

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
