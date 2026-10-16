namespace DevPilot.Models;

/// <summary>A file attached to a release, such as an installer or SHA256SUMS.txt.</summary>
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);
