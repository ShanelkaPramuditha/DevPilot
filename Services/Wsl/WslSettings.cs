using System.Globalization;
using System.IO;
using System.Text;
using DevPilot.Core;
using DevPilot.Models;
using DevPilot.Services.Platform;

namespace DevPilot.Services.Wsl;

/// <summary>
/// The settings this app can edit, and reading/writing the two config files.
/// Keys not listed here are kept untouched and can be edited as text.
/// </summary>
public static class WslSettings
{
    public const string DefaultUserKey = "user.default";
    public const string ProcessorsKey = "wsl2.processors";

    /// <summary>Per-distribution settings in /etc/wsl.conf.</summary>
    public static readonly IReadOnlyList<SettingDefinition> DistroSettings =
    [
        new("Startup", "boot", "systemd", "systemd",
            "Run systemd as PID 1 so services (docker, snap, ssh...) start like on a normal Linux machine.",
            SettingKind.Bool, "Off"),
        new("Startup", "boot", "command", "Startup command",
            "Command run as root each time the distribution starts.",
            SettingKind.Text, "None"),
        new("Startup", "user", "default", "Default user",
            "User that terminals and commands run as.",
            SettingKind.Choice, "User created at install"),

        new("Windows integration", "interop", "enabled", "Run Windows programs",
            "Allow starting Windows programs such as explorer.exe or code from Linux.",
            SettingKind.Bool, "On"),
        new("Windows integration", "interop", "appendWindowsPath", "Add Windows PATH",
            "Add the Windows PATH to the Linux PATH. Turning it off makes command lookups faster.",
            SettingKind.Bool, "On"),
        new("Windows integration", "automount", "enabled", "Mount Windows drives",
            "Mount drives such as C: under /mnt/c.",
            SettingKind.Bool, "On"),
        new("Windows integration", "automount", "root", "Drive mount folder",
            "Folder where Windows drives are mounted.",
            SettingKind.Text, "/mnt/"),
        new("Windows integration", "automount", "options", "Drive mount options",
            "Options for Windows drives, for example metadata,umask=22,fmask=11 to keep Linux file permissions.",
            SettingKind.Text, "None"),
        new("Windows integration", "automount", "mountFsTab", "Apply /etc/fstab",
            "Mount the entries in /etc/fstab at startup. Needed for automatic folder mounts.",
            SettingKind.Bool, "On"),

        new("Network", "network", "hostname", "Host name",
            "Host name of the distribution.",
            SettingKind.Text, "Windows computer name"),
        new("Network", "network", "generateHosts", "Generate /etc/hosts",
            "Let WSL write /etc/hosts from the Windows hosts file.",
            SettingKind.Bool, "On"),
        new("Network", "network", "generateResolvConf", "Generate /etc/resolv.conf",
            "Let WSL write the DNS settings. Turn off to use your own DNS servers.",
            SettingKind.Bool, "On"),
    ];

    /// <summary>Settings in %UserProfile%\.wslconfig, shared by all WSL 2 distributions.</summary>
    public static IReadOnlyList<SettingDefinition> GlobalSettings { get; } = CreateGlobalSettings();

    public static string GlobalConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig");

