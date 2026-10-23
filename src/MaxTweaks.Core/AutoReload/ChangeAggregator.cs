using System.Collections.Generic;
using System.Linq;

namespace MaxTweaks.Core.AutoReload;

/// <summary>
/// Collects map-folder change events and releases them as one batch once the disk has been quiet for
/// the debounce period. Thread-safe: <see cref="Record"/> is called from file watcher threads, the
/// rest from the game's main thread.
///
/// Folders that changed but have no Info.dat yet (a copy or extraction still in progress) are parked
/// with <see cref="Defer"/>: they come back in a later batch for another look, and are dropped once
/// they have waited longer than the caller's limit without any new event. A new event for a parked
/// folder moves it back to the normal debounce and restarts its wait limit.
/// </summary>
public sealed class ChangeAggregator
{
    private readonly object _lock = new();
    private readonly IClock _clock;
    private readonly HashSet<string> _pending = new(MapPaths.Comparer);
    private readonly Dictionary<string, double> _waitingSince = new(MapPaths.Comparer);
    private readonly Dictionary<string, double> _recheckAt = new(MapPaths.Comparer);
    private bool _rescan;
    private double _lastEventAt;

    public ChangeAggregator(IClock clock)
    {
        _clock = clock;
    }

    /// <summary>True while anything is pending or parked (cheap; safe to poll every tick).</summary>
    public bool HasWork
    {
        get
        {
            lock (_lock)
            {
                return _pending.Count > 0 || _rescan || _recheckAt.Count > 0;
            }
        }
    }

    /// <summary>A file or folder event for this map folder (normalized key).</summary>
    public void Record(string folderKey)
    {
        lock (_lock)
        {
            _pending.Add(folderKey);
            _recheckAt.Remove(folderKey);

            // New activity means a copy is still in progress: the wait limit restarts from here.
            _waitingSince.Remove(folderKey);
            _lastEventAt = _clock.Now;
        }
    }

    /// <summary>Events were lost; the next batch must trigger a library refresh.</summary>
    public void RecordRescan()
    {
        lock (_lock)
        {
            _rescan = true;
            _lastEventAt = _clock.Now;
        }
    }

    /// <summary>
    /// Returns the settled batch, or null if nothing is ready. Pending events are released only after
    /// <paramref name="debounceSeconds"/> without any new event; parked folders are released when
    /// their recheck time has come, independent of new events for other folders.
    /// </summary>
    public ChangeBatch? TryFlush(double debounceSeconds)
    {
        lock (_lock)
        {
            var now = _clock.Now;
            var settled = (_pending.Count > 0 || _rescan) && now - _lastEventAt >= debounceSeconds;
            List<string>? due = null;
            foreach (var entry in _recheckAt)
            {
                if (entry.Value <= now)
                {
                    (due ??= new List<string>()).Add(entry.Key);
                }
            }

            if (!settled && due == null)
            {
                return null;
            }

            var folders = new List<string>();
            var rescan = false;
            if (settled)
            {
                folders.AddRange(_pending);
                rescan = _rescan;
                _pending.Clear();
                _rescan = false;
            }

            if (due != null)
            {
                foreach (var key in due)
                {
                    _recheckAt.Remove(key);
                    folders.Add(key);
                }
            }

            return new ChangeBatch(folders, rescan);
        }
    }

    /// <summary>
    /// Puts a batch back unchanged (e.g. the song loader was busy). It is released again on the next
    /// <see cref="TryFlush"/> unless new events keep arriving.
    /// </summary>
    public void Requeue(ChangeBatch batch)
    {
        lock (_lock)
        {
            foreach (var folder in batch.Folders)
            {
                _pending.Add(folder);
                _recheckAt.Remove(folder);
            }

            _rescan |= batch.Rescan;
        }
    }

    /// <summary>
    /// Parks a folder that is not ready yet (no Info.dat). It is offered again after
    /// <paramref name="recheckSeconds"/>. Returns false, and forgets the folder, once it has been
    /// waiting for at least <paramref name="maxWaitSeconds"/> in total.
    /// </summary>
    public bool Defer(string folderKey, double recheckSeconds, double maxWaitSeconds)
    {
        lock (_lock)
        {
            var now = _clock.Now;
            if (!_waitingSince.TryGetValue(folderKey, out var since))
            {
                since = now;
                _waitingSince[folderKey] = now;
            }

            if (now - since >= maxWaitSeconds)
            {
                _waitingSince.Remove(folderKey);
                _recheckAt.Remove(folderKey);
                return false;
            }

            if (!_pending.Contains(folderKey))
            {
                _recheckAt[folderKey] = now + recheckSeconds;
            }

            return true;
        }
    }

    /// <summary>The folder was handled (or vanished); stop tracking how long it waited.</summary>
    public void Resolve(string folderKey)
    {
        lock (_lock)
        {
            _waitingSince.Remove(folderKey);
            _recheckAt.Remove(folderKey);
        }
    }

    /// <summary>Folders currently parked waiting for Info.dat (for diagnostics and tests).</summary>
    public IReadOnlyList<string> Waiting
    {
        get
        {
            lock (_lock)
            {
                return _waitingSince.Keys.ToList();
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _pending.Clear();
            _waitingSince.Clear();
            _recheckAt.Clear();
            _rescan = false;
        }
    }
}
