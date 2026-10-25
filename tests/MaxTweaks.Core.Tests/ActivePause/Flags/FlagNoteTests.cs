using System.Collections.Generic;
using MaxTweaks.Core.ActivePause.Flags;
using Xunit;

namespace MaxTweaks.Core.Tests.ActivePause.Flags;

public class FlagNoteTests
{
    [Fact]
    public void FlagNote_Equality_IgnoresSubMillisecond()
    {
        var a = new FlagNote(FlagNoteKind.Note, 96f, 42.3171f, 1, 0, "red", "down");
        var b = new FlagNote(FlagNoteKind.Note, 96.0004f, 42.3174f, 1, 0, "red", "down");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Contains(b, new HashSet<FlagNote> { a });
    }

    [Fact]
    public void FlagNote_Equality_DiffersOnMapData()
    {
        var a = new FlagNote(FlagNoteKind.Note, 96f, 42.317f, 1, 0, "red", "down");
        Assert.NotEqual(a, new FlagNote(FlagNoteKind.Bomb, 96f, 42.317f, 1, 0, "red", "down"));
        Assert.NotEqual(a, new FlagNote(FlagNoteKind.Note, 96f, 42.319f, 1, 0, "red", "down"));
        Assert.NotEqual(a, new FlagNote(FlagNoteKind.Note, 96f, 42.317f, 2, 0, "red", "down"));
        Assert.NotEqual(a, new FlagNote(FlagNoteKind.Note, 96f, 42.317f, 1, 1, "red", "down"));
        Assert.NotEqual(a, new FlagNote(FlagNoteKind.Note, 96f, 42.317f, 1, 0, "blue", "down"));
        Assert.NotEqual(a, new FlagNote(FlagNoteKind.Note, 96f, 42.317f, 1, 0, "red", "up"));
    }

    [Theory]
    [InlineData(FlagNoteKind.Note, "note")]
    [InlineData(FlagNoteKind.Bomb, "bomb")]
    [InlineData(FlagNoteKind.ChainHead, "chain_head")]
    [InlineData(FlagNoteKind.ChainLink, "chain_link")]
    public void KindId_RoundTrips(FlagNoteKind kind, string id)
    {
        Assert.Equal(id, FlagNote.KindId(kind));
        Assert.True(FlagNote.TryParseKind(id, out var parsed));
        Assert.Equal(kind, parsed);
    }

    [Theory]
    [InlineData("Up", "up")]
    [InlineData("DownRight", "down_right")]
    [InlineData("UpLeft", "up_left")]
    [InlineData("Any", "any")]
    [InlineData("None", "none")]
    [InlineData("", "none")]
    public void SnakeCase_MatchesSpecVocabulary(string name, string expected)
    {
        Assert.Equal(expected, FlagNote.SnakeCase(name));
    }
}
