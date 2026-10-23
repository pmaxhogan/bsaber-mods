using System;
using System.IO;
using ChaptersFromBookmarks;
using MaxTweaks.Core.Chapters;
using Xunit;

namespace MaxTweaks.Core.Tests.Chapters;

public sealed class ChaptersCliTests : IDisposable
{
    private readonly string _map = Path.Combine(Path.GetTempPath(), "maxtweaks-cli-" + Guid.NewGuid().ToString("N"));
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public ChaptersCliTests()
    {
        Directory.CreateDirectory(_map);
    }

    public void Dispose()
    {
        Directory.Delete(_map, recursive: true);
    }

    private string Output => Path.Combine(_map, "chapters.json");

    private void WriteMap(bool withBookmarks = true)
    {
        File.WriteAllText(Path.Combine(_map, "Info.dat"), """
            { "_difficultyBeatmapSets": [ { "_beatmapCharacteristicName": "Standard", "_difficultyBeatmaps": [
              { "_difficulty": "Hard", "_beatmapFilename": "Hard.dat" } ] } ] }
            """);
        File.WriteAllText(Path.Combine(_map, "Hard.dat"), withBookmarks
            ? """{ "_customData": { "_bookmarks": [ { "_time": 4, "_name": "Intro" }, { "_time": 36, "_name": "Drop" } ] } }"""
            : "{}");
    }

    private int Run(params string[] args) => ChaptersCli.Run(args, _out, _err);

    [Fact]
    public void Writes_ChaptersJson_IntoTheMapFolder()
    {
        WriteMap();
        Assert.Equal(ChaptersCli.ExitOk, Run(_map));
        var doc = ChaptersParser.Parse(File.ReadAllText(Output));
        Assert.Empty(doc.Warnings);
        Assert.Equal(2, doc.Chapters.Count);
        Assert.Contains("wrote 2 chapter(s)", _out.ToString());

        var bytes = File.ReadAllBytes(Output);
        Assert.NotEqual(0xEF, bytes[0]);
    }

    [Fact]
    public void ExistingFile_IsKept_WithoutForce_AndReplaced_WithForce()
    {
        WriteMap();
        File.WriteAllText(Output, "mine");

        Assert.Equal(ChaptersCli.ExitOutputExists, Run(_map));
        Assert.Equal("mine", File.ReadAllText(Output));
        Assert.Contains("--force", _err.ToString());

        Assert.Equal(ChaptersCli.ExitOk, Run(_map, "--force"));
        Assert.NotEqual("mine", File.ReadAllText(Output));
    }

    [Fact]
    public void Stdout_WritesNoFile()
    {
        WriteMap();
        Assert.Equal(ChaptersCli.ExitOk, Run(_map, "--stdout"));
        Assert.False(File.Exists(Output));
        Assert.Contains("\"startBeat\": 4.0", _out.ToString());
    }

    [Fact]
    public void Out_WritesElsewhere()
    {
        WriteMap();
        var other = Path.Combine(_map, "other.json");
        Assert.Equal(ChaptersCli.ExitOk, Run(_map, "--out", other));
        Assert.True(File.Exists(other));
        Assert.False(File.Exists(Output));
    }

    [Fact]
    public void WriteFailure_HasItsOwnExitCode()
    {
        WriteMap();
        var target = Path.Combine(_map, "no-such-dir", "x.json");
        Assert.Equal(ChaptersCli.ExitWriteFailed, Run(_map, "--out", target));
        Assert.Contains("cannot write", _err.ToString());
    }

    [Fact]
    public void NoBookmarks_ExitCode()
    {
        WriteMap(withBookmarks: false);
        Assert.Equal(ChaptersCli.ExitNoBookmarks, Run(_map));
        Assert.False(File.Exists(Output));
    }

    [Fact]
    public void MissingMap_ExitCode()
    {
        Assert.Equal(ChaptersCli.ExitMapUnreadable, Run(Path.Combine(_map, "nope")));
    }

    [Theory]
    [InlineData]
    [InlineData("--bogus")]
    [InlineData("a", "b")]
    public void Usage_ExitCode(params string[] args)
    {
        Assert.Equal(ChaptersCli.ExitUsage, Run(args));
    }

    [Fact]
    public void Help_IsOk()
    {
        Assert.Equal(ChaptersCli.ExitOk, Run("--help"));
        Assert.Contains("exit codes", _out.ToString());
    }
}
