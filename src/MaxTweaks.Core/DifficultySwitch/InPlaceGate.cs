using System;
using System.Collections.Generic;

namespace MaxTweaks.Core.DifficultySwitch;

/// <summary>
/// Decides whether a difficulty can be swapped in place or needs the fallback restart. Mods that
/// deserialize per-object custom data when the scene starts (Heck: Noodle Extensions, Chroma,
/// Vivify) or patch object placement from raw data (Mapping Extensions) would not see the freshly
/// loaded objects of an in-place swap, so maps that require or suggest them restart instead.
/// Rotation (360/90) maps restart too: their spawn rotation follows object callbacks that the clear
/// gap would skip.
/// </summary>
public static class InPlaceGate
{
    /// <summary>Name fragments of mods that block an in-place swap when required or suggested (case-insensitive).</summary>
    public static readonly IReadOnlyList<string> BlockingMods = new[] { "Noodle", "Chroma", "Vivify", "Mapping Extensions" };

    /// <summary>
    /// Reason the difficulty cannot be swapped in place, or null when it can.
    /// Every requirement blocks (an unknown required mod may hook the data too); suggestions block
    /// only when they name a <see cref="BlockingMods"/> entry.
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

        foreach (var fragment in BlockingMods)
        {
            if (modName!.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
