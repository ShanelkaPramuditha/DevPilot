namespace DevPilot.Models;

/// <summary>Default user of a distribution, used to own mounted files.</summary>
public sealed record DistroUser(string Uid, string Gid, string Home);
