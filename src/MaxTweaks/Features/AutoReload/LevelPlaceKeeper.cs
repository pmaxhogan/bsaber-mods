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
    private float _lastRestoreAt = -1f;

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
            // Window over. If the list was never rebuilt (hidden, or SongCore skipped the UI update),
            // nothing was lost.
            Cancel();
            return;
        }

        // Re-apply after every rebuild in the window, once it has been quiet: other mods may rebuild
        // the list again after SongsLoadedEvent (PlaylistManager refreshes its playlists then).
        if (_lastReloadAt < 0f || _lastReloadAt <= _lastRestoreAt || now - _lastReloadAt < QuietSeconds)
        {
            return;
        }

        _lastRestoreAt = now;
        try
        {
            Restore(_snapshot!);
        }
        catch
        {
            Cancel();
            throw;
        }
    }

    public void Cancel()
    {
        if (_subscribedTable != null)
        {
            _subscribedTable.didReloadDataEvent -= OnTableReloaded;
        }

        // Do not leave our pack preselection behind for an unrelated later list rebuild.
        if (_snapshot?.PackId != null && _filtering._levelPackIdToBeSelectedAfterPresent == _snapshot.PackId)
        {
            _filtering._levelPackIdToBeSelectedAfterPresent = null;
        }

        _subscribedTable = null;
        _snapshot = null;
        _restoreArmed = false;
        _lastRestoreAt = -1f;
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
            var found = false;
            for (var i = 0; i < levels.Count; i++)
            {
                if (levels[i].levelID != targetId)
                {
                    continue;
                }

                // Always fix the highlighted row: ReloadData keeps the selected row index, which is
                // wrong once a map was added or removed above it. Only fire the selection callback
                // when the level object changed (an updated map), so an unchanged song does not
                // restart its preview but the detail view never shows a stale level.
                var row = table._showLevelPackHeader ? i + 1 : i;
                table._selectedRow = row;
                tableView.SelectCellWithIdx(row, callbackTable: !ReferenceEquals(table._selectedBeatmapLevel, levels[i]));
                found = true;
                break;
            }

            if (!found)
            {
                // The selected map is gone: drop the stale selection so Play cannot start a deleted map.
                if (table._showLevelPackHeader)
                {
                    // Header row with callback: the detail area switches to the pack view.
                    table._selectedRow = 0;
                    tableView.SelectCellWithIdx(0, callbackTable: true);
                }
                else
                {
                    table.ClearSelection();
                }
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
