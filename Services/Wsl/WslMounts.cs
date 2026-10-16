using System.IO;
using System.Text.RegularExpressions;
using DevPilot.Models;

namespace DevPilot.Services.Wsl;

/// <summary>
/// Mounts Windows folders into a distribution with drvfs. Persistent mounts
/// are written to /etc/fstab, which WSL applies each time the distribution starts.
/// </summary>
public static partial class WslMounts
{
    // Shell helpers shared by the scripts below. Paths in /proc/mounts and
    // /etc/fstab escape backslash, space and tab as octal sequences.
    private const string Helpers = """
        esc() { printf '%s' "$1" | sed -e 's/\\/\\134/g' -e 's/ /\\040/g' -e 's/	/\\011/g'; }
        is_mounted() { D="$(esc "$1")" awk '$2 == ENVIRON["D"] { f = 1 } END { exit !f }' /proc/mounts; }
        in_fstab() { [ -f /etc/fstab ] && D="$(esc "$1")" awk '!/^[[:space:]]*#/ && $3 == "drvfs" && $2 == ENVIRON["D"] { f = 1 } END { exit !f }' /etc/fstab; }

        """;

    private const string ListScript = """
        cat /proc/mounts
        echo '--- fstab ---'
        cat /etc/fstab 2>/dev/null || true
        """;

    private const string UserScript = """
        id -u
        id -g
        printf '%s\n' "$HOME"
        """;

    // $1 Windows folder, $2 Linux folder, $3 mount options,
    // $4 1 to add to /etc/fstab, $5 uid, $6 gid
    private const string AddScript = Helpers + """
        set -e
        src="$1"; dst="$2"; opts="$3"; persist="$4"; uid="$5"; gid="$6"

        if is_mounted "$dst"; then
            echo "$dst is already a mount point." >&2; exit 1
        fi
        if [ -e "$dst" ] && [ ! -d "$dst" ]; then
            echo "$dst already exists and is not a folder." >&2; exit 1
        fi
        if [ -d "$dst" ] && [ -n "$(ls -A "$dst")" ]; then
            echo "The Linux folder $dst is not empty. Choose an empty or new folder so no files are hidden." >&2; exit 1
        fi
        if [ "$persist" = 1 ] && in_fstab "$dst"; then
            echo "/etc/fstab already has an entry for $dst." >&2; exit 1
        fi

        # Create the mount point; folders created here belong to the user.
        top=""; p="$dst"
        while [ ! -e "$p" ]; do top="$p"; p="$(dirname "$p")"; done
        mkdir -p "$dst"
        if [ -n "$top" ]; then chown -R "$uid:$gid" "$top"; fi

        mount -t drvfs "$src" "$dst" -o "$opts"

        if [ "$persist" = 1 ]; then
            touch /etc/fstab
            cp /etc/fstab /etc/fstab.devpilot.bak
            if [ -s /etc/fstab ] && [ -n "$(tail -c 1 /etc/fstab)" ]; then echo >> /etc/fstab; fi
            printf '%s %s drvfs %s 0 0\n' "$(esc "$src")" "$(esc "$dst")" "$opts" >> /etc/fstab
        fi
        """;

    // $1 Linux folder
    private const string MountScript = Helpers + """
        set -e
        if is_mounted "$1"; then exit 0; fi
        mount "$1"
        """;

    // $1 Linux folder
    private const string UnmountScript = Helpers + """
        set -e
        if is_mounted "$1"; then umount "$1"; fi
        """;

    // $1 Linux folder
    private const string RemoveScript = Helpers + """
        set -e
        dst="$1"
        if is_mounted "$dst"; then umount "$dst"; fi
        if in_fstab "$dst"; then
            cp /etc/fstab /etc/fstab.devpilot.bak
            tmp="$(mktemp)"
            D="$(esc "$dst")" awk '!(!/^[[:space:]]*#/ && $3 == "drvfs" && $2 == ENVIRON["D"])' /etc/fstab > "$tmp"
            cat "$tmp" > /etc/fstab
            rm -f "$tmp"
        fi
        # Remove the mount point if it was left empty.
        rmdir "$dst" 2>/dev/null || true
        """;

    /// <summary>
    /// Linux folders that must never be mounted over.
    /// </summary>
    private static readonly HashSet<string> ProtectedPaths =
    [
        "/", "/bin", "/boot", "/dev", "/etc", "/home", "/lib", "/lib32", "/lib64", "/media", "/mnt",
        "/opt", "/proc", "/root", "/run", "/sbin", "/srv", "/sys", "/tmp", "/usr", "/var"
    ];

    public static async Task<List<FolderMount>> GetMountsAsync(string distro)
    {
        string output = await WslService.RunScriptAsync(distro, ListScript, asRoot: true);
        string[] sections = output.Split("--- fstab ---");

        var mounted = ParseTable(sections[0], fromProcMounts: true);
        var persistent = sections.Length > 1 ? ParseTable(sections[1], fromProcMounts: false) : [];

        var mounts = new List<FolderMount>();
        foreach (string linuxPath in mounted.Keys.Union(persistent.Keys))
        {
            var entry = persistent.GetValueOrDefault(linuxPath) ?? mounted[linuxPath];
            mounts.Add(new FolderMount(
                WindowsPath: entry.Source,
                LinuxPath: linuxPath,
                IsMounted: mounted.ContainsKey(linuxPath),
                IsPersistent: persistent.ContainsKey(linuxPath),
                IsReadOnly: (mounted.GetValueOrDefault(linuxPath) ?? entry).ReadOnly));
        }

        return mounts.OrderBy(m => m.LinuxPath, StringComparer.Ordinal).ToList();
    }

