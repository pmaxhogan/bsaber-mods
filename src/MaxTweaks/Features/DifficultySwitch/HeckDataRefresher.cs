using System;
using System.Collections;
using System.Reflection;
using MaxTweaks.Common.Playback;
using Zenject;

namespace MaxTweaks.Features.DifficultySwitch;

/// <summary>
/// Re-runs Heck's custom-data deserialization for the swapped-in beatmap, so Chroma (and Heck's own
/// per-object data) see the new difficulty's notes, walls and events. Optional, reflection only, no
/// hard reference to Heck.
///
/// What Heck does at scene start (Heck 1.40.x, HarmonyPatches/PatchedPlayerInstaller.BindHeckSinglePlayer):
/// for a CustomBeatmapData it calls DeserializerManager.DeserializeBeatmapData(beatmapLevel,
/// transformedData, untransformedData, leftHanded, out tracks, out datas) and binds the track
/// dictionary and one DeserializedData per deserializer id ("Chroma", "Heck", ...) in the player
/// container. Consumers keep those instances and look objects up by reference
/// (DeserializedData.Resolve -> Dictionary.TryGetValue).
///
/// Precedent for a mid-song refresh: Heck's own ReLoad/ReLoader.Reload, which after filling the
/// injected beatmap data calls DeserializeBeatmapData again, refills the bound track dictionary and
/// calls DeserializedData.Remap(new) on every bound instance (Remap swaps the three dictionaries in
/// place, so every consumer sees the new data). This class does exactly that. It must run after the
/// fill: the dictionaries are keyed by the very item objects that now live in the scene's data.
///
/// If this fails, the swap still goes ahead: every Chroma and Heck lookup of a missing object goes
/// through Resolve, which uses TryGetValue and returns false, and all call sites in the installed
/// Chroma.dll/Heck.dll handle false/null (decompiled: NoteObjectColorize, ObjectColorize,
/// ChromaLightSwitchEventEffect, Ring*/Light*Chromafier, DebrisSpawnerSkip, BeatEffectSpawnerSkip,
/// GameObjectTracker, CoroutineEvent, AnimateComponent, FogAnimatorV2), so missing data only means
/// default colours. Noodle Extensions does throw on missing data (NoodleObjectsCallbacksManager),
/// which is why maps that need Noodle are gated to the restart.
/// </summary>
internal sealed class HeckDataRefresher
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly DiContainer _container;
    private readonly GameplayCoreSceneSetupData _setupData;

    public HeckDataRefresher(DiContainer container, GameplayCoreSceneSetupData setupData)
    {
        _container = container;
        _setupData = setupData;
    }

    /// <summary>
    /// Refreshes Heck's deserialized data for <paramref name="transformed"/> (the scene's data after the
    /// fill). Returns a short description of what happened for the log; throws on failure.
    /// </summary>
    public string Refresh(IReadonlyBeatmapData transformed, IReadonlyBeatmapData? untransformed)
    {
        var managerType = Type.GetType("Heck.Deserialize.DeserializerManager, Heck", throwOnError: false);
        if (managerType == null)
        {
            return "Heck not installed";
        }

        if (transformed.GetType().FullName != "CustomJSONData.CustomBeatmap.CustomBeatmapData")
        {
            return "not custom data";
        }

        var manager = _container.TryResolve(managerType);
        if (manager == null)
        {
            return "Heck deserializer not bound";
        }

        var deserialize = managerType.GetMethod("DeserializeBeatmapData", Instance)
            ?? throw new MissingMethodException(managerType.FullName, "DeserializeBeatmapData");
        var untransformedData = untransformed != null && untransformed.GetType() == transformed.GetType() ? untransformed : transformed;
        var args = new object?[] { _setupData.beatmapLevel, transformed, untransformedData, _setupData.playerSpecificSettings.leftHanded, null, null };
        deserialize.Invoke(manager, args);

        var remapped = 0;
        if (args[5] is IEnumerable datas)
        {
            var dataType = Type.GetType("Heck.Deserialize.DeserializedData, Heck", throwOnError: true)!;
            var remap = dataType.GetMethod("Remap", Instance) ?? throw new MissingMethodException(dataType.FullName, "Remap");
            foreach (var entry in datas)
            {
                // ValueTuple<object, DeserializedData>
                var entryType = entry.GetType();
                var id = entryType.GetField("Item1")?.GetValue(entry);
                var data = entryType.GetField("Item2")?.GetValue(entry);
                if (data == null)
                {
                    continue;
                }

                var bound = id == null ? _container.TryResolve(dataType) : _container.TryResolveId(dataType, id);
                if (bound != null)
                {
                    remap.Invoke(bound, new[] { data });
                    remapped++;
                }
            }
        }

        var tracks = 0;
        if (args[4] is IDictionary newTracks && _container.TryResolve(newTracks.GetType()) is IDictionary boundTracks)
        {
            boundTracks.Clear();
            foreach (DictionaryEntry pair in newTracks)
            {
                boundTracks[pair.Key] = pair.Value;
            }

            tracks = boundTracks.Count;
        }

        // Keep Heck's setup-data subclass consistent for anything that re-reads it later.
        if (ReadonlyFields.Find(_setupData.GetType(), "_untransformedBeatmapData") != null)
        {
            ReadonlyFields.Set(_setupData, "_untransformedBeatmapData", untransformedData);
        }

        return $"Heck data refreshed ({remapped} sets, {tracks} tracks)";
    }
}
