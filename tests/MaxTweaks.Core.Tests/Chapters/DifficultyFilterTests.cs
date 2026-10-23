using MaxTweaks.Core.Chapters;
using Xunit;

namespace MaxTweaks.Core.Tests.Chapters;

public class DifficultyFilterTests
{
    [Theory]
    [InlineData("ExpertPlus", "Standard", "ExpertPlus", true)]
    [InlineData("Expert+", "Standard", "ExpertPlus", true)]
    [InlineData("expert plus", "Standard", "ExpertPlus", true)]
    [InlineData("Expert", "Standard", "ExpertPlus", false)]
    [InlineData("Standard/Expert+", "Standard", "ExpertPlus", true)]
    [InlineData("Standard:Hard", "Standard", "Hard", true)]
    [InlineData("Lawless/Hard", "Standard", "Hard", false)]
    [InlineData("Lawless", "Lawless", "Easy", true)]
    [InlineData("one_saber", "OneSaber", "Normal", true)]
    [InlineData("Lawless", "Standard", "Easy", false)]
    public void Matches_SingleEntry(string entry, string characteristic, string difficulty, bool expected)
    {
        Assert.Equal(expected, DifficultyFilter.Matches(new[] { entry }, characteristic, difficulty));
    }

    [Fact]
    public void NullOrEmpty_MatchesEverything()
    {
        Assert.True(DifficultyFilter.Matches(null, "Standard", "Easy"));
        Assert.True(DifficultyFilter.Matches(new string[0], "Standard", "Easy"));
    }

    [Fact]
    public void AnyEntryMatching_IsEnough()
    {
        Assert.True(DifficultyFilter.Matches(new[] { "Easy", "Hard" }, "Standard", "Hard"));
        Assert.False(DifficultyFilter.Matches(new[] { "Easy", "Normal" }, "Standard", "Hard"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("Expert+", "expertplus")]
    [InlineData(" One-Saber_X ", "onesaberx")]
    public void Normalize(string? input, string expected)
    {
        Assert.Equal(expected, DifficultyFilter.Normalize(input));
    }
}
