using System.Text;
using DevPilot.Models;
using DevPilot.Services.Remote;

namespace DevPilot.Tests.Services;

public class RemoteAccessTests
{
    [Fact]
    public void ParseStatus_ReadsSshAndTailscale()
    {
        string[] output =
        [
            "pm|apt-get",
            "systemd|yes",
            "sshd|yes",
            "sshrunning|yes",
            "sshport|2022",
            "tailscale|yes",
            "--- tailscale ---",
            "{",
            "  \"BackendState\": \"Running\",",
            "  \"Self\": { \"DNSName\": \"ubuntu-mypc.tail1234.ts.net.\", \"TailscaleIPs\": [\"fd7a:115c:a1e0::1\", \"100.101.102.103\"] }",
            "}",
        ];

        var status = RemoteAccessService.ParseStatus(output);

        Assert.Equal("apt-get", status.PackageManager);
        Assert.True(status.HasSystemd);
        Assert.True(status.SshInstalled);
        Assert.True(status.SshRunning);
        Assert.Equal(2022, status.SshPort);
        Assert.True(status.TailscaleInstalled);
        Assert.True(status.TailscaleConnected);
        Assert.Equal("ubuntu-mypc.tail1234.ts.net", status.TailscaleName);
        Assert.Equal("100.101.102.103", status.TailscaleAddress);
    }

    [Theory]
    [InlineData(new[] { "ssh|2", "tailscale|1" }, 3)]
    [InlineData(new[] { "ssh|0", "tailscale|0" }, 0)]
    [InlineData(new[] { "ssh|", "tailscale|", "pgrep: not found" }, 0)]
    public void ParseSessionCount_AddsSshAndTailscaleSessions(string[] output, int expected) =>
        Assert.Equal(expected, RemoteAccessService.ParseSessionCount(output));

    [Fact]
    public void ParseStatus_NothingSetUp()
    {
        var status = RemoteAccessService.ParseStatus(["pm|apt-get", "systemd|no", "sshd|no", "sshrunning|no", "tailscale|no"]);

        Assert.False(status.HasSystemd);
        Assert.False(status.SshInstalled);
        Assert.Null(status.SshPort);
        Assert.False(status.TailscaleInstalled);
        Assert.False(status.TailscaleConnected);
        Assert.Null(status.TailscaleState);
    }

    [Fact]
    public void ParseTailscale_SignedOutOrBrokenJson()
    {
        Assert.Equal(("NeedsLogin", null, null), RemoteAccessService.ParseTailscale("""{ "BackendState": "NeedsLogin", "Self": null }"""));
        Assert.Equal((null, null, null), RemoteAccessService.ParseTailscale("failed to connect to local tailscaled"));
    }

    [Fact]
    public void ParsePortForwards_SkipsHeaders()
    {
        string[] output =
        [
            "",
            "Listen on ipv4:             Connect to ipv4:",
            "",
            "Address         Port        Address         Port",
            "--------------- ----------  --------------- ----------",
            "0.0.0.0         2222        127.0.0.1       2022",
        ];

        Assert.Equal([new PortForward("0.0.0.0", 2222, "127.0.0.1", 2022)], WindowsNetworkService.ParsePortForwards(output));
    }

    [Theory]
    [InlineData("Ubuntu-26.04", "MY-PC", "ubuntu-26-04-my-pc")]
    [InlineData("Debian", "Desk_Top", "debian-desk-top")]
    public void TailscaleHostName_IsAValidHostName(string distro, string machine, string expected) =>
        Assert.Equal(expected, RemoteAccessService.TailscaleHostName(distro, machine));

    [Fact]
    public void TailscaleHostName_IsAtMost63Characters()
    {
        string name = RemoteAccessService.TailscaleHostName(new string('a', 60), "pc-name");
        Assert.True(name.Length <= 63);
        Assert.False(name.EndsWith('-'));
    }

    [Fact]
    public void EncodeKeys_OneKeyPerLine()
    {
        string encoded = RemoteAccessService.EncodeKeys(["ssh-ed25519 AAAA one@pc\r\n", "", "ssh-rsa BBBB two@pc"]);
        Assert.Equal("ssh-ed25519 AAAA one@pc\nssh-rsa BBBB two@pc\n", Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
    }

    [Theory]
    [InlineData("apt-get", "apt-get install -y openssh-server")]
    [InlineData("pacman", "pacman -Sy --noconfirm --needed openssh")]
    public void BuildSshSetupScript_InstallsAndUsesTheDevPilotPort(string packageManager, string install)
    {
        string script = RemoteAccessService.BuildSshSetupScript(packageManager);
        Assert.Contains(install, script);
        Assert.Contains($"Port {RemoteAccessService.SshPort}", script);
        Assert.Contains("systemctl disable --now ssh.socket", script);
    }

    [Fact]
    public void BuildSshSetupScript_NeedsAPackageManager() =>
        Assert.Throws<InvalidOperationException>(() => RemoteAccessService.BuildSshSetupScript("none"));

    [Fact]
    public void BuildAllowScript_NatModeForwardsThePort()
    {
        string script = WindowsNetworkService.BuildAllowScript(mirrored: false, sshPort: 2022, forwardedPort: 2222);
        Assert.Contains("portproxy add v4tov4 listenaddress=0.0.0.0 listenport=2222 connectaddress=127.0.0.1 connectport=2022", script);
        Assert.Contains("-LocalPort 2222 -Profile Domain,Private", script);
        Assert.DoesNotContain("New-NetFirewallHyperVRule", script);
    }

    [Theory]
    [InlineData("192.168.8.2", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("169.254.53.148", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("198.19.255.1", false)]
    [InlineData("198.20.0.1", true)]
    public void IsReachableAddress_SkipsLinkLocalAndTunnelRanges(string address, bool reachable) =>
        Assert.Equal(reachable, WindowsNetworkService.IsReachableAddress(System.Net.IPAddress.Parse(address)));

    [Fact]
    public void ParseNetworkCategories_ByAdapter()
    {
        var categories = WindowsNetworkService.ParseNetworkCategories(["Wi-Fi|Public", "Meta|Private", "", "noise"]);
        Assert.Equal("Public", categories["wi-fi"]);
        Assert.Equal("Private", categories["Meta"]);
        Assert.Equal(2, categories.Count);
    }

    [Fact]
    public void BuildMakePrivateScript_QuotesAdapterNames() =>
        Assert.Equal(
            "Set-NetConnectionProfile -InterfaceAlias 'Wi-Fi' -NetworkCategory Private\nSet-NetConnectionProfile -InterfaceAlias 'Bob''s LAN' -NetworkCategory Private",
            WindowsNetworkService.BuildMakePrivateScript(["Wi-Fi", "Bob's LAN"]));

    [Fact]
    public async Task ProbeSshAsync_ReadsTheGreetingOrReturnsNull()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("SSH-2.0-OpenSSH_10.2\r\n"));
        });

        Assert.Equal("SSH-2.0-OpenSSH_10.2", await WindowsNetworkService.ProbeSshAsync("127.0.0.1", port, TimeSpan.FromSeconds(5)));
        await server;
        listener.Stop();

        Assert.Null(await WindowsNetworkService.ProbeSshAsync("127.0.0.1", port, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void BuildAllowScript_MirroredModeOpensTheHyperVFirewall()
    {
        string script = WindowsNetworkService.BuildAllowScript(mirrored: true, sshPort: 2022, forwardedPort: 2222);
        Assert.Contains("New-NetFirewallHyperVRule", script);
        Assert.Contains("-LocalPorts 2022", script);
        Assert.DoesNotContain("portproxy add", script);
    }
}
