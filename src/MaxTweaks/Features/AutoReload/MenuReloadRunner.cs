using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MaxTweaks.Common;
using MaxTweaks.Common.Toasts;
using MaxTweaks.Configuration;
using MaxTweaks.Core.AutoReload;
using SongCore;
using SongCore.UI;
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
    private const float RootRetrySeconds = 10f;

    private readonly AutoReloadService _service;
    private readonly AutoReloadConfig _config;
    private readonly ToastService _toasts;
    private readonly LevelPlaceKeeper _place;
    private readonly ProgressBar? _progressBar;

    private float _nextTick;
    private float _nextRootCheck;
    private InFlight? _inFlight;
    private bool _progressBarHidden;

    public MenuReloadRunner(
        AutoReloadService service,
        AutoReloadConfig config,
        ToastService toasts,
        LevelPlaceKeeper place,
        [InjectOptional] ProgressBar? progressBar)
    {
        _service = service;
        _config = config;
        _toasts = toasts;
        _place = place;
        _progressBar = progressBar;
    }

    public void Initialize()
    {
        Guard.Run(Feature, () =>
        {
            // Back in the menu (or the menu was rebuilt): a gameplay session can no longer be running.
            _service.ExitGameplay();
            Loader.SongsLoadedEvent += OnSongsLoaded;
            _toasts.Prewarm();
            if (_config.Enabled)
            {
                _service.EnsureWatching(SongCoreBridge.GetRoots());
            }
        });
    }

    public void Dispose()
    {
        Guard.Run(Feature, () =>
        {
            Loader.SongsLoadedEvent -= OnSongsLoaded;
            ShowProgressBar();
            _place.Cancel();
            _inFlight = null;
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
        if (!_config.Enabled)
        {
            if (_service.IsWatching)
            {
                _service.StopWatching();
            }

            return;
        }

        if (!_service.IsWatching && Time.unscaledTime >= _nextRootCheck)
        {
            // Re-enabled in settings, or no root existed yet: retry now and then, not every tick.
            _nextRootCheck = Time.unscaledTime + RootRetrySeconds;
            _service.EnsureWatching(SongCoreBridge.GetRoots());
        }

        _place.Tick();

        if (_inFlight != null)
        {
            if (Time.unscaledTime - _inFlight.StartedAt > RefreshTimeoutSeconds)
            {
                Plugin.Log.Warn($"[{Feature}] Song refresh did not finish in {RefreshTimeoutSeconds} s; giving up on the summary");
                _inFlight = null;
                ShowProgressBar();
            }

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
            fullRefresh |= !TryForget(index[folder]);
        }

        foreach (var folder in removed)
        {
            // The incremental refresh drops missing folders too; doing it here also covers the case
            // where SongCore skips that step (it only runs when its save-data cache is non-empty).
            TryForget(index[folder]);
        }

        HideProgressBar();
        _inFlight = new InFlight(before, updated, added, Time.unscaledTime);
        Loader.Instance.RefreshSongs(fullRefresh);
        if (!Loader.AreSongsLoading && _inFlight != null)
        {
            // Refused (e.g. a scene transition started). Changed maps were dropped already, so they
            // count as new next time; retry with what we have.
            Plugin.Log.Warn($"[{Feature}] SongCore refused the refresh; retrying shortly");
            _inFlight = null;
            ShowProgressBar();
            _place.Cancel();
            _service.Aggregator.Requeue(new ChangeBatch(batch.Folders, rescan: true));
        }
    }

    private bool TryForget(string songCoreKey)
    {
        try
        {
            SongCoreBridge.Forget(songCoreKey);
            return true;
        }
        catch (Exception e) when (e is MissingMethodException || e is MethodAccessException || e is NullReferenceException)
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
            ShowProgressBar();
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

    /// <summary>
    /// "Reload quietly": SongCore's own loading banner would pop up for every small reload. Only hide
    /// it when it is not currently showing something (deactivating mid-message would freeze it on
    /// screen); it is re-enabled as soon as the refresh finishes, fails or the menu goes away.
    /// </summary>
    private void HideProgressBar()
    {
        if (_progressBar == null || _progressBarHidden || !_progressBar.gameObject.activeSelf)
        {
            return;
        }

        var canvas = _progressBar._canvas;
        if (canvas != null && canvas.enabled)
        {
            return;
        }

        _progressBar.gameObject.SetActive(false);
        _progressBarHidden = true;
    }

    private void ShowProgressBar()
    {
        if (!_progressBarHidden)
        {
            return;
        }

        _progressBarHidden = false;
        if (_progressBar != null)
        {
            _progressBar.gameObject.SetActive(true);
        }
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
