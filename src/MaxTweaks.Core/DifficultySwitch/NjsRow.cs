using System;
using System.Collections.Generic;
using System.Globalization;

namespace MaxTweaks.Core.DifficultySwitch;

/// <summary>
/// The horizontal NJS row of the difficulty switch: the integers <see cref="Min"/> to
/// <see cref="Max"/>, plus the difficulty's own (default) NJS and the NJS in effect now, each
/// inserted at its sorted place when it is not already one of them (not an integer, or out of range).
/// Values closer than <see cref="Tolerance"/> are the same entry. A default that is not a positive
/// finite number gives an empty row (no NJS switch).
/// </summary>
public sealed class NjsRow
{
    /// <summary>Smallest integer value offered.</summary>
    public const int Min = 8;

    /// <summary>Largest integer value offered.</summary>
    public const int Max = 30;

    /// <summary>Two NJS values closer than this are equal.</summary>
    public const float Tolerance = 0.001f;

    private static readonly NjsRow Empty = new(Array.Empty<float>(), float.NaN, -1, float.NaN, -1);

    private readonly float[] _values;

    private NjsRow(float[] values, float defaultNjs, int defaultIndex, float current, int currentIndex)
    {
        _values = values;
        Default = defaultNjs;
        DefaultIndex = defaultIndex;
        Current = current;
        CurrentIndex = currentIndex;
    }

    /// <summary>Values, ascending (left to right).</summary>
    public IReadOnlyList<float> Values => _values;

    public int Count => _values.Length;

    /// <summary>The difficulty's own NJS (underlined).</summary>
    public float Default { get; }

    /// <summary>Index of <see cref="Default"/>, or -1 for an empty row.</summary>
    public int DefaultIndex { get; }

    /// <summary>The NJS in effect now (where the highlight starts).</summary>
    public float Current { get; }

    /// <summary>Index of <see cref="Current"/>, or -1 for an empty row.</summary>
    public int CurrentIndex { get; }

    /// <param name="defaultNjs">The difficulty's own NJS with the active modifiers.</param>
    /// <param name="currentNjs">The NJS in effect now; anything not positive and finite means the default.</param>
    public static NjsRow Build(float defaultNjs, float currentNjs)
    {
        if (!IsValid(defaultNjs))
        {
            return Empty;
        }

        if (!IsValid(currentNjs))
        {
            currentNjs = defaultNjs;
        }

        var values = new List<float>(Max - Min + 3);
        for (var i = Min; i <= Max; i++)
        {
            values.Add(i);
        }

        Insert(values, defaultNjs);
        Insert(values, currentNjs);
        var array = values.ToArray();
        return new NjsRow(array, defaultNjs, Find(array, defaultNjs), currentNjs, Find(array, currentNjs));
    }

    /// <summary>Index of the entry equal to <paramref name="njs"/> (within <see cref="Tolerance"/>), or -1.</summary>
    public int IndexOf(float njs) => Find(_values, njs);

    /// <summary>
    /// First index of a window of <paramref name="width"/> visible values that keeps
    /// <paramref name="highlight"/> centred where possible and never runs past either end.
    /// </summary>
    public static int WindowStart(int highlight, int count, int width)
    {
        var start = highlight - (width / 2);
        var maxStart = Math.Max(0, count - width);
        return Math.Max(0, Math.Min(start, maxStart));
    }

    /// <summary>"18", "16.5", "17.33" (invariant culture, at most two decimals).</summary>
    public static string Label(float njs) => njs.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Toast after a change: "NJS 18 (default 16)".</summary>
    public static string Toast(float njs, float defaultNjs) => $"NJS {Label(njs)} (default {Label(defaultNjs)})";

    /// <summary>Log line body: "njs 16 -> 18 (default 16)".</summary>
    public static string LogChange(float from, float to, float defaultNjs) => $"njs {Label(from)} -> {Label(to)} (default {Label(defaultNjs)})";

    /// <summary>Debug status field: "njs=18/16" (current/default).</summary>
    public static string Status(float current, float defaultNjs) => $"njs={Label(current)}/{Label(defaultNjs)}";

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> are the same NJS.</summary>
    public static bool Same(float a, float b) => Math.Abs(a - b) < Tolerance;

    private static bool IsValid(float njs) => njs > 0f && !float.IsInfinity(njs) && !float.IsNaN(njs);

    private static void Insert(List<float> values, float njs)
    {
        var at = 0;
        while (at < values.Count && values[at] < njs)
        {
            at++;
        }

        if ((at < values.Count && Same(values[at], njs)) || (at > 0 && Same(values[at - 1], njs)))
        {
            return;
        }

        values.Insert(at, njs);
    }

    private static int Find(float[] values, float njs)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (Same(values[i], njs))
            {
                return i;
            }
        }

        return -1;
    }
}
