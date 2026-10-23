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
/// A last prefix on LevelCollectionTableView.SetData filters the song table itself, so packs
/// added by other mods (PlaylistManager playlists) also show only custom levels.
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
                typeof(LevelCollectionTableSetDataPatch));
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

    /// <summary>Enabled or Hidden changed; the button listens to refresh its icon/visibility.</summary>
    public event Action? StateChanged;

    /// <summary>Flips the persisted state and rebuilds the open song list in place.</summary>
    public void ToggleHidden() => SetHidden(!_config.Hidden);

    /// <summary>Used by the icon and the settings page; applies live if the solo list is open.</summary>
    public void SetHidden(bool hidden)
    {
        if (_config.Hidden == hidden)
        {
            return;
        }

        Apply(() => _config.Hidden = hidden);
        Plugin.Log.Info($"[{Feature}] official songs {(hidden ? "hidden" : "shown")}");
    }

    /// <summary>Used by the settings page and the quick tab; applies live if the solo list is open.</summary>
    public void SetEnabled(bool enabled)
    {
        if (_config.Enabled == enabled)
        {
            return;
        }

        Apply(() => _config.Enabled = enabled);
        Plugin.Log.Info($"[{Feature}] feature {(enabled ? "enabled" : "disabled")}");
    }

    /// <summary>
    /// Changes a setting, rebuilds the song list only if the effective hiding state flipped (so
    /// e.g. enabling the feature while Hidden is false redraws nothing), then notifies listeners.
    /// </summary>
    private void Apply(Action change)
    {
        var before = ShouldHide(_levelFiltering._enableCustomLevels);
        change();
        if (ShouldHide(_levelFiltering._enableCustomLevels) != before)
        {
            Rebuild();
        }

        Guard.Run(Feature, () => StateChanged?.Invoke());
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
    /// LevelCollectionTableView.SetData prefix: the song table itself, run after BetterSongList's
    /// prefix (which swaps in a playlist's own level list and applies its sort/filter). Catches every
    /// official level that reaches the table through a pack this feature does not own, above all
    /// PlaylistManager playlists in the Custom Songs tab. Only the level list is replaced; the pack
    /// objects, including PlaylistLevelPack instances, are never touched. Allocation-free when every
    /// level is already custom.
    /// </summary>
    internal IReadOnlyList<BeatmapLevel> FilterTableLevels(IReadOnlyList<BeatmapLevel> levels)
    {
        if (levels == null || levels.Count == 0 || !ShouldHide(_levelFiltering._enableCustomLevels))
        {
            return levels!;
        }

        return OfficialSongPolicy.KeepCustomLevels(levels, LevelId);
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
            var search = controller._levelSearchViewController;
            var savedFilter = search._currentSearchFilter;
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

            // On All/Favorites the redraw goes through LevelSearchViewController.ResetFilter, which
            // clears the search text. SongCore rebuilds synchronously, so re-apply the user's filter
            // (text, difficulty, etc.) straight away; it also restarts the async search refresh.
            if (gameTarget == current
                && (gameTarget == LevelCategory.All || gameTarget == LevelCategory.Favorites)
                && search.isInViewControllerHierarchy)
            {
                search.Refresh(in savedFilter);
            }
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
