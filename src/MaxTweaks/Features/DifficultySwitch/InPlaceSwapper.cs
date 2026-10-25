using System;
using System.Collections.Generic;
using System.Reflection;
using MaxTweaks.Common.Playback;
using MaxTweaks.Core.DifficultySwitch;
using UnityEngine;

namespace MaxTweaks.Features.DifficultySwitch;

/// <summary>
/// Replaces the running scene's beatmap with another difficulty without touching the audio.
///
/// Why in place: the IReadonlyBeatmapData instance bound in the GameCore container is cached by
/// BeatmapCallbacksController (_beatmapData), BeatmapObjectSpawnController (_beatmapData),
/// PrepareLevelCompletionResults, StandardLevelGameplayManager, VideoProjectionEffect, mods
/// (PracticePlugin, Heck, Noodle) and more, so the reference cannot be swapped; its contents are
/// (precedent: Heck ReLoad/ReLoader.FillBeatmapData). Every non-delegate instance field of the
/// runtime type is copied from the freshly loaded data: BeatmapData._allBeatmapData (the linked
/// list the callbacks walk), _allBeatmapDataItemToNodeMap, _beatmapDataItemsPerTypeAndId,
/// _beatmapObjectsInTimeRowProcessor, _specialBasicBeatmapEventKeywords, _numberOfLines and the
/// cuttableNotesCount/obstaclesCount/bombsCount backing fields, plus CustomJSONData's
/// CustomBeatmapData lists and custom data when present. Event delegate fields are kept, so the
/// callbacks controller's subscriptions (beatmapEventDataWasInsertedEvent...) stay intact.
///
/// Then, in this order:
/// 1. Note jump speed and offset of the new difficulty, the way PracticePlugin 9.x changes NJS
///    (AudioSpeedController.UpdateSpawnMovementData): remove the spawn controller's three callback
///    wrappers, write noteJumpMovementSpeed / noteJumpValueType / noteJumpValue into its InitData,
///    then do what BeatmapObjectSpawnController.Start does with them: re-init
///    VariableMovementDataProvider (the NJS callback is registered in its constructor, so no double
///    registration) with the minimum relative NJS of the (new) data, and re-register the three spawn
///    callbacks under the new spawnAheadTime key. Start itself is NOT called again (PracticePlugin
///    does): Chroma's EnvironmentEnhancementManager has an affinity prefix on it that re-runs the
///    environment enhancements, which duplicated TransformControllers ("Overwriting existing
///    [TransformController]" in QA). VariableMovementDataProvider only recomputes its
///    jump duration when the NJS value changes, so it is forced (prev NJS = NaN) to pick up an
///    offset-only change.
/// 0. Before the fill, PlaybackSeeker.RevertEventsToStart() rewinds every event of the old data to its
///    previous state/default, so event types only the old difficulty used do not keep their state.
/// 2. PlaybackSeeker.ResyncCallbacks(now, objectsFrom): every cursor pointed into the old list, so all
///    restart from the first item; events up to now are replayed (lights, BPM, NJS events match the
///    new difficulty), objects before objectsFrom = now + clear gap are dropped by the controller's
///    object filter time.
/// Score, combo, energy and the ScoreController are untouched (score carries over).
///
/// All-or-nothing: on any exception the old field values, InitData and filter time are restored,
/// the spawn controller is re-inited for the old difficulty and the cursors are resynced, then the
/// exception is rethrown so the caller falls back to a restart.
/// </summary>
internal sealed class InPlaceSwapper
{
    private static readonly Dictionary<Type, FieldInfo[]> DataFieldsCache = new();

    private readonly IReadonlyBeatmapData _beatmapData;
    private readonly BeatmapObjectSpawnController _spawnController;
    private readonly PlaybackSeeker _seeker;
    private readonly GameplayCoreSceneSetupData _setupData;
    private readonly HeckDataRefresher _heckRefresher;

    public InPlaceSwapper(
        IReadonlyBeatmapData beatmapData,
        BeatmapObjectSpawnController spawnController,
        PlaybackSeeker seeker,
        GameplayCoreSceneSetupData setupData,
        HeckDataRefresher heckRefresher)
    {
        _beatmapData = beatmapData;
        _spawnController = spawnController;
        _seeker = seeker;
        _setupData = setupData;
        _heckRefresher = heckRefresher;
    }

