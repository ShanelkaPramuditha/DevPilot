using DevPilot.Core;

namespace DevPilot.Models;

/// <summary>A published GitHub release of DevPilot.</summary>
public sealed record ReleaseInfo(
    SemanticVersion Version,
    string Tag,
    string Title,
    string Notes,
    string PageUrl,
    DateTimeOffset? PublishedAt,
    bool IsPreRelease,
    IReadOnlyList<ReleaseAsset> Assets);
