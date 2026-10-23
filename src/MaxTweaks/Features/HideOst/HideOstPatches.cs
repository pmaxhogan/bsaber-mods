using System;
using System.Collections.Generic;
using HarmonyLib;
using MaxTweaks.Common;
using LevelCategory = SelectLevelCategoryViewController.LevelCategory;

namespace MaxTweaks.Features.HideOst;

// Applied as one unit through SafePatcher (feature id "HideOst"); every body defers to
// HideOstController.Current and does nothing outside a live menu or when the feature is off.
// Targets verified by decompiling Beat Saber 1.40.8 Main.dll / DataModels.dll.

/// <summary>
/// LevelFilteringNavigationController.Setup(songPackMask, levelPackToBeSelectedAfterPresent,
/// startLevelCategory, hidePacksIfOneOrNone, enableCustomLevels). With the Music Packs tab removed,
/// a start category of MusicPacks (e.g. BetterSongList restoring its LastCategory) would make
/// SelectLevelCategoryViewController select cell -1, and UpdateSecondChildControllerContent(None)
/// throws ArgumentOutOfRangeException. Remap it to Custom Songs first.
/// </summary>
[HarmonyPatch(typeof(LevelFilteringNavigationController), nameof(LevelFilteringNavigationController.Setup))]
internal static class LevelFilteringSetupPatch
{
    private static void Prefix(ref LevelCategory startLevelCategory, bool enableCustomLevels)
    {
        var controller = HideOstController.Current;
        if (controller == null)
        {
            return;
        }

        var requested = startLevelCategory;
        startLevelCategory = Guard.Run(
            HideOstController.Feature,
            () => controller.CoerceStartCategory(requested, enableCustomLevels),
            requested);
    }
}

/// <summary>
/// LevelFilteringNavigationController.SetupBeatmapLevelPacks (private): fills the OST, DLC and
/// all-official pack arrays and the category tab list. Runs after it to strip official content.
/// </summary>
[HarmonyPatch(typeof(LevelFilteringNavigationController), nameof(LevelFilteringNavigationController.SetupBeatmapLevelPacks))]
internal static class SetupBeatmapLevelPacksPatch
{
    private static void Postfix(LevelFilteringNavigationController __instance)
    {
        var controller = HideOstController.Current;
        if (controller == null)
        {
            return;
        }

        Guard.Run(HideOstController.Feature, () => controller.FilterPacks(__instance));
    }
}

/// <summary>
/// LevelCollectionTableView.SetData(IReadOnlyList of BeatmapLevel beatmapLevels, favoriteLevelIds,
/// beatmapLevelsAreSorted, sortBeatmapLevels): the last stop before the song rows are built.
/// BetterSongList 0.4.2 prefixes this at HarmonyPriority(int.MaxValue) and, for a selected
/// playlist, REPLACES the list with PlaylistsUtil.GetLevelsForLevelCollection(...) before sorting
/// and filtering it. That is why filtering one level up (LevelCollectionViewController.SetData)
/// let a playlist's official songs through. Low priority puts this prefix after BSL's, so it
/// filters BSL's final (already sorted) list; removing rows keeps the order. BSL caches its
/// unfiltered input and re-calls SetData with it on refresh, which passes through here again.
/// </summary>
[HarmonyPatch(typeof(LevelCollectionTableView), nameof(LevelCollectionTableView.SetData))]
internal static class LevelCollectionTableSetDataPatch
{
    [HarmonyPriority(Priority.Low)]
    private static void Prefix(ref IReadOnlyList<BeatmapLevel> beatmapLevels)
    {
        var controller = HideOstController.Current;
        if (controller == null)
        {
            return;
        }

        // Same contract as Guard.Run, written out so the common no-change path allocates nothing
        // (no closure) on every song table refresh.
        try
        {
            beatmapLevels = controller.FilterTableLevels(beatmapLevels);
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"[{HideOstController.Feature}] {e}");
        }
    }
}