    public static async Task<DistroUser> GetDefaultUserAsync(string distro)
    {
        string[] lines = (await WslService.RunScriptAsync(distro, UserScript, asRoot: false))
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return lines.Length >= 3
            ? new DistroUser(lines[0], lines[1], lines[2])
            : new DistroUser("0", "0", "/root");
    }

    public static Task AddAsync(string distro, string windowsPath, string linuxPath, bool persistent, bool readOnly, DistroUser user)
    {
        string options = $"uid={user.Uid},gid={user.Gid}" + (readOnly ? ",ro" : "");
        return WslService.RunScriptAsync(distro, AddScript, asRoot: true,
            ToDrvfsPath(windowsPath), linuxPath, options, persistent ? "1" : "0", user.Uid, user.Gid);
    }

    public static Task MountAsync(string distro, string linuxPath) =>
        WslService.RunScriptAsync(distro, MountScript, asRoot: true, linuxPath);

    public static Task UnmountAsync(string distro, string linuxPath) =>
        WslService.RunScriptAsync(distro, UnmountScript, asRoot: true, linuxPath);

    public static Task RemoveAsync(string distro, string linuxPath) =>
        WslService.RunScriptAsync(distro, RemoveScript, asRoot: true, linuxPath);

    /// <summary>
    /// Expands "~", normalises the path and checks it is a safe place to
    /// mount. Returns an error message, or null when the path is valid.
    /// </summary>
    public static string? ValidateLinuxPath(string input, DistroUser user, out string linuxPath)
    {
        linuxPath = input.Trim();
        if (linuxPath == "~" || linuxPath.StartsWith("~/", StringComparison.Ordinal))
        {
            linuxPath = user.Home.TrimEnd('/') + linuxPath[1..];
        }

        linuxPath = MultipleSlashes().Replace(linuxPath, "/");
        if (linuxPath.Length > 1)
        {
            linuxPath = linuxPath.TrimEnd('/');
        }

        if (!linuxPath.StartsWith('/'))
        {
            return "Enter a full Linux path, for example ~/projects or /home/me/projects.";
        }

        if (linuxPath.Any(char.IsControl) || linuxPath.Split('/').Any(part => part is "." or ".."))
        {
            return "The Linux path contains characters or parts that are not allowed.";
        }

        if (ProtectedPaths.Contains(linuxPath)
            || linuxPath == user.Home.TrimEnd('/')
            || DriveMount().IsMatch(linuxPath)
            || linuxPath.StartsWith("/proc/", StringComparison.Ordinal)
            || linuxPath.StartsWith("/sys/", StringComparison.Ordinal)
            || linuxPath.StartsWith("/dev/", StringComparison.Ordinal))
        {
            return $"{linuxPath} is a system folder and cannot be used. Choose a new folder, for example ~/projects.";
        }

        return null;
    }

    /// <summary>
    /// drvfs takes Windows paths; forward slashes avoid escaping problems
    /// in /etc/fstab.
    /// </summary>
    private static string ToDrvfsPath(string windowsPath) =>
        Path.GetFullPath(windowsPath).TrimEnd('\\').Replace('\\', '/');

    private sealed record TableEntry(string Source, bool ReadOnly);

    /// <summary>
    /// Reads Windows-folder mounts from /proc/mounts or /etc/fstab, keyed by
    /// Linux path. Whole-drive mounts such as /mnt/c are left out.
    /// </summary>
    private static Dictionary<string, TableEntry> ParseTable(string text, bool fromProcMounts)
    {
        var entries = new Dictionary<string, TableEntry>(StringComparer.Ordinal);

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] fields = Whitespace().Split(line);
            if (fields.Length < 4)
            {
                continue;
            }

            string type = fields[2];
            string options = fields[3];

            // WSL 1 reports drvfs; WSL 2 reports 9p or virtiofs with the
            // Windows path in the options.
            if (fromProcMounts ? type is not ("drvfs" or "9p" or "virtiofs") : type != "drvfs")
            {
                continue;
            }

            string source = Unescape(fields[0]);
            if (!WindowsPath().IsMatch(source) && OptionPath().Match(options) is { Success: true } match)
            {
                source = Unescape(match.Groups[1].Value);
            }

            if (!WindowsPath().IsMatch(source))
            {
                continue;
            }

            source = source.Replace('/', '\\');
            if (source.TrimEnd('\\').Length <= 2)
            {
                // A whole drive (C:\), mounted by WSL itself.
                continue;
            }

            bool readOnly = options.Split(',').Contains("ro");
            entries[Unescape(fields[1])] = new TableEntry(source.TrimEnd('\\'), readOnly);
        }

        return entries;
    }

    private static string Unescape(string value) =>
        OctalEscape().Replace(value, m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString());

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\\([0-7]{3})")]
    private static partial Regex OctalEscape();

    [GeneratedRegex(@"^([A-Za-z]:|\\\\|//)")]
    private static partial Regex WindowsPath();

    [GeneratedRegex(@"(?:^|[,;])path=([^;,]+)")]
    private static partial Regex OptionPath();

    [GeneratedRegex(@"^/mnt/[a-zA-Z]$")]
    private static partial Regex DriveMount();

    [GeneratedRegex(@"/{2,}")]
    private static partial Regex MultipleSlashes();
}
