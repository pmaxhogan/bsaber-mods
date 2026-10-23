using System;
using System.Collections.Generic;
using System.Threading;
using MaxTweaks.Configuration;
using MaxTweaks.Core.AutoReload;

namespace MaxTweaks.Features.AutoReload;

/// <summary>
/// App lifetime: owns the folder watchers and the change aggregator, so changes keep being collected
/// across menu rebuilds (soft restart) and while a level is being played. The menu runner and the
/// gameplay notifier pull settled batches from here on the main thread; nothing in this class
/// touches Unity or SongCore.
/// </summary>
internal sealed class AutoReloadService : IDisposable
{
    /// <summary>How long a folder without Info.dat is waited for (since its last event) before it is dropped.</summary>
    public const double MaxInfoDatWaitSeconds = 120;

    private readonly AutoReloadConfig _config;
    private readonly List<MapFolderWatcher> _watchers = new();
    private DateTime _lastSettledUtc = DateTime.UtcNow;
    private ChangeBatch? _deferred;

    public AutoReloadService(AutoReloadConfig config)
    {
        _config = config;
        Aggregator = new ChangeAggregator(new StopwatchClock());
    }

    public ChangeAggregator Aggregator { get; }

    public PlayingMapTracker PlayingMap { get; } = new();

    public bool InGameplay { get; private set; }

    public bool IsWatching => _watchers.Count > 0;

    /// <summary>Configured quiet period, clamped to a sane range.</summary>
    public double DebounceSeconds
    {
        get
        {
            var value = _config.DebounceSeconds;
            return float.IsNaN(value) ? 2 : Math.Min(30, Math.Max(0.25, value));
        }
    }

    /// <summary>Starts watchers for roots not watched yet (roots can appear later, e.g. mod folders).</summary>
    public void EnsureWatching(IReadOnlyList<WatchRoot> roots)
    {
        foreach (var root in roots)
        {
            var known = false;
            foreach (var watcher in _watchers)
            {
                if (MapPaths.Comparer.Equals(watcher.Root.WatchPath, root.WatchPath))
                {
                    known = true;
                    break;
                }
            }

            if (known)
            {
                continue;
            }

            var created = new MapFolderWatcher(root, Aggregator.Record, OnOverflow);
            created.Start();
            _watchers.Add(created);
        }
    }

    public void StopWatching()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        if (_watchers.Count > 0)
        {
            Plugin.Log.Info("[AutoReload] Stopped watching map folders");
        }

        _watchers.Clear();
        Aggregator.Clear();
        _deferred = null;
    }

    public void EnterGameplay(string? playingFolderKey)
    {
        InGameplay = true;
        PlayingMap.Start(playingFolderKey);
    }

    public void ExitGameplay()
    {
        InGameplay = false;
        PlayingMap.Stop();
    }

    /// <summary>Keeps a batch settled during gameplay until the menu can reload.</summary>
    public void Defer(ChangeBatch batch) => _deferred = ChangeBatch.Merge(_deferred, batch);

    public ChangeBatch? TakeDeferred()
    {
        var batch = _deferred;
        _deferred = null;
        return batch;
    }

    /// <summary>Marks that everything up to now has been picked up (lower bound for overflow rescans).</summary>
    public void MarkSettled() => _lastSettledUtc = DateTime.UtcNow;

    public void Dispose() => StopWatching();

    private void OnOverflow(MapFolderWatcher watcher)
    {
        Aggregator.RecordRescan();

        // Events since the last settled batch may be lost: report folders touched since then (with a
        // margin for coarse file times). Blocking IO, so off the watcher's thread.
        var since = _lastSettledUtc.AddSeconds(-10);
        ThreadPool.QueueUserWorkItem(_ => watcher.ReportChangedSince(since));
    }
}
