using DevPilot.Core;

namespace DevPilot.Tests.Core;

public class FuzzyMatcherTests
{
    [Fact]
    public void Score_EmptyQueryMatchesEverything()
    {
        Assert.Equal(0, FuzzyMatcher.Score("", "Anything"));
    }

    [Fact]
    public void Score_CharactersMustBeInOrder()
    {
        Assert.NotNull(FuzzyMatcher.Score("termub", "Open terminal: Ubuntu"));
        Assert.Null(FuzzyMatcher.Score("ubterm", "Open terminal: Ubuntu"));
    }

    [Theory]
    [InlineData("ot", "Open Terminal")]
    [InlineData("wsls", "WSL Settings")]
    [InlineData("sshweb", "SSH: web")]
    [InlineData("TERMINAL", "open terminal")]
    public void Score_MatchesCharactersInOrder(string query, string text)
    {
        Assert.NotNull(FuzzyMatcher.Score(query, text));
    }

    [Theory]
    [InlineData("xyz", "Open Terminal")]
    [InlineData("termo", "Open Terminal")]
    public void Score_RejectsMissingCharacters(string query, string text)
    {
        Assert.Null(FuzzyMatcher.Score(query, text));
    }

    [Fact]
    public void Score_RanksBetterMatchesHigher()
    {
        // Substring at a word start > substring elsewhere > scattered letters.
        int start = FuzzyMatcher.Score("set", "Settings: Ubuntu")!.Value;
        int middle = FuzzyMatcher.Score("set", "Reset view")!.Value;
        int scattered = FuzzyMatcher.Score("set", "Start Ubuntu-Experimental Terminal")!.Value;

        Assert.True(start > middle, $"{start} > {middle}");
        Assert.True(middle > scattered, $"{middle} > {scattered}");
    }

    [Fact]
    public void Score_PrefersMatchesAtWordStarts()
    {
        // Whole-text matches win; among them, one starting a word ranks above one inside a word.
        Assert.True(FuzzyMatcher.Score("term", "Open Terminal") > FuzzyMatcher.Score("term", "Determine"));
        // Among scattered matches, letters at word starts (initials) rank higher.
        Assert.True(FuzzyMatcher.Score("otu", "Open Terminal: Ubuntu") > FuzzyMatcher.Score("otu", "Docker output"));
    }
}
