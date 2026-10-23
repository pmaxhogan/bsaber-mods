using System;
using System.Linq;
using MaxTweaks.Core.Chapters;
using Xunit;

namespace MaxTweaks.Core.Tests.Chapters;

public class ChapterSlotsTests
{
    private static int[] Rows(ChapterSlots slots) => Enumerable.Range(0, slots.RowCount).Select(slots.ChapterAt).ToArray();

    [Fact]
    public void StartsEmpty()
    {
        Assert.Equal(new[] { -1, -1, -1 }, Rows(new ChapterSlots(3)));
    }

    [Fact]
    public void NotFrozen_FollowsStackOrder_AndReportsChanges()
    {
        var slots = new ChapterSlots(3);
        Assert.True(slots.Update(new[] { 0, 2 }, 2, frozen: false));
        Assert.Equal(new[] { 0, 2, -1 }, Rows(slots));

        Assert.False(slots.Update(new[] { 0, 2 }, 2, frozen: false));

        // A shorter chapter starting on top leaves the others where they are.
        Assert.True(slots.Update(new[] { 0, 2, 5 }, 3, frozen: false));
        Assert.Equal(new[] { 0, 2, 5 }, Rows(slots));

        // The bottom chapter ending closes the gap (the stacking rule forces the move).
        slots.Update(new[] { 2, 5 }, 2, frozen: false);
        Assert.Equal(new[] { 2, 5, -1 }, Rows(slots));
    }

    [Fact]
    public void Frozen_KeepsRows_FillsLowestFreeRow_ThenReflowsWhenReleased()
    {
        var slots = new ChapterSlots(3);
        slots.Update(new[] { 0, 2, 5 }, 3, frozen: false);

        // Bottom chapter ends while aiming: its row empties, others stay put.
        Assert.True(slots.Update(new[] { 2, 5 }, 2, frozen: true));
        Assert.Equal(new[] { -1, 2, 5 }, Rows(slots));

        // A new chapter starts while still aiming: it takes the free row.
        slots.Update(new[] { 1, 2, 5 }, 3, frozen: true);
        Assert.Equal(new[] { 1, 2, 5 }, Rows(slots));

        // Nothing changes while the set is the same.
        Assert.False(slots.Update(new[] { 1, 2, 5 }, 3, frozen: true));

        // Released: normal order again.
        slots.Update(new[] { 2, 5 }, 2, frozen: false);
        Assert.Equal(new[] { 2, 5, -1 }, Rows(slots));
    }

    [Fact]
    public void Frozen_DropsNewChapters_WhenNoRowIsFree()
    {
        var slots = new ChapterSlots(2);
        slots.Update(new[] { 0, 1 }, 2, frozen: false);
        Assert.False(slots.Update(new[] { 0, 1, 2 }, 3, frozen: true));
        Assert.Equal(new[] { 0, 1 }, Rows(slots));
    }

    [Fact]
    public void CountIsClampedToTheArray()
    {
        var slots = new ChapterSlots(3);
        slots.Update(new[] { 4 }, 5, frozen: false);
        Assert.Equal(new[] { 4, -1, -1 }, Rows(slots));
    }

    [Fact]
    public void BadArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChapterSlots(0));
        Assert.Throws<ArgumentNullException>(() => new ChapterSlots(1).Update(null!, 0, false));
    }
}
