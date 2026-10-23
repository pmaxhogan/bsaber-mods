using System;

namespace MaxTweaks.Core.Blink;

/// <summary>
/// Blink thresholds as plain values (the plugin copies them out of BlinkConfig once per song, so
/// Core never sees the BSIPA config type). All times are in seconds.
/// </summary>
public readonly struct BlinkSettings
{
    /// <summary>Fade in and fade out length; part of <see cref="Duration"/>.</summary>
    public const float DefaultFade = 0.3f;

    public BlinkSettings(float quietBefore, float clearAhead, float duration, float cooldown, float fade = DefaultFade)
    {
        QuietBefore = Math.Max(0f, quietBefore);
        Duration = Math.Max(0.1f, duration);

        // The overlay must never be up while notes or head walls arrive, so the look-ahead always
        // covers at least the blink itself.
        ClearAhead = Math.Max(Math.Max(0f, clearAhead), Duration);
        Cooldown = Math.Max(0f, cooldown);

        // Fades can never overlap: on a very short blink they shrink to half the length each.
        Fade = Math.Min(Math.Max(0f, fade), Duration / 2f);
    }

    /// <summary>No note may have been due in the last this-many song seconds.</summary>
    public float QuietBefore { get; }

    /// <summary>No note and no head-hitting wall in the next this-many song seconds (at least Duration).</summary>
    public float ClearAhead { get; }

    /// <summary>Total blink length (played seconds), fades included.</summary>
    public float Duration { get; }

    /// <summary>Played seconds required between two blink starts.</summary>
    public float Cooldown { get; }

    public float Fade { get; }
}
