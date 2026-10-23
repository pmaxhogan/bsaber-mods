namespace MaxTweaks.Core.Blink;

/// <summary>
/// Turns the song clock into played (real) seconds per frame. A paused song does not advance, so
/// pauses add nothing; a seek (backwards, or forwards by more than <see cref="MaxStep"/>) is not
/// play either and is reported as a jump so the caller can reset its quiet window.
/// </summary>
public sealed class PlayClock
{
    /// <summary>
    /// Largest song-time step still treated as continuous play (covers frame hitches). It is in song
    /// seconds, so at 1.5x speed a hitch of about 333 ms already counts as a seek. Harmless: a seek
    /// only restarts the quiet window and drops a running blink, it never starts one.
    /// </summary>
    public const float MaxStep = 0.5f;

    /// <summary>Backward steps smaller than this are audio-sync jitter, not a seek.</summary>
    public const float BackwardTolerance = 0.05f;

    private bool _started;
    private float _lastSongTime;

    /// <summary>True when the last <see cref="Advance"/> saw a seek.</summary>
    public bool Jumped { get; private set; }

    public void Reset()
    {
        _started = false;
        Jumped = false;
    }

    /// <param name="songTime">Current song time in seconds.</param>
    /// <param name="timeScale">Song speed (practice speed / speed modifiers); song seconds per real second.</param>
    /// <returns>Played real seconds since the previous call (0 on the first call, pauses and seeks).</returns>
    public float Advance(float songTime, float timeScale)
    {
        Jumped = false;
        if (!_started)
        {
            _started = true;
            _lastSongTime = songTime;
            return 0f;
        }

        var delta = songTime - _lastSongTime;
        _lastSongTime = songTime;
        if (delta < -BackwardTolerance || delta > MaxStep)
        {
            Jumped = true;
            return 0f;
        }

        if (delta <= 0f)
        {
            return 0f;
        }

        return timeScale > 0f ? delta / timeScale : delta;
    }
}
