using MaxTweaks.Core.Blink;
using Xunit;

namespace MaxTweaks.Core.Tests.Blink;

public class BlinkEnvelopeTests
{
    [Theory]
    [InlineData(-0.1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(0.15f, 0.5f)]
    [InlineData(0.3f, 1f)]
    [InlineData(1.5f, 1f)]
    [InlineData(2.7f, 1f)]
    [InlineData(2.85f, 0.5f)]
    [InlineData(3f, 0f)]
    [InlineData(4f, 0f)]
    public void Trapezoid(float elapsed, float expected)
    {
        Assert.Equal(expected, BlinkEnvelope.Alpha(elapsed, 3f, 0.3f), 3);
    }

    [Fact]
    public void ShortDuration_FadesShrinkToHalf()
    {
        Assert.Equal(1f, BlinkEnvelope.Alpha(0.5f, 1f, 2f), 3);
        Assert.Equal(0.5f, BlinkEnvelope.Alpha(0.25f, 1f, 2f), 3);
    }

    [Fact]
    public void NoFade_IsFlat()
    {
        Assert.Equal(1f, BlinkEnvelope.Alpha(0f, 1f, 0f));
        Assert.Equal(0f, BlinkEnvelope.Alpha(0.5f, 0f, 0.3f));
    }
}

public class BlinkSettingsTests
{
    [Fact]
    public void ClampsNonsense()
    {
        var s = new BlinkSettings(-1f, -5f, 0f, -10f, -1f);
        Assert.Equal(0f, s.QuietBefore);
        Assert.Equal(0.1f, s.Duration);
        Assert.Equal(0.1f, s.ClearAhead);
        Assert.Equal(0f, s.Cooldown);
        Assert.Equal(0f, s.Fade);
    }

    [Fact]
    public void Defaults()
    {
        var s = new BlinkSettings(3f, 8f, 3f, 300f);
        Assert.Equal(3f, s.QuietBefore);
        Assert.Equal(8f, s.ClearAhead);
        Assert.Equal(3f, s.Duration);
        Assert.Equal(300f, s.Cooldown);
        Assert.Equal(0.3f, s.Fade);
    }

    [Fact]
    public void FadeLimitedToHalfDuration()
    {
        Assert.Equal(0.5f, new BlinkSettings(3f, 8f, 1f, 300f, 2f).Fade);
    }
}

public class BlinkCooldownTests
{
    [Fact]
    public void StartsEmpty_AccumulatesPositiveOnly_Restarts()
    {
        var c = new BlinkCooldown();
        Assert.False(c.IsReady(1f));
        Assert.True(c.IsReady(0f));
        c.AddPlayed(0.6f);
        c.AddPlayed(-5f);
        c.AddPlayed(0f);
        c.AddPlayed(0.6f);
        Assert.True(c.IsReady(1.2f));
        c.Restart();
        Assert.Equal(0f, c.PlayedSinceLastBlink);
    }
}

public class PlayClockTests
{
    [Fact]
    public void FirstCallIsZero_ThenDeltas()
    {
        var c = new PlayClock();
        Assert.Equal(0f, c.Advance(10f, 1f));
        Assert.Equal(0.02f, c.Advance(10.02f, 1f), 4);
        Assert.False(c.Jumped);
    }

    [Fact]
    public void TimeScaleConvertsToRealSeconds()
    {
        var c = new PlayClock();
        c.Advance(0f, 1.5f);
        Assert.Equal(0.02f, c.Advance(0.03f, 1.5f), 4);
        Assert.Equal(0.03f, c.Advance(0.06f, 0f), 4);
    }

    [Fact]
    public void PausedOrJitter_IsZeroWithoutJump()
    {
        var c = new PlayClock();
        c.Advance(5f, 1f);
        Assert.Equal(0f, c.Advance(5f, 1f));
        Assert.Equal(0f, c.Advance(4.98f, 1f));
        Assert.False(c.Jumped);
    }

