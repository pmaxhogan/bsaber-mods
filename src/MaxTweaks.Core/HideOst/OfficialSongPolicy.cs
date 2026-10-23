using System;
using System.Collections.Generic;

namespace MaxTweaks.Core.HideOst;

/// <summary>
/// Rules for the Hide OSTs feature. "Official" means anything that is not a custom level: base OST,
/// Extras and every DLC music pack. Custom levels are recognised by their level id prefix, which
/// SongCore gives every custom map (including WIP maps).
/// </summary>
public static class OfficialSongPolicy
{
    /// <summary>Level id prefix of every custom (SongCore-loaded) level.</summary>
    public const string CustomLevelIdPrefix = "custom_level_";

    private static readonly SongCategory[] HiddenModeCategories =
    {
        SongCategory.CustomSongs,
        SongCategory.Favorites,
        SongCategory.All,
    };

    public static bool IsCustomLevelId(string? levelId) =>
        levelId != null && levelId.StartsWith(CustomLevelIdPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Whether official songs are hidden right now. Only in the solo song list (the only place the
    /// toggle button exists), and never when the game has custom levels switched off, because then
    /// hiding official songs would leave an empty list.
    /// </summary>
    public static bool ShouldHide(bool featureEnabled, bool hidden, bool inSoloFlow, bool customLevelsEnabled) =>
        featureEnabled && hidden && inSoloFlow && customLevelsEnabled;

    /// <summary>
    /// Removes the Music Packs tab (OST, Extras and DLC) from the category tabs. If that would leave
    /// no tab at all the input is returned unchanged, so the category selector is never empty.
    /// </summary>
    public static SongCategory[] FilterCategories(IReadOnlyList<SongCategory> categories)
    {
        if (categories == null)
        {
            throw new ArgumentNullException(nameof(categories));
        }

        var result = new List<SongCategory>(categories.Count);
        foreach (var category in categories)
        {
            if (category != SongCategory.MusicPacks)
            {
                result.Add(category);
            }
        }

        return result.Count > 0 ? result.ToArray() : ToArray(categories);
    }

    /// <summary>
    /// Picks the tab to show when <paramref name="requested"/> may not be available. None is passed
    /// through (the game then picks its first tab). An unavailable tab falls back to Custom Songs,
    /// then to the first available tab, then to None.
    /// </summary>
    public static SongCategory CoerceCategory(SongCategory requested, IReadOnlyList<SongCategory> available)
    {
        if (available == null)
        {
            throw new ArgumentNullException(nameof(available));
        }

        if (requested == SongCategory.None || Contains(available, requested))
        {
            return requested;
        }

        if (Contains(available, SongCategory.CustomSongs))
        {
            return SongCategory.CustomSongs;
        }

        return available.Count > 0 ? available[0] : SongCategory.None;
    }

    /// <summary>
    /// Start tab for a song list that is about to be built with official songs hidden (the tab set
    /// is not known yet at that point, but it is always Custom Songs, Favorites and All).
    /// </summary>
    public static SongCategory CoerceStartCategory(SongCategory requested) =>
        CoerceCategory(requested, HiddenModeCategories);

    /// <summary>
    /// The custom levels of <paramref name="levels"/>, in their original order. Returns the same
    /// array instance (no allocation) when every level is already custom, so callers can detect "no
    /// change" with a reference comparison; otherwise a new, smaller array.
    /// </summary>
    public static T[] KeepCustomLevels<T>(T[] levels, Func<T, string?> levelId)
    {
        if (levels == null)
        {
            throw new ArgumentNullException(nameof(levels));
        }

        if (levelId == null)
        {
            throw new ArgumentNullException(nameof(levelId));
        }

        var custom = 0;
        for (var i = 0; i < levels.Length; i++)
        {
            if (IsCustomLevelId(levelId(levels[i])))
            {
                custom++;
            }
        }

        if (custom == levels.Length)
        {
            return levels;
        }

        var result = new T[custom];
        var next = 0;
        for (var i = 0; i < levels.Length; i++)
        {
            if (IsCustomLevelId(levelId(levels[i])))
            {
                result[next++] = levels[i];
            }
        }

        return result;
    }

    private static bool Contains(IReadOnlyList<SongCategory> list, SongCategory value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return true;
            }
        }

        return false;
    }

    private static SongCategory[] ToArray(IReadOnlyList<SongCategory> list)
    {
        var copy = new SongCategory[list.Count];
        for (var i = 0; i < list.Count; i++)
        {
            copy[i] = list[i];
        }

        return copy;
    }
}
