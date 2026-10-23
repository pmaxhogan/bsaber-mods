using System.Collections.Generic;
using MaxTweaks.Core.Chapters;
using Xunit;

namespace MaxTweaks.Core.Tests.Chapters;

public class ChapterResolverTests
{
    private static List<Chapter> Resolve(string json, out List<string> warnings, BpmTimeline? tempo = null, float songEnd = 300f, string characteristic = "Standard", string difficulty = "Expert")
    {
        warnings = new List<string>();
        return ChapterResolver.Resolve(ChaptersParser.Parse(json), tempo, characteristic, difficulty, songEnd, warnings);
    }

    [Fact]
    public void TimeChapters_PassThrough()
    {
        var chapters = Resolve("""{ "chapters": [ { "start": "0:10", "end": "0:40", "label": "A" } ] }""", out var warnings);
        Assert.Empty(warnings);
        var a = Assert.Single(chapters);
        Assert.Equal("A", a.Label);
        Assert.Equal(10f, a.Start);
        Assert.Equal(40f, a.End);
        Assert.Equal(30f, a.Duration);
    }

    [Fact]
    public void BeatChapters_UseTempo_WithMidSongChange()
    {
        var tempo = BpmTimeline.FromSecondChanges(120f, new[] { new BpmChange(10f, 60f) });
        var chapters = Resolve("""{ "chapters": [ { "startBeat": 10, "endBeat": 25, "label": "B" } ] }""", out var warnings, tempo);
        Assert.Empty(warnings);
        var b = Assert.Single(chapters);
        Assert.Equal(5f, b.Start, 4);
        Assert.Equal(15f, b.End, 4);
    }

    [Fact]
    public void BeatChapters_WithoutTempo_AreSkipped()
    {
        var chapters = Resolve("""{ "chapters": [ { "startBeat": 10, "endBeat": 25 } ] }""", out var warnings, tempo: null);
        Assert.Empty(chapters);
        Assert.Contains("BPM is unknown", Assert.Single(warnings));
    }

    [Fact]
    public void MissingEnd_UsesSongEnd()
    {
        var tempo = BpmTimeline.Constant(120f);
        var chapters = Resolve("""{ "chapters": [ { "start": 200 }, { "startBeat": 400 } ] }""", out var warnings, tempo, songEnd: 250f);
        Assert.Empty(warnings);
        Assert.Equal(2, chapters.Count);
        Assert.Equal(250f, chapters[0].End);
        Assert.Equal(200f, chapters[1].Start);
        Assert.Equal(250f, chapters[1].End);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void MissingEnd_UnknownSongEnd_IsSkipped(float songEnd)
    {
        var chapters = Resolve("""{ "chapters": [ { "start": 10 }, { "start": 10, "end": 20 } ] }""", out var warnings, songEnd: songEnd);
        var kept = Assert.Single(chapters);
        Assert.Equal(20f, kept.End);
        Assert.Contains("song length is unknown", Assert.Single(warnings));
    }

    [Fact]
    public void DefaultLabel_UsesFilePosition()
    {
        var chapters = Resolve("""{ "chapters": [ { "start": 1, "end": 2, "label": "x" }, { "start": 5, "end": 9 } ] }""", out _);
        Assert.Equal("Chapter 2", chapters[1].Label);
        Assert.Equal(1, chapters[1].Index);
    }

    [Theory]
    [InlineData("{ \"start\": 10, \"end\": 10 }", "is not after start")]
    [InlineData("{ \"start\": 10, \"end\": 5 }", "is not after start")]
    [InlineData("{ \"start\": 10, \"end\": 10.5 }", "shorter than 1s")]
    [InlineData("{ \"start\": 300, \"end\": 400 }", "after the song ends")]
    [InlineData("{ \"start\": 350 }", "after the song ends")]
    [InlineData("{ \"start\": 299.5, \"end\": 400 }", "shorter than 1s")]
    public void InvalidRanges_AreSkipped(string entry, string reason)
    {
        var chapters = Resolve("{ \"chapters\": [ " + entry + " ] }", out var warnings);
        Assert.Empty(chapters);
        Assert.Contains(reason, Assert.Single(warnings));
    }

    [Fact]
    public void ExactlyOneSecond_IsKept()
    {
        var chapters = Resolve("""{ "chapters": [ { "start": 10, "end": 11 } ] }""", out var warnings);
        Assert.Single(chapters);
        Assert.Empty(warnings);
    }

    [Fact]
    public void EndPastSong_IsClamped()
    {
        var chapters = Resolve("""{ "chapters": [ { "start": 280, "end": 400 } ] }""", out var warnings);
        Assert.Empty(warnings);
        Assert.Equal(300f, Assert.Single(chapters).End);
    }

    [Fact]
    public void DifficultyFilter_IsApplied()
    {
        const string json = """
            { "chapters": [
              { "start": 1, "end": 5, "label": "all" },
              { "start": 1, "end": 5, "label": "ex+", "difficulties": ["Expert+"] },
              { "start": 1, "end": 5, "label": "lawless-ex", "difficulties": ["Lawless/Expert"] }
            ] }
            """;
        Assert.Equal(new[] { "all", "ex+" }, Labels(Resolve(json, out _, difficulty: "ExpertPlus")));
        Assert.Equal(new[] { "all" }, Labels(Resolve(json, out _, difficulty: "Expert")));
        Assert.Equal(new[] { "all", "lawless-ex" }, Labels(Resolve(json, out _, characteristic: "Lawless", difficulty: "Expert")));
    }

    [Fact]
    public void ChapterHelpers()
    {
        var chapter = new Chapter(3, "x", 10f, 20f);
        Assert.True(chapter.Contains(10f));
        Assert.False(chapter.Contains(20f));
        Assert.Equal(0f, chapter.Progress(5f));
        Assert.Equal(0.5f, chapter.Progress(15f));
        Assert.Equal(1f, chapter.Progress(25f));
        Assert.Equal("#3 'x' [0:10.000, 0:20.000)", chapter.ToString());
    }

    private static List<string> Labels(List<Chapter> chapters) => chapters.ConvertAll(c => c.Label);
}