    /// <summary>What the last successful <see cref="Swap"/> did with Heck's custom data (for the log).</summary>
    public string LastCustomDataNote { get; private set; } = string.Empty;

    /// <summary>
    /// Swaps <paramref name="source"/> (the transformed data of <paramref name="target"/>) into the
    /// scene at the current song time. <paramref name="source"/> must not be used afterwards.
    /// </summary>
    /// <param name="clearGapSeconds">Clear gap in real seconds (scaled by the practice speed).</param>
    /// <param name="staleRealSeconds">
    /// Real seconds since the song time was last updated (e.g. a synchronous load this frame); the
    /// audio kept playing, so the switch time is advanced by this much.
    /// </param>
    /// <returns>The objects-from time (end of the clear gap).</returns>
    public float Swap(BeatmapKey target, PreloadedBeatmap loaded, float clearGapSeconds, float staleRealSeconds)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var source = loaded.Transformed;
        var dest = _beatmapData;
        if (ReferenceEquals(source, dest))
        {
            throw new InvalidOperationException("source is the scene's own data");
        }

        if (dest is not BeatmapData || source.GetType() != dest.GetType())
        {
            throw new InvalidOperationException($"data type mismatch: scene {dest.GetType().FullName}, loaded {source.GetType().FullName}");
        }

        if (!source.areValid || source.numberOfLines != dest.numberOfLines)
        {
            throw new InvalidOperationException($"loaded data invalid or has {source.numberOfLines} lines (scene {dest.numberOfLines})");
        }

        if (source.allBeatmapDataItems.Count == 0)
        {
            throw new InvalidOperationException("loaded data is empty");
        }

        if (!_seeker.CanSeek)
        {
            throw new InvalidOperationException("song is not running");
        }

        var fields = DataFields(dest.GetType());
        var oldValues = new object?[fields.Length];
        var newValues = new object?[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            oldValues[i] = fields[i].GetValue(dest);
            newValues[i] = fields[i].GetValue(source);
        }

        var initData = _spawnController._initData;
        var oldNjs = initData.noteJumpMovementSpeed;
        var oldValueType = initData.noteJumpValueType;
        var oldValue = initData.noteJumpValue;
        var oldFilter = _seeker.ObjectFilterTime;
        var now = _seeker.SongTime;
        var timeScale = _seeker.TimeScale > 0f ? _seeker.TimeScale : 1f;
        var paused = _seeker.IsPaused;

        GetNoteJumpValues(target, out var njs, out var valueType, out var value);

