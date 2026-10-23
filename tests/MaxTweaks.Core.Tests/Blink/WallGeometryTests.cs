using MaxTweaks.Core.Blink;
using Xunit;

namespace MaxTweaks.Core.Tests.Blink;

public class WallGeometryTests
{
    private const int Lanes = 4;

    // A 1.8 m player standing in the middle; eyes around 1.7 m.
    private static readonly HeadZone Standing = HeadZone.Around(0f, 1.7f, 0f, 1.7f, 0.15f);

    [Fact]
    public void VanillaBox_MatchesGameFormula()
    {
        // lineIndex 1, Base layer, width 2, height 5: x = (-1.5 + 1) * 0.6 + (1.2 - 0.6) / 2 = 0.
        Assert.True(WallGeometry.TryGetVanillaBox(1, 0, 2, 5, Lanes, 0f, out var box));
        Assert.Equal(0f, box.CenterX, 4);
        Assert.Equal(1.2f, box.Width, 4);
        Assert.Equal(0.1f, box.Bottom, 4); // 0.25 - 0.15 = 0.1, clamped at 0.1
        Assert.Equal(3.0f, box.Height, 4); // min(3.0, 3.1 - 0.1)
        Assert.Equal(-0.6f, box.Left, 4);
        Assert.Equal(0.6f, box.Right, 4);
        Assert.Equal(3.1f, box.Top, 4);
    }

    [Fact]
    public void VanillaBox_JumpOffsetRaisesAndClipsAtCeiling()
    {
        Assert.True(WallGeometry.TryGetVanillaBox(0, 2, 1, 5, Lanes, 0.2f, out var box));
        Assert.Equal(1.5f, box.Bottom, 4); // 1.45 - 0.15 + 0.2
        Assert.Equal(1.6f, box.Height, 4); // min(3.0, 3.1 - 1.5)
        Assert.Equal(-0.9f, box.CenterX, 4);
    }

    [Fact]
    public void VanillaBox_NegativeJumpOffsetClampsBottom()
    {
        Assert.True(WallGeometry.TryGetVanillaBox(0, 0, 1, 1, Lanes, -1f, out var box));
        Assert.Equal(0.1f, box.Bottom, 4);
    }

    [Fact]
    public void FullHeightCentreWall_HitsHead()
    {
        Assert.True(WallGeometry.HitsHead(1, 0, 2, 5, Lanes, 0f, Standing));
    }

    [Fact]
    public void CrouchWall_HitsStandingHead()
    {
        // Top-layer full-width "duck" wall.
        Assert.True(WallGeometry.HitsHead(0, 2, 4, 3, Lanes, 0f, Standing));
    }

    [Fact]
    public void OuterLaneWall_PassesBesideTheHead()
    {
        // Lane 0 spans x -1.2..-0.6, lane 3 spans 0.6..1.2: clear of a centred head (+-0.15).
        Assert.False(WallGeometry.HitsHead(0, 0, 1, 5, Lanes, 0f, Standing));
        Assert.False(WallGeometry.HitsHead(3, 0, 1, 5, Lanes, 0f, Standing));
    }

    [Fact]
    public void InnerLaneWall_HitsCentredHeadAndLeaningHead()
    {
        // Lane 1 spans x -0.6..0. A head at x = 0 touches its edge. Leaning right to x = 0.4 does not
        // count as clear: the player re-centres while the wall arrives, so neutral x = 0 stays in.
        Assert.True(WallGeometry.HitsHead(1, 0, 1, 5, Lanes, 0f, Standing));
        var leaning = HeadZone.Around(0.4f, 1.7f, 0f, 1.7f, 0.15f);
        Assert.True(WallGeometry.HitsHead(1, 0, 1, 5, Lanes, 0f, leaning));
        Assert.Equal(-0.15f, leaning.XMin, 4);
        Assert.Equal(0.55f, leaning.XMax, 4);
    }

