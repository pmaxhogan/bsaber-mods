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
    private readonly Dictionary<string, string> _retiredLevels = new();
    private ChangeBatch? _deferred;
    private int _session;
    private DateTime? _stoppedAtUtc;

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
        // Re-enabled after being switched off: changes made meanwhile raised no events. Report every
        // map folder touched since the stop (with a margin for coarse file times) and force one
        // refresh so adds/removes are found too.
        var catchUpSince = _stoppedAtUtc?.AddSeconds(-5);
        if (catchUpSince != null && roots.Count > 0)
        {
            _stoppedAtUtc = null;
            Aggregator.RecordRescan();
        }

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
            if (catchUpSince != null)
            {
                var since = catchUpSince.Value;
                ThreadPool.QueueUserWorkItem(_ => created.ReportChangedSince(since));
            }
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
            _stoppedAtUtc = DateTime.UtcNow;
        }

        _watchers.Clear();
        Aggregator.Clear();
        _deferred = null;
    }

    /// <summary>Registers a play; returns its session number for <see cref="ExitGameplay"/>.</summary>
    public int EnterGameplay(string? playingFolderKey)
    {
        _session++;
        InGameplay = true;
        PlayingMap.Start(playingFolderKey);
        return _session;
    }

    /// <summary>
    /// Ends a play, but only if it is still the current one: when a level is restarted the new
    /// player container can be set up before the old one is disposed.
    /// </summary>
    public void ExitGameplay(int session)
    {
        if (session == _session)
        {
            ResetGameplay();
        }
    }

    /// <summary>No play can be running (the menu was just built).</summary>
    public void ResetGameplay()
    {
        InGameplay = false;
        PlayingMap.Stop();
    }

    /// <summary>
    /// The playing folder was found after the play started. Returns true if a change to it was
    /// already set aside during this play (so the caller still shows the toast).
    /// </summary>
    public bool SetPlayingFolder(int session, string folderKey)
    {
        if (session != _session)
        {
            return false;
        }

        PlayingMap.Start(folderKey);
        return PlayingMap.ShouldNotify(_deferred);
    }

    /// <summary>Remembers which folder a level ID came from before the map was reloaded under a new ID.</summary>
    public void RememberRetiredLevel(string levelId, string folderKey)
    {
        if (_retiredLevels.Count > 256)
        {
            _retiredLevels.Clear();
        }

        _retiredLevels[levelId] = folderKey;
    }

    /// <summary>Folder of a level ID retired by a reload (a stale level object can still be played).</summary>
    public string? FolderOfRetiredLevel(string? levelId) =>
        levelId != null && _retiredLevels.TryGetValue(levelId, out var folder) ? folder : null;

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
