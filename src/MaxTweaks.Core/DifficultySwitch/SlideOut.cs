using System;

namespace MaxTweaks.Core.DifficultySwitch;

public enum SlideDirection
{
    Down = -1,
    Up = 1,
}

/// <summary>
/// Notes and bombs already in flight when the difficulty switches slide vertically out of view
/// instead of vanishing: up when switching to a harder difficulty, down when switching to an easier
/// one (the list is hardest-on-top, so the old notes move the way the highlight moved).
/// </summary>
public static class SlideOut
{
    public const float DurationSeconds = 0.3f;

    /// <summary>Total vertical travel in metres.</summary>
    public const float DistanceMeters = 3f;

    public static SlideDirection For(int currentRank, int targetRank) =>
        targetRank > currentRank ? SlideDirection.Up : SlideDirection.Down;

    /// <summary>Progress 0..1 after <paramref name="elapsed"/> seconds (clamped; non-positive duration = done).</summary>
    public static float Progress(float elapsed, float duration)
    {
        if (!(duration > 0f) || float.IsNaN(elapsed))
        {
            return 1f;
        }

        return Math.Min(1f, Math.Max(0f, elapsed / duration));
    }

    /// <summary>Signed vertical offset in metres: ease-in (quadratic) so the notes accelerate away.</summary>
    public static float Offset(float elapsed, float duration, float distance, SlideDirection direction)
    {
        var t = Progress(elapsed, duration);
        return (int)direction * distance * t * t;
    }

    public static bool IsFinished(float elapsed, float duration) => Progress(elapsed, duration) >= 1f;
}
