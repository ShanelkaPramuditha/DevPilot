namespace DevPilot.Models;

/// <summary>
/// Resource usage of the WSL 2 virtual machine, which all WSL 2
/// distributions share.
/// </summary>
public sealed record VmUsage(
    double CpuPercent,
    int Cores,
    string LoadAverage,
    long MemoryTotal,
    long MemoryUsed,
    long SwapTotal,
    long SwapUsed,
    TimeSpan Uptime)
{
    /// <summary>Memory nobody is using.</summary>
    public long MemoryFree { get; init; }

    /// <summary>File cache and reclaimable kernel memory Linux keeps for speed.</summary>
    public long MemoryCache { get; init; }

    /// <summary>Memory held by programs: what is neither free nor cache (same as MemoryUsed).</summary>
    public long MemoryPrograms => MemoryUsed;

    /// <summary>The distribution the VM figures were read through.</summary>
    public string HostDistro { get; init; } = "";

    public double MemoryPercent => MemoryTotal > 0 ? 100.0 * MemoryUsed / MemoryTotal : 0;

    public double SwapPercent => SwapTotal > 0 ? 100.0 * SwapUsed / SwapTotal : 0;
}