    [Fact]
    public void Seeks_AreJumps()
    {
        var c = new PlayClock();
        c.Advance(5f, 1f);
        Assert.Equal(0f, c.Advance(2f, 1f));
        Assert.True(c.Jumped);
        Assert.Equal(0f, c.Advance(2f + PlayClock.MaxStep + 0.1f, 1f));
        Assert.True(c.Jumped);
        Assert.Equal(0.1f, c.Advance(2.7f, 1f), 4);
        Assert.False(c.Jumped);
    }

    [Fact]
    public void Reset_StartsOver()
    {
        var c = new PlayClock();
        c.Advance(5f, 1f);
        c.Advance(1f, 1f);
        c.Reset();
        Assert.False(c.Jumped);
        Assert.Equal(0f, c.Advance(100f, 1f));
        Assert.False(c.Jumped);
    }
}

public class BlinkArtTests
{
    [Fact]
    public void Vignette_ClearCentre_FullCorners_Monotonic()
    {
        Assert.Equal(0f, BlinkArt.VignetteMask(0f, 0f));
        Assert.Equal(0f, BlinkArt.VignetteMask(0.05f, 0.05f));
        Assert.Equal(1f, BlinkArt.VignetteMask(1f, 1f));
        Assert.Equal(1f, BlinkArt.VignetteMask(0.7f, 0f));

        var previous = 0f;
        for (var r = 0f; r <= 1f; r += 0.01f)
        {
            var v = BlinkArt.VignetteMask(r, 0f);
            Assert.True(v >= previous - 1e-6f);
            previous = v;
        }
    }

    [Fact]
    public void RadiusForAngle_Endpoints()
    {
        Assert.Equal(0f, BlinkArt.RadiusForAngle(0f), 4);
        Assert.Equal(1f, BlinkArt.RadiusForAngle(BlinkArt.QuadHalfAngleDegrees), 4);
    }

    [Fact]
    public void SmoothStep_Edges()
    {
        Assert.Equal(0f, BlinkArt.SmoothStep(0f, 1f, -1f));
        Assert.Equal(0.5f, BlinkArt.SmoothStep(0f, 1f, 0.5f), 4);
        Assert.Equal(1f, BlinkArt.SmoothStep(0f, 1f, 2f));
        Assert.Equal(0f, BlinkArt.SmoothStep(1f, 1f, 0.5f));
        Assert.Equal(1f, BlinkArt.SmoothStep(1f, 1f, 1.5f));
    }

    [Fact]
    public void ClosedEye_DrawsLidAndLashes_NotCorners()
    {
        var px = 2f / 128f;

        // Lid bottom at (0, -0.2) and its ends near (+-0.8, 0.3).
        Assert.Equal(1f, BlinkArt.ClosedEye(0f, -0.2f, px));
        Assert.Equal(1f, BlinkArt.ClosedEye(0.79f, 0.29f, px));

        // Middle lash goes straight down from the lid.
        Assert.Equal(1f, BlinkArt.ClosedEye(0f, -0.4f, px));

        // Empty above the lid (eye is closed) and in the corners.
        Assert.Equal(0f, BlinkArt.ClosedEye(0f, 0.3f, px));
        Assert.Equal(0f, BlinkArt.ClosedEye(-0.95f, -0.95f, px));
        Assert.Equal(0f, BlinkArt.ClosedEye(0.95f, 0.95f, px));

        // Anti-aliased edge is between 0 and 1.
        var edge = BlinkArt.ClosedEye(0f, -0.2f + 0.075f, px);
        Assert.InRange(edge, 0.1f, 0.9f);
    }

    [Fact]
    public void SegmentDistance_HandlesPointSegment()
    {
        Assert.Equal(5f, BlinkArt.SegmentDistance(3f, 4f, 0f, 0f, 0f, 0f), 4);
        Assert.Equal(1f, BlinkArt.SegmentDistance(0.5f, 1f, 0f, 0f, 1f, 0f), 4);
        Assert.Equal(1f, BlinkArt.SegmentDistance(2f, 0f, 0f, 0f, 1f, 0f), 4);
    }
}
