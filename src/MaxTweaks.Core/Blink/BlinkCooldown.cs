namespace MaxTweaks.Core.Blink;

/// <summary>
/// Played time since the last blink. Lives for the whole app session (bound at App location), so
/// the cooldown carries across songs. Only played time is added (see <see cref="PlayClock"/>):
/// menus, pauses and seeks never count.
/// The session starts with an empty counter: the first blink comes after one full cooldown of
/// play, because the reminder is for long sessions, not the first song.
/// </summary>
public sealed class BlinkCooldown
{
    public float PlayedSinceLastBlink { get; private set; }

    public void AddPlayed(float seconds)
    {
        if (seconds > 0f)
        {
            PlayedSinceLastBlink += seconds;
        }
    }

    public bool IsReady(float cooldownSeconds) => PlayedSinceLastBlink >= cooldownSeconds;

    /// <summary>Call when a blink starts.</summary>
    public void Restart() => PlayedSinceLastBlink = 0f;
}
