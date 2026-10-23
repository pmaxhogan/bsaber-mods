using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MaxTweaks.Core.Chapters;
using MaxTweaks.Core.Chapters.Bookmarks;
using Xunit;

namespace MaxTweaks.Core.Tests.Chapters;

public class BookmarkParserTests
{
    [Fact]
    public void V2_Bookmarks()
    {
        var list = BookmarkParser.FromDifficultyJson("""
            { "_version": "2.0.0", "_customData": { "_bookmarks": [
              { "_time": 69, "_name": "Vocal", "_color": [1, 0, 0, 1] },
              { "_time": 5, "_name": " Intro " }
            ] } }
            """);
        Assert.NotNull(list);
        Assert.Equal(new[] { 5.0, 69.0 }, list!.Select(b => b.Beat));
        Assert.Equal(new[] { "Intro", "Vocal" }, list.Select(b => b.Name));
    }

    [Fact]
    public void V3_Bookmarks_BothKeyStyles()
    {
        var list = BookmarkParser.FromDifficultyJson("""
            { "version": "3.3.0", "customData": { "bookmarks": [
              { "b": 10, "n": "gold dust intro", "c": [0.6, 0.25, 1, 1] },
              { "_time": 42.5, "_name": "converted from v2" },
              { "b": "74", "n": "string beat" }
            ] } }
            """);
        Assert.Equal(new[] { 10.0, 42.5, 74.0 }, list!.Select(b => b.Beat));
        Assert.Equal("converted from v2", list![1].Name);
    }

    [Fact]
    public void BadEntries_AreDropped_AndOrderIsStable()
    {
        var list = BookmarkParser.FromDifficultyJson("""
            { "customData": { "bookmarks": [ 5, { "n": "no beat" }, { "b": -1, "n": "negative" }, { "b": "x" },
              { "b": 8, "n": "first" }, { "b": 8, "n": "second" }, { "b": 2 } ] } }
            """);
        Assert.Equal(new[] { "", "first", "second" }, list!.Select(b => b.Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{ \"_customData\": { } }")]
    [InlineData("{ \"customData\": { \"bookmarks\": [] } }")]
    [InlineData("{ \"customData\": { \"bookmarks\": 5 } }")]
    public void NoBookmarks_ReturnsNull(string? json)
    {
        Assert.Null(BookmarkParser.FromDifficultyJson(json));
    }

    [Fact]
    public void V4_BookmarkFile()
    {
        var file = BookmarkParser.FromBookmarkFileJson("""
            { "name": "ChroMapper", "characteristic": "Standard", "difficulty": "ExpertPlus", "color": "00FFFF",
              "bookmarks": [ { "beat": 32, "label": "Drop", "text": "the big drop" }, { "beat": 0, "text": "Start" } ] }
            """);
        Assert.NotNull(file);
        Assert.Equal("Standard", file!.Characteristic);
        Assert.Equal("ExpertPlus", file.Difficulty);
        Assert.Equal(new[] { "Start", "Drop" }, file.Bookmarks.Select(b => b.Name));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"bookmarks\": {} }")]
    [InlineData("broken")]
    public void V4_NotABookmarkFile(string json)
    {
        Assert.Null(BookmarkParser.FromBookmarkFileJson(json));
    }
}

public class MapInfoParserTests
{
    [Fact]
    public void V2Info()
    {
        var list = MapInfoParser.Parse("""
            { "_version": "2.1.0", "_beatsPerMinute": 174, "_difficultyBeatmapSets": [
              { "_beatmapCharacteristicName": "Standard", "_difficultyBeatmaps": [
                { "_difficulty": "Expert", "_beatmapFilename": "ExpertStandard.dat" },
                { "_difficulty": "ExpertPlus", "_beatmapFilename": "ExpertPlusStandard.dat" } ] },
              { "_beatmapCharacteristicName": "Lawless", "_difficultyBeatmaps": [ { "_difficulty": "Hard" }, 5 ] },
              { "_difficultyBeatmaps": [ { "_difficulty": "Easy", "_beatmapFilename": "Easy.dat" } ] },
              7
            ] }
            """);
        Assert.NotNull(list);
        Assert.Equal(
            new[] { "Standard/Expert/ExpertStandard.dat", "Standard/ExpertPlus/ExpertPlusStandard.dat", "Lawless/Hard/", "Standard/Easy/Easy.dat" },
            list!.Select(d => $"{d.Characteristic}/{d.Difficulty}/{d.BeatmapFileName}"));
    }

