using System;
using System.Collections.Generic;

namespace MaxTweaks.Core.Blink;

/// <summary>Decides, for one wall of the timeline, whether it would hit the player's head now.</summary>
public interface IWallHeadTest
{
    /// <param name="wallId">The id the wall was added with (<see cref="BlinkTimeline.Builder.AddWall"/>).</param>
    bool HitsHead(int wallId);
}

/// <summary>
/// Read-only index of a map's note and wall times (song seconds), built once per song. Queries
/// are stateless binary searches (O(log n), no allocation), so seeking backwards or forwards in
/// practice needs no cursor fix-up.
/// </summary>
public sealed class BlinkTimeline
{
    private readonly float[] _noteStarts;
    private readonly float[] _noteMaxEnds;
    private readonly float[] _wallStarts;
    private readonly float[] _wallEnds;
    private readonly float[] _wallMaxEnds;
    private readonly int[] _wallIds;

    private BlinkTimeline(List<Span> notes, List<Span> walls)
    {
        notes.Sort(CompareSpans);
        walls.Sort(CompareSpans);

        _noteStarts = new float[notes.Count];
        _noteMaxEnds = new float[notes.Count];
        var maxEnd = float.NegativeInfinity;
        for (var i = 0; i < notes.Count; i++)
        {
            _noteStarts[i] = notes[i].Start;
            maxEnd = Math.Max(maxEnd, notes[i].End);
            _noteMaxEnds[i] = maxEnd;
        }

        _wallStarts = new float[walls.Count];
        _wallEnds = new float[walls.Count];
        _wallMaxEnds = new float[walls.Count];
        _wallIds = new int[walls.Count];
        maxEnd = float.NegativeInfinity;
        for (var i = 0; i < walls.Count; i++)
        {
            _wallStarts[i] = walls[i].Start;
            _wallEnds[i] = walls[i].End;
            _wallIds[i] = walls[i].Id;
            maxEnd = Math.Max(maxEnd, walls[i].End);
            _wallMaxEnds[i] = maxEnd;
        }
    }

    public int NoteCount => _noteStarts.Length;

    public int WallCount => _wallStarts.Length;

    /// <summary>True if any note (or chain) is due inside [from, to] (inclusive).</summary>
    public bool AnyNoteIn(float from, float to)
    {
        if (to < from)
        {
            return false;
        }

        // Spans starting at or before 'to'; the latest end among them decides.
        var count = UpperBound(_noteStarts, to);
        return count > 0 && _noteMaxEnds[count - 1] >= from;
    }

    /// <summary>
    /// True if any wall overlapping [from, to] (inclusive; walls already passing count) hits the
    /// head according to <paramref name="test"/>. Only walls in the window are tested.
    /// Cost: O(log n) plus one float comparison per wall between the window and the earliest wall
    /// still reaching it; a very long early wall can make that most of the map, but only cheap
    /// comparisons (HitsHead runs for overlapping walls only) and BlinkScheduler calls this last,
    /// in frames where every other rule already allows a blink.
    /// </summary>
    public bool AnyHeadWallIn(float from, float to, IWallHeadTest test)
    {
        if (to < from)
        {
            return false;
        }

        // Walk back from the last wall starting by 'to' while some earlier wall could still reach 'from'.
        for (var i = UpperBound(_wallStarts, to) - 1; i >= 0 && _wallMaxEnds[i] >= from; i--)
        {
            if (_wallEnds[i] >= from && test.HitsHead(_wallIds[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <returns>Number of elements of the sorted array that are &lt;= value.</returns>
    private static int UpperBound(float[] sorted, float value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (sorted[mid] <= value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static int CompareSpans(Span a, Span b)
    {
        var byStart = a.Start.CompareTo(b.Start);
        return byStart != 0 ? byStart : a.Id.CompareTo(b.Id);
    }

    private readonly struct Span
    {
        public Span(float start, float end, int id)
        {
            Start = start;
            End = Math.Max(start, end);
            Id = id;
        }

        public float Start { get; }

        public float End { get; }

        public int Id { get; }
    }

    /// <summary>Collects items in any order; <see cref="Build"/> sorts them.</summary>
    public sealed class Builder
    {
        private readonly List<Span> _notes = new();
        private readonly List<Span> _walls = new();

        /// <summary>A note or bomb due at <paramref name="time"/>.</summary>
        public Builder AddNote(float time) => AddNoteSpan(time, time);

        /// <summary>Something to hit across a time span (a chain: head to tail).</summary>
        public Builder AddNoteSpan(float start, float end)
        {
            if (IsFinite(start) && IsFinite(end))
            {
                _notes.Add(new Span(start, end, _notes.Count));
            }

            return this;
        }

        /// <summary>A wall passing the player from <paramref name="start"/> to <paramref name="end"/>.</summary>
        /// <param name="id">Caller's handle, passed back to <see cref="IWallHeadTest.HitsHead"/>.</param>
        public Builder AddWall(float start, float end, int id)
        {
            if (IsFinite(start) && IsFinite(end))
            {
                _walls.Add(new Span(start, end, id));
            }

            return this;
        }

        public BlinkTimeline Build() => new(new List<Span>(_notes), new List<Span>(_walls));

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
