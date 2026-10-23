using System;
using System.Collections.Generic;
using HarmonyLib;
using MaxTweaks.Common;
using MaxTweaks.Configuration;
using MaxTweaks.Core.HideOst;
using Zenject;
using LevelCategory = SelectLevelCategoryViewController.LevelCategory;

namespace MaxTweaks.Features.HideOst;

/// <summary>
/// Hides every official song (base OST, Extras, all DLC music packs) from the solo song list.
///
/// How it hooks Beat Saber 1.40.8 (see HideOstPatches.cs): LevelFilteringNavigationController
/// builds three pack arrays in SetupBeatmapLevelPacks (_ostBeatmapLevelPacks,
/// _musicPacksBeatmapLevelPacks, _allOfficialBeatmapLevelPacks) and the category tab list
/// (_enabledLevelCategories). A postfix empties the official arrays and drops the Music Packs tab.
/// Everything downstream is derived from those fields: SongCore 3.15.3 replaces UpdateCustomSongs
/// with a prefix that concatenates _ostBeatmapLevelPacks + _musicPacksBeatmapLevelPacks +
/// _customLevelPacks into _allBeatmapLevelPacks and hands that to the search view (which serves the
/// All and Favorites tabs and text search), so they become custom-only without touching SongCore.
/// </summary>
internal class HideOstController : IInitializable, IDisposable
{
    internal const string Feature = "HideOst";

    // Harmony patches are global and outlive the menu container (rebuilt on every soft restart), so
    // they are applied once per process and route through Current, which is null between menus.
    private static bool _patchAttempted;
    private static Harmony? _harmony;

    private readonly HideOstConfig _config;
    private readonly SafePatcher _patcher;
    private readonly FeatureStatus _status;
    private readonly SoloFreePlayFlowCoordinator _soloFlow;
    private readonly LevelFilteringNavigationController _levelFiltering;

    public HideOstController(
        HideOstConfig config,
        SafePatcher patcher,
        FeatureStatus status,
        SoloFreePlayFlowCoordinator soloFlow,
        LevelFilteringNavigationController levelFiltering)
    {
        _config = config;
        _patcher = patcher;
        _status = status;
        _soloFlow = soloFlow;
        _levelFiltering = levelFiltering;
    }

    internal static HideOstController? Current { get; private set; }

    /// <summary>Patches are in place and the feature has not disabled itself.</summary>
    public bool Available => _harmony != null && !_status.IsFailed(Feature);

    /// <summary>The toggle button belongs in the song list only while the solo flow is open.</summary>
    public bool ButtonVisible => Available && _config.Enabled && _soloFlow.isActivated;

    public bool Hidden => _config.Hidden;

    public void Initialize()
    {
        if (!_patchAttempted)
        {
            _patchAttempted = true;
            _harmony = _patcher.TryPatch(
                Feature,
                typeof(LevelFilteringSetupPatch),
                typeof(SetupBeatmapLevelPacksPatch),
                typeof(LevelSearchSetupPatch));
        }

        Current = this;
    }

    public void Dispose()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    /// <summary>Flips the persisted state and rebuilds the open song list in place.</summary>
    public void ToggleHidden()
    {
        _config.Hidden = !_config.Hidden;
        Plugin.Log.Info($"[{Feature}] official songs {(_config.Hidden ? "hidden" : "shown")}");
        Rebuild();
    }

    private bool ShouldHide(bool customLevelsEnabled) =>
        Available && OfficialSongPolicy.ShouldHide(_config.Enabled, _config.Hidden, _soloFlow.isActivated, customLevelsEnabled);

    /// <summary>Setup prefix: never start on the Music Packs tab when it is about to be removed.</summary>
    internal LevelCategory CoerceStartCategory(LevelCategory requested, bool customLevelsEnabled)
    {
        if (!ShouldHide(customLevelsEnabled))
        {
            return requested;
        }

        return ToGame(OfficialSongPolicy.CoerceStartCategory(ToCore(requested)));
    }

    /// <summary>SetupBeatmapLevelPacks postfix: strip official packs and the Music Packs tab.</summary>
    internal void FilterPacks(LevelFilteringNavigationController controller)
    {
        if (!ShouldHide(controller._enableCustomLevels))
        {
            return;
        }

        var empty = Array.Empty<BeatmapLevelPack>();
        controller._ostBeatmapLevelPacks = empty;
        controller._musicPacksBeatmapLevelPacks = empty;
        controller._allOfficialBeatmapLevelPacks = empty;
        controller._enabledLevelCategories = ToGame(OfficialSongPolicy.FilterCategories(ToCore(controller._enabledLevelCategories)));
    }

