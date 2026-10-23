using System;
using System.Collections.Generic;

namespace MaxTweaks.Core.Chapters;

/// <summary>
/// A tempo change: from <see cref="Position"/> onwards the tempo is <see cref="Bpm"/>. The position is
/// in seconds or in beats depending on which <see cref="BpmTimeline"/> factory receives it.
/// </summary>
public readonly struct BpmChange
{
    public BpmChange(float position, float bpm)
    {
        Position = position;
        Bpm = bpm;
    }

    public float Position { get; }

    public float Bpm { get; }
}

/// <summary>
/// Piecewise-constant tempo map converting beats to song seconds and back. Built either from tempo
/// changes positioned in seconds (what the game hands us: BPMChangeBeatmapEventData.time is already
/// in seconds) or in beats (what map files store). Invalid entries (non-positive or non-finite BPM,
/// negative or non-finite position) are ignored; changes are sorted, and a later change at the
/// same position wins.
/// </summary>
public sealed class BpmTimeline
{
    // Segment i covers [_segmentSeconds[i], _segmentSeconds[i + 1]) at _segmentBpm[i].
    private readonly double[] _segmentSeconds;
    private readonly double[] _segmentBeats;
    private readonly double[] _segmentBpm;

    private BpmTimeline(List<(double Seconds, double Beats, double Bpm)> segments)
    {
        _segmentSeconds = new double[segments.Count];
        _segmentBeats = new double[segments.Count];
        _segmentBpm = new double[segments.Count];
        for (var i = 0; i < segments.Count; i++)
        {
            _segmentSeconds[i] = segments[i].Seconds;
            _segmentBeats[i] = segments[i].Beats;
            _segmentBpm[i] = segments[i].Bpm;
        }
    }

    /// <summary>Tempo at song start.</summary>
    public float StartBpm => (float)_segmentBpm[0];

    /// <summary>Number of constant-tempo segments (1 when the tempo never changes).</summary>
    public int SegmentCount => _segmentBpm.Length;

    public static BpmTimeline Constant(float bpm) => FromSecondChanges(bpm, Array.Empty<BpmChange>());

    /// <param name="startBpm">Tempo before the first change (usually the Info.dat BPM).</param>
    /// <param name="changes">Tempo changes positioned in song seconds.</param>
    public static BpmTimeline FromSecondChanges(float startBpm, IEnumerable<BpmChange> changes)
    {
        RequireBpm(startBpm);
        var sorted = Clean(changes);
        var segments = new List<(double Seconds, double Beats, double Bpm)> { (0, 0, startBpm) };
        foreach (var change in sorted)
        {
            var last = segments[segments.Count - 1];
            var beats = last.Beats + ((change.Position - last.Seconds) * last.Bpm / 60.0);
            Append(segments, change.Position, beats, change.Bpm);
        }

        return new BpmTimeline(segments);
    }

    /// <param name="startBpm">Tempo before the first change (usually the Info.dat BPM).</param>
    /// <param name="changes">Tempo changes positioned in beats.</param>
    public static BpmTimeline FromBeatChanges(float startBpm, IEnumerable<BpmChange> changes)
    {
        RequireBpm(startBpm);
        var sorted = Clean(changes);
        var segments = new List<(double Seconds, double Beats, double Bpm)> { (0, 0, startBpm) };
        foreach (var change in sorted)
        {
            var last = segments[segments.Count - 1];
            var seconds = last.Seconds + ((change.Position - last.Beats) * 60.0 / last.Bpm);
            Append(segments, seconds, change.Position, change.Bpm);
        }

        return new BpmTimeline(segments);
    }

    public float BeatToSeconds(float beat)
    {
        var i = FindSegment(_segmentBeats, beat);
        return (float)(_segmentSeconds[i] + ((beat - _segmentBeats[i]) * 60.0 / _segmentBpm[i]));
    }

    public float SecondsToBeat(float seconds)
    {
        var i = FindSegment(_segmentSeconds, seconds);
        return (float)(_segmentBeats[i] + ((seconds - _segmentSeconds[i]) * _segmentBpm[i] / 60.0));
    }

    private static void Append(List<(double Seconds, double Beats, double Bpm)> segments, double seconds, double beats, double bpm)
    {
        var last = segments[segments.Count - 1];
        if (Math.Abs(seconds - last.Seconds) < 1e-9)
        {
            // Change at the same position as the previous segment start: it replaces that tempo.
            segments[segments.Count - 1] = (last.Seconds, last.Beats, bpm);
        }
        else
        {
            segments.Add((seconds, beats, bpm));
        }
    }

    private static List<(double Position, double Bpm)> Clean(IEnumerable<BpmChange> changes)
    {
        var list = new List<(double Position, double Bpm, int Order)>();
        var order = 0;
        foreach (var change in changes)
        {
            if (IsFinite(change.Position) && change.Position >= 0 && IsFinite(change.Bpm) && change.Bpm > 0)
            {
                list.Add((change.Position, change.Bpm, order));
            }

            order++;
        }

        // Stable: equal positions keep input order so the later one is appended last and wins.
        list.Sort((a, b) => a.Position != b.Position ? a.Position.CompareTo(b.Position) : a.Order.CompareTo(b.Order));
        var result = new List<(double Position, double Bpm)>(list.Count);
        foreach (var item in list)
        {
            result.Add((item.Position, item.Bpm));
        }

        return result;
    }

    private static int FindSegment(double[] starts, double value)
    {
        // Last segment whose start is <= value; values before 0 extrapolate the first segment.
        var lo = 0;
        var hi = starts.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (starts[mid] <= value)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    private static void RequireBpm(float bpm)
    {
        if (!IsFinite(bpm) || bpm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bpm), bpm, "BPM must be a positive finite number");
        }
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