    [Fact]
    public void DriftedHead_CentreWallHits_FarSideWallDoesNot()
    {
        // Player drifted 0.5 m left of centre during a quiet gap.
        var drifted = HeadZone.Around(-0.5f, 1.7f, 0f, 1.7f, 0.15f);

        // Centre wall (lanes 1-2, x -0.6..0.6) hits both the drifted and the neutral position.
        Assert.True(WallGeometry.HitsHead(1, 0, 2, 5, Lanes, 0f, drifted));

        // Lane 3 (x 0.6..1.2) is beside both positions.
        Assert.False(WallGeometry.HitsHead(3, 0, 1, 5, Lanes, 0f, drifted));

        // Lane 0 (x -1.2..-0.6) is only near the drifted head: it still counts (zone reaches -0.65).
        Assert.True(WallGeometry.HitsHead(0, 0, 1, 5, Lanes, 0f, drifted));
    }

    [Fact]
    public void LowWall_UnderTheHead_IsFine()
    {
        // Base layer, height 1: 0.1 .. 0.7 m. Way below a standing head.
        Assert.False(WallGeometry.HitsHead(1, 0, 2, 1, Lanes, 0f, Standing));
    }

    [Fact]
    public void CrouchingNow_StillProtectedAtStandingHeight()
    {
        // Head currently at 1.0 m (crouched) but the player stands at 1.7: a wall spanning
        // 1.3..3.1 must still count.
        var crouched = HeadZone.Around(0f, 1.0f, 0f, 1.7f, 0.15f);
        Assert.True(WallGeometry.HitsHead(0, 2, 4, 3, Lanes, 0f, crouched));
        Assert.Equal(0.85f, crouched.YMin, 4);
        Assert.Equal(1.85f, crouched.YMax, 4);
    }

    [Theory]
    [InlineData(1000, 0, 1, 1)] // ME precision lineIndex
    [InlineData(-2000, 0, 1, 1)]
    [InlineData(4, 0, 1, 1)] // outside 4 lanes
    [InlineData(-1, 0, 1, 1)]
    [InlineData(0, 3, 1, 1)] // layer beyond Top
    [InlineData(0, -1, 1, 1)]
    [InlineData(0, 0, 0, 1)] // zero / negative width
    [InlineData(0, 0, -3, 1)]
    [InlineData(0, 0, 1000, 1)] // ME precision width
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 0, 1, 1005)] // ME precision height
    public void OutOfRangeValues_AreTreatedAsHeadHitting(int lineIndex, int layer, int width, int height)
    {
        Assert.False(WallGeometry.TryGetVanillaBox(lineIndex, layer, width, height, Lanes, 0f, out _));

        // Even a zone far away from anything is considered hit.
        var farAway = HeadZone.Around(50f, 50f, 50f, 50f, 0.1f);
        Assert.True(WallGeometry.HitsHead(lineIndex, layer, width, height, Lanes, 0f, farAway));
    }

    [Fact]
    public void InvalidLineCountOrOffset_IsOutOfRange()
    {
        Assert.False(WallGeometry.TryGetVanillaBox(0, 0, 1, 1, 0, 0f, out _));
        Assert.False(WallGeometry.TryGetVanillaBox(0, 0, 1, 1, Lanes, float.NaN, out _));
        Assert.False(WallGeometry.TryGetVanillaBox(0, 0, 1, 1, Lanes, float.PositiveInfinity, out _));
    }

    [Fact]
    public void Overlaps_DegenerateBoxNeverHits()
    {
        Assert.False(WallGeometry.Overlaps(new WallBox(0f, 0f, 0f, 3f), Standing));
        Assert.False(WallGeometry.Overlaps(new WallBox(0f, 1f, 3.1f, 0f), Standing));
    }

    [Fact]
    public void HeadZone_NormalisesSwappedBounds()
    {
        var z = new HeadZone(1f, -1f, 2f, 0f);
        Assert.Equal(-1f, z.XMin);
        Assert.Equal(1f, z.XMax);
        Assert.Equal(0f, z.YMin);
        Assert.Equal(2f, z.YMax);
    }

    [Theory]
    [InlineData(0, 0.25f)]
    [InlineData(1, 0.85f)]
    [InlineData(2, 1.45f)]
    [InlineData(7, 1.45f)]
    public void LineY_MatchesGame(int layer, float expected)
    {
        Assert.Equal(expected, WallGeometry.LineY(layer), 4);
    }
}
