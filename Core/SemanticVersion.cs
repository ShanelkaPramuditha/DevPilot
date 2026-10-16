using System.Globalization;
using System.Text.RegularExpressions;

namespace DevPilot.Core;

/// <summary>
/// A version such as 1.5.0 or 1.5.0-beta.2, compared by the Semantic Versioning rules:
/// a pre-release sorts before its release, and numeric labels compare as numbers
/// (beta.2 &lt; beta.10). Build metadata after '+' is ignored.
/// </summary>
public sealed partial record SemanticVersion(int Major, int Minor, int Patch, string PreRelease) : IComparable<SemanticVersion>
{
    public bool IsPreRelease => PreRelease.Length > 0;

    /// <summary>Parses "1.5.0", "v1.5.0-beta.2" or "1.5.0+abc123". Returns null when the text is not a version.</summary>
    public static SemanticVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Pattern().Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        return new SemanticVersion(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
            match.Groups[4].Success ? match.Groups[4].Value : "");
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int result = Major.CompareTo(other.Major);
        if (result == 0)
        {
            result = Minor.CompareTo(other.Minor);
        }

        if (result == 0)
        {
            result = Patch.CompareTo(other.Patch);
        }

        return result != 0 ? result : ComparePreRelease(PreRelease, other.PreRelease);
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";

    private static int ComparePreRelease(string left, string right)
    {
        // A release is newer than any of its pre-releases.
        if (left.Length == 0 || right.Length == 0)
        {
            return right.Length.CompareTo(left.Length);
        }

        string[] a = left.Split('.');
        string[] b = right.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool aNumber = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out int aValue);
            bool bNumber = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out int bValue);

            int result = (aNumber, bNumber) switch
            {
                (true, true) => aValue.CompareTo(bValue),
                (true, false) => -1,   // numbers sort before words
                (false, true) => 1,
                _ => string.CompareOrdinal(a[i], b[i])
            };

            if (result != 0)
            {
                return result;
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    [GeneratedRegex(@"^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex Pattern();
}
