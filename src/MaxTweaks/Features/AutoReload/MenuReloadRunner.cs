using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MaxTweaks.Common;
using MaxTweaks.Common.Toasts;
using MaxTweaks.Configuration;
using MaxTweaks.Core.AutoReload;
using SongCore;
using UnityEngine;
using Zenject;

namespace MaxTweaks.Features.AutoReload;

/// <summary>
/// Menu scene: turns settled change batches into one quiet incremental SongCore refresh, keeps the
/// list position, then toasts a summary. Only ticks while the menu scene is active (its scene
/// context is disabled during gameplay), and also refuses while a gameplay session is registered.
/// </summary>
internal sealed class MenuReloadRunner : IInitializable, ITickable, IDisposable
{
    private const string Feature = "AutoReload";
    private const float TickInterval = 0.25f;
    private const float RefreshTimeoutSeconds = 180f;
    private const float RootCheckSeconds = 10f;

    private readonly AutoReloadService _service;
    private readonly AutoReloadConfig _config;
    private readonly IToastService _toasts;
    private readonly LevelPlaceKeeper _place;
    private readonly SongCoreHooks _hooks;

    private float _nextTick;
    private float _nextRootCheck;
    private InFlight? _inFlight;
    private bool _unsilenceOnNextTick;

    public MenuReloadRunner(
        AutoReloadService service,
        AutoReloadConfig config,
        IToastService toasts,
        LevelPlaceKeeper place,
        SongCoreHooks hooks)
    {
        _service = service;
        _config = config;
        _toasts = toasts;
        _place = place;
        _hooks = hooks;
    }

    public void Initialize()
    {
        Guard.Run(Feature, () =>
        {
            // Back in the menu (or the menu was rebuilt): a gameplay session can no longer be running.
            _service.ResetGameplay();
            Loader.SongsLoadedEvent += OnSongsLoaded;
            _toasts.Prewarm();
            if (_config.Enabled)
            {
                _nextRootCheck = Time.unscaledTime + RootCheckSeconds;
                _service.EnsureWatching(SongCoreBridge.GetRoots());
            }
        });
    }

    public void Dispose()
    {
        Guard.Run(Feature, () =>
        {
            Loader.SongsLoadedEvent -= OnSongsLoaded;
            _hooks.SilenceProgressBar = false;
            _place.Cancel();
            var staleIds = _hooks.TakeStaleIds();
            if (staleIds.Count > 0)
            {
                SongCoreBridge.SettleStaleIds(staleIds);
            }

            _inFlight = null;
            _unsilenceOnNextTick = false;
        });
    }

    public void Tick()
    {
        var now = Time.unscaledTime;
        if (now < _nextTick)
        {
            return;
        }

        _nextTick = now + TickInterval;
        Guard.Run(Feature, TickCore);
    }

    private void TickCore()
    {
        if (_unsilenceOnNextTick)
        {
            // Not in OnSongsLoaded itself: SongCore's banner handler for the same event may run after
            // ours, and its RefreshLevelPacks continuation (which copies the new save data into the
            // game's loader) may also still be queued on the main thread.
            _unsilenceOnNextTick = false;
            _hooks.SilenceProgressBar = false;
            SongCoreBridge.SettleStaleIds(_hooks.TakeStaleIds());
        }

        if (!_config.Enabled)
        {
            if (_service.IsWatching)
            {
                _service.StopWatching();
            }

            return;
        }

        if (Time.unscaledTime >= _nextRootCheck)
        {
            // Picks up a re-enable, a root that did not exist yet, or a SongCore folder added later.
            _nextRootCheck = Time.unscaledTime + RootCheckSeconds;
            _service.EnsureWatching(SongCoreBridge.GetRoots());
        }

        _place.Tick();

        if (_inFlight != null)
        {
            CheckInFlight(_inFlight);
            return;
        }

        // The batch stays in the aggregator until SongCore is idle (first menu load, user refresh...).
        if (_service.InGameplay || !SongCoreBridge.IsIdle || _place.ListCovered)
        {
            return;
        }

        var batch = ChangeBatch.Merge(_service.TakeDeferred(), _service.Aggregator.TryFlush(_service.DebounceSeconds));
        if (batch != null && !batch.IsEmpty)
        {
            Process(batch);
        }
    }

    private void CheckInFlight(InFlight inFlight)
    {
        if (!Loader.AreSongsLoading && !Loader.AreSongsLoaded && _hooks.SilenceProgressBar)
        {
            // Cancelled by a scene transition. SongCore resumes the refresh itself when the menu is
            // back; let that one show its banner. The summary still comes from its SongsLoadedEvent.
            _hooks.SilenceProgressBar = false;
        }

        if (Time.unscaledTime - inFlight.StartedAt > RefreshTimeoutSeconds)
        {
            Plugin.Log.Warn($"[{Feature}] Song refresh did not finish in {RefreshTimeoutSeconds} s; giving up on the summary");
            _inFlight = null;
            _hooks.SilenceProgressBar = false;
            _place.Cancel();
            SongCoreBridge.SettleStaleIds(_hooks.TakeStaleIds());
        }
    }

