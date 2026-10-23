using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using MaxTweaks.Core.AutoReload;
using SongCore;
using SongCore.Data;
using UnityEngine;

namespace MaxTweaks.Features.AutoReload;

/// <summary>
/// Everything that reads or changes SongCore 3.15.3 state (verified against the decompiled
/// Plugins/SongCore.dll). Main thread only for the mutating calls.
///
/// Key facts:
/// - Loaded maps live in Loader.CustomLevels / CustomWIPLevels and each SeparateSongFolder.Levels,
///   keyed by the folder path SongCore scanned (junction path, not resolved).
/// - RefreshSongs(false) only loads folders whose key is NOT in those dictionaries, so an edited map
///   is skipped; Loader.DeleteSingleSong(path, deleteFolder: false) (private) drops one entry with its
///   hash/ID bookkeeping so the next incremental refresh loads it again. Folders missing on disk are
///   removed by the incremental refresh itself.
/// </summary>
internal static class SongCoreBridge
{
    public const string InfoDat = "Info.dat";

    public static bool IsIdle => Loader.Instance != null && Loader.AreSongsLoaded && !Loader.AreSongsLoading;

    /// <summary>The roots SongCore scans: CustomLevels, CustomWIPLevels and its extra folders.</summary>
    public static List<WatchRoot> GetRoots()
    {
        var roots = new List<WatchRoot>();
        AddRoot(roots, "CustomLevels", CustomLevelPathHelper.customLevelsDirectoryPath, Array.Empty<string>());

        // SongCore extracts zips into CustomWIPLevels\Cache on a full refresh: not ours to watch.
        AddRoot(roots, "CustomWIPLevels", Path.Combine(Application.dataPath, "CustomWIPLevels"), new[] { "Cache" });

        var cacheFolders = new HashSet<SeparateSongFolder>();
        foreach (var folder in Loader.SeparateSongFolders)
        {
            if (folder.CacheFolder != null)
            {
                cacheFolders.Add(folder.CacheFolder);
            }
        }

        foreach (var folder in Loader.SeparateSongFolders)
        {
            var entry = folder.SongFolderEntry;
            if (entry == null || string.IsNullOrEmpty(entry.Path) || cacheFolders.Contains(folder))
            {
                continue;
            }

            AddRoot(roots, entry.Name ?? "Separate folder", entry.Path, folder.CacheFolder != null ? new[] { "Cache" } : Array.Empty<string>());
        }

        return roots;
    }

    /// <summary>Normalized folder key to SongCore's own dictionary key, for every loaded map.</summary>
    public static Dictionary<string, string> LoadedIndex()
    {
        var index = new Dictionary<string, string>(MapPaths.Comparer);
        AddKeys(index, Loader.CustomLevels);
        AddKeys(index, Loader.CustomWIPLevels);
        foreach (var folder in Loader.SeparateSongFolders)
        {
            AddKeys(index, folder.Levels);
        }

        return index;
    }

    public static MapFolderState Probe(string folderKey)
    {
        try
        {
            var directory = new DirectoryInfo(folderKey);
            if (!directory.Exists || (directory.Attributes & FileAttributes.Hidden) != 0)
            {
                return MapFolderState.Missing;
            }

            return File.Exists(Path.Combine(folderKey, InfoDat)) ? MapFolderState.Ready : MapFolderState.NoInfoDat;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return MapFolderState.NoInfoDat;
        }
    }

    /// <summary>
    /// Drops a loaded map so the next incremental refresh loads it from disk again. With
    /// <paramref name="keepOldSaveData"/>, the old level's save data and song data stay registered
    /// under its old ID until <see cref="SettleStaleIds"/>, because the song list keeps showing the old
    /// level until SongCore rebuilds it, and selecting it would otherwise throw inside SongCore.
    /// Returns the old level ID when its data was kept, else null.
    /// </summary>
    public static string? Forget(string songCoreKey, bool keepOldSaveData)
    {
        var oldId = keepOldSaveData ? LevelInFolder(NormalizeKey(songCoreKey))?.levelID : null;
        CustomLevelLoader.LoadedSaveData? saved = null;
        SongData? songData = null;
        var saveData = Loader.CustomLevelLoader._loadedBeatmapSaveData;
        if (oldId != null && saveData.TryGetValue(oldId, out var existing))
        {
            saved = existing;
            Collections.CustomSongsData.TryGetValue(oldId, out songData);
        }

        Loader.Instance.DeleteSingleSong(songCoreKey, deleteFolder: false);
        if (oldId == null || saved == null)
        {
            return null;
        }

        saveData[oldId] = saved.Value;
        if (songData != null)
        {
            Collections.CustomSongsData.TryAdd(oldId, songData);
        }

        return oldId;
    }

