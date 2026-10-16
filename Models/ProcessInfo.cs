using System.Globalization;
using DevPilot.Core;

namespace DevPilot.Models;

/// <summary>A process in the "Top processes" list.</summary>
public sealed record ProcessInfo(int Pid, string Name, long MemoryBytes, double CpuPercent)
{
    public string MemoryText => Format.Size(MemoryBytes);

    public string CpuText => CpuPercent.ToString("0.0", CultureInfo.CurrentCulture) + "%";
}
