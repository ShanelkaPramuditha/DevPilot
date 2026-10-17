using System.Text.RegularExpressions;

namespace DevPilot.Services.Wsl;

/// <summary>Rules for naming a new distribution (import, clone).</summary>
public static partial class DistroNames
{
    /// <summary>
    /// Returns why a name cannot be used, or null when it can. WSL accepts letters,
    /// digits, '.', '-' and '_', and names are compared without case.
    /// </summary>
    public static string? Validate(string? name, IEnumerable<string> existingNames)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Enter a name for the distribution.";
        }

        if (!Allowed().IsMatch(name))
        {
            return "Use only letters, digits, dots (.), hyphens (-) and underscores (_).";
        }

        return existingNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            ? $"A distribution named {name} already exists."
            : null;
    }

    /// <summary>A free name based on another: Ubuntu → Ubuntu-copy, Ubuntu-copy-2, ...</summary>
    public static string SuggestCopyName(string original, IEnumerable<string> existingNames)
    {
        var taken = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        string name = $"{original}-copy";
        for (int i = 2; taken.Contains(name); i++)
        {
            name = $"{original}-copy-{i}";
        }

        return name;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex Allowed();
}
