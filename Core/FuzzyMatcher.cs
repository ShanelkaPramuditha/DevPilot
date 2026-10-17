namespace DevPilot.Core;

/// <summary>
/// Matches typed text against command names the way editor command palettes do: the
/// typed characters must appear in order, and matches that are contiguous, at the start
/// of words, or at the start of the text rank higher.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>A score (higher is better), or null when the text does not match.</summary>
    public static int? Score(string query, string text)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return 0;
        }

        // A plain substring is the strongest signal; earlier is better.
        int index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return 1000 - index + (index == 0 || IsWordStart(text, index) ? 200 : 0);
        }

        int score = 0;
        int position = 0;
        int previous = -2;
        foreach (char c in query)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            int found = IndexOf(text, c, position);
            if (found < 0)
            {
                return null;
            }

            score += 10;
            if (found == previous + 1)
            {
                score += 15;   // continues the previous match
            }

            if (IsWordStart(text, found))
            {
                score += 20;   // "ot" → "Open Terminal"
            }

            score -= Math.Min(found - position, 10);   // characters skipped
            previous = found;
            position = found + 1;
        }

        return score;
    }

    private static int IndexOf(string text, char c, int start)
    {
        for (int i = start; i < text.Length; i++)
        {
            if (char.ToUpperInvariant(text[i]) == char.ToUpperInvariant(c))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsWordStart(string text, int index) =>
        index == 0
        || !char.IsLetterOrDigit(text[index - 1])
        || (char.IsUpper(text[index]) && char.IsLower(text[index - 1]));
}
