using System.Collections.Generic;
using MaxTweaks.Core.Chapters;

namespace MaxTweaks.Common.Playback;

/// <summary>
/// Builds the played beatmap's tempo map (<see cref="BpmTimeline"/>) from its BPM change events
/// (BPMChangeBeatmapEventData.time is already in song seconds) and the start BPM (BpmController's
/// init data, else the level's BPM). Shared by Chapters (beat-based chapters) and Active pause (beat
/// steps). Re-run it after a difficulty switch: the beatmap data is replaced in place.
/// </summary>
internal static class BeatmapTempo
{
    /// <summary>Start BPM the tempo map would use, or 0 when there is none.</summary>
    public static float StartBpm(GameplayCoreSceneSetupData setupData, BpmController.InitData? bpmInitData)
    {
        var startBpm = bpmInitData?.startBpm ?? 0f;
        if (!(startBpm > 0f))
        {
            startBpm = setupData.beatmapLevel.beatsPerMinute;
        }

        return startBpm;
    }

    /// <summary>Throws ArgumentOutOfRangeException when the map has no positive start BPM.</summary>
    public static BpmTimeline Build(IReadonlyBeatmapData beatmapData, GameplayCoreSceneSetupData setupData, BpmController.InitData? bpmInitData)
    {
        var changes = new List<BpmChange>();
        for (var node = beatmapData.allBeatmapDataItems.First; node != null; node = node.Next)
        {
            if (node.Value is BPMChangeBeatmapEventData bpmChange)
            {
                changes.Add(new BpmChange(bpmChange.time, bpmChange.bpm));
            }
        }

        return BpmTimeline.FromSecondChanges(StartBpm(setupData, bpmInitData), changes);
    }
}
