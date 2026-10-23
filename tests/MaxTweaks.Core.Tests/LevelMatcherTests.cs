using MaxTweaks.Core.Debugging;
using Xunit;

namespace MaxTweaks.Core.Tests;

public class LevelMatcherTests
{
    private static readonly LevelCandidate[] Levels =
    {
        new("custom_level_AAA", "849a (Sandstorm - Skyler Wallace)", "Sandstorm"),
        new("custom_level_BBB", "8699 (Bangarang - xScaramouche)", "Bangarang"),
        new("custom_level_CCC", "4d62 (Bangarang - nitronik.exe)", "Bangarang"),
        new("BeatSaber", null, "Beat Saber"),
    };

    [Fact]
    public void ExactIdWins()
    {
        var hits = LevelMatcher.Match(Levels, "custom_level_bbb");
        Assert.Single(hits);
        Assert.Equal("custom_level_BBB", hits[0].LevelId);
    }

    [Fact]
    public void FolderSubstring()
    {
        var hits = LevelMatcher.Match(Levels, "849a");
        Assert.Single(hits);
        Assert.Equal("custom_level_AAA", hits[0].LevelId);
    }

    [Fact]
    public void ExactSongNameBeatsSubstring()
    {
        var hits = LevelMatcher.Match(Levels, "beat saber");
        Assert.Single(hits);
        Assert.Equal("BeatSaber", hits[0].LevelId);
        Assert.Equal("BeatSaber (Beat Saber)", hits[0].ToString());
    }

    [Fact]
    public void AmbiguousReturnsAll()
    {
        Assert.Equal(2, LevelMatcher.Match(Levels, "Bangarang").Count);
    }

    [Fact]
    public void DuplicatesById_AreCollapsed()
    {
        var dup = new[] { Levels[0], new LevelCandidate("custom_level_AAA", "other copy 849a", "Sandstorm") };
        Assert.Single(LevelMatcher.Match(dup, "849a"));
    }

    [Fact]
    public void NoMatch()
    {
        Assert.Empty(LevelMatcher.Match(Levels, "nothing here"));
    }
}
