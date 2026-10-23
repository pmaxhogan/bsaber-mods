using System;
using System.Collections.Generic;

namespace MaxTweaks.Core.Chapters;

/// <summary>
/// Which chapters are active at a song time and how they stack on screen. Chapters are kept in stack
/// order: longest first (drawn at the bottom), ties broken by earlier start, then file order. Querying
/// is allocation-free so it can run every frame.
/// </summary>
public sealed class ChapterStack
{
    private readonly Chapter[] _ordered;

    public ChapterStack(IEnumerable<Chapter> chapters)
    {
        var list = new List<Chapter>(chapters);
        list.Sort(CompareStackOrder);
        _ordered = list.ToArray();
        MaxConcurrent = ComputeMaxConcurrent(_ordered);
    }

    /// <summary>All chapters in stack order (index 0 = bottom-most when active).</summary>
    public IReadOnlyList<Chapter> Chapters => _ordered;

    public int Count => _ordered.Length;

    /// <summary>The most chapters that are ever active at the same time.</summary>
    public int MaxConcurrent { get; }

    /// <summary>
    /// Fills <paramref name="result"/> with indices into <see cref="Chapters"/> of the chapters active
    /// at <paramref name="time"/>, bottom line first. If more are active than fit, the longest ones
    /// (bottom of the stack) are dropped so the most specific chapters stay visible.
    /// </summary>
    /// <returns>The number of entries written.</returns>
    public int GetActive(float time, int[] result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var active = 0;
        for (var i = 0; i < _ordered.Length; i++)
        {
            if (_ordered[i].Contains(time))
            {
                active++;
            }
        }

        var skip = Math.Max(0, active - result.Length);
        var written = 0;
        for (var i = 0; i < _ordered.Length && written < result.Length; i++)
        {
            if (!_ordered[i].Contains(time))
            {
                continue;
            }

            if (skip > 0)
            {
                skip--;
                continue;
            }

            result[written++] = i;
        }

        return written;
    }

    internal static int CompareStackOrder(Chapter a, Chapter b)
    {
        var byDuration = b.Duration.CompareTo(a.Duration);
        if (byDuration != 0)
        {
            return byDuration;
        }

        var byStart = a.Start.CompareTo(b.Start);
        return byStart != 0 ? byStart : a.Index.CompareTo(b.Index);
    }

    private static int ComputeMaxConcurrent(Chapter[] chapters)
    {
        // Sweep: +1 at each start, -1 at each end; ends sort before starts at the same time because
        // ranges are half-open, so back-to-back chapters never count as overlapping.
        var edges = new List<(float Time, int Delta)>(chapters.Length * 2);
        foreach (var chapter in chapters)
        {
            edges.Add((chapter.Start, 1));
            edges.Add((chapter.End, -1));
        }

        edges.Sort((x, y) => x.Time != y.Time ? x.Time.CompareTo(y.Time) : x.Delta.CompareTo(y.Delta));
        int current = 0, max = 0;
        foreach (var edge in edges)
        {
            current += edge.Delta;
            max = Math.Max(max, current);
        }

        return max;
    }
}
