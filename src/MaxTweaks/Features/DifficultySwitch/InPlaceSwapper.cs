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
///    and run BeatmapObjectSpawnController.Start() again. Start re-inits the movement data, re-inits
///    VariableMovementDataProvider (the NJS callback is registered in its constructor, so no double
///    registration), reads the minimum relative NJS from the (new) data and re-registers the spawn
///    callbacks under the new spawnAheadTime key. VariableMovementDataProvider only recomputes its
///    jump duration when the NJS value changes, so it is forced (prev NJS = NaN) to pick up an
///    offset-only change.
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

    public InPlaceSwapper(
        IReadonlyBeatmapData beatmapData,
        BeatmapObjectSpawnController spawnController,
        PlaybackSeeker seeker,
        GameplayCoreSceneSetupData setupData)
    {
        _beatmapData = beatmapData;
        _spawnController = spawnController;
        _seeker = seeker;
        _setupData = setupData;
    }

    /// <summary>
    /// Swaps <paramref name="source"/> (the transformed data of <paramref name="target"/>) into the
    /// scene at the current song time. <paramref name="source"/> must not be used afterwards.
    /// </summary>
    /// <returns>The objects-from time (end of the clear gap).</returns>
    public float Swap(BeatmapKey target, IReadonlyBeatmapData source, float clearGapSeconds)
    {
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

        GetNoteJumpValues(target, out var njs, out var valueType, out var value);

        try
        {
            for (var i = 0; i < fields.Length; i++)
            {
                fields[i].SetValue(dest, newValues[i]);
            }

            ReinitSpawning(njs, valueType, value, now);

            var objectsFrom = ClearGap.ObjectsFromTime(now, clearGapSeconds, _seeker.SpawnAheadTime);
            _seeker.ResyncCallbacks(now, objectsFrom);

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

        spawn._isInitialized = false;
        spawn.Start();

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
