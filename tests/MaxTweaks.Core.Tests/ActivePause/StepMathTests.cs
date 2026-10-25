using System;
using System.Linq;
using MaxTweaks.Core.ActivePause;
using MaxTweaks.Core.Chapters;
using Xunit;

namespace MaxTweaks.Core.Tests.ActivePause;

public class StepMathTests
{
    // 120 BPM: one beat = 0.5 s, a quarter beat = 0.125 s.
    private static readonly BpmTimeline Tempo120 = BpmTimeline.Constant(120f);

    private static ActivePauseStep Step(string label) => ActivePauseStep.Find(label) ?? throw new ArgumentException(label);

    [Fact]
    public void Steps_AreTheTwelveButtonsInOrder()
    {
        Assert.Equal(
            new[] { "-1m", "-15s", "-5s", "-1s", "-1b", "-.25b", "+.25b", "+1b", "+1s", "+5s", "+15s", "+1m" },
            ActivePauseStep.All.Select(s => s.Label).ToArray());
    }

    [Theory]
    [InlineData("-1m", -60f, StepUnit.Seconds, -1)]
    [InlineData("-15s", -15f, StepUnit.Seconds, -1)]
    [InlineData("-5s", -5f, StepUnit.Seconds, -1)]
    [InlineData("-1s", -1f, StepUnit.Seconds, -1)]
    [InlineData("-1b", -1f, StepUnit.Beats, -1)]
    [InlineData("-.25b", -0.25f, StepUnit.Beats, -1)]
    [InlineData("+.25b", 0.25f, StepUnit.Beats, 1)]
    [InlineData("+1b", 1f, StepUnit.Beats, 1)]
    [InlineData("+1s", 1f, StepUnit.Seconds, 1)]
    [InlineData("+5s", 5f, StepUnit.Seconds, 1)]
    [InlineData("+15s", 15f, StepUnit.Seconds, 1)]
    [InlineData("+1m", 60f, StepUnit.Seconds, 1)]
    public void Steps_HaveAmountUnitAndDirection(string label, float amount, StepUnit unit, int direction)
    {
        var step = Step(label);
        Assert.Equal(amount, step.Amount);
        Assert.Equal(unit, step.Unit);
        Assert.Equal(direction, step.Direction);
        Assert.Equal(label, step.ToString());
    }

    [Fact]
    public void Find_IsCaseInsensitiveAndNullForUnknown()
    {
        Assert.Same(ActivePauseStep.All[11], ActivePauseStep.Find("+1M"));
        Assert.Null(ActivePauseStep.Find("+2b"));
        Assert.Null(ActivePauseStep.Find(string.Empty));
    }

    [Theory]
    [InlineData("+1s", 42.317f, 43.317f)]
    [InlineData("-1s", 42.317f, 41.317f)]
    [InlineData("+5s", 42.317f, 47.317f)]
    [InlineData("-15s", 42.317f, 27.317f)]
    [InlineData("+1m", 42.317f, 102.317f)]
    [InlineData("-1m", 20f, -40f)]
    public void SecondSteps_ArePlainOffsets(string label, float from, float expected)
    {
        Assert.Equal(expected, StepMath.Target(from, Step(label), Tempo120), 3);

        // The tempo does not matter for seconds steps.
        Assert.Equal(expected, StepMath.Target(from, Step(label), null), 3);
    }

    [Theory]
    [InlineData(10f, 10.5f)] // on a beat line: a full beat
    [InlineData(10.2f, 10.5f)] // between lines: the next line, not +0.5 s
    [InlineData(10.499f, 10.5f)] // just before a line: that line
    [InlineData(0f, 0.5f)]
    public void PlusOneBeat_GoesToNextBeatLine(float from, float expected)
    {
        Assert.Equal(expected, StepMath.Target(from, Step("+1b"), Tempo120), 4);
    }

    [Theory]
    [InlineData(10f, 9.5f)] // on a beat line: a full beat back
    [InlineData(10.2f, 10f)] // between lines: the previous line
    [InlineData(10.001f, 10f)] // just after a line: that line
    [InlineData(0.3f, 0f)]
    [InlineData(0f, -0.5f)] // before the song start: unclamped here, see Clamp
    public void MinusOneBeat_GoesToPreviousBeatLine(float from, float expected)
    {
        Assert.Equal(expected, StepMath.Target(from, Step("-1b"), Tempo120), 4);
    }

    [Theory]
    [InlineData("+.25b", 10f, 10.125f)]
    [InlineData("+.25b", 10.2f, 10.25f)]
    [InlineData("+.25b", 10.125f, 10.25f)]
    [InlineData("-.25b", 10f, 9.875f)]
    [InlineData("-.25b", 10.2f, 10.125f)]
    [InlineData("-.25b", 10.125f, 10f)]
    public void QuarterBeats_UseTheQuarterGrid(string label, float from, float expected)
    {
        Assert.Equal(expected, StepMath.Target(from, Step(label), Tempo120), 4);
    }

    [Fact]
    public void ReadBackFloatError_StillCountsAsOnTheGrid()
    {
        // A song time read back a hair off the line after a seek must still move a full unit.
        Assert.Equal(11f, StepMath.Target(10.5f + 1e-5f, Step("+1b"), Tempo120), 4);
        Assert.Equal(11f, StepMath.Target(10.5f - 1e-5f, Step("+1b"), Tempo120), 4);
        Assert.Equal(10f, StepMath.Target(10.5f + 1e-5f, Step("-1b"), Tempo120), 4);
        Assert.Equal(10f, StepMath.Target(10.5f - 1e-5f, Step("-1b"), Tempo120), 4);
    }

