using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MaxTweaks.Core.AutoReload;

namespace MaxTweaks.Features.AutoReload;

/// <summary>
/// Watches one <see cref="WatchRoot"/> and reports changed map folders (as SongCore folder keys).
/// Uses <see cref="NativeDirectoryWatcher"/> (Win32 ReadDirectoryChangesW on its own thread). Falls
/// back to polling the root's top-level folder stamps only if the native watcher cannot start or
/// stops with an error. Callbacks run on background threads; consumers must only queue work.
/// </summary>
internal sealed class MapFolderWatcher : IDisposable
{
    private const int PollIntervalMs = 5000;

    private static readonly Func<string, bool> IsDirectory = Directory.Exists;

    private readonly Action<string> _onFolderChanged;
    private readonly Action<MapFolderWatcher> _onOverflow;
    private readonly object _lock = new();
    private NativeDirectoryWatcher? _watcher;
    private Timer? _pollTimer;
    private Dictionary<string, long>? _snapshot;
    private int _polling;
    private bool _disposed;

    public MapFolderWatcher(WatchRoot root, Action<string> onFolderChanged, Action<MapFolderWatcher> onOverflow)
    {
        Root = root;
        _onFolderChanged = onFolderChanged;
        _onOverflow = onOverflow;
    }

    public WatchRoot Root { get; }

    public string Mode { get; private set; } = "stopped";

    public void Start()
    {
        try
        {
            var watcher = new NativeDirectoryWatcher(Root.WatchPath, Report, () => _onOverflow(this), OnWatcherError);
            watcher.Start();
            lock (_lock)
            {
                _watcher = watcher;
            }

            Mode = "native";
            Plugin.Log.Info($"[AutoReload] Watching {Root} (backend native ReadDirectoryChangesW)");
        }
        catch (Exception e)
        {
            Plugin.Log.Warn($"[AutoReload] Native watcher unavailable for {Root}: {e.Message}; polling every {PollIntervalMs / 1000} s instead");
            StartPolling();
        }
    }

    /// <summary>
    /// Reports every map folder under the root with a file or folder time at or after
    /// <paramref name="sinceUtc"/>. Used after a watcher buffer overflow, when the events themselves
    /// were lost. Blocking; run it on a worker thread.
    /// </summary>
    public void ReportChangedSince(DateTime sinceUtc)
    {
        try
        {
            foreach (var directory in new DirectoryInfo(Root.WatchPath).GetDirectories())
            {
                if (IsIgnored(directory))
                {
                    continue;
                }

                if (LatestTime(directory) >= sinceUtc)
                {
                    _onFolderChanged(MapPaths.FolderKey(Root.SongCorePath, directory.Name));
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Warn($"[AutoReload] Rescan of {Root} failed: {e.Message}");
        }
    }

    public void Dispose()
    {
        NativeDirectoryWatcher? watcher;
        lock (_lock)
        {
            _disposed = true;
            watcher = _watcher;
            _watcher = null;
            _pollTimer?.Dispose();
            _pollTimer = null;
        }

        watcher?.Dispose();
        Mode = "stopped";
    }

    private static DateTime LatestTime(DirectoryInfo directory)
    {
        var latest = directory.CreationTimeUtc > directory.LastWriteTimeUtc ? directory.CreationTimeUtc : directory.LastWriteTimeUtc;
        foreach (var file in directory.GetFiles())
        {
            if (file.LastWriteTimeUtc > latest)
            {
                latest = file.LastWriteTimeUtc;
            }

            if (file.CreationTimeUtc > latest)
            {
                latest = file.CreationTimeUtc;
            }
        }

        return latest;
    }

    private static long Stamp(DirectoryInfo directory)
    {
        var stamp = FolderSnapshot.Empty;
        foreach (var file in directory.GetFiles())
        {
            stamp = FolderSnapshot.Mix(stamp, FolderSnapshot.NameHash(file.Name));
            stamp = FolderSnapshot.Mix(stamp, file.LastWriteTimeUtc.Ticks ^ file.Length);
            stamp = FolderSnapshot.Mix(stamp, file.CreationTimeUtc.Ticks);
        }

        return stamp;
    }

    private bool IsIgnored(DirectoryInfo directory)
    {
        if ((directory.Attributes & FileAttributes.Hidden) != 0)
        {
            return true;
        }

        foreach (var ignored in Root.IgnoredFolderNames)
        {
            if (MapPaths.Comparer.Equals(ignored, directory.Name))
            {
                return true;
            }
        }

        return false;
    }

    private void Report(string fullPath)
    {
        try
        {
            if (MapPaths.TryGetMapFolderName(Root.WatchPath, fullPath, Root.IgnoredFolderNames, IsDirectory, out var name))
            {
                _onFolderChanged(MapPaths.FolderKey(Root.SongCorePath, name));
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"[AutoReload] {e}");
        }
    }

    private void OnWatcherError(string message)
    {
        Plugin.Log.Warn($"[AutoReload] Watcher error on {Root}: {message}; switching to polling every {PollIntervalMs / 1000} s");
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            // Called on the watcher's own thread, which ends right after this; just drop it.
            _watcher = null;
        }

        // Folders may have changed while the watcher was failing.
        _onOverflow(this);
        StartPolling();
    }

    private void StartPolling()
    {
        lock (_lock)
        {
            if (_disposed || _pollTimer != null)
            {
                return;
            }

            Mode = "polling";
            _pollTimer = new Timer(_ => Poll(), null, 0, PollIntervalMs);
        }
    }

    private void Poll()
    {
        // Skip a tick rather than overlap when a scan of a big library takes longer than the interval.
        if (Interlocked.Exchange(ref _polling, 1) == 1)
        {
            return;
        }

        try
        {
            var current = new Dictionary<string, long>(MapPaths.Comparer);
            var root = new DirectoryInfo(Root.WatchPath);
            if (root.Exists)
            {
                foreach (var directory in root.GetDirectories())
                {
                    if (!IsIgnored(directory))
                    {
                        current[directory.Name] = Stamp(directory);
                    }
                }
            }

            var previous = _snapshot;
            _snapshot = current;
            if (previous == null)
            {
                return;
            }

            foreach (var name in FolderSnapshot.Diff(previous, current))
            {
                _onFolderChanged(MapPaths.FolderKey(Root.SongCorePath, name));
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Warn($"[AutoReload] Polling {Root} failed: {e.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }
}
