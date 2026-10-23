using System.Collections.Generic;
using MaxTweaks.Core.Chapters;
using MaxTweaks.Core.Chapters.Bookmarks;
using Xunit;

namespace MaxTweaks.Core.Tests.Chapters;

public class ChaptersRobustnessTests
{
    // Beyond Int64 (Newtonsoft stores it as BigInteger) and beyond float range.
    private const string Huge = "1234567890123456789012345678901234567890123456789";

    [Theory]
    [InlineData("{ \"start\": " + Huge + " }", "non-negative number of seconds")]
    [InlineData("{ \"start\": 1, \"end\": " + Huge + " }", "non-negative number of seconds")]
    [InlineData("{ \"startBeat\": " + Huge + " }", "non-negative number of beats")]
    [InlineData("{ \"startBeat\": 1, \"endBeat\": -" + Huge + " }", "non-negative number of beats")]
    [InlineData("{ \"start\": 1e300 }", "non-negative number of seconds")]
    [InlineData("{ \"startBeat\": \"1e300\" }", "non-negative number of beats")]
    public void HugeNumbers_AreRejected_NotThrown(string entry, string reason)
    {
        var doc = ChaptersParser.Parse("{ \"chapters\": [ " + entry + " ] }");
        Assert.Empty(doc.Chapters);
        Assert.Contains(reason, Assert.Single(doc.Warnings));
    }

    [Fact]
    public void LargeButValidInteger_IsAccepted()
    {
        var doc = ChaptersParser.Parse("""{ "chapters": [ { "start": 9007199254740993, "label": "far" } ] }""");
        Assert.Empty(doc.Warnings);
        Assert.Single(doc.Chapters);
    }

    [Fact]
    public void Bookmarks_WithHugeBeats_AreDropped()
    {
        var list = BookmarkParser.FromDifficultyJson("{ \"customData\": { \"bookmarks\": [ { \"b\": " + Huge + "0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000, \"n\": \"x\" }, { \"b\": " + Huge + ", \"n\": \"big\" }, { \"b\": 2, \"n\": \"ok\" } ] } }");
        Assert.NotNull(list);
        Assert.Equal(new[] { "ok", "big" }, list!.ConvertAll(b => b.Name));
    }

    [Fact]
    public void V4AudioRegions_ConvertBeatsLikeTheGame()
    {
        // 44.1 kHz: region 1 = 10 s for 20 beats (120 BPM), region 2 = 10 s for 10 beats (60 BPM).
        var tempo = BpmTimeline.FromAudioRegions(120f, 44100, new[]
        {
            new BpmRegion(0, 441000, 0f, 20f),
            new BpmRegion(441000, 882000, 20f, 30f),
            new BpmRegion(882000, 882000, 30f, 30f), // degenerate, ignored
        });

        Assert.Equal(2, tempo.SegmentCount);
        Assert.Equal(10f, tempo.BeatToSeconds(20f), 3);
        Assert.Equal(20f, tempo.BeatToSeconds(30f), 3);

        var warnings = new List<string>();
        var chapters = ChapterResolver.Resolve(
            ChaptersParser.Parse("""{ "chapters": [ { "startBeat": 10, "endBeat": 25, "label": "across the change" } ] }"""),
            tempo, "Standard", "Expert", 60f, warnings);
        Assert.Empty(warnings);
        var chapter = Assert.Single(chapters);
        Assert.Equal(5f, chapter.Start, 3);
        Assert.Equal(15f, chapter.End, 3);
    }

    [Fact]
    public void V4AudioRegions_BadFrequency_Throws()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => BpmTimeline.FromAudioRegions(120f, 0, new BpmRegion[0]));
    }
}