    private static List<SettingDefinition> CreateGlobalSettings()
    {
        long? ram = SystemInfo.TotalMemoryBytes;
        string halfRam = ram is { } total ? $"50% of RAM ({Format.Size(total / 2)})" : "50% of RAM";
        int cores = Environment.ProcessorCount;

        return
        [
            new("Resource limits", "wsl2", "memory", "Memory limit",
                "Most memory the WSL 2 VM may use, for example 4GB.",
                SettingKind.Size, halfRam),
            new("Resource limits", "wsl2", "processors", "Processors",
                "Number of logical processors the VM may use.",
                SettingKind.Choice, $"all {cores}",
                Enumerable.Range(1, cores).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToList()),
            new("Resource limits", "wsl2", "swap", "Swap size",
                "Size of the swap file, for example 2GB. 0 turns swap off.",
                SettingKind.Size, "25% of memory"),
            new("Resource limits", "wsl2", "swapFile", "Swap file",
                "Windows path of the swap file.",
                SettingKind.Text, @"%Temp%\swap.vhdx"),
            new("Resource limits", "experimental", "autoMemoryReclaim", "Reclaim memory",
                "Give unused cached memory back to Windows. gradual frees it slowly, dropCache frees it all when idle.",
                SettingKind.Choice, "WSL default", ["disabled", "gradual", "dropCache"]),
            new("Resource limits", "wsl2", "vmIdleTimeout", "Idle shutdown (ms)",
                "Milliseconds the VM stays on with nothing running before it shuts down.",
                SettingKind.Number, "60000"),

            new("Disks", "wsl2", "defaultVhdSize", "Default disk size",
                "Maximum size of virtual disks for newly installed distributions, for example 512GB.",
                SettingKind.Size, "1TB"),
            new("Disks", "experimental", "sparseVhd", "Sparse disks",
                "New distributions use disk files that give freed space back to Windows automatically.",
                SettingKind.Bool, "Off"),

            new("Networking", "wsl2", "networkingMode", "Networking mode",
                "nat uses a private network; mirrored shares the Windows network (better VPN and IPv6 support).",
                SettingKind.Choice, "nat", ["nat", "mirrored"]),
            new("Networking", "wsl2", "localhostForwarding", "Localhost forwarding",
                "Reach servers running in Linux from Windows at localhost.",
                SettingKind.Bool, "On"),
            new("Networking", "wsl2", "dnsTunneling", "DNS tunneling",
                "Resolve DNS through Windows. Helps with VPNs.",
                SettingKind.Bool, "On"),
            new("Networking", "wsl2", "firewall", "Windows Firewall",
                "Apply Windows Firewall rules to WSL traffic.",
                SettingKind.Bool, "On"),
            new("Networking", "wsl2", "autoProxy", "Use Windows proxy",
                "Use the proxy configured in Windows.",
                SettingKind.Bool, "On"),

            new("Features", "wsl2", "guiApplications", "Linux GUI apps",
                "Run Linux GUI applications (WSLg).",
                SettingKind.Bool, "On"),
            new("Features", "wsl2", "nestedVirtualization", "Nested virtualization",
                "Allow running virtual machines inside WSL, for example for Android emulators.",
                SettingKind.Bool, "On"),
        ];
    }

    // $1: base64 of the new file
    private const string WriteWslConfScript = """
        set -e
        tmp="$(mktemp)"
        printf '%s' "$1" | base64 -d > "$tmp"
        if [ -f /etc/wsl.conf ]; then cp /etc/wsl.conf /etc/wsl.conf.devpilot.bak; fi
        cat "$tmp" > /etc/wsl.conf
        chmod 644 /etc/wsl.conf
        rm -f "$tmp"
        """;

    private const string UsersScript = """
        awk -F: '$3 >= 1000 && $3 < 60000 { print $1 }' /etc/passwd
        echo root
        echo '--- current ---'
        id -un
        """;

    public static Task<string> ReadWslConfAsync(string distro) =>
        WslService.RunScriptAsync(distro, "cat /etc/wsl.conf 2>/dev/null || true", asRoot: true);

    public static Task WriteWslConfAsync(string distro, string content) =>
        WslService.RunScriptAsync(distro, WriteWslConfScript, asRoot: true,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n"))));

    /// <summary>Login users of a distribution and the one it currently starts as.</summary>
    public static async Task<(List<string> Users, string? Current)> GetUsersAsync(string distro)
    {
        string output = await WslService.RunScriptAsync(distro, UsersScript, asRoot: false);
        string[] parts = output.Split("--- current ---");
        var users = parts[0]
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct()
            .ToList();
        string? current = parts.Length > 1 ? parts[1].Trim() : null;
        return (users, string.IsNullOrEmpty(current) ? null : current);
    }

    public static string ReadGlobalConfig() =>
        File.Exists(GlobalConfigPath) ? File.ReadAllText(GlobalConfigPath) : "";

    public static void WriteGlobalConfig(string content)
    {
        if (File.Exists(GlobalConfigPath))
        {
            File.Copy(GlobalConfigPath, GlobalConfigPath + ".devpilot.bak", overwrite: true);
        }

        // WSL reads the file as plain UTF-8; a byte-order mark would break the first line.
        File.WriteAllText(GlobalConfigPath, content.Replace("\r\n", "\n").Replace("\n", "\r\n"),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Normalises a size such as "4", "4 gb" or "512MB" to WSL's form ("4GB").
    /// Returns null when it is not a valid size.
    /// </summary>
    public static string? NormalizeSize(string input)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            input.Trim(), @"^(\d+(?:\.\d+)?)\s*([KMGT]?B?)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        string unit = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "" or "G" or "GB" => "GB",
            "K" or "KB" => "KB",
            "M" or "MB" => "MB",
            "T" or "TB" => "TB",
            "B" => "B",
            _ => "GB"
        };

        return match.Groups[1].Value + unit;
    }
}
