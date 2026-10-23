using System;
using MaxTweaks.Core.Chapters;
using Xunit;

namespace MaxTweaks.Core.Tests.Chapters;

public class ChaptersPlacementTests
{
    private static double ElevationDegrees(double y, double z, double eye) => Math.Atan2(y - eye, z) * 180.0 / Math.PI;

    [Fact]
    public void NoClock_UsesMinimum()
    {
        Assert.Equal(ChaptersPlacement.MinBottomY, ChaptersPlacement.BottomY(null, 1.6f));
    }

    [Theory]
    [InlineData(1.2f)]
    [InlineData(1.5f)]
    [InlineData(1.6f)]
    [InlineData(1.8f)]
    public void DefaultClock_StackBottomIsAboveClockTop_FromTheEye(float eye)
    {
        var clock = ClockPlacement.EnhancementsDefault;
        var bottom = ChaptersPlacement.BottomY(clock, eye);

        // Clock text top: centre (3.0, 3.9) + half a line (10 * 1.45 * 0.02 / 2 = 0.145) along the
        // screen's up, tilted 35 degrees towards the player.
        var topY = 3.0 + (0.145 * Math.Cos(35 * Math.PI / 180));
        var topZ = 3.9 - (0.145 * Math.Sin(35 * Math.PI / 180));
        var gap = ElevationDegrees(bottom, ChaptersPlacement.StackZ, eye) - ElevationDegrees(topY, topZ, eye);
        Assert.InRange(gap, ChaptersPlacement.MarginDegrees - 0.01, ChaptersPlacement.MarginDegrees + 0.01);
        Assert.True(bottom >= ChaptersPlacement.MinBottomY);
    }

    [Fact]
    public void DefaultClock_AtTypicalEyeHeight_IsAboutFourAndAHalfMetres()
    {
        // The QA overlap was at 4.0 m; with a 1.6 m eye the stack must start near 4.6 m.
        Assert.InRange(ChaptersPlacement.BottomY(ClockPlacement.EnhancementsDefault, 1.6f), 4.5f, 4.7f);
    }

    [Fact]
    public void LowClock_FallsBackToMinimum()
    {
        Assert.Equal(ChaptersPlacement.MinBottomY, ChaptersPlacement.BottomY(new ClockPlacement(1.0f, 3.9f, 0f, 10f), 1.6f));
    }

    [Fact]
    public void ClockBehindOrSteep_FallsBackToMinimum()
    {
        Assert.Equal(ChaptersPlacement.MinBottomY, ChaptersPlacement.BottomY(new ClockPlacement(3f, -2f, 0f, 10f), 1.6f));
        Assert.Equal(ChaptersPlacement.MinBottomY, ChaptersPlacement.BottomY(new ClockPlacement(50f, 0.5f, 0f, 10f), 1.6f));
    }

    [Fact]
    public void ParseEnhancements_ReadsPositionRotationAndSize()
    {
        var clock = ChaptersPlacement.ParseEnhancementsClock("""
            { "Version": "3.0.18", "Clock": { "Enabled": true, "ShowInGame": true, "Size": 12.5,
              "Position": { "x": 0.0, "y": 3.5, "z": 4.2 }, "Rotation": { "x": 330.0, "y": 0.0, "z": 0.0 } } }
            """);
        Assert.NotNull(clock);
        Assert.Equal(3.5f, clock!.Y);
        Assert.Equal(4.2f, clock.Z);
        Assert.Equal(330f, clock.PitchDegrees);
        Assert.Equal(12.5f, clock.FontSize);
    }

    [Theory]
    [InlineData("{ \"Clock\": { \"Enabled\": false } }")]
    [InlineData("{ \"Clock\": { \"Enabled\": true, \"ShowInGame\": false } }")]
    public void ParseEnhancements_HiddenClock_IsNull(string json)
    {
        Assert.Null(ChaptersPlacement.ParseEnhancementsClock(json));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("broken")]
    [InlineData("{}")]
    [InlineData("{ \"Clock\": { \"Position\": { \"y\": \"high\" }, \"Size\": 1e9 } }")]
    public void ParseEnhancements_MissingOrBadValues_UseDefaults(string? json)
    {
        var clock = ChaptersPlacement.ParseEnhancementsClock(json);
        Assert.NotNull(clock);
        Assert.Equal(3.0f, clock!.Y);
        Assert.Equal(3.9f, clock.Z);
        Assert.Equal(325f, clock.PitchDegrees);
        Assert.Equal(10f, clock.FontSize);
    }
}
