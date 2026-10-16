using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using DevPilot.Models;

namespace DevPilot.Services.Wsl;

/// <summary>
/// Reads usage from inside running distributions. Stopped distributions are
/// never started just to measure them.
/// </summary>
public static class UsageStats
{
    // Two /proc/stat samples half a second apart give the current CPU use.
    private const string VmScript = """
        head -n 1 /proc/stat
        sleep 0.5
        head -n 1 /proc/stat
        echo "cores $(grep -c ^processor /proc/cpuinfo)"
        grep -E '^(MemTotal|MemFree|MemAvailable|Buffers|Cached|SReclaimable|SwapTotal|SwapFree):' /proc/meminfo
        echo "uptime $(cut -d ' ' -f 1 /proc/uptime)"
        echo "load $(cut -d ' ' -f 1-3 /proc/loadavg)"
        """;

    // Each distribution has its own process namespace, so /proc lists only
    // its own processes.
    private const string DistroScript = """
        echo "procs $(ls -d /proc/[0-9]* 2>/dev/null | wc -l)"
        echo "rss $(cat /proc/[0-9]*/status 2>/dev/null | awk '/^VmRSS:/ { s += $2 } END { print s + 0 }')"
        echo "df $(df -kP / 2>/dev/null | awk 'NR == 2 { print $2, $3, $4 }')"
        """;