    private void Process(ChangeBatch batch)
    {
        _service.MarkSettled();
        var index = SongCoreBridge.LoadedIndex();
        var updated = new List<string>();
        var removed = new List<string>();
        var added = new List<string>();
        var recheck = Math.Max(1.0, _service.DebounceSeconds);

        foreach (var folder in batch.Folders)
        {
            var kind = MapChangeClassifier.Classify(SongCoreBridge.Probe(folder), index.ContainsKey(folder));
            if (kind == MapChangeKind.AwaitingInfoDat)
            {
                if (!_service.Aggregator.Defer(folder, recheck, AutoReloadService.MaxInfoDatWaitSeconds))
                {
                    Plugin.Log.Info($"[{Feature}] Gave up waiting for Info.dat in {folder}");
                }

                continue;
            }

            _service.Aggregator.Resolve(folder);
            switch (kind)
            {
                case MapChangeKind.Added:
                    added.Add(folder);
                    break;
                case MapChangeKind.Updated:
                    updated.Add(folder);
                    break;
                case MapChangeKind.Removed:
                    removed.Add(folder);
                    break;
            }
        }

        if (added.Count == 0 && updated.Count == 0 && removed.Count == 0 && !batch.Rescan)
        {
            return;
        }

        Plugin.Log.Info($"[{Feature}] Reloading: {added.Count} new, {updated.Count} changed, {removed.Count} gone{(batch.Rescan ? ", rescan" : string.Empty)}");
        var before = new List<string>(index.Keys);
        try
        {
            _place.Capture();
        }
        catch (Exception e)
        {
            // Keeping the place is a nicety: never let a UI hiccup cancel the reload (the batch is
            // already taken from the aggregator).
            Plugin.Log.Warn($"[{Feature}] Could not remember the list position: {e}");
            _place.Cancel();
        }

        var fullRefresh = false;
        foreach (var folder in updated)
        {
            fullRefresh |= !TryForget(index[folder], keepOldSaveData: _hooks.Available);
        }

        foreach (var folder in removed)
        {
            // The incremental refresh drops missing folders too; doing it here also covers the case
            // where SongCore skips that step (it only runs when its save-data cache is non-empty).
            TryForget(index[folder], keepOldSaveData: false);
        }

        _hooks.SilenceProgressBar = true;
        _inFlight = new InFlight(before, updated, added, Time.unscaledTime);
        Loader.Instance.RefreshSongs(fullRefresh);
        if (!Loader.AreSongsLoading && _inFlight != null)
        {
            // Refused (e.g. a scene transition started). Changed maps were dropped already, so they
            // count as new next time; retry with what we have.
            Plugin.Log.Warn($"[{Feature}] SongCore refused the refresh; retrying shortly");
            _inFlight = null;
            _hooks.SilenceProgressBar = false;
            _place.Cancel();
            SongCoreBridge.SettleStaleIds(_hooks.TakeStaleIds());
            _service.Aggregator.Requeue(new ChangeBatch(batch.Folders, rescan: true));
        }
    }

    private bool TryForget(string songCoreKey, bool keepOldSaveData)
    {
        try
        {
            var folderKey = SongCoreBridge.NormalizeKey(songCoreKey);
            var oldId = SongCoreBridge.LevelInFolder(folderKey)?.levelID;
            if (oldId != null)
            {
                _service.RememberRetiredLevel(oldId, folderKey);
            }

            var staleId = SongCoreBridge.Forget(songCoreKey, keepOldSaveData);
            if (staleId != null)
            {
                _hooks.AddStaleId(staleId);
            }

            return true;
        }
        catch (Exception e) when (e is MissingMethodException || e is MissingFieldException || e is MethodAccessException || e is NullReferenceException)
        {
            // SongCore changed: fall back to a full refresh, which reloads every map.
            Plugin.Log.Warn($"[{Feature}] Could not drop {songCoreKey} from SongCore ({e.Message}); using a full refresh");
            return false;
        }
    }

    private void OnSongsLoaded(Loader loader, ConcurrentDictionary<string, BeatmapLevel> levels)
    {
        Guard.Run(Feature, () =>
        {
            var inFlight = _inFlight;
            if (inFlight == null)
            {
                return;
            }

            _inFlight = null;
            _unsilenceOnNextTick = true;
            _place.ArmRestore();

            var summary = ReloadSummary.Compute(inFlight.Before, SongCoreBridge.LoadedIndex().Keys, inFlight.Updated, inFlight.Added);
            Plugin.Log.Info($"[{Feature}] Reload done: {summary}");
            var text = summary.ToToastText();
            if (text != null && _config.ShowToasts)
            {
                _toasts.Show(text);
            }
        });
    }

    private sealed class InFlight
    {
        public InFlight(List<string> before, List<string> updated, List<string> added, float startedAt)
        {
            Before = before;
            Updated = updated;
            Added = added;
            StartedAt = startedAt;
        }

        public List<string> Before { get; }

        public List<string> Updated { get; }

        public List<string> Added { get; }

        public float StartedAt { get; }
    }
}
