using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MaxTweaks.Common;
using SongCore;
using SongCore.UI;
using Zenject;

namespace MaxTweaks.Features.AutoReload;

/// <summary>
/// Two small SongCore patches (global Harmony via SafePatcher, feature id "AutoReload"), active only
/// while one of our reloads is in flight:
/// - Silence SongCore's "Loading songs..." banner (ProgressBar.ShowMessage and its two loader event
///   handlers), so a reload stays quiet. Deactivating the banner instead leaves it stuck on screen.
/// - Keep updated maps' old save data selectable during the refresh: we put the old
///   _loadedBeatmapSaveData entry back after DeleteSingleSong (so selecting the still-listed old level
///   does not throw in SongCore's HandleDidSelectLevel), and remove those IDs from the copy that
///   Loader.StoreLoadedBeatmapSaveData takes at the start of the refresh, so the freshly loaded save
///   data is not blocked by its TryAdd when the level ID did not change.
/// If patching fails, the banner shows and the old behaviour (no stale entries kept) is used.
/// </summary>
internal sealed class SongCoreHooks : IInitializable, IDisposable
{
    public const string Feature = "AutoReload";

    private static readonly object StaleLock = new();
    private static readonly HashSet<string> StaleIds = new();
    private static volatile bool _silenceProgressBar;

    private readonly SafePatcher _patcher;
    private Harmony? _harmony;

    public SongCoreHooks(SafePatcher patcher)
    {
        _patcher = patcher;
    }

    public bool Available => _harmony != null;

    public bool SilenceProgressBar
    {
        get => _silenceProgressBar;
        set => _silenceProgressBar = value;
    }

    public void Initialize()
    {
        _harmony = _patcher.TryPatch(Feature, typeof(ProgressBarSilencePatch), typeof(StoreSaveDataPatch));
    }

    public void Dispose()
    {
        _silenceProgressBar = false;
        ClearStaleIds();
        _harmony?.UnpatchSelf();
        _harmony = null;
    }

    public void AddStaleId(string levelId)
    {
        lock (StaleLock)
        {
            StaleIds.Add(levelId);
        }
    }

    public List<string> TakeStaleIds()
    {
        lock (StaleLock)
        {
            var ids = new List<string>(StaleIds);
            StaleIds.Clear();
            return ids;
        }
    }

    private static void ClearStaleIds()
    {
        lock (StaleLock)
        {
            StaleIds.Clear();
        }
    }

    [HarmonyPatch]
    private static class ProgressBarSilencePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ProgressBar), nameof(ProgressBar.ShowMessage), new[] { typeof(string), typeof(bool) });
            yield return AccessTools.Method(typeof(ProgressBar), nameof(ProgressBar.ShowMessage), new[] { typeof(string), typeof(float) });
            yield return AccessTools.Method(typeof(ProgressBar), "SongLoaderOnLoadingStartedEvent");
            yield return AccessTools.Method(typeof(ProgressBar), "SongLoaderOnSongsLoadedEvent");
        }

        private static bool Prefix() => !_silenceProgressBar;
    }

    [HarmonyPatch(typeof(Loader), "StoreLoadedBeatmapSaveData")]
    private static class StoreSaveDataPatch
    {
        // Runs on SongCore's loading thread.
        private static void Postfix()
        {
            try
            {
                lock (StaleLock)
                {
                    foreach (var id in StaleIds)
                    {
                        Loader.LoadedBeatmapSaveData.TryRemove(id, out _);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.Error($"[{Feature}] {e}");
            }
        }
    }
}
