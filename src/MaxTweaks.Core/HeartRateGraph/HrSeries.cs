using System.Collections.Generic;

namespace MaxTweaks.Core.HeartRateGraph;

/// <summary>One heart-rate reading placed on the song timeline.</summary>
public readonly struct HrSample
{
    public HrSample(double time, int bpm, bool startsRun)
    {
        Time = time;
        Bpm = bpm;
        StartsRun = startsRun;
    }

    /// <summary>Song time of the reading, in seconds.</summary>
    public double Time { get; }

    public int Bpm { get; }

    /// <summary>True when the line does not connect this sample to the one before it.</summary>
    public bool StartsRun { get; }
}

/// <summary>
/// The heart-rate readings of one song, ordered by song time. The graph draws consecutive samples
/// as one line; a sample that <see cref="HrSample.StartsRun"/> begins a new line, so a strap
/// dropout or a skip ahead leaves a hole instead of a long straight stroke.
/// - Reading while the song time stands still (paused): the newest value replaces the last sample.
/// - Song time jumped back (practice seek, chapter jump): everything from the new time on is
///   dropped and drawn again as the song is replayed.
/// </summary>
public sealed class HrSeries
{
    /// <summary>Readings further apart than this (song seconds) are not joined. Straps report about once a second.</summary>
    public const double DefaultMaxGapSeconds = 5.0;

    /// <summary>Readings closer than this (song seconds) to the last sample update it instead of adding a point.</summary>
    public const double DefaultMinStepSeconds = 0.25;

    private readonly List<HrSample> _samples = new();
    private readonly double _maxGap;
    private readonly double _minStep;

    public HrSeries(double maxGapSeconds = DefaultMaxGapSeconds, double minStepSeconds = DefaultMinStepSeconds)
    {
        _maxGap = maxGapSeconds;
        _minStep = minStepSeconds;
    }

    public IReadOnlyList<HrSample> Samples => _samples;

    /// <summary>Changes whenever the samples change, so a view can skip rebuilding an unchanged graph.</summary>
    public int Version { get; private set; }

    public void Add(double songTime, int bpm)
    {
        if (double.IsNaN(songTime) || double.IsInfinity(songTime) || bpm <= 0)
        {
            return;
        }

        var count = _samples.Count;
        if (count > 0 && songTime <= _samples[count - 1].Time - _minStep)
        {
            var keep = count;
            while (keep > 0 && _samples[keep - 1].Time >= songTime)
            {
                keep--;
            }

            _samples.RemoveRange(keep, count - keep);
            count = keep;
            Version++;
        }

        if (count == 0)
        {
            _samples.Add(new HrSample(songTime, bpm, true));
            Version++;
            return;
        }

        var last = _samples[count - 1];
        var elapsed = songTime - last.Time;
        if (elapsed < _minStep)
        {
            if (last.Bpm != bpm)
            {
                _samples[count - 1] = new HrSample(last.Time, bpm, last.StartsRun);
                Version++;
            }

            return;
        }

        _samples.Add(new HrSample(songTime, bpm, elapsed > _maxGap));
        Version++;
    }

    public void Clear()
    {
        if (_samples.Count == 0)
        {
            return;
        }

        _samples.Clear();
        Version++;
    }
}
