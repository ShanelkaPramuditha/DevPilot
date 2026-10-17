namespace DevPilot.Models;

/// <summary>
/// A "Host" (or "Match") block of an SSH config file. The common options have their own
/// properties; everything else in the block is listed in <see cref="OtherOptions"/>.
/// </summary>
public sealed record SshHost(
    int BlockIndex,
    string Patterns,
    bool IsMatchBlock,
    string? HostName,
    string? User,
    string? Port,
    string? IdentityFile,
    string? ProxyJump,
    string? ForwardAgent,
    IReadOnlyList<KeyValuePair<string, string>> OtherOptions)
{
    /// <summary>The name to connect with: the first pattern without wildcards, if any.</summary>
    public string? Alias => IsMatchBlock
        ? null
        : Patterns.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(p => !p.StartsWith('!') && p.IndexOfAny(['*', '?']) < 0);

    public bool CanConnect => Alias is not null;

    public string KindText => IsMatchBlock ? "Match" : Alias is null ? "Defaults" : "Host";

    /// <summary>How the block is shown in the list: catch-all and Match blocks are labelled.</summary>
    public string DisplayName => IsMatchBlock
        ? $"Match {Patterns}"
        : Alias is null ? $"{Patterns}  (defaults for matching hosts)" : Patterns;

    public string PortText => Port ?? "";

    public string OtherOptionsText => OtherOptions.Count == 0
        ? ""
        : string.Join(", ", OtherOptions.Select(o => o.Key));

    public string OtherOptionsTooltip => OtherOptions.Count == 0
        ? "No other options"
        : string.Join("\n", OtherOptions.Select(o => $"{o.Key} {o.Value}"));
}