    [Fact]
    public void V4Info()
    {
        var list = MapInfoParser.Parse("""
            { "version": "4.0.0", "difficultyBeatmaps": [
              { "characteristic": "Standard", "difficulty": "Hard", "beatmapDataFilename": "Hard.dat", "lightshowDataFilename": "Lights.dat" },
              { "difficulty": "Easy" },
              { "characteristic": "Standard" }
            ] }
            """);
        Assert.Equal(new[] { "Standard/Hard/Hard.dat", "Standard/Easy/" }, list!.Select(d => $"{d.Characteristic}/{d.Difficulty}/{d.BeatmapFileName}"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("nope")]
    public void Unrecognised_ReturnsNull(string json)
    {
        Assert.Null(MapInfoParser.Parse(json));
    }
}

public class BookmarkChapterBuilderTests
{
    private static DifficultyBookmarks Diff(string characteristic, string difficulty, params (double Beat, string Name)[] bookmarks) =>
        new(characteristic, difficulty, bookmarks.Select(b => new Bookmark(b.Beat, b.Name)).ToList());

    [Fact]
    public void SameBookmarksEverywhere_NoDifficultyFilter_LastChapterOpen()
    {
        var notes = new List<string>();
        var chapters = BookmarkChapterBuilder.Build(new[]
        {
            Diff("Standard", "Expert", (5, "Intro"), (69, "Vocal"), (101, "")),
            Diff("Standard", "ExpertPlus", (5, "Intro"), (69, "Vocal"), (101, "")),
            Diff("Standard", "Easy"),
        }, notes);

        Assert.Equal(3, chapters.Count);
        Assert.All(chapters, c => Assert.Null(c.Difficulties));
        Assert.Equal((5.0, (double?)69.0, "Intro"), (chapters[0].StartBeat, chapters[0].EndBeat, chapters[0].Label));
        Assert.Equal((69.0, (double?)101.0), (chapters[1].StartBeat, chapters[1].EndBeat));
        Assert.Null(chapters[2].EndBeat);
        Assert.Equal("Chapter 3", chapters[2].Label);
        Assert.Contains(notes, n => n.Contains("Standard/Easy: no bookmarks"));
    }

    [Fact]
    public void DifferentBookmarks_AreGroupedPerDifficulty()
    {
        var notes = new List<string>();
        var chapters = BookmarkChapterBuilder.Build(new[]
        {
            Diff("Standard", "Hard", (0, "A"), (16, "B")),
            Diff("Standard", "Expert", (0, "A"), (32, "C")),
            Diff("Standard", "ExpertPlus", (0, "A"), (16, "B")),
            Diff("Standard", "Easy"),
        }, notes);

        Assert.Equal(4, chapters.Count);
        Assert.Equal(new[] { "Hard", "ExpertPlus" }, chapters[0].Difficulties);
        Assert.Equal(new[] { "Hard", "ExpertPlus" }, chapters[1].Difficulties);
        Assert.Equal(new[] { "Expert" }, chapters[2].Difficulties);
        Assert.Equal(32.0, chapters[2].EndBeat);
        Assert.Contains(notes, n => n.Contains("difficulties without bookmarks get no chapters"));
    }

    [Fact]
    public void MultipleCharacteristics_AreQualified()
    {
        var chapters = BookmarkChapterBuilder.Build(new[]
        {
            Diff("Standard", "Expert", (0, "A")),
            Diff("Lawless", "Expert", (4, "A")),
        }, new List<string>());
        Assert.Equal(new[] { "Standard/Expert" }, chapters[0].Difficulties);
        Assert.Equal(new[] { "Lawless/Expert" }, chapters[1].Difficulties);
    }

    [Fact]
    public void DuplicateBeats_SkipTheEmptyChapter()
    {
        var notes = new List<string>();
        var chapters = BookmarkChapterBuilder.Build(new[] { Diff("Standard", "Hard", (8, "x"), (8, "y"), (16, "z")) }, notes);
        Assert.Equal(new[] { "y", "z" }, chapters.Select(c => c.Label));
        Assert.Contains(notes, n => n.Contains("'x'"));
    }

    [Fact]
    public void NoBookmarksAnywhere_GivesNothing()
    {
        Assert.Empty(BookmarkChapterBuilder.Build(new[] { Diff("Standard", "Hard") }, new List<string>()));
    }

    [Fact]
    public void ToJson_RoundTripsThroughTheChaptersParser()
    {
        var json = BookmarkChapterBuilder.ToJson(new[]
        {
            new GeneratedChapter(5, 69, "Intro", null),
            new GeneratedChapter(69.5, null, "Vocal \"2\"", new[] { "Expert", "Standard/ExpertPlus" }),
        });

        Assert.DoesNotContain("\r", json);
        Assert.EndsWith("}\n", json);
        Assert.StartsWith("{\n  \"chapters\": [", json);

        var doc = ChaptersParser.Parse(json);
        Assert.Empty(doc.Warnings);
        Assert.Equal(2, doc.Chapters.Count);
        Assert.Equal(5f, doc.Chapters[0].StartBeat);
        Assert.Equal(69f, doc.Chapters[0].EndBeat);
        Assert.Null(doc.Chapters[1].EndBeat);
        Assert.Equal("Vocal \"2\"", doc.Chapters[1].Label);
        Assert.Equal(new[] { "Expert", "Standard/ExpertPlus" }, doc.Chapters[1].Difficulties);
    }
}

public sealed class MapBookmarkReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "maxtweaks-tests-" + Guid.NewGuid().ToString("N"));

