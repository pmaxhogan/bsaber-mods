using System.Collections.Generic;
using MaxTweaks.Core.Blink;
using Xunit;

namespace MaxTweaks.Core.Tests.Blink;

internal sealed class FakeWalls : IWallHeadTest
{
    private readonly HashSet<int> _hitting;

    public FakeWalls(params int[] hitting)
    {
        _hitting = new HashSet<int>(hitting);
    }

    public List<int> Tested { get; } = new();

    public bool HitsHead(int wallId)
    {
        Tested.Add(wallId);
        return _hitting.Contains(wallId);
    }
}

public class BlinkTimelineTests
{
    [Fact]
    public void EmptyTimeline_HasNothing()
    {
        var t = new BlinkTimeline.Builder().Build();
        Assert.Equal(0, t.NoteCount);
        Assert.Equal(0, t.WallCount);
        Assert.False(t.AnyNoteIn(-100f, 100f));
        Assert.False(t.AnyHeadWallIn(-100f, 100f, new FakeWalls(0)));
    }

    [Fact]
    public void AnyNoteIn_IsInclusiveOnBothEnds()
    {
        var t = new BlinkTimeline.Builder().AddNote(5f).Build();
        Assert.True(t.AnyNoteIn(5f, 6f));
        Assert.True(t.AnyNoteIn(4f, 5f));
        Assert.False(t.AnyNoteIn(5.01f, 6f));
        Assert.False(t.AnyNoteIn(3f, 4.99f));
    }

    [Fact]
    public void AnyNoteIn_UnsortedInputIsSorted()
    {
        var t = new BlinkTimeline.Builder().AddNote(30f).AddNote(1f).AddNote(12f).Build();
        Assert.True(t.AnyNoteIn(11f, 13f));
        Assert.False(t.AnyNoteIn(13f, 29f));
        Assert.True(t.AnyNoteIn(0f, 1f));
    }

    [Fact]
    public void AnyNoteIn_InvertedWindowIsEmpty()
    {
        var t = new BlinkTimeline.Builder().AddNote(5f).Build();
        Assert.False(t.AnyNoteIn(6f, 4f));
    }

    [Fact]
    public void ChainSpan_CoversItsWholeLength()
    {
        // A long chain from 2s to 9s plus a later note: a window inside the chain is busy even
        // though no item starts there.
        var t = new BlinkTimeline.Builder().AddNoteSpan(2f, 9f).AddNote(20f).Build();
        Assert.True(t.AnyNoteIn(5f, 6f));
        Assert.True(t.AnyNoteIn(9f, 15f));
        Assert.False(t.AnyNoteIn(9.5f, 19f));
    }

    [Fact]
    public void NonFiniteItemsAreIgnored()
    {
        var t = new BlinkTimeline.Builder()
            .AddNote(float.NaN)
            .AddNoteSpan(1f, float.PositiveInfinity)
            .AddWall(float.NaN, 3f, 1)
            .Build();
        Assert.Equal(0, t.NoteCount);
        Assert.Equal(0, t.WallCount);
    }

    [Fact]
    public void Walls_OnlyHeadHittingOnesCount()
    {
        var t = new BlinkTimeline.Builder().AddWall(10f, 12f, 1).AddWall(10f, 12f, 2).Build();
        Assert.False(t.AnyHeadWallIn(9f, 11f, new FakeWalls()));
        Assert.True(t.AnyHeadWallIn(9f, 11f, new FakeWalls(2)));
    }

    [Fact]
    public void Walls_AlreadyPassingCount()
    {
        // Wall started before the window and is still passing.
        var t = new BlinkTimeline.Builder().AddWall(0f, 20f, 7).Build();
        Assert.True(t.AnyHeadWallIn(10f, 18f, new FakeWalls(7)));
        Assert.False(t.AnyHeadWallIn(20.5f, 28f, new FakeWalls(7)));
    }

    [Fact]
    public void Walls_LongEarlyWallFoundBehindShortLaterOnes()
    {
        // Overlapping walls: a long head wall from 0 to 50 and short side walls after it. The
        // prefix max end must keep the scan going past the short ones.
        var b = new BlinkTimeline.Builder().AddWall(0f, 50f, 100);
        for (var i = 0; i < 10; i++)
        {
            b.AddWall(1f + i, 1.5f + i, i);
        }

        var t = b.Build();
        var walls = new FakeWalls(100);
        Assert.True(t.AnyHeadWallIn(30f, 38f, walls));
        Assert.Contains(100, walls.Tested);
    }

    [Fact]
    public void Walls_ScanStopsEarlyWhenNothingCanReach()
    {
        var b = new BlinkTimeline.Builder();
        for (var i = 0; i < 100; i++)
        {
            b.AddWall(i, i + 0.5f, i);
        }

        var t = b.Build();
        var walls = new FakeWalls();
        Assert.False(t.AnyHeadWallIn(90.6f, 92f, walls));

        // Only walls 91 and 92 can touch [90.6, 92]; 90 ends at 90.5 and stops the scan.
        Assert.Equal(new[] { 92, 91 }, walls.Tested);
    }

    [Fact]
    public void Walls_WindowAfterAllWalls()
    {
        var t = new BlinkTimeline.Builder().AddWall(1f, 2f, 1).Build();
        Assert.False(t.AnyHeadWallIn(3f, 5f, new FakeWalls(1)));
        Assert.False(t.AnyHeadWallIn(5f, 3f, new FakeWalls(1)));
    }

    [Fact]
    public void Walls_InvertedSpanIsClampedToZeroLength()
    {
        var t = new BlinkTimeline.Builder().AddWall(5f, 4f, 1).Build();
        Assert.True(t.AnyHeadWallIn(5f, 6f, new FakeWalls(1)));
        Assert.False(t.AnyHeadWallIn(4f, 4.9f, new FakeWalls(1)));
    }
}
