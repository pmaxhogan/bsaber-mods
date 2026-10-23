using System;
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
/// LevelCollectionViewController.SetData(BeatmapLevel[] beatmapLevels, headerText, headerSprite,
/// sortLevels, sortBeatmapLevels, noDataInfoPrefab): the song table. In 1.40.8 both
/// LevelCollectionNavigationController.SetDataForPack and SetDataForLevelCollection call it with
/// pack.AllBeatmapLevels().ToArray(), so replacing the array never touches a pack object.
/// BetterSongList sorts/filters later, in LevelCollectionTableView.SetData, and sees our array.
/// </summary>
[HarmonyPatch(typeof(LevelCollectionViewController), nameof(LevelCollectionViewController.SetData))]
internal static class LevelCollectionSetDataPatch
{
    private static void Prefix(ref BeatmapLevel[] beatmapLevels)
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
