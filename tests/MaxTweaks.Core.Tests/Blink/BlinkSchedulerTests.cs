using MaxTweaks.Core.Blink;
using Xunit;

namespace MaxTweaks.Core.Tests.Blink;

public class BlinkSchedulerTests
{
    private const float Frame = 0.02f;

    // Spec defaults: quiet 3, ahead 8, duration 3, cooldown 300.
    private static readonly BlinkSettings Defaults = new(3f, 8f, 3f, 300f);

    private static BlinkCooldown ReadyCooldown()
    {
        var c = new BlinkCooldown();
        c.AddPlayed(1000f);
        return c;
    }

    /// <summary>Plays frames from 'from' to 'to'; returns the song time of the first blink start, or null.</summary>
    private static float? PlayUntilBlink(BlinkScheduler s, float from, float to, float songEnd, IWallHeadTest walls, BlinkSettings? settings = null, float timeScale = 1f)
    {
        var cfg = settings ?? Defaults;
        for (var t = from; t <= to; t += Frame * timeScale)
        {
            var wasBlinking = s.IsBlinking;
            s.Tick(t, timeScale, songEnd, false, cfg, walls);
            if (!wasBlinking && s.IsBlinking)
            {
                return t;
            }
        }

        return null;
    }

    [Fact]
    public void BlinksInALongGap_AfterQuietBefore()
    {
        // Notes at 10s and 30s: the gap 10..30 allows a blink once 3s have passed after 10
        // and 8s remain before 30, i.e. from t = 13 to t = 22.
        var timeline = new BlinkTimeline.Builder().AddNote(10f).AddNote(30f).Build();
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        var start = PlayUntilBlink(s, 9f, 40f, 200f, new FakeWalls());
        Assert.NotNull(start);
        Assert.InRange(start!.Value, 13f, 13f + (2 * Frame));
    }

    [Fact]
    public void NoBlink_WhenGapIsTooShort()
    {
        // Gap 10..20: needs t >= 13 and t + 8 < 20, impossible.
        var timeline = new BlinkTimeline.Builder().AddNote(10f).AddNote(20f).Build();
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        Assert.Null(PlayUntilBlink(s, 9f, 21f, 200f, new FakeWalls()));
        Assert.Equal(BlinkBlocker.NotesNearby, s.LastBlocker);
    }

    [Fact]
    public void BombsAndChainsBlockLikeNotes()
    {
        // Bombs are added as notes by the plugin; a chain spanning the gap blocks it.
        var timeline = new BlinkTimeline.Builder().AddNote(10f).AddNoteSpan(15f, 25f).AddNote(60f).Build();
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        var start = PlayUntilBlink(s, 9f, 60f, 200f, new FakeWalls());
        Assert.NotNull(start);
        Assert.InRange(start!.Value, 28f, 28f + (2 * Frame));
    }

    [Fact]
    public void SongStart_NeedsQuietBeforeOfPlay()
    {
        // No notes until 30s. Played from 0, the first possible start is t = 3.
        var timeline = new BlinkTimeline.Builder().AddNote(30f).Build();
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        Assert.Equal(BlinkBlocker.TooSoonAfterStart, s.Evaluate(1f, 1f, 200f, Defaults, new FakeWalls()));
        var start = PlayUntilBlink(s, 0f, 30f, 200f, new FakeWalls());
        Assert.NotNull(start);
        Assert.InRange(start!.Value, 3f, 3f + (2 * Frame));
    }

