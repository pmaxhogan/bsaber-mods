using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaxTweaks.Features.DifficultySwitch;

/// <summary>
/// Loads other difficulties of the playing level exactly the way the game loads the played one
/// (GameplayCoreSceneSetupData.LoadTransformedBeatmapDataAsync): the scene's own BeatmapDataLoader
/// with the scene's environment infos, modifiers and player settings, then the same transform
/// pipeline (BeatmapDataTransformHelper.CreateTransformedBeatmapData: left-handed mirror, zen, no
/// bombs/walls, no arrows, obstacle merging, strobe filter). Mods that patch these (CustomJSONData,
/// SongCore) therefore apply as for a normal start.
///
/// The private GameplayCoreSceneSetupData.TransformBeatmapData is NOT reused: it picks the
/// environment-effects preset from the scene's own (old) difficulty; ours uses the target's.
///
/// Preloading runs one difficulty at a time: the loader parses on the thread pool (the game's own
/// async path), the transform runs on the thread pool too (pure data code), and results are stored on
/// the main thread (the Unity synchronisation context resumes the awaits there). Each loaded data
/// object is used for one swap only: the swap moves its internals into the scene's data instance.
/// </summary>
internal sealed class BeatmapPreloader : IDisposable
{
    private const string Feature = "DifficultySwitch";

    private readonly GameplayCoreSceneSetupData _setupData;
    private readonly Dictionary<BeatmapKey, IReadonlyBeatmapData> _ready = new();
    private readonly Queue<BeatmapKey> _queue = new();
    private readonly HashSet<BeatmapKey> _queued = new();
    private readonly CancellationTokenSource _cancel = new();
    private bool _running;
    private bool _disposed;

    public BeatmapPreloader(GameplayCoreSceneSetupData setupData)
    {
        _setupData = setupData;
    }

    /// <summary>Adds <paramref name="key"/> to the background queue (no-op when ready or queued).</summary>
    public void Queue(BeatmapKey key)
    {
        if (_disposed || _ready.ContainsKey(key) || !_queued.Add(key))
        {
            return;
        }

        _queue.Enqueue(key);
        if (!_running)
        {
            _ = RunQueue();
        }
    }

    public bool IsReady(BeatmapKey key) => _ready.ContainsKey(key);

    /// <summary>Takes the preloaded data for <paramref name="key"/> (it can be used once).</summary>
    public bool TryTake(BeatmapKey key, out IReadonlyBeatmapData data)
    {
        if (_ready.TryGetValue(key, out data!))
        {
            _ready.Remove(key);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Loads <paramref name="key"/> synchronously on the main thread (a frame hitch, no audio break).
    /// Used when a switch is committed before its preload finished. Returns null when the level has
    /// no data for it.
    /// </summary>
    public IReadonlyBeatmapData? LoadNow(BeatmapKey key)
    {
        var setup = _setupData;
        var levelData = setup.beatmapLevelData ?? throw new InvalidOperationException("scene has no beatmap level data");
        var raw = setup._beatmapDataLoader.LoadBeatmapData(
            levelData,
            key,
            setup.beatmapLevel.beatsPerMinute,
            IsDesignatedEnvironment(key),
            setup.targetEnvironmentInfo,
            setup.originalEnvironmentInfo,
            BeatmapLevelDataVersion.Original,
            setup.gameplayModifiers,
            setup.playerSpecificSettings);
        return raw == null ? null : Transform(raw, key);
    }

    public void Dispose()
    {
        _disposed = true;
        _ready.Clear();
        _queue.Clear();
        _cancel.Cancel();
    }

    private async Task RunQueue()
    {
        _running = true;
        try
        {
            while (!_disposed && _queue.Count > 0)
            {
                var key = _queue.Dequeue();
                try
                {
                    var data = await LoadAsync(key);
                    if (_disposed)
                    {
                        return;
                    }

                    if (data != null)
                    {
                        _ready[key] = data;
                        Plugin.Log.Debug($"[{Feature}] preloaded {key.difficulty}");
                    }
                    else
                    {
                        Plugin.Log.Warn($"[{Feature}] no beatmap data for {key.difficulty}");
                    }
                }
                catch (Exception e)
                {
                    if (!_disposed)
                    {
                        Plugin.Log.Warn($"[{Feature}] preload of {key.difficulty} failed (a switch to it loads synchronously): {e}");
                    }
                }
                finally
                {
                    _queued.Remove(key);
                }
            }
        }
        finally
        {
            _running = false;
        }
    }

    private async Task<IReadonlyBeatmapData?> LoadAsync(BeatmapKey key)
    {
        var setup = _setupData;
        var levelData = setup.beatmapLevelData ?? throw new InvalidOperationException("scene has no beatmap level data");
        var version = BeatmapLevelDataVersion.Original;
        var entitlement = setup._beatmapLevelsEntitlementModel;
        if (entitlement != null)
        {
            version = await entitlement.GetLevelDataVersionAsync(key.levelId, _cancel.Token);
        }

        // enableBeatmapDataCaching false: do not replace the loader's one-entry cache (used on restart).
        var raw = await setup._beatmapDataLoader.LoadBeatmapDataAsync(
            levelData,
            key,
            setup.beatmapLevel.beatsPerMinute,
            IsDesignatedEnvironment(key),
            setup.targetEnvironmentInfo,
            setup.originalEnvironmentInfo,
            version,
            setup.gameplayModifiers,
            setup.playerSpecificSettings,
            false);
        if (raw == null || _disposed)
        {
            return null;
        }

        return await Task.Run(() => Transform(raw, key), _cancel.Token);
    }

    private bool IsDesignatedEnvironment(BeatmapKey key)
    {
        var setup = _setupData;
        EnvironmentName name = setup.beatmapLevel.GetEnvironmentName(key.beatmapCharacteristic, key.difficulty);
        return name == (EnvironmentName)setup.targetEnvironmentInfo.serializedName;
    }

    private IReadonlyBeatmapData Transform(IReadonlyBeatmapData raw, BeatmapKey key)
    {
        var setup = _setupData;
        var settings = setup.playerSpecificSettings;
        var preset = key.difficulty == BeatmapDifficulty.ExpertPlus
            ? settings.environmentEffectsFilterExpertPlusPreset
            : settings.environmentEffectsFilterDefaultPreset;
        return BeatmapDataTransformHelper.CreateTransformedBeatmapData(
            raw,
            setup.beatmapLevel,
            setup.gameplayModifiers,
            settings.leftHanded,
            preset,
            setup.originalEnvironmentInfo.environmentIntensityReductionOptions,
            in setup._settingsManager.settings);
    }
}
