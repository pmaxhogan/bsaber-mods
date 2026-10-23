using System;
using HMUI;
using UnityEngine;

namespace MaxTweaks.Features.AutoReload;

/// <summary>
/// Keeps the player's place in the song list across a reload: category/pack, selected song and
/// scroll position.
///
/// What a SongCore refresh does to the list (game 1.40.8, decompiled): RefreshLevelPacks ->
/// LevelFilteringNavigationController.UpdateCustomSongs (async) -> UpdateSecondChildControllerContent
/// -> ShowPacksInSecondChildController selects pack _levelPackIdToBeSelectedAfterPresent (else the
/// first) -> LevelCollectionTableView.SetData -> ReloadData + ScrollToCellWithIdx(0).
///
/// Pack: set _levelPackIdToBeSelectedAfterPresent before the refresh. BetterSongList's PackPreselect
/// prefix only fills that field when it is null, so ours wins and BSL keeps working.
/// Song + scroll: after the table reloaded (didReloadDataEvent) and has been quiet for a moment (BSL
/// scrolls in a SetData postfix; the All/Favorites search filters asynchronously), select the song
/// again (by folder, since an updated map gets a new level ID) and restore the exact scroll offset.
/// </summary>
internal sealed class LevelPlaceKeeper : IDisposable
{
    private const float QuietSeconds = 0.3f;
    private const float RestoreTimeoutSeconds = 6f;

    private readonly LevelFilteringNavigationController _filtering;
    private readonly LevelCollectionViewController _collection;

    private Snapshot? _snapshot;
    private TableView? _subscribedTable;
    private bool _restoreArmed;
    private float _armedAt;
    private float _lastReloadAt = -1f;

    public LevelPlaceKeeper(LevelFilteringNavigationController filtering, LevelCollectionViewController collection)
    {
        _filtering = filtering;
        _collection = collection;
    }

    /// <summary>
    /// True while the song list is part of the current flow but covered (results screen, practice
    /// setup, a dialog). SongCore only rebuilds the list UI when it is visible, so reloading now would
    /// leave the list and the selected song pointing at the old level objects (an updated map's old
    /// level ID is gone, so pressing Play would fail). Wait until the list is visible again.
    /// </summary>
    public bool ListCovered => _filtering.isInViewControllerHierarchy && !_filtering.isActiveAndEnabled;

    /// <summary>Remembers the current place; call right before starting the refresh.</summary>
    public void Capture()
    {
        Cancel();
        if (!_collection.isActiveAndEnabled)
        {
            return;
        }

        var table = _collection._levelCollectionTableView;
        var tableView = table._tableView;
        var selected = table._selectedBeatmapLevel;
        _snapshot = new Snapshot(
            _filtering.selectedLevelCategory,
            _filtering.selectedBeatmapLevelPack?.packID,
            selected?.levelID,
            SongCoreBridge.FolderOfLevel(selected?.levelID),
            tableView.scrollView.position);

        if (_snapshot.Category == SelectLevelCategoryViewController.LevelCategory.CustomSongs && _snapshot.PackId != null)
        {
            _filtering._levelPackIdToBeSelectedAfterPresent = _snapshot.PackId;
        }

        _subscribedTable = tableView;
        _lastReloadAt = -1f;
        tableView.didReloadDataEvent += OnTableReloaded;
    }

    /// <summary>The refresh finished; restore once the list has been rebuilt.</summary>
    public void ArmRestore()
    {
        if (_snapshot == null)
        {
            return;
        }

        _restoreArmed = true;
        _armedAt = Time.unscaledTime;
    }

    /// <summary>Call from the menu tick.</summary>
    public void Tick()
    {
        if (!_restoreArmed)
        {
            return;
        }

        var now = Time.unscaledTime;
        if (now - _armedAt > RestoreTimeoutSeconds)
        {
            // The list was not rebuilt (it was hidden, or SongCore skipped the UI update): nothing lost.
            Cancel();
            return;
        }

        if (_lastReloadAt < 0f || now - _lastReloadAt < QuietSeconds)
        {
            return;
        }

        try
        {
            Restore(_snapshot!);
        }
        finally
        {
            Cancel();
        }
    }

    public void Cancel()
    {
        if (_subscribedTable != null)
        {
            _subscribedTable.didReloadDataEvent -= OnTableReloaded;
        }

        _subscribedTable = null;
        _snapshot = null;
        _restoreArmed = false;
    }

    public void Dispose() => Cancel();

    private void OnTableReloaded(TableView tableView) => _lastReloadAt = Time.unscaledTime;

    private void Restore(Snapshot snapshot)
    {
        if (!_collection.isActiveAndEnabled)
        {
            return;
        }

        var table = _collection._levelCollectionTableView;
        var levels = table._beatmapLevels;
        var tableView = table._tableView;
        var targetId = snapshot.FolderKey != null
            ? SongCoreBridge.LevelInFolder(snapshot.FolderKey)?.levelID ?? snapshot.LevelId
            : snapshot.LevelId;

        if (targetId != null && levels != null)
        {
            for (var i = 0; i < levels.Count; i++)
            {
                if (levels[i].levelID != targetId)
                {
                    continue;
                }

                // Re-select even if the ID matches: after an update the old BeatmapLevel object may
                // still be shown in the detail view.
                if (!ReferenceEquals(table._selectedBeatmapLevel, levels[i]))
                {
                    tableView.SelectCellWithIdx(table._showLevelPackHeader ? i + 1 : i, callbackTable: true);
                }

                break;
            }
        }

        tableView.ScrollToPosition(snapshot.ScrollPosition, animated: false);
    }

    private sealed class Snapshot
    {
        public Snapshot(SelectLevelCategoryViewController.LevelCategory category, string? packId, string? levelId, string? folderKey, float scrollPosition)
        {
            Category = category;
            PackId = packId;
            LevelId = levelId;
            FolderKey = folderKey;
            ScrollPosition = scrollPosition;
        }

        public SelectLevelCategoryViewController.LevelCategory Category { get; }

        public string? PackId { get; }

        public string? LevelId { get; }

        public string? FolderKey { get; }

        public float ScrollPosition { get; }
    }
}
