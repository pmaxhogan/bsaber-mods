using System;

namespace MaxTweaks.Core.Blink;

/// <summary>Why a blink could not start this frame (or None when it may).</summary>
public enum BlinkBlocker
{
    None,

    /// <summary>A blink is already showing.</summary>
    Blinking,

    /// <summary>Not enough played time since the last blink.</summary>
    Cooldown,

    /// <summary>The song (or a seek) started less than QuietBefore ago: not enough quiet observed yet.</summary>
    TooSoonAfterStart,

    /// <summary>The blink would not finish before the song ends.</summary>
    SongEnding,

    /// <summary>A note, bomb or chain is due in the quiet window or the look-ahead window.</summary>
    NotesNearby,

    /// <summary>A wall that would hit the head passes during the look-ahead window.</summary>
    HeadWall,
}

/// <summary>
/// Per-song blink state machine. Call <see cref="Tick"/> once per frame with the song clock; it
/// starts a blink when every rule holds and returns the overlay opacity. Allocation free.
/// Rules (spec "Blink"): cooldown elapsed; no notes in the last QuietBefore song seconds (and at
/// least that much play since the song start or the last seek); no notes and no head-hitting walls
/// in the next ClearAhead song seconds; the blink fits before the song ends.
/// </summary>
public sealed class BlinkScheduler
{
    private readonly BlinkTimeline _timeline;
    private readonly BlinkCooldown _cooldown;
    private readonly PlayClock _clock = new();
    private float _segmentStart;
    private float _elapsed;

    public BlinkScheduler(BlinkTimeline timeline, BlinkCooldown cooldown, float startSongTime)
    {
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        _cooldown = cooldown ?? throw new ArgumentNullException(nameof(cooldown));
        _segmentStart = startSongTime;
    }

    public bool IsBlinking { get; private set; }

    /// <summary>Played seconds since the current blink started.</summary>
    public float Elapsed => _elapsed;

    /// <summary>Result of the latest start check (for logging and tests).</summary>
    public BlinkBlocker LastBlocker { get; private set; } = BlinkBlocker.TooSoonAfterStart;

    /// <summary>Song time from which quiet is counted (song start or the last seek target).</summary>
    public float SegmentStart => _segmentStart;

    /// <summary>Starts a blink now regardless of the rules (debug key). Does not touch the cooldown.</summary>
    public void ForceStart()
    {
        IsBlinking = true;
        _elapsed = 0f;
    }

    /// <summary>Advances one frame.</summary>
    /// <param name="songTime">Current song time (seconds).</param>
    /// <param name="timeScale">Song speed: song seconds per real second.</param>
    /// <param name="songEndTime">Song time at which the level ends; values &lt;= 0 mean unknown.</param>
    /// <param name="paused">Game paused or song not playing: nothing advances and the overlay hides.</param>
    /// <returns>Overlay opacity 0..1 for this frame.</returns>
    public float Tick(float songTime, float timeScale, float songEndTime, bool paused, in BlinkSettings settings, IWallHeadTest walls)
    {
        if (paused)
        {
            // Keep the clock's reference so the frozen time does not count as a seek on resume.
            _clock.Advance(songTime, timeScale);
            return 0f;
        }

        var played = _clock.Advance(songTime, timeScale);
        if (_clock.Jumped)
        {
            // A seek is not play: restart the quiet window at the new position and drop any blink,
            // the new position may be full of notes.
            _segmentStart = songTime;
            IsBlinking = false;
        }

        _cooldown.AddPlayed(played);

        if (IsBlinking)
        {
            _elapsed += played;
            if (_elapsed < settings.Duration)
            {
                LastBlocker = BlinkBlocker.Blinking;
                return BlinkEnvelope.Alpha(_elapsed, settings.Duration, settings.Fade);
            }

            IsBlinking = false;
        }

        LastBlocker = Evaluate(songTime, timeScale, songEndTime, settings, walls);
        if (LastBlocker == BlinkBlocker.None)
        {
            IsBlinking = true;
            _elapsed = 0f;
            _cooldown.Restart();
        }

        return 0f;
    }

    /// <summary>The start rules, without side effects.</summary>
    public BlinkBlocker Evaluate(float songTime, float timeScale, float songEndTime, in BlinkSettings settings, IWallHeadTest walls)
    {
        if (!_cooldown.IsReady(settings.Cooldown))
        {
            return BlinkBlocker.Cooldown;
        }

        if (songTime - settings.QuietBefore < _segmentStart)
        {
            return BlinkBlocker.TooSoonAfterStart;
        }

        var scale = timeScale > 0f ? timeScale : 1f;
        var blinkSongSpan = settings.Duration * scale;
        if (songEndTime > 0f && songTime + blinkSongSpan > songEndTime)
        {
            return BlinkBlocker.SongEnding;
        }

        var ahead = Math.Max(settings.ClearAhead, blinkSongSpan);
        if (_timeline.AnyNoteIn(songTime - settings.QuietBefore, songTime + ahead))
        {
            return BlinkBlocker.NotesNearby;
        }

        if (_timeline.AnyHeadWallIn(songTime, songTime + ahead, walls))
        {
            return BlinkBlocker.HeadWall;
        }

        return BlinkBlocker.None;
    }
}
