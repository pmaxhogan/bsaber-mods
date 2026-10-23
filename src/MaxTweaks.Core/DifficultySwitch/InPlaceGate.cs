using System;
using System.Collections.Generic;

namespace MaxTweaks.Core.DifficultySwitch;

/// <summary>
/// Decides whether a difficulty can be swapped in place or needs the fallback restart.
/// - Any requirement restarts: Noodle Extensions spawns from its own deserialized data and throws for
///   objects it never saw, Vivify/Mapping Extensions change objects from scene-start data, a required
///   Chroma map usually also builds per-difficulty environment geometry at scene start, and an unknown
///   required mod may hook the data too.
/// - Suggestions restart only for <see cref="BlockingSuggestions"/>. A Chroma suggestion swaps in
///   place: the swap re-runs Heck's deserialization for the new objects (and Chroma's lookups are
///   null-safe if that fails).
/// - Rotation (360/90) maps restart: their spawn rotation follows object callbacks that the clear gap
///   would skip.
/// </summary>
public static class InPlaceGate
{
    /// <summary>Name fragments of suggested mods that still block an in-place swap (case-insensitive).</summary>
    public static readonly IReadOnlyList<string> BlockingSuggestions = new[] { "Noodle", "Vivify", "Mapping Extensions" };

    /// <summary>
    /// Reason the difficulty cannot be swapped in place, or null when it can.
    /// Every requirement blocks; suggestions block only when they name a
    /// <see cref="BlockingSuggestions"/> entry.
    /// </summary>
    public static string? Check(IEnumerable<string>? requirements, IEnumerable<string>? suggestions, bool hasRotationEvents)
    {
        if (hasRotationEvents)
        {
            return "rotation (360/90) map";
        }

        if (requirements != null)
        {
            foreach (var requirement in requirements)
            {
                if (!string.IsNullOrWhiteSpace(requirement))
                {
                    return "requires " + requirement.Trim();
                }
            }
        }

        if (suggestions != null)
        {
            foreach (var suggestion in suggestions)
            {
                if (IsBlocking(suggestion))
                {
                    return "suggests " + suggestion.Trim();
                }
            }
        }

        return null;
    }

    public static bool IsBlocking(string? modName)
    {
        if (string.IsNullOrWhiteSpace(modName))
        {
            return false;
        }

        foreach (var fragment in BlockingSuggestions)
        {
            if (modName!.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the suggestions name Chroma (in-place swap with a Heck data refresh).</summary>
    public static bool SuggestsChroma(IEnumerable<string>? suggestions)
    {
        if (suggestions == null)
        {
            return false;
        }

        foreach (var suggestion in suggestions)
        {
            if (suggestion != null && suggestion.IndexOf("Chroma", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
