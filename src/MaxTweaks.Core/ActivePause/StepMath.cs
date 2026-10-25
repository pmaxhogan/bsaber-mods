using System;
using System.Globalization;
using MaxTweaks.Core.Chapters;

namespace MaxTweaks.Core.ActivePause;

/// <summary>
/// Where an active-pause step lands.
///
/// Seconds steps are plain offsets. Beat steps snap to the beat grid of their own size (whole beats
/// for +/-1b, quarter beats for +/-.25b) in the map's tempo (<see cref="BpmTimeline"/>, beat 0 at
/// song time 0): a forward step goes to the next grid line strictly after the current time, a
/// backward step to the previous grid line strictly before it. So from a time exactly on the grid a
/// step moves one full grid unit, and from a time between lines it goes to the neighbouring line
/// (not "time + 1 beat"). Times within <see cref="GridToleranceBeats"/> of a line count as on it,
/// which absorbs the float error of reading back the song time after a seek.
///
/// Without a usable tempo (null) beat steps fall back to plain offsets of
/// 60 / <see cref="FallbackBpm"/> seconds per beat.
/// </summary>
public static class StepMath
{
    /// <summary>Tempo assumed for beat steps when the map has no usable BPM.</summary>
    public const float FallbackBpm = 120f;

    /// <summary>Distance from a grid line, in beats, that still counts as on the line.</summary>
    public const double GridToleranceBeats = 0.002;

    /// <summary>Unclamped target time (song seconds) of <paramref name="step"/> from <paramref name="time"/>.</summary>
    public static float Target(float time, ActivePauseStep step, BpmTimeline? tempo)
    {
        if (step == null)
        {
            throw new ArgumentNullException(nameof(step));
        }

        if (step.Unit == StepUnit.Seconds)
        {
            return time + step.Amount;
        }

        if (tempo == null)
        {
            return time + (step.Amount * 60f / FallbackBpm);
        }

        var beat = tempo.SecondsToBeat(time);
        var target = SnapBeat(beat, Math.Abs(step.Amount), step.Direction);
        return tempo.BeatToSeconds((float)target);
    }

    /// <summary>
    /// The next grid line after <paramref name="beat"/> (<paramref name="direction"/> &gt; 0) or the
    /// previous one before it (direction &lt; 0), on a grid of <paramref name="grid"/> beats.
    /// </summary>
    public static double SnapBeat(double beat, double grid, int direction)
    {
        if (!(grid > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(grid), grid, "grid must be positive");
        }

        var units = beat / grid;
        var tolerance = GridToleranceBeats / grid;
        var line = direction >= 0
            ? Math.Floor(units + tolerance) + 1
            : Math.Ceiling(units - tolerance) - 1;
        return line * grid;
    }

    /// <summary>
    /// Clamps a seek target to [0, songEnd - endMargin] (never below 0, also when the song is shorter
    /// than the margin). NaN becomes 0.
    /// </summary>
    public static float Clamp(float time, float songEnd, float endMargin)
    {
        var latest = Math.Max(0f, songEnd - endMargin);
        if (float.IsNaN(time) || time < 0f)
        {
            return 0f;
        }

        return Math.Min(time, latest);
    }

    /// <summary>
    /// Clamped target of <paramref name="step"/>: <see cref="Target"/> then <see cref="Clamp"/>, and a
    /// step never moves against its direction (a forward step from inside the end margin stays put
    /// instead of jumping back to the clamp; likewise a backward step from before 0).
    /// </summary>
    public static float ClampedTarget(float time, ActivePauseStep step, BpmTimeline? tempo, float songEnd, float endMargin)
    {
        var target = Clamp(Target(time, step, tempo), songEnd, endMargin);
        return step.Direction > 0 ? Math.Max(time, target) : Math.Min(time, target);
    }

    /// <summary>Song time for log lines: invariant culture, 3 decimals, "s" suffix (e.g. "42.317s").</summary>
    public static string FormatTime(float seconds) => seconds.ToString("0.000", CultureInfo.InvariantCulture) + "s";
}
