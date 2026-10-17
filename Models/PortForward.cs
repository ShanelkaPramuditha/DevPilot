namespace DevPilot.Models;

/// <summary>A Windows port forward ("netsh interface portproxy" v4tov4 entry).</summary>
public sealed record PortForward(string ListenAddress, int ListenPort, string ConnectAddress, int ConnectPort);
