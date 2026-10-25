using MaxTweaks.Core.ActivePause.Flags;
using Xunit;

namespace MaxTweaks.Core.Tests.ActivePause.Flags;

public class RayMathTests
{
    [Fact]
    public void Hit_AtKnownDistance()
    {
        Assert.True(RayMath.RaySphere(0, 0, 0, 0, 0, 1, 0, 0, 5, 0.5f, out var distance));
        Assert.Equal(4.5f, distance, 4);
    }

    [Fact]
    public void Hit_WithUnnormalizedDirection()
    {
        Assert.True(RayMath.RaySphere(0, 0, 0, 0, 0, 10, 0, 0, 5, 0.5f, out var distance));
        Assert.Equal(4.5f, distance, 4);
    }

    [Fact]
    public void Hit_Grazing()
    {
        Assert.True(RayMath.RaySphere(0, 0.49f, 0, 0, 0, 1, 0, 0, 5, 0.5f, out _));
    }

    [Fact]
    public void Miss_Beside()
    {
        Assert.False(RayMath.RaySphere(0, 0.6f, 0, 0, 0, 1, 0, 0, 5, 0.5f, out _));
    }

    [Fact]
    public void Miss_Behind()
    {
        Assert.False(RayMath.RaySphere(0, 0, 0, 0, 0, -1, 0, 0, 5, 0.5f, out _));
    }

    [Fact]
    public void OriginInside_HitsAtZero()
    {
        Assert.True(RayMath.RaySphere(0, 0, 5.1f, 1, 0, 0, 0, 0, 5, 0.5f, out var distance));
        Assert.Equal(0f, distance);
    }

    [Fact]
    public void ZeroDirection_Misses()
    {
        Assert.False(RayMath.RaySphere(0, 0, 0, 0, 0, 0, 0, 0, 5, 0.5f, out _));
    }
}
