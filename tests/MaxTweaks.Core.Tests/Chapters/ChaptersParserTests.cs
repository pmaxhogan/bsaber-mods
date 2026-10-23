using System.Linq;
using MaxTweaks.Core.Chapters;
using Xunit;

namespace MaxTweaks.Core.Tests.Chapters;

public class ChaptersParserTests
{
    [Fact]
    public void Parses_TimeAndBeatChapters()
    {
        var doc = ChaptersParser.Parse("""
            { "chapters": [
              { "start": "0:12.5", "end": 45, "label": " Intro " },
              { "startBeat": 64, "endBeat": "128.5", "label": "Drop", "difficulties": ["Expert", "ExpertPlus"] },
              { "start": 120, "label": "Outro" }
            ] }
            """);

        Assert.Empty(doc.Warnings);
        Assert.Equal(3, doc.Chapters.Count);

        var intro = doc.Chapters[0];
        Assert.Equal(0, intro.Index);
        Assert.Equal("Intro", intro.Label);
        Assert.False(intro.UsesBeats);
        Assert.Equal(12.5f, intro.Start);
        Assert.Equal(45f, intro.End);
        Assert.Null(intro.Difficulties);

        var drop = doc.Chapters[1];
        Assert.True(drop.UsesBeats);
        Assert.Equal(64f, drop.StartBeat);
        Assert.Equal(128.5f, drop.EndBeat);
        Assert.Equal(new[] { "Expert", "ExpertPlus" }, drop.Difficulties);

        var outro = doc.Chapters[2];
        Assert.Equal(120f, outro.Start);
        Assert.Null(outro.End);
    }

    [Fact]
    public void Accepts_BareArray_CaseInsensitiveKeys_AndSingleDifficultyString()
    {
        var doc = ChaptersParser.Parse("""[ { "START": "1:00", "End": "1:30", "Label": "A", "Difficulties": "Hard" } ]""");
        Assert.Empty(doc.Warnings);
        var chapter = Assert.Single(doc.Chapters);
        Assert.Equal(60f, chapter.Start);
        Assert.Equal(90f, chapter.End);
        Assert.Equal(new[] { "Hard" }, chapter.Difficulties);
    }

    [Fact]
    public void MissingOrEmptyLabel_IsNull()
    {
        var doc = ChaptersParser.Parse("""{ "chapters": [ { "start": 1, "end": 5 }, { "start": 1, "end": 5, "label": "  " }, { "start": 1, "end": 5, "label": null } ] }""");
        Assert.Empty(doc.Warnings);
        Assert.All(doc.Chapters, c => Assert.Null(c.Label));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_GivesWarning(string? json)
    {
        var doc = ChaptersParser.Parse(json);
        Assert.Empty(doc.Chapters);
        Assert.Single(doc.Warnings);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"chapters\": [] } trailing")]
    [InlineData("{ \"chapters\": [ }")]
    public void InvalidJson_GivesWarning_NoThrow(string json)
    {
        var doc = ChaptersParser.Parse(json);
        Assert.Empty(doc.Chapters);
        Assert.Contains("not valid JSON", Assert.Single(doc.Warnings));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"chapters\": 5 }")]
    [InlineData("\"text\"")]
    public void MissingChaptersArray_GivesWarning(string json)
    {
        var doc = ChaptersParser.Parse(json);
        Assert.Empty(doc.Chapters);
        Assert.Contains("no \"chapters\" array", Assert.Single(doc.Warnings));
    }

    [Theory]
    [InlineData("5", "not an object")]
    [InlineData("{ \"label\": 5, \"start\": 1 }", "\"label\" must be a string")]
    [InlineData("{ \"start\": 1, \"endBeat\": 4 }", "mixes start/end")]
    [InlineData("{ \"end\": 10 }", "missing \"start\"")]
    [InlineData("{ \"endBeat\": 10 }", "missing \"startBeat\"")]
    [InlineData("{ \"start\": \"1:75\" }", "must be seconds or")]
    [InlineData("{ \"start\": true }", "must be seconds or")]
    [InlineData("{ \"start\": -1 }", "non-negative number of seconds")]
    [InlineData("{ \"start\": 1, \"end\": \"x\" }", "\"end\" must be seconds")]
    [InlineData("{ \"startBeat\": \"abc\" }", "must be a number of beats")]
    [InlineData("{ \"startBeat\": -4 }", "non-negative number of beats")]
    [InlineData("{ \"startBeat\": [1] }", "must be a number of beats")]
    [InlineData("{ \"start\": 1, \"difficulties\": [1] }", "must contain strings")]
    [InlineData("{ \"start\": 1, \"difficulties\": {} }", "string or an array")]
    public void BadEntry_IsSkippedWithReason(string entry, string reason)
    {
        var doc = ChaptersParser.Parse("{ \"chapters\": [ " + entry + ", { \"start\": 1, \"end\": 9, \"label\": \"ok\" } ] }");
        var ok = Assert.Single(doc.Chapters);
        Assert.Equal("ok", ok.Label);
        Assert.Equal(1, ok.Index);
        var warning = Assert.Single(doc.Warnings);
        Assert.Contains("chapter #1", warning);
        Assert.Contains(reason, warning);
    }

    [Fact]
    public void Warning_IncludesLabelWhenKnown()
    {
        var doc = ChaptersParser.Parse("""{ "chapters": [ { "label": "Bridge", "end": 3 } ] }""");
        Assert.Contains("('Bridge')", Assert.Single(doc.Warnings));
    }

    [Fact]
    public void EmptyDifficulties_AppliesToAll_WithWarning()
    {
        var doc = ChaptersParser.Parse("""{ "chapters": [ { "start": 1, "end": 5, "difficulties": [] }, { "start": 1, "end": 5, "difficulties": [" "] } ] }""");
        Assert.Equal(2, doc.Chapters.Count);
        Assert.All(doc.Chapters, c => Assert.Null(c.Difficulties));
        Assert.Equal(2, doc.Warnings.Count);
    }

    [Fact]
    public void NullFields_AreTreatedAsMissing()
    {
        var doc = ChaptersParser.Parse("""{ "chapters": [ { "start": 2, "end": null, "startBeat": null, "difficulties": null } ] }""");
        Assert.Empty(doc.Warnings);
        var chapter = Assert.Single(doc.Chapters);
        Assert.Equal(2f, chapter.Start);
        Assert.Null(chapter.End);
        Assert.False(chapter.UsesBeats);
    }

    [Fact]
    public void DateLikeStrings_AreNotConvertedToDates()
    {
        // Newtonsoft would otherwise turn ISO-looking strings into DateTime tokens.
        var doc = ChaptersParser.Parse("""{ "chapters": [ { "start": "2020-01-01T00:00:00", "end": 5 } ] }""");
        Assert.Empty(doc.Chapters);
        Assert.Contains("2020-01-01T00:00:00", doc.Warnings.Single());
    }
}
