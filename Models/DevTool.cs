namespace DevPilot.Models;

/// <summary>
/// A developer tool DevPilot can install, on Windows (<see cref="Windows"/>) and/or inside
/// WSL distributions (<see cref="Wsl"/>). A null package means "not offered there".
/// </summary>
public sealed record DevTool(
    string Id,
    string Name,
    string Description,
    string Website,
    WindowsPackage? Windows,
    WslPackage? Wsl,
    string? WslNote = null);
