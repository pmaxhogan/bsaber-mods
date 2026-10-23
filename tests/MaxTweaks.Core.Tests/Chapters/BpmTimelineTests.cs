using System;
using MaxTweaks.Core.Chapters;
using Xunit;

namespace MaxTweaks.Core.Tests.Chapters;

public class BpmTimelineTests
{
    [Fact]
    public void Constant_ConvertsBothWays()
    {
        var tempo = BpmTimeline.Constant(120f);
        Assert.Equal(1, tempo.SegmentCount);
        Assert.Equal(120f, tempo.StartBpm);
        Assert.Equal(0f, tempo.BeatToSeconds(0f));
        Assert.Equal(32f, tempo.BeatToSeconds(64f), 4);
        Assert.Equal(64f, tempo.SecondsToBeat(32f), 4);
    }

    [Fact]
    public void SecondChanges_AccumulateBeats()
    {
        // 120 BPM for 10 s (= 20 beats), then 60 BPM.
        var tempo = BpmTimeline.FromSecondChanges(120f, new[] { new BpmChange(10f, 60f) });
        Assert.Equal(2, tempo.SegmentCount);
        Assert.Equal(5f, tempo.BeatToSeconds(10f), 4);
        Assert.Equal(10f, tempo.BeatToSeconds(20f), 4);
        Assert.Equal(15f, tempo.BeatToSeconds(25f), 4);
        Assert.Equal(25f, tempo.SecondsToBeat(15f), 4);
    }

    [Fact]
    public void BeatChanges_AccumulateSeconds()
    {
        // 120 BPM until beat 20 (= 10 s), then 240 BPM.
        var tempo = BpmTimeline.FromBeatChanges(120f, new[] { new BpmChange(20f, 240f) });
        Assert.Equal(10f, tempo.BeatToSeconds(20f), 4);
        Assert.Equal(12.5f, tempo.BeatToSeconds(30f), 4);
        Assert.Equal(30f, tempo.SecondsToBeat(12.5f), 4);
    }

    [Fact]
    public void MultipleChanges_UnsortedInput_IsSorted()
    {
        var tempo = BpmTimeline.FromSecondChanges(60f, new[] { new BpmChange(20f, 120f), new BpmChange(10f, 30f) });
        // 0-10 s at 60 = 10 beats; 10-20 s at 30 = 5 beats; then 120.
        Assert.Equal(3, tempo.SegmentCount);
        Assert.Equal(10f, tempo.BeatToSeconds(10f), 4);
        Assert.Equal(20f, tempo.BeatToSeconds(15f), 4);
        Assert.Equal(21f, tempo.BeatToSeconds(17f), 4);
    }

    [Fact]
    public void ChangeAtZero_ReplacesStartBpm_AndLaterDuplicateWins()
    {
        var tempo = BpmTimeline.FromSecondChanges(100f, new[] { new BpmChange(0f, 150f), new BpmChange(0f, 120f) });
        Assert.Equal(1, tempo.SegmentCount);
        Assert.Equal(120f, tempo.StartBpm);
        Assert.Equal(1f, tempo.BeatToSeconds(2f), 4);
    }

    [Fact]
    public void InvalidChanges_AreIgnored()
    {
        var tempo = BpmTimeline.FromSecondChanges(120f, new[]
        {
            new BpmChange(5f, 0f),
            new BpmChange(5f, -10f),
            new BpmChange(-1f, 60f),
            new BpmChange(float.NaN, 60f),
            new BpmChange(5f, float.PositiveInfinity),
        });
        Assert.Equal(1, tempo.SegmentCount);
        Assert.Equal(30f, tempo.BeatToSeconds(60f), 4);
    }

    [Fact]
    public void NegativeValues_ExtrapolateFirstSegment()
    {
        var tempo = BpmTimeline.FromSecondChanges(120f, new[] { new BpmChange(10f, 60f) });
        Assert.Equal(-1f, tempo.BeatToSeconds(-2f), 4);
        Assert.Equal(-2f, tempo.SecondsToBeat(-1f), 4);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-120f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidStartBpm_Throws(float bpm)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BpmTimeline.Constant(bpm));
        Assert.Throws<ArgumentOutOfRangeException>(() => BpmTimeline.FromBeatChanges(bpm, Array.Empty<BpmChange>()));
    }
}
