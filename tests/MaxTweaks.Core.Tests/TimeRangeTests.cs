using MaxTweaks.Core;
using Xunit;

namespace MaxTweaks.Core.Tests;

public class TimeRangeTests
{
    [Fact]
    public void Contains_IsHalfOpen()
    {
        var range = new TimeRange(1f, 2f);
        Assert.True(range.Contains(1f));
        Assert.True(range.Contains(1.5f));
        Assert.False(range.Contains(2f));
    }

    [Fact]
    public void Overlaps_DetectsSharedTime()
    {
        var a = new TimeRange(0f, 10f);
        Assert.True(a.Overlaps(new TimeRange(9f, 12f)));
        Assert.False(a.Overlaps(new TimeRange(10f, 12f)));
    }

    [Fact]
    public void Constructor_RejectsInvertedRange()
    {
        Assert.Throws<System.ArgumentException>(() => new TimeRange(2f, 1f));
    }

    [Fact]
    public void Duration_And_Equality()
    {
        var a = new TimeRange(1f, 4f);
        Assert.Equal(3f, a.Duration);
        Assert.Equal(new TimeRange(1f, 4f), a);
        Assert.True(a.Equals((object)new TimeRange(1f, 4f)));
        Assert.Equal(new TimeRange(1f, 4f).GetHashCode(), a.GetHashCode());
        Assert.Equal("[1, 4)", a.ToString());
    }
}
