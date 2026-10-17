namespace DevPilot.Models;

/// <summary>What a distribution offers for installing tools: its package manager, name, whether systemd runs, and the default user.</summary>
public sealed record DistroToolInfo(string PackageManager, string OsName, bool HasSystemd, string DefaultUser);
