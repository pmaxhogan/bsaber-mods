using System;

namespace MaxTweaks.Core.DifficultySwitch;

/// <summary>
/// The empty window after an in-place difficulty switch. Every beatmap object (note, bomb, wall,
/// arc, chain) of the new difficulty whose time is at or after the switch and before
/// <see cref="ObjectsFromTime"/> is dropped; events (lights, BPM, NJS) are kept. Objects before the
/// switch time are already behind the callback cursors and never spawn either.
///
/// Units: the configured gap is in real seconds (what the player experiences, like Blink's
/// settings); beatmap times are song seconds, which run timeScale (practice speed) times as fast.
/// The gap is measured in object times: the first object of the new difficulty is due at the end of
/// the gap and starts flying in one spawn-ahead time earlier.
///
/// The window is at least one spawn-ahead time (plus <see cref="SpawnMargin"/>, song seconds) long
/// even when the configured gap is shorter, so the first object of the new difficulty always spawns
/// at the far end of the track and flies in normally instead of popping up mid-flight.
/// </summary>
public static class ClearGap
{
    public const float DefaultSeconds = 3f;
    public const float MinSeconds = 0f;
    public const float MaxSeconds = 10f;

    /// <summary>Extra song seconds on top of the spawn-ahead time for the minimum window.</summary>
    public const float SpawnMargin = 0.1f;

    /// <summary>Configured gap clamped to [<see cref="MinSeconds"/>, <see cref="MaxSeconds"/>]; NaN = default.</summary>
    public static float Clamp(float seconds)
    {
        if (float.IsNaN(seconds))
        {
            return DefaultSeconds;
        }

        return Math.Min(MaxSeconds, Math.Max(MinSeconds, seconds));
    }

    /// <summary>
    /// First object time (song seconds) that may spawn after a switch at <paramref name="switchTime"/>:
    /// switchTime + max(gap (real seconds) * timeScale, spawnAheadTime + margin).
    /// A non-positive or NaN <paramref name="timeScale"/> counts as 1.
    /// </summary>
    public static float ObjectsFromTime(float switchTime, float gapRealSeconds, float spawnAheadTime, float timeScale = 1f)
    {
        var ahead = float.IsNaN(spawnAheadTime) || spawnAheadTime < 0f ? 0f : spawnAheadTime;
        var scale = timeScale > 0f ? timeScale : 1f;
        return switchTime + Math.Max(Clamp(gapRealSeconds) * scale, ahead + SpawnMargin);
    }

    /// <summary>True when an object at <paramref name="objectTime"/> is removed by the window.</summary>
    public static bool IsCleared(float objectTime, float switchTime, float objectsFromTime) =>
        objectTime >= switchTime && objectTime < objectsFromTime;

    /// <summary>True when an object at <paramref name="objectTime"/> can still spawn after the switch.</summary>
    public static bool CanSpawn(float objectTime, float objectsFromTime) => objectTime >= objectsFromTime;
}
