using System;
using System.Collections.Generic;

namespace MaxTweaks.Core.DifficultySwitch;

/// <summary>
/// Ordering and naming of the difficulties shown in the switch list. Difficulties are game ranks
/// (BeatmapDifficulty as int: Easy 0, Normal 1, Hard 2, Expert 3, ExpertPlus 4). The list is shown
/// hardest first, so index 0 is the top row and "up" (stick forward) means a lower index.
/// </summary>
public static class DifficultyOrder
{
    private static readonly string[] Labels = { "Easy", "Normal", "Hard", "Expert", "Expert+" };

    /// <summary>Distinct ranks, hardest first.</summary>
    public static int[] HardestFirst(IEnumerable<int> ranks)
    {
        if (ranks == null)
        {
            throw new ArgumentNullException(nameof(ranks));
        }

        var set = new SortedSet<int>(ranks);
        var result = new int[set.Count];
        var i = result.Length - 1;
        foreach (var rank in set)
        {
            result[i--] = rank;
        }

        return result;
    }

    /// <summary>Index of <paramref name="rank"/> in <paramref name="ordered"/>, or -1.</summary>
    public static int IndexOf(IReadOnlyList<int> ordered, int rank)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i] == rank)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The game's display name for a rank ("Expert+"), or "Difficulty N" for unknown ranks.</summary>
    public static string DefaultLabel(int rank) =>
        rank >= 0 && rank < Labels.Length ? Labels[rank] : "Difficulty " + rank;

    /// <summary>A map's custom label when it has one (trimmed), else <see cref="DefaultLabel"/>.</summary>
    public static string Label(int rank, string? customLabel) =>
        string.IsNullOrWhiteSpace(customLabel) ? DefaultLabel(rank) : customLabel!.Trim();
}
