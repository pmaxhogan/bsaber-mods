using System;

namespace MaxTweaks.Common.Playback;

/// <summary>
/// Which difficulty the running scene currently plays. Starts as the scene's own
/// GameplayCoreSceneSetupData.beatmapKey; the difficulty switch replaces the beatmap data in place
/// and then calls <see cref="Set"/>, so features that resolve per-difficulty things (Chapters) can
/// re-resolve on <see cref="Changed"/>. The injected IReadonlyBeatmapData instance stays the same
/// object (its contents are replaced), so re-read it after the event.
///
/// Bound per player container together with <see cref="PlaybackSeeker"/> (practice mode only); inject
/// it optionally. GameplayCoreSceneSetupData.beatmapKey itself is NOT updated by an in-place switch.
/// </summary>
internal sealed class ActiveBeatmap
{
    public ActiveBeatmap(GameplayCoreSceneSetupData setupData)
    {
        Key = setupData.beatmapKey;
    }

    /// <summary>Raised on the main thread after the beatmap data was replaced: (old key, new key).</summary>
    public event Action<BeatmapKey, BeatmapKey>? Changed;

    public BeatmapKey Key { get; private set; }

    public void Set(BeatmapKey key)
    {
        var old = Key;
        Key = key;
        if (Changed == null)
        {
            return;
        }

        foreach (var handler in Changed.GetInvocationList())
        {
            Guard.Run("Playback", () => ((Action<BeatmapKey, BeatmapKey>)handler)(old, key));
        }
    }
}
