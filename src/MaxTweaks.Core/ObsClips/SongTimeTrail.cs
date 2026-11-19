using System;
using System.Collections.Generic;
using System.Globalization;

namespace MaxTweaks.Core.ObsClips;

/// <summary>
/// Remembers where the song was at recent moments, so a clip picked up to a minute after the fact
/// can be named after the song time it starts at (pauses, seeks and practice speed included).
/// </summary>
public sealed class SongTimeTrail
{
    private const double StepSeconds = 0.5;

    private readonly List<KeyValuePair<double, double>> _points = new();
    private readonly double _keepSeconds;

    public SongTimeTrail(double keepSeconds = 180.0)
    {
        _keepSeconds = keepSeconds;
    }

    /// <summary>"1m23s", "0m05s", "12m00s": a song time for a file name.</summary>
    public static string Text(double songSeconds)
    {
        var total = double.IsNaN(songSeconds) || songSeconds < 0.0 ? 0L : (long)Math.Min(songSeconds, 359999.0);
        return (total / 60).ToString(CultureInfo.InvariantCulture) + "m" + (total % 60).ToString("00", CultureInfo.InvariantCulture) + "s";
    }

    public void Clear() => _points.Clear();

    /// <summary>Call often; points closer together than half a second are skipped.</summary>
    public void Add(double clock, double songSeconds)
    {
        var count = _points.Count;
        if (count > 0 && clock - _points[count - 1].Key < StepSeconds)
        {
            return;
        }

        _points.Add(new KeyValuePair<double, double>(clock, songSeconds));
        var drop = 0;
        while (drop < _points.Count && _points[drop].Key < clock - _keepSeconds)
        {
            drop++;
        }

        _points.RemoveRange(0, drop);
    }

    /// <summary>The song time at <paramref name="clock"/>: the last point at or before it, else the oldest one.</summary>
    public bool TryAt(double clock, out double songSeconds)
    {
        for (var i = _points.Count - 1; i >= 0; i--)
        {
            if (_points[i].Key <= clock)
            {
                songSeconds = _points[i].Value;
                return true;
            }
        }

        songSeconds = _points.Count > 0 ? _points[0].Value : 0.0;
        return _points.Count > 0;
    }
}
