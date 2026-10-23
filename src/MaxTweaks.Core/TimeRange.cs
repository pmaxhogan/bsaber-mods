using System;

namespace MaxTweaks.Core;

/// <summary>A half-open time interval [Start, End) in song seconds.</summary>
public readonly struct TimeRange : IEquatable<TimeRange>
{
    public TimeRange(float start, float end)
    {
        if (end < start)
        {
            throw new ArgumentException($"end ({end}) is before start ({start})");
        }

        Start = start;
        End = end;
    }

    public float Start { get; }

    public float End { get; }

    public float Duration => End - Start;

    public bool Contains(float time) => time >= Start && time < End;

    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;

    public bool Equals(TimeRange other) => Start.Equals(other.Start) && End.Equals(other.End);

    public override bool Equals(object? obj) => obj is TimeRange other && Equals(other);

    public override int GetHashCode() => (Start.GetHashCode() * 397) ^ End.GetHashCode();

    public override string ToString() => $"[{Start}, {End})";
}
