using System;
using System.Collections.Generic;

namespace MaxTweaks.Core.AutoReload;

/// <summary>
/// Remembers map folders MaxTweaks itself just wrote a sidecar file into (active-pause flags).
/// Windows reports any entry change inside a map folder (a file created, replaced or deleted) as a
/// change to the folder itself, which the watcher cannot tell apart from an editor saving the map;
/// such folder-only events are dropped for <see cref="WindowSeconds"/> after a mark. Events for the
/// map's own files still count. Thread-safe (the watcher reports from its own thread).
/// </summary>
public sealed class SidecarWriteGuard
{
    public const double WindowSeconds = 5.0;

    private readonly IClock _clock;
    private readonly Dictionary<string, double> _marks = new(MapPaths.Comparer);
    private readonly object _lock = new();

    public SidecarWriteGuard(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Call right before writing into <paramref name="folder"/> (and again right after).</summary>
    public void Mark(string folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        lock (_lock)
        {
            _marks[MapPaths.Normalize(folder)] = _clock.Now;
        }
    }

    /// <summary>True when <paramref name="folder"/> was marked less than <see cref="WindowSeconds"/> ago.</summary>
    public bool IsSuppressed(string folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return false;
        }

        lock (_lock)
        {
            var key = MapPaths.Normalize(folder);
            if (!_marks.TryGetValue(key, out var at))
            {
                return false;
            }

            if (_clock.Now - at < WindowSeconds)
            {
                return true;
            }

            _marks.Remove(key);
            return false;
        }
    }
}