    /// <summary>
    /// Reads VM-wide usage through a running WSL 2 distribution.
    /// Returns null when none is running (the VM is off).
    /// </summary>
    public static async Task<VmUsage?> GetVmUsageAsync(IEnumerable<WslDistro> distros)
    {
        var host = distros.FirstOrDefault(d => d.IsRunning && d.Version == "2");
        if (host is null)
        {
            return null;
        }

        string output = await WslService.RunScriptAsync(host.Name, VmScript, asRoot: false);
        var lines = output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var cpuSamples = lines.Where(l => l.StartsWith("cpu ", StringComparison.Ordinal)).ToList();
        double cpu = cpuSamples.Count >= 2 ? CpuPercent(cpuSamples[0], cpuSamples[1]) : 0;

        var values = lines
            .Select(l => l.Split([' ', ':'], 2, StringSplitOptions.TrimEntries))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0])
            .ToDictionary(g => g.Key, g => g.Last()[1]);

        long Kb(string key) =>
            values.TryGetValue(key, out string? v) && long.TryParse(v.Split(' ')[0], out long kb) ? kb * 1024 : 0;

        long memTotal = Kb("MemTotal");
        long swapTotal = Kb("SwapTotal");
        double uptimeSeconds = values.TryGetValue("uptime", out string? up)
            && double.TryParse(up, NumberStyles.Float, CultureInfo.InvariantCulture, out double s) ? s : 0;

        return new VmUsage(
            CpuPercent: cpu,
            Cores: values.TryGetValue("cores", out string? c) && int.TryParse(c, out int cores) ? cores : 0,
            LoadAverage: values.GetValueOrDefault("load") ?? "—",
            MemoryTotal: memTotal,
            // "Used" as the free command reports it: neither free nor cache.
            MemoryUsed: Math.Max(0, memTotal - Kb("MemFree") - (Kb("Buffers") + Kb("Cached") + Kb("SReclaimable"))),
            SwapTotal: swapTotal,
            SwapUsed: Math.Max(0, swapTotal - Kb("SwapFree")),
            Uptime: TimeSpan.FromSeconds(uptimeSeconds))
        {
            MemoryFree = Kb("MemFree"),
            MemoryCache = Kb("Buffers") + Kb("Cached") + Kb("SReclaimable"),
            HostDistro = host.Name
        };
    }

    // Top processes by memory, then listening ports. Run as root so ss can
    // name the processes of every user. Each part is optional: minimal
    // distributions may not have ps or ss.
    private const string DetailsScript = """
        if command -v ps >/dev/null 2>&1; then
            ps -eo pid=,rss=,pcpu=,comm= --sort=-rss 2>/dev/null | head -n 8
        fi
        echo '--- ports ---'
        if command -v ss >/dev/null 2>&1; then
            ss -Htulnp 2>/dev/null
        fi
        """;

    /// <summary>
    /// Top processes and listening ports of a running distribution.
    /// </summary>
    public static async Task<DistroDetails> GetDetailsAsync(string distro)
    {
        string output = await WslService.RunScriptAsync(distro, DetailsScript, asRoot: true);
        string[] parts = output.Split("--- ports ---");

        var processes = new List<ProcessInfo>();
        foreach (string line in parts[0].Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length == 4 && int.TryParse(f[0], out int pid) && long.TryParse(f[1], out long rss)
                && double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double cpu))
            {
                processes.Add(new ProcessInfo(pid, f[3].Trim(), rss * 1024, cpu));
            }
        }

        var ports = new List<PortInfo>();
        if (parts.Length > 1)
        {
            foreach (string line in parts[1].Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                // tcp LISTEN 0 4096 127.0.0.53%lo:53 0.0.0.0:* users:(("systemd-resolve",pid=69,fd=17))
                string[] f = Regex.Split(line, @"\s+");
                if (f.Length < 5)
                {
                    continue;
                }

                string local = f[4];
                int colon = local.LastIndexOf(':');
                if (colon <= 0 || !int.TryParse(local[(colon + 1)..], out int port))
                {
                    continue;
                }

                string address = local[..colon].Split('%')[0].Trim('[', ']');
                var owner = Regex.Match(line, @"users:\(\(""([^""]+)"",pid=(\d+)");
                ports.Add(new PortInfo(
                    Protocol: f[0].ToUpperInvariant(),
                    Port: port,
                    Address: address,
                    Process: owner.Success ? owner.Groups[1].Value : "",
                    Pid: owner.Success && int.TryParse(owner.Groups[2].Value, out int pid) ? pid : null));
            }
        }

        return new DistroDetails(
            processes,
            ports
                .GroupBy(p => (p.Protocol, p.Port, p.Address))
                .Select(g => g.First())
                .OrderBy(p => p.Port)
                .ThenBy(p => p.Protocol)
                .ToList());
    }

    /// <summary>
    /// Asks Linux to drop its file cache, so the VM can give that memory back to Windows.
    /// </summary>
    public static Task DropCachesAsync(string distro) =>
        WslService.RunScriptAsync(distro, "sync\necho 3 > /proc/sys/vm/drop_caches", asRoot: true);

    /// <summary>
    /// Working set of the WSL VM process on Windows (what Task Manager shows as vmmemWSL).
    /// </summary>
    public static long? GetWindowsVmMemory()
    {
        try
        {
            var processes = Process.GetProcessesByName("vmmemWSL");
            if (processes.Length == 0)
            {
                processes = Process.GetProcessesByName("vmmem");
            }

            long total = 0;
            foreach (var process in processes)
            {
                using (process)
                {
                    total += process.WorkingSet64;
                }
            }

            return processes.Length > 0 ? total : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads per-distribution usage. Stopped distributions only report their
    /// disk file and the free space on the Windows drive that holds it.
    /// </summary>
    public static async Task<DistroUsage> GetDistroUsageAsync(WslDistro distro)
    {
        var (hostDrive, hostFree) = GetHostDriveSpace(distro.Location);
        var usage = new DistroUsage(distro) { HostDrive = hostDrive, HostFree = hostFree };

        if (!distro.IsRunning)
        {
            return usage;
        }

        try
        {
            string output = await WslService.RunScriptAsync(distro.Name, DistroScript, asRoot: false);
            var values = output
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Split(' ', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1].Trim());

            string[] df = values.GetValueOrDefault("df")?.Split(' ') ?? [];

            long? Df(int index) =>
                df.Length == 3 && long.TryParse(df[index], out long kb) ? kb * 1024 : null;

            return usage with
            {
                Processes = int.TryParse(values.GetValueOrDefault("procs"), out int procs) ? procs : null,
                MemoryBytes = long.TryParse(values.GetValueOrDefault("rss"), out long rss) ? rss * 1024 : null,
                DiskSize = Df(0),
                DiskUsed = Df(1),
                DiskAvailable = Df(2)
            };
        }
        catch
        {
            // The distribution may have stopped between listing and measuring.
            return usage;
        }
    }

    /// <summary>
    /// Free space on the Windows drive that holds a distribution's files.
    /// </summary>
    public static (string? Drive, long? Free) GetHostDriveSpace(string? location)
    {
        try
        {
            string? root = string.IsNullOrEmpty(location) ? null : Path.GetPathRoot(location);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return (null, null);
            }

            var drive = new DriveInfo(root);
            return drive.IsReady ? (root.TrimEnd('\\'), drive.AvailableFreeSpace) : (null, null);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// CPU use between two "cpu ..." lines of /proc/stat: the share of
    /// time that was not idle or waiting on I/O.
    /// </summary>
    private static double CpuPercent(string first, string second)
    {
        static (long Busy, long Total) Parse(string line)
        {
            long[] v = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Select(x => long.TryParse(x, out long n) ? n : 0)
                .ToArray();
            long total = v.Take(8).Sum();
            long idle = v.Length > 4 ? v[3] + v[4] : 0;
            return (total - idle, total);
        }

        var (busy1, total1) = Parse(first);
        var (busy2, total2) = Parse(second);
        long totalDelta = total2 - total1;
        return totalDelta > 0 ? Math.Clamp(100.0 * (busy2 - busy1) / totalDelta, 0, 100) : 0;
    }
}