    /// <summary>
    /// LevelSearchViewController.Setup prefix: belt and braces for All, Favorites and text search.
    /// After FilterPacks the input normally holds only custom packs and is returned as is (no
    /// allocation); a pack that still mixes in official levels is replaced by a custom-only copy.
    /// </summary>
    internal BeatmapLevelPack[] FilterSearchPacks(BeatmapLevelPack[] packs)
    {
        if (packs == null || !ShouldHide(_levelFiltering._enableCustomLevels))
        {
            return packs!;
        }

        List<BeatmapLevelPack>? result = null;
        for (var i = 0; i < packs.Length; i++)
        {
            var pack = packs[i];
            var levels = pack.AllBeatmapLevels();
            var verdict = OfficialSongPolicy.ClassifyPack(levels, LevelId);
            if (verdict == PackVerdict.KeepAsIs && result == null)
            {
                continue;
            }

            if (result == null)
            {
                result = new List<BeatmapLevelPack>(packs.Length);
                for (var j = 0; j < i; j++)
                {
                    result.Add(packs[j]);
                }
            }

            switch (verdict)
            {
                case PackVerdict.KeepAsIs:
                    result.Add(pack);
                    break;
                case PackVerdict.KeepCustomOnly:
                    // Same packID so the search filter's song-pack mask still matches it.
                    result.Add(new BeatmapLevelPack(
                        pack.packID,
                        pack.packName,
                        pack.shortPackName,
                        pack.coverImage,
                        pack.smallCoverImage,
                        pack.packBuyOption,
                        OfficialSongPolicy.KeepCustom(levels, LevelId),
                        pack.contentRating));
                    break;
            }
        }

        return result == null ? packs : result.ToArray();
    }

    private static readonly Func<BeatmapLevel, string?> LevelId = level => level?.levelID;

    /// <summary>
    /// Re-runs the game's own pack setup (our postfix re-applies the current state), re-seeds the
    /// category tabs and redraws the second panel. SongCore's UpdateCustomSongs prefix rebuilds the
    /// search packs synchronously when the panel asks for them.
    /// </summary>
    private void Rebuild()
    {
        Guard.Run(Feature, () =>
        {
            var controller = _levelFiltering;
            if (!_soloFlow.isActivated || !controller.isInViewControllerHierarchy || controller._enabledLevelCategories == null)
            {
                return;
            }

            var current = controller.selectedLevelCategory;
            var keepPackId = current == LevelCategory.CustomSongs ? controller.selectedBeatmapLevelPack?.packID : null;

            controller.SetupBeatmapLevelPacks();

            var available = ToCore(controller._enabledLevelCategories);
            var target = OfficialSongPolicy.CoerceCategory(ToCore(current), available);
            if (target == SongCategory.None && available.Length > 0)
            {
                target = available[0];
            }

            var gameTarget = ToGame(target);
            if (gameTarget == LevelCategory.CustomSongs && keepPackId != null)
            {
                controller._levelPackIdToBeSelectedAfterPresent = keepPackId;
            }

            controller._selectLevelCategoryViewController.Setup(gameTarget, controller._enabledLevelCategories);
            controller.UpdateSecondChildControllerContent(gameTarget);
        });
    }

    private static SongCategory ToCore(LevelCategory category) => category switch
    {
        LevelCategory.MusicPacks => SongCategory.MusicPacks,
        LevelCategory.CustomSongs => SongCategory.CustomSongs,
        LevelCategory.Favorites => SongCategory.Favorites,
        LevelCategory.All => SongCategory.All,
        _ => SongCategory.None,
    };

    private static LevelCategory ToGame(SongCategory category) => category switch
    {
        SongCategory.MusicPacks => LevelCategory.MusicPacks,
        SongCategory.CustomSongs => LevelCategory.CustomSongs,
        SongCategory.Favorites => LevelCategory.Favorites,
        SongCategory.All => LevelCategory.All,
        _ => LevelCategory.None,
    };

    private static SongCategory[] ToCore(LevelCategory[] categories)
    {
        var result = new SongCategory[categories.Length];
        for (var i = 0; i < categories.Length; i++)
        {
            result[i] = ToCore(categories[i]);
        }

        return result;
    }

    private static LevelCategory[] ToGame(SongCategory[] categories)
    {
        var result = new LevelCategory[categories.Length];
        for (var i = 0; i < categories.Length; i++)
        {
            result[i] = ToGame(categories[i]);
        }

        return result;
    }
}