    [Fact]
    public void RepeatedSteps_WalkTheGrid()
    {
        var time = 10.2f;
        var forward = new[] { 10.5f, 11f, 11.5f };
        foreach (var expected in forward)
        {
            time = StepMath.Target(time, Step("+1b"), Tempo120);
            Assert.Equal(expected, time, 4);
        }

        var back = new[] { 11.375f, 11.25f, 11.125f };
        foreach (var expected in back)
        {
            time = StepMath.Target(time, Step("-.25b"), Tempo120);
            Assert.Equal(expected, time, 4);
        }
    }

    [Fact]
    public void BeatSteps_FollowTempoChanges()
    {
        // 120 BPM for 10 s (beats 0..20), then 60 BPM (one beat = 1 s).
        var tempo = BpmTimeline.FromSecondChanges(120f, new[] { new BpmChange(10f, 60f) });
        Assert.Equal(10f, StepMath.Target(9.7f, Step("+1b"), tempo), 4);
        Assert.Equal(11f, StepMath.Target(10f, Step("+1b"), tempo), 4);
        Assert.Equal(12f, StepMath.Target(11.4f, Step("+1b"), tempo), 4);
        Assert.Equal(10.25f, StepMath.Target(10f, Step("+.25b"), tempo), 4);
        Assert.Equal(9.5f, StepMath.Target(10f, Step("-1b"), tempo), 4);
        Assert.Equal(10f, StepMath.Target(10.6f, Step("-1b"), tempo), 4);
    }

    [Theory]
    [InlineData("+1b", 10.2f, 10.7f)]
    [InlineData("-1b", 10.2f, 9.7f)]
    [InlineData("+.25b", 10.2f, 10.325f)]
    [InlineData("-.25b", 10.2f, 10.075f)]
    public void NoTempo_FallsBackToPlainHalfSecondBeats(string label, float from, float expected)
    {
        Assert.Equal(expected, StepMath.Target(from, Step(label), null), 4);
        Assert.Equal(120f, StepMath.FallbackBpm);
    }

    [Fact]
    public void Target_RejectsNullStep()
    {
        Assert.Throws<ArgumentNullException>(() => StepMath.Target(1f, null!, Tempo120));
    }

    [Theory]
    [InlineData(1.0, 1.0, 1, 2.0)]
    [InlineData(1.0, 1.0, -1, 0.0)]
    [InlineData(1.4, 1.0, 1, 2.0)]
    [InlineData(1.4, 1.0, -1, 1.0)]
    [InlineData(-0.6, 1.0, 1, 0.0)]
    [InlineData(-0.6, 1.0, -1, -1.0)]
    [InlineData(1.3, 0.25, 1, 1.5)]
    [InlineData(1.3, 0.25, -1, 1.25)]
    [InlineData(1.001, 1.0, 1, 2.0)] // within tolerance: on the line
    [InlineData(0.999, 1.0, -1, 0.0)]
    [InlineData(1.003, 1.0, -1, 1.0)] // outside tolerance: between lines
    public void SnapBeat_Rules(double beat, double grid, int direction, double expected)
    {
        Assert.Equal(expected, StepMath.SnapBeat(beat, grid, direction), 6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void SnapBeat_RejectsBadGrid(double grid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StepMath.SnapBeat(1.0, grid, 1));
    }

    [Theory]
    [InlineData(42f, 100f, 0.3f, 42f)]
    [InlineData(-40f, 100f, 0.3f, 0f)] // -1m at 20 s
    [InlineData(160f, 100f, 0.3f, 99.7f)] // +1m near the end
    [InlineData(99.9f, 100f, 0.3f, 99.7f)]
    [InlineData(5f, 0.2f, 0.3f, 0f)] // song shorter than the margin
    [InlineData(float.NaN, 100f, 0.3f, 0f)]
    public void Clamp_KeepsInsideTheSong(float time, float songEnd, float margin, float expected)
    {
        Assert.Equal(expected, StepMath.Clamp(time, songEnd, margin), 4);
    }

    [Fact]
    public void ClampedTarget_CombinesTargetAndClamp()
    {
        Assert.Equal(0f, StepMath.ClampedTarget(20f, Step("-1m"), Tempo120, 120f, 0.3f), 4);
        Assert.Equal(119.7f, StepMath.ClampedTarget(100f, Step("+1m"), Tempo120, 120f, 0.3f), 4);
        Assert.Equal(10.5f, StepMath.ClampedTarget(10.2f, Step("+1b"), Tempo120, 120f, 0.3f), 4);
    }

    [Theory]
    [InlineData("+1s", 119.8f)] // inside the last 0.3 s: stays, never back to 119.7
    [InlineData("+1b", 119.8f)]
    [InlineData("+.25b", 119.75f)]
    [InlineData("+1m", 119.71f)]
    public void ForwardSteps_NeverMoveBackward(string label, float from)
    {
        Assert.Equal(from, StepMath.ClampedTarget(from, Step(label), Tempo120, 120f, 0.3f), 4);
    }

    [Theory]
    [InlineData("-1s", -0.5f)]
    [InlineData("-1b", -0.2f)]
    public void BackwardSteps_NeverMoveForward(string label, float from)
    {
        Assert.Equal(from, StepMath.ClampedTarget(from, Step(label), Tempo120, 120f, 0.3f), 4);
    }

    [Theory]
    [InlineData(42.317f, "42.317s")]
    [InlineData(0f, "0.000s")]
    [InlineData(42.8571f, "42.857s")]
    [InlineData(123.4f, "123.400s")]
    public void FormatTime_IsInvariantWithThreeDecimals(float seconds, string expected)
    {
        var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(expected, StepMath.FormatTime(seconds));
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}