    public MapBookmarkReaderTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void V2Map_ReadsEachDifficultyFile()
    {
        File.WriteAllText(Path.Combine(_dir, "info.dat"), """
            { "_difficultyBeatmapSets": [ { "_beatmapCharacteristicName": "Standard", "_difficultyBeatmaps": [
              { "_difficulty": "Hard", "_beatmapFilename": "Hard.dat" },
              { "_difficulty": "Expert", "_beatmapFilename": "Missing.dat" } ] } ] }
            """);
        File.WriteAllText(Path.Combine(_dir, "Hard.dat"), """{ "_customData": { "_bookmarks": [ { "_time": 4, "_name": "Go" } ] } }""");

        var notes = new List<string>();
        var result = MapBookmarkReader.Read(_dir, notes);

        Assert.Equal(2, result.Count);
        Assert.Equal("Go", Assert.Single(result[0].Bookmarks).Name);
        Assert.Empty(result[1].Bookmarks);
        Assert.Contains(notes, n => n.Contains("Missing.dat"));
    }

    [Fact]
    public void V4Map_UsesBookmarkFolder_MatchedByContent()
    {
        File.WriteAllText(Path.Combine(_dir, "Info.dat"), """
            { "version": "4.0.0", "difficultyBeatmaps": [
              { "characteristic": "Standard", "difficulty": "ExpertPlus", "beatmapDataFilename": "EP.dat" },
              { "characteristic": "Standard", "difficulty": "Hard", "beatmapDataFilename": "H.dat" } ] }
            """);
        File.WriteAllText(Path.Combine(_dir, "EP.dat"), """{ "version": "4.0.0", "colorNotes": [] }""");
        File.WriteAllText(Path.Combine(_dir, "H.dat"), """{ "version": "4.0.0", "customData": { "bookmarks": [ { "b": 1, "n": "inline" } ] } }""");
        Directory.CreateDirectory(Path.Combine(_dir, "Bookmarks"));
        File.WriteAllText(Path.Combine(_dir, "Bookmarks", "anything.bookmarks.dat"), """
            { "characteristic": "Standard", "difficulty": "Expert+", "bookmarks": [ { "beat": 16, "label": "Drop" } ] }
            """);
        File.WriteAllText(Path.Combine(_dir, "Bookmarks", "Hard.bookmarks.dat"), """
            { "characteristic": "Standard", "difficulty": "Hard", "bookmarks": [ { "beat": 99, "label": "ignored" } ] }
            """);
        File.WriteAllText(Path.Combine(_dir, "Bookmarks", "junk.txt"), "junk");

        var notes = new List<string>();
        var result = MapBookmarkReader.Read(_dir, notes);

        Assert.Equal("Drop", Assert.Single(result[0].Bookmarks).Name);
        Assert.Equal("inline", Assert.Single(result[1].Bookmarks).Name);
        Assert.Contains(notes, n => n.Contains("junk.txt"));
    }

    [Fact]
    public void MissingInfo_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => MapBookmarkReader.Read(_dir, new List<string>()));
        Assert.Throws<FileNotFoundException>(() => MapBookmarkReader.Read(Path.Combine(_dir, "nope"), new List<string>()));
    }

    [Fact]
    public void BadInfo_Throws()
    {
        File.WriteAllText(Path.Combine(_dir, "Info.dat"), "{}");
        Assert.Throws<InvalidDataException>(() => MapBookmarkReader.Read(_dir, new List<string>()));
    }
}
