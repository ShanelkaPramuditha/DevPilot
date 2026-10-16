namespace DevPilot.Models;

/// <summary>Top processes and listening ports of one distribution.</summary>
public sealed record DistroDetails(List<ProcessInfo> Processes, List<PortInfo> Ports);
