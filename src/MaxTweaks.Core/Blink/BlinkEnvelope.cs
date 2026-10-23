using System;

namespace MaxTweaks.Core.Blink;

/// <summary>Overlay opacity over a blink: linear fade in, hold at 1, linear fade out.</summary>
public static class BlinkEnvelope
{
    /// <returns>Opacity in [0, 1]; 0 before the start and after the end.</returns>
    public static float Alpha(float elapsed, float duration, float fade)
    {
        if (elapsed < 0f || elapsed >= duration || duration <= 0f)
        {
            return 0f;
        }

        fade = Math.Min(Math.Max(0f, fade), duration / 2f);
        if (fade <= 0f)
        {
            return 1f;
        }

        var fadeIn = elapsed / fade;
        var fadeOut = (duration - elapsed) / fade;
        return Math.Min(1f, Math.Min(fadeIn, fadeOut));
    }
}
