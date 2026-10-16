namespace DevPilot.Models;

/// <summary>
/// Metadata for a distribution in the WSL online catalog.
/// </summary>
public sealed record CatalogEntry(
    string Name,
    string FriendlyName,
    string Version,
    string Publisher,
    bool Amd64,
    bool Arm64,
    string? Amd64Url,
    string? Arm64Url,
    string Support,
    bool IsRecommended);
