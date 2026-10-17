namespace DevPilot.Models;

/// <summary>What a distribution has for remote access: its SSH server and Tailscale.</summary>
public sealed record RemoteAccessStatus(
    string PackageManager,
    bool HasSystemd,
    bool SshInstalled,
    bool SshRunning,
    int? SshPort,
    bool TailscaleInstalled,
    string? TailscaleState,
    string? TailscaleName,
    string? TailscaleAddress)
{
    /// <summary>Tailscale is signed in and connected.</summary>
    public bool TailscaleConnected => TailscaleState == "Running";
}