    [Fact]
    public void PracticeStart_CountsFromTheStartTime()
    {
        var timeline = new BlinkTimeline.Builder().AddNote(100f).Build();
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 50f);
        Assert.Equal(50f, s.SegmentStart);
        var start = PlayUntilBlink(s, 50f, 100f, 200f, new FakeWalls());
        Assert.InRange(start!.Value, 53f, 53f + (2 * Frame));
    }

    [Fact]
    public void SongEnd_BlinkMustFitBeforeTheEnd()
    {
        // Last note at 10, song ends at 15: from t = 13 only 2 s remain (< 3 s blink).
        var timeline = new BlinkTimeline.Builder().AddNote(10f).Build();
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        Assert.Null(PlayUntilBlink(s, 9f, 15f, 15f, new FakeWalls()));
        Assert.Equal(BlinkBlocker.SongEnding, s.LastBlocker);
    }

    [Fact]
    public void SongEnd_LongOutroAllowsBlink()
    {
        var timeline = new BlinkTimeline.Builder().AddNote(10f).Build();
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        var start = PlayUntilBlink(s, 9f, 30f, 30f, new FakeWalls());
        Assert.InRange(start!.Value, 13f, 13f + (2 * Frame));
    }

    [Fact]
    public void SongEnd_UnknownEndIsIgnored()
    {
        var timeline = new BlinkTimeline.Builder().AddNote(10f).Build();
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        Assert.Equal(BlinkBlocker.None, s.Evaluate(14f, 1f, 0f, Defaults, new FakeWalls()));
    }

    [Fact]
    public void HeadWall_InLookahead_Blocks_SideWallDoesNot()
    {
        var timeline = new BlinkTimeline.Builder()
            .AddNote(10f).AddNote(60f)
            .AddWall(18f, 19f, 1) // side wall
            .AddWall(30f, 34f, 2) // head wall
            .Build();
        var walls = new FakeWalls(2);
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 0f);

        // At 13.1 the window 13.1..21.1 only has the side wall: allowed.
        Assert.Equal(BlinkBlocker.None, s.Evaluate(13.1f, 1f, 200f, Defaults, walls));

        // At 23 the head wall at 30 is inside 23..31.
        Assert.Equal(BlinkBlocker.HeadWall, s.Evaluate(23f, 1f, 200f, Defaults, walls));

        // At 34.5 the head wall has passed and 60 is far away.
        Assert.Equal(BlinkBlocker.None, s.Evaluate(34.5f, 1f, 200f, Defaults, walls));
    }

    [Fact]
    public void HeadWall_StillPassing_Blocks()
    {
        var timeline = new BlinkTimeline.Builder().AddWall(5f, 25f, 1).Build();
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        Assert.Equal(BlinkBlocker.HeadWall, s.Evaluate(20f, 1f, 200f, Defaults, new FakeWalls(1)));
        Assert.Equal(BlinkBlocker.None, s.Evaluate(25.1f, 1f, 200f, Defaults, new FakeWalls(1)));
    }

    [Fact]
    public void HeadWall_BehindThePlayer_DoesNotBlock()
    {
        // Walls already gone do not matter, even if they ended within QuietBefore.
        var timeline = new BlinkTimeline.Builder().AddWall(5f, 9f, 1).Build();
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        Assert.Equal(BlinkBlocker.None, s.Evaluate(10f, 1f, 200f, Defaults, new FakeWalls(1)));
    }

    [Fact]
    public void OverlappingWalls_AnyHeadWallBlocks()
    {
        var timeline = new BlinkTimeline.Builder()
            .AddWall(10f, 30f, 1)
            .AddWall(12f, 13f, 2)
            .AddWall(14f, 40f, 3)
            .Build();
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        Assert.Equal(BlinkBlocker.None, s.Evaluate(20f, 1f, 200f, Defaults, new FakeWalls(2)));
        Assert.Equal(BlinkBlocker.HeadWall, s.Evaluate(20f, 1f, 200f, Defaults, new FakeWalls(3)));
    }

    [Fact]
    public void Blink_RunsForDurationWithFades_ThenStops()
    {
        var timeline = new BlinkTimeline.Builder().Build();
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        var walls = new FakeWalls();

        // Start at t = 3 (quiet observed).
        var t = 0f;
        while (!s.IsBlinking)
        {
            Assert.Equal(0f, s.Tick(t, 1f, 200f, false, Defaults, walls));
            t += 0.1f;
        }

        var peak = 0f;
        var frames = 0;
        while (s.IsBlinking)
        {
            t += 0.1f;
            var a = s.Tick(t, 1f, 200f, false, Defaults, walls);
            peak = System.Math.Max(peak, a);
            frames++;
            Assert.InRange(a, 0f, 1f);
        }

        Assert.Equal(1f, peak);
        Assert.InRange(frames, 29, 31); // 3 s at 0.1 s frames
    }

    [Fact]
    public void Cooldown_PreventsSecondBlinkAndCarriesAcrossSongs()
    {
        var cooldown = ReadyCooldown();
        var settings = new BlinkSettings(3f, 8f, 3f, 60f);

        // Song 1: blinks once, then plays another 20 s of silence: no second blink.
        var song1 = new BlinkScheduler(new BlinkTimeline.Builder().Build(), cooldown, 0f);
        var first = PlayUntilBlink(song1, 0f, 10f, 200f, new FakeWalls(), settings);
        Assert.NotNull(first);
        PlayUntilBlink(song1, first!.Value + Frame, 30f, 200f, new FakeWalls(), settings);
        Assert.False(song1.IsBlinking);
        Assert.InRange(cooldown.PlayedSinceLastBlink, 26f, 28f);

        // Song 2 (new scheduler, same cooldown): needs the remaining ~33 s of play.
        var song2 = new BlinkScheduler(new BlinkTimeline.Builder().Build(), cooldown, 0f);
        var start = PlayUntilBlink(song2, 0f, 100f, 200f, new FakeWalls(), settings);
        Assert.NotNull(start);
        Assert.InRange(start!.Value, 31f, 35f);
    }

    [Fact]
    public void Pause_FreezesBlinkAndCooldownAndHidesOverlay()
    {
        var cooldown = ReadyCooldown();
        var s = new BlinkScheduler(new BlinkTimeline.Builder().Build(), cooldown, 0f);
        var walls = new FakeWalls();
        Assert.NotNull(PlayUntilBlink(s, 0f, 5f, 200f, walls));
        var t = 3.5f;
        s.Tick(t, 1f, 200f, false, Defaults, walls);
        var elapsed = s.Elapsed;
        var played = cooldown.PlayedSinceLastBlink;

        // Paused for many frames: song time frozen, overlay hidden, nothing advances.
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(0f, s.Tick(t, 1f, 200f, true, Defaults, walls));
        }

        Assert.True(s.IsBlinking);
        Assert.Equal(elapsed, s.Elapsed);
        Assert.Equal(played, cooldown.PlayedSinceLastBlink);

        // Resume: continues where it left off.
        var a = s.Tick(t + Frame, 1f, 200f, false, Defaults, walls);
        Assert.True(a > 0f);
        Assert.Equal(elapsed + Frame, s.Elapsed, 4);
    }

    [Fact]
    public void SeekBackwards_RestartsQuietWindowAndCancelsBlink()
    {
        // Practice: notes at 10 and 40. Blink starts at 13; then the player seeks back to 5.
        var timeline = new BlinkTimeline.Builder().AddNote(10f).AddNote(40f).Build();
        var cooldown = ReadyCooldown();
        var s = new BlinkScheduler(timeline, cooldown, 0f);
        Assert.NotNull(PlayUntilBlink(s, 9f, 20f, 200f, new FakeWalls()));
        Assert.True(s.IsBlinking);

        var alpha = s.Tick(5f, 1f, 200f, false, Defaults, new FakeWalls());
        Assert.Equal(0f, alpha);
        Assert.False(s.IsBlinking);
        Assert.Equal(5f, s.SegmentStart);
    }

    [Fact]
    public void SeekBackwards_IntoQuietGap_WaitsForQuietBeforeAgain()
    {
        // Notes only at 50. Cooldown short so a second blink can happen after the seek.
        var settings = new BlinkSettings(3f, 8f, 3f, 0f);
        var timeline = new BlinkTimeline.Builder().AddNote(50f).Build();
        var s = new BlinkScheduler(timeline, new BlinkCooldown(), 0f);
        var walls = new FakeWalls();

        for (var t = 20f; t < 21f; t += Frame)
        {
            s.Tick(t, 1f, 200f, false, settings, walls);
        }

        // Seek back to 10: first possible start is 13 again (quiet re-observed), not immediately.
        s.Tick(10f, 1f, 200f, false, settings, walls);
        Assert.False(s.IsBlinking);
        Assert.Equal(BlinkBlocker.TooSoonAfterStart, s.Evaluate(11f, 1f, 200f, settings, walls));
        var start = PlayUntilBlink(s, 10f + Frame, 30f, 200f, walls, settings);
        Assert.InRange(start!.Value, 13f, 13f + (2 * Frame));
    }

    [Theory]
    [InlineData(3.5f)] // back
    [InlineData(40f)] // forward
    public void SeekWhilePaused_CancelsBlinkAndRestartsQuietWindow(float target)
    {
        var timeline = new BlinkTimeline.Builder().AddNote(100f).Build();
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        var walls = new FakeWalls();
        Assert.NotNull(PlayUntilBlink(s, 0f, 10f, 200f, walls));
        s.Tick(3.5f, 1f, 200f, false, Defaults, walls);
        s.Tick(4f, 1f, 200f, false, Defaults, walls);
        Assert.True(s.IsBlinking);

        // Paused; the player skips from the pause menu, then resumes.
        s.Tick(4f, 1f, 200f, true, Defaults, walls);
        s.Tick(4f, 1f, 200f, true, Defaults, walls);
        Assert.Equal(0f, s.Tick(target, 1f, 200f, true, Defaults, walls));
        Assert.False(s.IsBlinking);
        Assert.Equal(target, s.SegmentStart);

        Assert.Equal(0f, s.Tick(target + Frame, 1f, 200f, false, Defaults, walls));
        Assert.False(s.IsBlinking);

        // With the cooldown out of the way, the quiet window must be observed again from the target.
        var noCooldown = new BlinkSettings(3f, 8f, 3f, 0f);
        Assert.Equal(BlinkBlocker.TooSoonAfterStart, s.Evaluate(target + Frame, 1f, 200f, noCooldown, walls));
    }

    [Fact]
    public void SeekForward_IsNotPlayedTime()
    {
        var cooldown = new BlinkCooldown();
        var s = new BlinkScheduler(new BlinkTimeline.Builder().AddNote(1000f).Build(), cooldown, 0f);
        s.Tick(0f, 1f, 2000f, false, Defaults, new FakeWalls());
        s.Tick(100f, 1f, 2000f, false, Defaults, new FakeWalls());
        Assert.Equal(0f, cooldown.PlayedSinceLastBlink);
        Assert.Equal(100f, s.SegmentStart);
    }

    [Fact]
    public void Cooldown_NotReady_Blocks()
    {
        var s = new BlinkScheduler(new BlinkTimeline.Builder().Build(), new BlinkCooldown(), 0f);
        Assert.Equal(BlinkBlocker.Cooldown, s.Evaluate(50f, 1f, 200f, Defaults, new FakeWalls()));
    }

    [Fact]
    public void FreshSession_FirstBlinkAfterOneFullCooldown()
    {
        var settings = new BlinkSettings(3f, 8f, 3f, 20f);
        var cooldown = new BlinkCooldown();
        var s = new BlinkScheduler(new BlinkTimeline.Builder().Build(), cooldown, 0f);
        var start = PlayUntilBlink(s, 0f, 60f, 200f, new FakeWalls(), settings);
        Assert.InRange(start!.Value, 20f - Frame, 20f + (2 * Frame));
    }

    [Fact]
    public void FasterSongSpeed_UsesRealSecondsForCooldownAndBlink()
    {
        // At 2x speed, 20 played seconds take 40 song seconds.
        var settings = new BlinkSettings(3f, 8f, 3f, 20f);
        var cooldown = new BlinkCooldown();
        var s = new BlinkScheduler(new BlinkTimeline.Builder().Build(), cooldown, 0f);
        var start = PlayUntilBlink(s, 0f, 100f, 500f, new FakeWalls(), settings, timeScale: 2f);
        Assert.InRange(start!.Value, 40f - (2 * Frame), 40f + (4 * Frame));

        // The blink needs 6 song seconds at 2x before the end.
        var s2 = new BlinkScheduler(new BlinkTimeline.Builder().Build(), ReadyCooldown(), 0f);
        Assert.Equal(BlinkBlocker.SongEnding, s2.Evaluate(95f, 2f, 100f, settings, new FakeWalls()));
        Assert.Equal(BlinkBlocker.None, s2.Evaluate(94f, 2f, 100f, settings, new FakeWalls()));
    }

    [Fact]
    public void SpeedUp_QuietBeforeIsRealSeconds()
    {
        // At 1.5x, 3 real seconds of quiet are 4.5 song seconds: last note at 10 -> start at 14.5.
        var timeline = new BlinkTimeline.Builder().AddNote(10f).AddNote(60f).Build();
        var s = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        Assert.Equal(BlinkBlocker.NotesNearby, s.Evaluate(14f, 1.5f, 200f, Defaults, new FakeWalls()));
        Assert.Equal(BlinkBlocker.None, s.Evaluate(14.6f, 1.5f, 200f, Defaults, new FakeWalls()));
        var start = PlayUntilBlink(s, 9f, 60f, 200f, new FakeWalls(), timeScale: 1.5f);
        Assert.InRange(start!.Value, 14.5f, 14.5f + (3 * Frame));
    }

    [Fact]
    public void SpeedUp_ClearAheadIsRealSeconds()
    {
        // Gap 10..25 song s. At 1x a blink fits (13 + 8 < 25); at 1.5x it needs 4.5 s quiet and
        // 12 song s clear, so 14.5 + 12 > 25: no blink.
        var timeline = new BlinkTimeline.Builder().AddNote(10f).AddNote(25f).Build();
        var normal = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        Assert.NotNull(PlayUntilBlink(normal, 9f, 25f, 200f, new FakeWalls()));

        var fast = new BlinkScheduler(timeline, ReadyCooldown(), 0f);
        Assert.Null(PlayUntilBlink(fast, 9f, 25f, 200f, new FakeWalls(), timeScale: 1.5f));

        // A head wall 11 song s ahead is outside 8 s at 1x but inside 12 song s at 1.5x.
        var walls = new BlinkTimeline.Builder().AddWall(31f, 32f, 1).Build();
        var w = new BlinkScheduler(walls, ReadyCooldown(), 0f);
        Assert.Equal(BlinkBlocker.None, w.Evaluate(20f, 1f, 200f, Defaults, new FakeWalls(1)));
        Assert.Equal(BlinkBlocker.HeadWall, w.Evaluate(20f, 1.5f, 200f, Defaults, new FakeWalls(1)));
    }

    [Fact]
    public void ZeroTimeScale_FallsBackToOne()
    {
        var s = new BlinkScheduler(new BlinkTimeline.Builder().Build(), ReadyCooldown(), 0f);
        Assert.Equal(BlinkBlocker.SongEnding, s.Evaluate(98f, 0f, 100f, Defaults, new FakeWalls()));
    }

    [Fact]
    public void ClearAheadShorterThanDuration_StillCoversTheBlink()
    {
        // ClearAhead 1 but duration 3: a note at t + 2 must still block.
        var settings = new BlinkSettings(0f, 1f, 3f, 0f);
        Assert.Equal(3f, settings.ClearAhead);
        var s = new BlinkScheduler(new BlinkTimeline.Builder().AddNote(12f).Build(), ReadyCooldown(), 0f);
        Assert.Equal(BlinkBlocker.NotesNearby, s.Evaluate(10f, 1f, 200f, settings, new FakeWalls()));
    }

    [Fact]
    public void ForceStart_IgnoresRulesAndKeepsCooldown()
    {
        var cooldown = new BlinkCooldown();
        cooldown.AddPlayed(5f);
        var s = new BlinkScheduler(new BlinkTimeline.Builder().AddNote(1f).Build(), cooldown, 0f);
        s.ForceStart();
        Assert.True(s.IsBlinking);
        s.Tick(1f, 1f, 200f, false, Defaults, new FakeWalls());
        var a = s.Tick(1.2f, 1f, 200f, false, Defaults, new FakeWalls());
        Assert.InRange(a, 0.6f, 0.7f);
        Assert.InRange(cooldown.PlayedSinceLastBlink, 5.1f, 5.3f);
    }

    [Fact]
    public void Constructor_RejectsNulls()
    {
        Assert.Throws<System.ArgumentNullException>(() => new BlinkScheduler(null!, new BlinkCooldown(), 0f));
        Assert.Throws<System.ArgumentNullException>(() => new BlinkScheduler(new BlinkTimeline.Builder().Build(), null!, 0f));
    }
}
