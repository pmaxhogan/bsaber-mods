using System;

namespace MaxTweaks.Core.Playback;

/// <summary>
/// Where a beatmap callback cursor goes after a seek. The game's callback loop
/// (BeatmapCallbacksController.ManualUpdate) processes every item with time - aheadTime &lt;= songTime,
/// and only fires object callbacks for objects at or after the controller's filter time. After a
/// seek to <c>audioTime</c> with objects resuming at <c>objectsFromTime</c>, a cursor ends on the last
/// item before the value returned here; items from there on are processed by the game itself.
///
/// The natural position is audioTime + aheadTime (what the game would have reached). But when
/// objectsFromTime lies before that (a skip target closer than one spawn-ahead time, low NJS or a
/// large offset, or a plain seek with objectsFromTime == audioTime), walking that far would mark the
/// objects in between as processed and they would never spawn. So the target is capped at
/// max(audioTime, objectsFromTime): those objects then spawn on the next update (part-way through
/// their flight) instead of being lost. Events between the cap and the natural position fire on the
/// next update too, which is where the game would have fired them anyway for that ahead time.
/// </summary>
public static class SeekMath
{
    public static float CursorTarget(float audioTime, float aheadTime, float objectsFromTime)
    {
        var natural = audioTime + Math.Max(0f, aheadTime);
        var cap = Math.Max(audioTime, objectsFromTime);
        return Math.Min(natural, cap);
    }
}
