using System;
using System.Collections.Generic;
using MaxTweaks.Core.ObsRecording;
using Xunit;

namespace MaxTweaks.Core.Tests.ObsRecording;

public class RecordingFileNameTests
{
    private static readonly Func<string, bool> NothingExists = _ => false;

    [Theory]
    [InlineData("Easy", "Standard", "Easy")]
    [InlineData("Normal", "Standard", "Normal")]
    [InlineData("Hard", "Standard", "Hard")]
    [InlineData("Expert", "Standard", "Expert")]
    [InlineData("ExpertPlus", "Standard", "Expert+")]
    [InlineData("expertplus", "standard", "Expert+")]
    [InlineData("ExpertPlus", "OneSaber", "OneSaber Expert+")]
    [InlineData("Hard", "360Degree", "360Degree Hard")]
    [InlineData("Expert", null, "Expert")]
    [InlineData("Expert", "", "Expert")]
    [InlineData(" Expert ", " Lawless ", "Lawless Expert")]
    [InlineData(null, "Lawless", "Lawless")]
    [InlineData(null, null, "")]
    public void DifficultyLabel_ReadsLikeTheGame(string? difficulty, string? characteristic, string expected)
    {
        Assert.Equal(expected, RecordingFileName.DifficultyLabel(difficulty, characteristic));
    }

    [Theory]
    [InlineData("Plain Song", "Plain Song")]
    [InlineData("AC/DC", "AC DC")]
    [InlineData("Re:Zero", "Re Zero")]
    [InlineData("What? <Why> \"How\" a|b a*b a\\b", "What Why How a b a b a b")]
    [InlineData("  padded   out  ", "padded out")]
    [InlineData("ends with dots...", "ends with dots")]
    [InlineData("trailing space and dot . ", "trailing space and dot")]
    [InlineData("tab\tand\nnewline\u0001x\u007f", "tab and newline x")]
    [InlineData(".hidden", ".hidden")]
    [InlineData("???", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Sanitize_MakesWindowsSafeNames(string? input, string expected)
    {
        Assert.Equal(expected, RecordingFileName.Sanitize(input, 0));
    }

    [Fact]
    public void Sanitize_KeepsUnicode()
    {
        const string name = "\u5922\u306e\u7d9a\u304d \u00e9t\u00e9";
        Assert.Equal(name, RecordingFileName.Sanitize(name, 0));
    }

    [Fact]
    public void Sanitize_CutsToMaxLength_AndTrimsAgain()
    {
        Assert.Equal("abcde", RecordingFileName.Sanitize("abcdefgh", 5));
        Assert.Equal("abc", RecordingFileName.Sanitize("abc. efgh", 5));
        Assert.Equal("abcdefgh", RecordingFileName.Sanitize("abcdefgh", 8));
    }

    [Fact]
    public void Sanitize_DoesNotSplitASurrogatePair()
    {
        // "ab" + U+1F3B5 (two UTF-16 code units) + "cd": a cut after 3 units would split the pair.
        const string text = "ab\ud83c\udfb5cd";
        Assert.Equal("ab", RecordingFileName.Sanitize(text, 3));
        Assert.Equal("ab\ud83c\udfb5", RecordingFileName.Sanitize(text, 4));
    }

    [Fact]
    public void Build_AppendsSongAndDifficulty_KeepingFolderTimestampAndExtension()
    {
        var result = RecordingFileName.Build("C:/Users/me/Videos/beat saber/2026-11-14 15-05-07.mp4", "Some Song", "Expert+", NothingExists);
        Assert.Equal("C:/Users/me/Videos/beat saber/2026-11-14 15-05-07 - Some Song - Expert+.mp4", result);
    }

    [Fact]
    public void Build_WorksWithBackslashes_AndDotsInFolders()
    {
        var result = RecordingFileName.Build(@"D:\rec.v2\2026-11-14 15-05-07.mkv", "A: B", "OneSaber Hard", NothingExists);
        Assert.Equal(@"D:\rec.v2\2026-11-14 15-05-07 - A B - OneSaber Hard.mkv", result);
    }

    [Fact]
    public void Build_NoFolder_NoExtension()
    {
        Assert.Equal("clip - Song - Easy", RecordingFileName.Build("clip", "Song", "Easy", NothingExists));
        Assert.Equal("dir.x/clip - Song - Easy", RecordingFileName.Build("dir.x/clip", "Song", "Easy", NothingExists));
        Assert.Equal(".hidden - Song - Easy", RecordingFileName.Build(".hidden", "Song", "Easy", NothingExists));
    }

    [Fact]
    public void Build_SkipsEmptyParts()
    {
        Assert.Equal("C:/v/a - Expert.mp4", RecordingFileName.Build("C:/v/a.mp4", "???", "Expert", NothingExists));
        Assert.Equal("C:/v/a - Song.mp4", RecordingFileName.Build("C:/v/a.mp4", "Song", null, NothingExists));
        Assert.Equal("C:/v/a.mp4", RecordingFileName.Build("C:/v/a.mp4", null, " ", NothingExists));
    }

    [Fact]
    public void Build_CapsALongSongName()
    {
        var result = RecordingFileName.Build("C:/v/a.mp4", new string('x', 300), "Expert", NothingExists);
        Assert.Equal("C:/v/a - " + new string('x', RecordingFileName.MaxSongNameLength) + " - Expert.mp4", result);
    }

    [Fact]
    public void Build_NeverReturnsATakenName()
    {
        var taken = new HashSet<string>
        {
            "C:/v/a - Song - Expert.mp4",
            "C:/v/a - Song - Expert (2).mp4",
        };
        Assert.Equal("C:/v/a - Song - Expert (3).mp4", RecordingFileName.Build("C:/v/a.mp4", "Song", "Expert", taken.Contains));
    }

    [Theory]
    [InlineData("C:/v/a b.mp4", "a b.mp4")]
    [InlineData(@"C:\v\a.mkv", "a.mkv")]
    [InlineData("plain", "plain")]
    public void NameOf_DropsTheFolder(string path, string expected)
    {
        Assert.Equal(expected, RecordingFileName.NameOf(path));
    }

    [Fact]
    public void Label_ToString_AndNulls()
    {
        Assert.Equal("'Song' (Expert+)", new RecordingLabel("Song", "Expert+").ToString());
        var empty = new RecordingLabel(null, null);
        Assert.Equal(string.Empty, empty.SongName);
        Assert.Equal(string.Empty, empty.Difficulty);
    }
}
