using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevPilot.Models;
using DevPilot.Services.Platform;
using DevPilot.Services.Wsl;

namespace DevPilot.Services.Remote;

/// <summary>
/// The distribution side of remote access: an OpenSSH server (for other computers on the
/// same network) and Tailscale (for other networks). Scripts run as root through wsl.exe.
/// </summary>
public static partial class RemoteAccessService
{
    /// <summary>
    /// Port the distribution's SSH server listens on. Not 22, so it never clashes with an
    /// SSH server on Windows itself.
    /// </summary>
    public const int SshPort = 2022;

    /// <summary>Port other computers use in NAT mode; Windows forwards it to <see cref="SshPort"/>.</summary>
    public const int ForwardedPort = 2222;

    private static readonly Dictionary<string, string> Utf8Output = new() { ["WSL_UTF8"] = "1" };

    private const string StatusScript = """
        pm=none
        for c in apt-get dnf pacman zypper; do
            if command -v "$c" >/dev/null 2>&1; then pm=$c; break; fi
        done
        echo "pm|$pm"
        echo "systemd|$([ -d /run/systemd/system ] && echo yes || echo no)"
        echo "sshd|$([ -x /usr/sbin/sshd ] && echo yes || echo no)"
        echo "sshrunning|$(pgrep -x sshd >/dev/null 2>&1 && echo yes || echo no)"
        if [ -x /usr/sbin/sshd ]; then
            echo "sshport|$(/usr/sbin/sshd -T 2>/dev/null | awk '$1 == "port" { print $2; exit }')"
        fi
        if command -v tailscale >/dev/null 2>&1; then
            echo "tailscale|yes"
            echo "--- tailscale ---"
            tailscale status --json 2>/dev/null
        else
            echo "tailscale|no"
        fi
        """;

    public static async Task<RemoteAccessStatus> GetStatusAsync(string distro, CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        int exit = await ProcessRunner.RunAsync(WslService.WslPath, ["--distribution", distro, "--user", "root", "--exec", "sh", "-s"],
            lines.Add, cancellationToken, StatusScript, Utf8Output);
        if (exit != 0 && lines.Count == 0)
        {
            throw new InvalidOperationException($"Could not read {distro} (wsl.exe exited with code {exit}).");
        }

        return ParseStatus(lines);
    }

    public static RemoteAccessStatus ParseStatus(IReadOnlyList<string> lines)
    {
        int marker = lines.ToList().IndexOf("--- tailscale ---");
        var values = (marker < 0 ? lines : lines.Take(marker))
            .Select(l => l.Split('|', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0])
            .ToDictionary(g => g.Key, g => g.Last()[1].Trim());

        string? state = null, name = null, address = null;
        if (marker >= 0)
        {
            (state, name, address) = ParseTailscale(string.Join('\n', lines.Skip(marker + 1)));
        }

        bool Yes(string key) => values.GetValueOrDefault(key) == "yes";

        return new RemoteAccessStatus(
            PackageManager: values.GetValueOrDefault("pm") ?? "none",
            HasSystemd: Yes("systemd"),
            SshInstalled: Yes("sshd"),
            SshRunning: Yes("sshrunning"),
            SshPort: int.TryParse(values.GetValueOrDefault("sshport"), out int port) ? port : null,
            TailscaleInstalled: Yes("tailscale"),
            TailscaleState: state,
            TailscaleName: name,
            TailscaleAddress: address);
    }

