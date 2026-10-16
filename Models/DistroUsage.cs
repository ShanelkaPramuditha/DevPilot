using System.Globalization;
using DevPilot.Core;

namespace DevPilot.Models;

/// <summary>A row in the per-distribution usage table.</summary>
public sealed record DistroUsage(WslDistro Distro)
{
    public int? Processes { get; init; }

    public long? MemoryBytes { get; init; }

    /// <summary>Space used inside the distribution's file system.</summary>
    public long? DiskUsed { get; init; }

    /// <summary>
    /// Maximum size of the virtual disk as Linux sees it (1 TB by default).
    /// The disk file only grows to this as data is added.
    /// </summary>
    public long? DiskSize { get; init; }

    /// <summary>Space Linux reports as still available inside the virtual disk.</summary>
    public long? DiskAvailable { get; init; }

    /// <summary>Windows drive that holds the virtual disk file, for example "C:".</summary>
    public string? HostDrive { get; init; }

    /// <summary>Free space on that Windows drive.</summary>
    public long? HostFree { get; init; }

    /// <summary>
    /// Space the distribution can really still use: the virtual disk can only
    /// grow while the Windows drive has room, so the smaller of the two.
    /// </summary>
    public long? FreeSpace => (DiskAvailable, HostFree) switch
    {
        ({ } inside, { } host) => Math.Min(inside, host),
        (null, { } host) => host,
        ({ } inside, null) => inside,
        _ => null
    };

    private bool LimitedByHostDrive => HostFree is { } host && (DiskAvailable is not { } inside || host < inside);

    public string Name => Distro.Name;

    public string State => Distro.State;

    public bool IsRunning => Distro.IsRunning;

    public string ProcessesText => Processes?.ToString(CultureInfo.CurrentCulture) ?? "—";

    /// <summary>Share of the VM's memory, set once the VM total is known.</summary>
    public double MemoryShare { get; init; }

    public string MemoryText => Format.Size(MemoryBytes);

    /// <summary>Used share of the space the distribution can really use.</summary>
    public double DiskPercent => DiskUsed is { } used && FreeSpace is { } free && used + free > 0
        ? 100.0 * used / (used + free)
        : 0;

    public bool HasDiskUsage => DiskUsed is not null && FreeSpace is not null;

    public string DiskText => (DiskUsed, FreeSpace) switch
    {
        ({ } used, { } free) => $"{Format.Size(used)} used · {Format.Size(free)} free",
        (null, { } free) when HostDrive is not null => $"{Format.Size(free)} free on {HostDrive}",
        _ => "—"
    };

    public string DiskTooltip
    {
        get
        {
            var lines = new List<string>();
            if (DiskUsed is { } used)
            {
                lines.Add($"Used inside Linux: {Format.Size(used)}");
            }

            if (HostDrive is not null && HostFree is { } host)
            {
                lines.Add($"Windows drive {HostDrive} has {Format.Size(host)} free");
            }

            if (DiskSize is { } size)
            {
                lines.Add($"Virtual disk maximum size: {Format.Size(size)} (the file grows as needed)");
            }

            if (FreeSpace is not null)
            {
                lines.Add(LimitedByHostDrive
                    ? $"Free space is limited by drive {HostDrive}, not by the virtual disk."
                    : "Free space is limited by the virtual disk's maximum size.");
            }

            return lines.Count > 0 ? string.Join("\n", lines) : "Start the distribution to measure its disk use.";
        }
    }

    public string DiskFileText => Distro.DiskSizeText;
}