    /// <summary>
    /// After the refresh: an old ID that was reloaded under the same ID gets fresh SongCore song data
    /// (SongCore only TryAdds it, so the old data would stick); an ID that no longer exists is removed.
    /// </summary>
    public static void SettleStaleIds(IEnumerable<string> oldIds)
    {
        var saveData = Loader.CustomLevelLoader._loadedBeatmapSaveData;
        foreach (var id in oldIds)
        {
            if (Loader.GetLevelById(id) != null && saveData.TryGetValue(id, out var current))
            {
                var songData = new SongData();
                songData.PopulateFromLoadedSaveData(current);
                Collections.CustomSongsData[id] = songData;
            }
            else
            {
                saveData.Remove(id);
                Collections.CustomSongsData.TryRemove(id, out _);
            }
        }
    }

    /// <summary>Normalized folder key of a loaded custom level, or null (OST/DLC or unknown).</summary>
    public static string? FolderOfLevel(string? levelId)
    {
        if (levelId == null || Loader.CustomLevelLoader == null)
        {
            return null;
        }

        return Loader.CustomLevelLoader._loadedBeatmapSaveData.TryGetValue(levelId, out var data)
            ? NormalizeKey(data.customLevelFolderInfo.folderPath)
            : null;
    }

    /// <summary>The level currently loaded from a folder (its ID changes when the map's hash changes).</summary>
    public static BeatmapLevel? LevelInFolder(string folderKey)
    {
        return Find(Loader.CustomLevels, folderKey) ?? Find(Loader.CustomWIPLevels, folderKey) ?? FindInSeparateFolders(folderKey);
    }

    public static string NormalizeKey(string path)
    {
        try
        {
            return MapPaths.Normalize(Path.GetFullPath(path));
        }
        catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
        {
            return MapPaths.Normalize(path);
        }
    }

    private static BeatmapLevel? FindInSeparateFolders(string folderKey)
    {
        foreach (var folder in Loader.SeparateSongFolders)
        {
            var level = Find(folder.Levels, folderKey);
            if (level != null)
            {
                return level;
            }
        }

        return null;
    }

    private static BeatmapLevel? Find(ConcurrentDictionary<string, BeatmapLevel>? levels, string folderKey)
    {
        if (levels == null)
        {
            return null;
        }

        if (levels.TryGetValue(folderKey, out var direct))
        {
            return direct;
        }

        foreach (var entry in levels)
        {
            if (MapPaths.Comparer.Equals(NormalizeKey(entry.Key), folderKey))
            {
                return entry.Value;
            }
        }

        return null;
    }

    private static void AddKeys(Dictionary<string, string> index, ConcurrentDictionary<string, BeatmapLevel>? levels)
    {
        if (levels == null)
        {
            return;
        }

        foreach (var key in levels.Keys)
        {
            index[NormalizeKey(key)] = key;
        }
    }

    private static void AddRoot(List<WatchRoot> roots, string name, string path, IReadOnlyCollection<string> ignored)
    {
        string songCorePath;
        try
        {
            songCorePath = MapPaths.Normalize(Path.GetFullPath(path));
        }
        catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
        {
            Plugin.Log.Warn($"[AutoReload] Skipping folder {name}: bad path '{path}'");
            return;
        }

        if (!Directory.Exists(songCorePath))
        {
            return;
        }

        var watchPath = MapPaths.Normalize(NativePaths.ResolveFinalPath(songCorePath));
        foreach (var existing in roots)
        {
            if (MapPaths.Comparer.Equals(existing.WatchPath, watchPath) || MapPaths.Comparer.Equals(existing.SongCorePath, songCorePath))
            {
                return;
            }
        }

        roots.Add(new WatchRoot(name, songCorePath, watchPath, ignored));
    }
}