        try
        {
            // Event types that only the old difficulty uses would otherwise keep their last state.
            _seeker.RevertEventsToStart();

            for (var i = 0; i < fields.Length; i++)
            {
                fields[i].SetValue(dest, newValues[i]);
            }

            // Before the resync, so replayed events already find their (Chroma) custom data.
            LastCustomDataNote = RefreshCustomData(dest, loaded.Untransformed);

            // The audio clock is only read once per frame; while playing it moved on during the load
            // and the work above.
            if (!paused)
            {
                now = _seeker.SongTime + ((staleRealSeconds + (float)watch.Elapsed.TotalSeconds) * timeScale);
            }

            ReinitSpawning(njs, valueType, value, now);

            var objectsFrom = ClearGap.ObjectsFromTime(now, clearGapSeconds, _seeker.SpawnAheadTime, timeScale);
            _seeker.ResyncCallbacks(now, objectsFrom);
            _seeker.SyncPracticePluginNoteJump(njs, value);

            if (dest.allBeatmapDataItems != source.allBeatmapDataItems)
            {
                throw new InvalidOperationException("data fill did not take effect");
            }

            return objectsFrom;
        }
        catch
        {
            Rollback(fields, oldValues, oldNjs, oldValueType, oldValue, oldFilter, now);
            throw;
        }
    }

    /// <summary>The note jump speed the spawn controller uses now (InitData).</summary>
    public float CurrentNoteJumpSpeed => _spawnController._initData.noteJumpMovementSpeed;

    /// <summary>
    /// The difficulty's own NJS with the active modifiers (what the game computed for
    /// <paramref name="key"/> at level start, e.g. fast notes), the NJS row's underlined default.
    /// </summary>
    public float DefaultNoteJumpSpeed(BeatmapKey key)
    {
        GetNoteJumpValues(key, out var njs, out _, out _);
        return njs;
    }

    /// <summary>
    /// Changes only the note jump speed of the running song, in place (the NJS row): the way
    /// PracticePlugin's AudioSpeedController.UpdateSpawnMovementData does it, minus its
    /// BeatmapObjectSpawnController.Start call (see the class remarks about Chroma). The offset
    /// (noteJumpValueType/noteJumpValue) stays as it is. The spawn callbacks move to the new
    /// spawnAheadTime key, so the cursor of the old key (the last object spawned) is carried over to
    /// the new key's fresh cursor, like PracticePlugin's SetNewLastNodeForCallback: objects already
    /// spawned stay, the next ones spawn at the new speed, no clear gap and no object filter change.
    /// Objects whose new spawn time has already passed (a lower NJS spawns earlier) appear on the next
    /// frame part-way along their jump. On any exception the old NJS and cursor are restored and the
    /// exception is rethrown.
    /// </summary>
    /// <returns>The new spawn ahead time.</returns>
    public float SetNoteJumpSpeed(float njs)
    {
        if (!(njs > 0f) || float.IsInfinity(njs))
        {
            throw new ArgumentOutOfRangeException(nameof(njs), njs, "NJS must be positive");
        }

        var spawn = _spawnController;
        var callbacks = spawn._beatmapCallbacksController;
        var initData = spawn._initData;
        var oldNjs = initData.noteJumpMovementSpeed;
        var valueType = initData.noteJumpValueType;
        var value = initData.noteJumpValue;
        var oldAhead = spawn._noteDataCallbackWrapper?.aheadTime ?? spawn._variableMovementDataProvider.spawnAheadTime;
        callbacks._callbacksInTimes.TryGetValue(oldAhead, out var oldEntry);
        var node = oldEntry?.lastProcessedNode;
        var now = _seeker.SongTime;

        try
        {
            ReinitSpawning(njs, valueType, value, now);
            var ahead = spawn._variableMovementDataProvider.spawnAheadTime;
            CarryCursor(ahead, node);
            return ahead;
        }
        catch
        {
            try
            {
                ReinitSpawning(oldNjs, valueType, value, now);
                CarryCursor(spawn._variableMovementDataProvider.spawnAheadTime, node);
                Plugin.Log.Warn("[DifficultySwitch] NJS change rolled back");
            }
            catch (Exception e)
            {
                Plugin.Log.Error($"[DifficultySwitch] NJS rollback failed: {e}");
            }

            throw;
        }
    }

    // A fresh CallbacksInTime starts before the first item and would spawn every past object (only
    // the object filter time stops them); an existing one at the same ahead time is already where
    // the spawn cursor belongs.
    private void CarryCursor(float aheadTime, LinkedListNode<BeatmapDataItem>? node)
    {
        if (node != null
            && _spawnController._beatmapCallbacksController._callbacksInTimes.TryGetValue(aheadTime, out var entry)
            && entry.lastProcessedNode == null)
        {
            entry.lastProcessedNode = node;
        }
    }

    private string RefreshCustomData(IReadonlyBeatmapData filled, IReadonlyBeatmapData untransformed)
    {
        try
        {
            return _heckRefresher.Refresh(filled, untransformed);
        }
        catch (Exception e)
        {
            // Not fatal: every Chroma/Heck lookup of a missing object returns "no data" (see HeckDataRefresher).
            var inner = e is TargetInvocationException { InnerException: { } cause } ? cause : e;
            Plugin.Log.Warn($"[DifficultySwitch] Heck custom data refresh failed, continuing without custom data for the new objects: {inner}");
            return "Heck data refresh failed: " + inner.Message;
        }
    }

    private void Rollback(FieldInfo[] fields, object?[] oldValues, float njs, BeatmapObjectSpawnMovementData.NoteJumpValueType valueType, float value, float oldFilter, float now)
    {
        try
        {
            for (var i = 0; i < fields.Length; i++)
            {
                fields[i].SetValue(_beatmapData, oldValues[i]);
            }

            ReinitSpawning(njs, valueType, value, now);

            // Nothing new spawns mid-flight until the restart takes over.
            var objectsFrom = Mathf.Max(oldFilter, now + _seeker.SpawnAheadTime + ClearGap.SpawnMargin);
            _seeker.ResyncCallbacks(now, objectsFrom);
            Plugin.Log.Warn("[DifficultySwitch] in-place swap rolled back");
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"[DifficultySwitch] rollback failed (the fallback restart replaces the scene): {e}");
        }
    }

    private void GetNoteJumpValues(BeatmapKey key, out float njs, out BeatmapObjectSpawnMovementData.NoteJumpValueType valueType, out float value)
    {
        var setup = _setupData;
        var basic = setup.beatmapLevel.GetDifficultyBeatmapData(key.beatmapCharacteristic, key.difficulty)
            ?? throw new InvalidOperationException($"no basic data for {key.difficulty}");
        njs = key.difficulty.NoteJumpMovementSpeed(basic.noteJumpMovementSpeed, setup.gameplayModifiers.fastNotes);
        BeatmapObjectSpawnControllerHelpers.GetNoteJumpValues(setup.playerSpecificSettings, basic.noteJumpStartBeatOffset, out valueType, out value);
    }

    private void ReinitSpawning(float njs, BeatmapObjectSpawnMovementData.NoteJumpValueType valueType, float value, float songTime)
    {
        var spawn = _spawnController;
        var callbacks = spawn._beatmapCallbacksController;
        callbacks.RemoveBeatmapCallback(spawn._noteDataCallbackWrapper);
        callbacks.RemoveBeatmapCallback(spawn._obstacleDataCallbackWrapper);
        callbacks.RemoveBeatmapCallback(spawn._sliderDataCallbackWrapper);
        spawn._noteDataCallbackWrapper = null;
        spawn._obstacleDataCallbackWrapper = null;
        spawn._sliderDataCallbackWrapper = null;

        var initData = spawn._initData;
        ReadonlyFields.Set(initData, nameof(BeatmapObjectSpawnController.InitData.noteJumpMovementSpeed), njs);
        ReadonlyFields.Set(initData, nameof(BeatmapObjectSpawnController.InitData.noteJumpValueType), valueType);
        ReadonlyFields.Set(initData, nameof(BeatmapObjectSpawnController.InitData.noteJumpValue), value);

        var provider = spawn._variableMovementDataProvider as VariableMovementDataProvider;
        if (provider != null)
        {
            provider._prevNoteJumpMovementSpeed = float.NaN;
        }

        // BeatmapObjectSpawnController.Start without its mod hooks (the movement data Init there does
        // not depend on the NJS and is left as is).
        var minRelativeNjs = 0f;
        foreach (var item in _beatmapData.GetBeatmapDataItems<NoteJumpSpeedEventData>(0))
        {
            minRelativeNjs = Mathf.Min(minRelativeNjs, item.relativeNoteJumpSpeed);
        }

        var movement = spawn._beatmapObjectSpawnMovementData;
        var vmdp = spawn._variableMovementDataProvider;
        vmdp.Init(
            movement.startHalfJumpDurationInBeats,
            movement.maxHalfJumpDistance,
            njs,
            minRelativeNjs,
            initData.beatsPerMinute,
            valueType,
            value,
            movement.centerPos,
            Vector3.forward);
        var ahead = vmdp.spawnAheadTime;
        spawn._noteDataCallbackWrapper = callbacks.AddBeatmapCallback<NoteData>(ahead, spawn.HandleNoteDataCallback);
        spawn._obstacleDataCallbackWrapper = callbacks.AddBeatmapCallback<ObstacleData>(ahead, spawn.HandleObstacleDataCallback);
        spawn._sliderDataCallbackWrapper = callbacks.AddBeatmapCallback<SliderData>(ahead, spawn.HandleSliderDataCallback);

        if (provider != null)
        {
            // Init's own ManualUpdate ran before the new spawnAheadTime was known; recompute now.
            provider._prevNoteJumpMovementSpeed = float.NaN;
            provider.ManualUpdate(songTime);
        }
    }

    private static FieldInfo[] DataFields(Type type)
    {
        if (DataFieldsCache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var list = new List<FieldInfo>();
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (var field in t.GetFields(Flags))
            {
                if (!typeof(Delegate).IsAssignableFrom(field.FieldType))
                {
                    list.Add(field);
                }
            }
        }

        var result = list.ToArray();
        DataFieldsCache[type] = result;
        return result;
    }
}