    /// <summary>Reads "tailscale status --json": the connection state, this machine's name and its Tailscale IPv4 address.</summary>
    public static (string? State, string? Name, string? Address) ParseTailscale(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            string? state = root.TryGetProperty("BackendState", out var s) ? s.GetString() : null;
            string? name = null, address = null;
            if (root.TryGetProperty("Self", out var self) && self.ValueKind == JsonValueKind.Object)
            {
                name = self.TryGetProperty("DNSName", out var dns) ? dns.GetString()?.TrimEnd('.') : null;
                if (self.TryGetProperty("TailscaleIPs", out var ips) && ips.ValueKind == JsonValueKind.Array)
                {
                    address = ips.EnumerateArray().Select(i => i.GetString()).FirstOrDefault(i => i is not null && i.Contains('.', StringComparison.Ordinal));
                }
            }

            return (state, string.IsNullOrEmpty(name) ? null : name, address);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    /// <summary>
    /// Counts signed-in remote sessions: OpenSSH session processes are named after the user
    /// ("sshd-session: dev@pts/0", or "sshd: dev@notty" before OpenSSH 9.8; connections still
    /// signing in are not counted), and Tailscale SSH runs each session as "tailscaled be-child ssh".
    /// </summary>
    private const string SessionsScript = """
        echo "ssh|$(pgrep -fc '^sshd(-session)?: [^[ ]+@' 2>/dev/null)"
        echo "tailscale|$(pgrep -fc 'be-child ssh' 2>/dev/null)"
        """;

    /// <summary>The number of remote sessions open in a running distribution.</summary>
    public static async Task<int> CountSessionsAsync(string distro, CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        await ProcessRunner.RunAsync(WslService.WslPath, ["--distribution", distro, "--user", "root", "--exec", "sh", "-s"],
            lines.Add, cancellationToken, SessionsScript, Utf8Output);
        return ParseSessionCount(lines);
    }

    public static int ParseSessionCount(IEnumerable<string> lines) =>
        lines.Select(l => l.Split('|'))
            .Where(p => p.Length == 2)
            .Sum(p => int.TryParse(p[1].Trim(), out int n) ? n : 0);

    /// <summary>
    /// Installs and starts the OpenSSH server on <see cref="SshPort"/>. Ubuntu starts SSH
    /// through a socket unit that ignores the configured port, so that is switched off.
    /// </summary>
    public static string BuildSshSetupScript(string packageManager)
    {
        string install = packageManager switch
        {
            "apt-get" => "export DEBIAN_FRONTEND=noninteractive\napt-get update\napt-get install -y openssh-server",
            "dnf" => "dnf install -y openssh-server",
            "pacman" => "pacman -Sy --noconfirm --needed openssh",
            "zypper" => "zypper --non-interactive install openssh-server",
            _ => throw new InvalidOperationException("This distribution has no supported package manager (apt, dnf, pacman or zypper).")
        };

        return $$"""
            set -e
            {{install}}

            # Our settings live in their own file; make sure the main config reads that folder.
            mkdir -p /etc/ssh/sshd_config.d
            if ! grep -qE '^[[:space:]]*Include[[:space:]]+/etc/ssh/sshd_config.d' /etc/ssh/sshd_config; then
                sed -i '1i Include /etc/ssh/sshd_config.d/*.conf' /etc/ssh/sshd_config
            fi
            printf '# Added by DevPilot (remote access)\nPort {{SshPort}}\n' > /etc/ssh/sshd_config.d/10-devpilot.conf
            ssh-keygen -A
            /usr/sbin/sshd -t

            if [ -d /run/systemd/system ]; then
                if systemctl is-enabled ssh.socket >/dev/null 2>&1; then systemctl disable --now ssh.socket; fi
                if systemctl cat ssh.service >/dev/null 2>&1; then svc=ssh; else svc=sshd; fi
                systemctl enable "$svc"
                systemctl restart "$svc"
            else
                mkdir -p /run/sshd
                pkill -x sshd || true
                /usr/sbin/sshd
            fi
            echo "SSH server is listening on port {{SshPort}}."
            """;
    }

    /// <summary>
    /// Adds public keys to the user's authorized_keys (keys already there are skipped).
    /// $1 is the user, $2 the keys in Base64 (one per line).
    /// </summary>
    public const string AuthorizeKeysScript = """
        set -e
        home=$(getent passwd "$1" | cut -d: -f6)
        group=$(id -gn "$1")
        mkdir -p "$home/.ssh"
        touch "$home/.ssh/authorized_keys"
        printf '%s' "$2" | base64 -d | while IFS= read -r key; do
            if [ -n "$key" ] && ! grep -qxF "$key" "$home/.ssh/authorized_keys"; then
                printf '%s\n' "$key" >> "$home/.ssh/authorized_keys"
                echo "Added: ${key##* }"
            fi
        done
        chown -R "$1:$group" "$home/.ssh"
        chmod 700 "$home/.ssh"
        chmod 600 "$home/.ssh/authorized_keys"
        echo "$(grep -c . "$home/.ssh/authorized_keys") key(s) can now log in as $1."
        """;

    public static string EncodeKeys(IEnumerable<string> publicKeys) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('\n', publicKeys.Select(k => k.Trim()).Where(k => k.Length > 0)) + "\n"));

    /// <summary>
    /// Installs Tailscale with its official script and signs in with Tailscale SSH turned on.
    /// Runs in a terminal window because signing in shows a link to open. $1 is the machine name.
    /// </summary>
    public static string BuildTailscaleSetupScript(string packageManager)
    {
        string curl = packageManager switch
        {
            "apt-get" => "apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y curl",
            "dnf" => "dnf install -y curl",
            "pacman" => "pacman -Sy --noconfirm --needed curl",
            "zypper" => "zypper --non-interactive install curl",
            _ => "true"
        };

        return $$"""
            set -e
            command -v curl >/dev/null 2>&1 || { {{curl}}; }
            if ! command -v tailscale >/dev/null 2>&1; then
                curl -fsSL https://tailscale.com/install.sh | sh
            fi
            if [ -d /run/systemd/system ]; then
                systemctl enable --now tailscaled
            else
                mkdir -p /var/lib/tailscale
                (tailscaled --state=/var/lib/tailscale/tailscaled.state >/var/log/tailscaled.log 2>&1 &)
                sleep 3
            fi
            echo
            echo "Sign in to Tailscale with the link below (Ctrl+click it). Tailscale SSH is turned on."
            tailscale up --ssh --hostname "$1"
            echo
            tailscale status
            echo
            printf 'Done. Press Enter to close this window.'
            read -r _
            """;
    }

    /// <summary>A Tailscale machine name from the distribution and PC names: "ubuntu-26-04-mypc".</summary>
    public static string TailscaleHostName(string distro, string machineName)
    {
        string name = NonHostChars().Replace($"{distro}-{machineName}".ToLowerInvariant(), "-").Trim('-');
        return name.Length > 63 ? name[..63].TrimEnd('-') : name;
    }

    /// <summary>Runs a script as root, streaming its output.</summary>
    public static Task<int> RunAsync(string distro, string script, IEnumerable<string> arguments, Action<string> onLine, CancellationToken cancellationToken) =>
        ProcessRunner.RunAsync(WslService.WslPath, ["--distribution", distro, "--user", "root", "--exec", "sh", "-s", "--", .. arguments],
            onLine, cancellationToken, script, Utf8Output);

    /// <summary>Runs a script as root in a terminal window (for interactive steps such as signing in).</summary>
    public static Process RunInTerminal(string distro, string script, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo { FileName = WslService.WslPath, UseShellExecute = false };
        foreach (string argument in new[] { "--distribution", distro, "--user", "root", "--exec", "sh", "-c", script.ReplaceLineEndings("\n"), "sh" }.Concat(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start wsl.exe.");
    }

    [GeneratedRegex("[^a-z0-9-]+")]
    private static partial Regex NonHostChars();
}
