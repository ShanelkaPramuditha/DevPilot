namespace DevPilot.Models;

/// <summary>
/// The values the host dialog edits. Empty values remove the option from the block;
/// options not listed here are left as they are.
/// </summary>
public sealed record SshHostEdit(
    string Patterns,
    string? HostName,
    string? User,
    string? Port,
    string? IdentityFile,
    string? ProxyJump,
    string? ForwardAgent);
