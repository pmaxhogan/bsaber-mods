using System;
using System.Collections.Generic;
using System.Linq;
using MaxTweaks.Core.ObsClips;
using Xunit;

namespace MaxTweaks.Core.Tests.ObsClips;

public class ClipPickerTests
{
    private const double Clip = 10.0;

    /// <summary>The oldest a stretch's start may be when it is claimed: the usable buffer.</summary>
    private const double UsableBuffer = 60.0 - ClipPicker.HeadMarginSeconds;

    [Fact]
    public void Bar_FallsAsTheBudgetBuilds()
    {
        Assert.Equal(double.PositiveInfinity, ClipPicker.Bar(0.2));
        Assert.Equal(1.9, ClipPicker.Bar(ClipPicker.MinBudget), 6);
        Assert.Equal(1.36, ClipPicker.Bar(1.0), 6);
        Assert.Equal(1.0, ClipPicker.Bar(1.5), 6);
        Assert.True(ClipPicker.Bar(2.0) < ClipPicker.Bar(1.5));
    }

    [Fact]
    public void Burst_IsClaimedJustBeforeItLeavesTheBuffer()
    {
        var sim = new Sim();
        sim.Play(30, 2);
        sim.Play(10, 8);
        sim.Play(80, 2);

        var (at, start, pick) = Assert.Single(sim.Picks);
        Assert.InRange(start, 29.0, 31.0);
        Assert.Equal(Clip, pick.Seconds);
        Assert.Equal(ClipReason.Notes, pick.Reason);
        Assert.True(pick.Interest > 2.0);
        Assert.InRange(pick.Hits, 70, 82);

        // Held as long as the buffer allows, in case something better comes.
        Assert.InRange(pick.SecondsAgo, UsableBuffer - ClipPicker.ExpiryMarginSeconds - 0.6, UsableBuffer + 0.6);
        Assert.InRange(at, 80.0, 86.0);
    }

    [Fact]
    public void BetterLaterStretch_GetsTheBudget()
    {
        // Budget for one clip only by the time the first burst would leave the buffer.
        var sim = new Sim(startBudget: 0.3);
        sim.Play(30, 2);
        sim.Play(10, 6);
        sim.Play(20, 2);
        sim.Play(10, 10);
        sim.Play(60, 2);

        var (_, start, _) = Assert.Single(sim.Picks);
        Assert.InRange(start, 59.0, 61.0);
    }

    [Fact]
    public void BothStretches_AreClipped_WhenTheBudgetCoversBoth()
    {
        var sim = new Sim(startBudget: 2.0);
        sim.Play(30, 2);
        sim.Play(10, 6);
        sim.Play(20, 2);
        sim.Play(10, 10);
        sim.Play(60, 2);

        Assert.Equal(2, sim.Picks.Count);
        Assert.InRange(sim.Picks[0].Start, 29.0, 31.0);
        Assert.InRange(sim.Picks[1].Start, 59.0, 61.0);
    }

    [Fact]
    public void NoClip_BeforeTwentySecondsOfPlay()
    {
        var sim = new Sim(startBudget: 2.0);
        sim.Play(5, 2);
        sim.Play(10, 9);

        Assert.Null(sim.Picker.Finish(sim.Now, armed: true));
    }

    [Fact]
    public void Finish_TakesTheBestStretchStillInTheBuffer()
    {
        var sim = new Sim();
        sim.Play(30, 2);
        sim.Play(10, 8);
        sim.Play(5, 2);
        Assert.Empty(sim.Picks);

        var pick = sim.Picker.Finish(sim.Now, armed: true);

        Assert.NotNull(pick);
        Assert.InRange(sim.Now - pick!.SecondsAgo, 29.0, 31.0);
        Assert.InRange(sim.Picker.Budget, 0.36, 0.39);

        // The level is over: nothing more until the next one starts.
        Assert.Null(sim.Picker.Advance(sim.Now + 1.0, playing: true, armed: true));
        Assert.Null(sim.Picker.Finish(sim.Now + 2.0, armed: true));
    }

    [Fact]
    public void Finish_OnlyConsidersTheBest_AndRespectsTheBar()
    {
        // A flat song: the best stretch is ordinary (interest about 1), and the budget is too small for that.
        var sim = new Sim(startBudget: 0.3);
        sim.Play(40, 3);

        Assert.Null(sim.Picker.Finish(sim.Now, armed: true));
    }

    [Fact]
    public void NotArmed_PicksNothing_ButTheBudgetBuildsToItsCap()
    {
        var sim = new Sim { Armed = false };
        sim.Play(30, 2);
        sim.Play(10, 8);
        sim.Play(300, 2);

        Assert.Empty(sim.Picks);
        Assert.Equal(ClipPicker.MaxBudget, sim.Picker.Budget, 6);
        Assert.Null(sim.Picker.Finish(sim.Now, armed: false));
    }

    [Fact]
    public void Refund_GivesTheBudgetBack_UpToTheCap()
    {
        var picker = new ClipPicker(new ClipSettings(), startBudget: 0.5);
        picker.Refund();
        Assert.Equal(1.5, picker.Budget, 6);
        picker.Refund();
        Assert.Equal(ClipPicker.MaxBudget, picker.Budget, 6);
    }

    [Fact]
    public void BudgetOnlyBuildsWhilePlaying()
    {
        var sim = new Sim(startBudget: 0.0);
        sim.Play(60, 0);
        Assert.Equal(0.5, sim.Picker.Budget, 2);
        sim.Pause(600);
        Assert.Equal(0.5, sim.Picker.Budget, 2);
    }

    [Fact]
    public void QuietOrMissedStretches_AreNeverClips()
    {
        // Under 5 hits in any 10 s.
        var sparse = new Sim(startBudget: 2.0);
        sparse.Play(400, 0.3);
        Assert.Empty(sparse.Picks);
        Assert.Null(sparse.Picker.Finish(sparse.Now, armed: true));

        // Plenty of notes, none hit.
        var missed = new Sim(startBudget: 2.0);
        missed.Play(400, 6, missShare: 1.0);
        Assert.Empty(missed.Picks);
    }

    [Fact]
    public void Misses_CountAgainstAStretch()
    {
        // Two equally dense bursts; half of the second one is missed, half of the notes that are left hit.
        var sim = new Sim(startBudget: 0.3);
        sim.Play(30, 2);
        sim.Play(10, 8);
        sim.Play(20, 2);
        sim.Play(10, 16, missShare: 0.5);
        sim.Play(60, 2);

        var (_, start, _) = Assert.Single(sim.Picks);
        Assert.InRange(start, 29.0, 31.0);
    }

    [Fact]
    public void Pause_SplitsTheCandidates()
    {
        var sim = new Sim(startBudget: 2.0);
        sim.Play(30, 2);
        sim.Play(6, 9);
        var pausedAt = sim.Now;
        sim.Pause(5);
        var resumedAt = sim.Now;
        sim.Play(4, 9);
        sim.Play(120, 2);

        Assert.NotEmpty(sim.Picks);
        foreach (var (_, start, pick) in sim.Picks)
        {
            var end = start + pick.Seconds;
            Assert.True(
                end <= pausedAt - ClipPicker.EndGuardSeconds + 0.01 || start >= resumedAt + ClipPicker.StartGuardSeconds - 0.01,
                $"stretch {start:0.0}-{end:0.0} touches the pause {pausedAt:0.0}-{resumedAt:0.0}");
        }
    }

    [Fact]
    public void LongPause_LetsTheStretchBeforeItRunOut_OrClaimsItInTime()
    {
        // A burst, then a pause far longer than the buffer: the claim has to happen during the pause.
        var sim = new Sim();
        sim.Play(30, 2);
        sim.Play(10, 8);
        sim.Play(3, 2);
        sim.Pause(200);

        var (at, start, pick) = Assert.Single(sim.Picks);
        Assert.InRange(start, 29.0, 31.0);
        Assert.True(pick.SecondsAgo <= UsableBuffer + 0.6);
        Assert.True(at < 43.0 + 60.0);
    }

    [Fact]
    public void NothingFromBeforeTheLevelStarted()
    {
        // The clock runs long before the level (menu time); the buffer is full of menu.
        var sim = new Sim(startBudget: 2.0, levelStart: 1000.0);
        sim.Play(9, 9);
        sim.Play(120, 2);

        Assert.NotEmpty(sim.Picks);
        Assert.All(sim.Picks, p => Assert.True(p.Start >= 1000.0 + ClipPicker.StartGuardSeconds - 0.01));
    }

    [Fact]
    public void NewLevel_ForgetsThePreviousOne()
    {
        var sim = new Sim(startBudget: 2.0);
        sim.Play(30, 2);
        sim.Play(10, 8);
        sim.Play(2, 2);
        var secondLevel = sim.Now;
        sim.Picker.StartLevel(sim.Now);
        sim.Play(150, 3);

        Assert.All(sim.Picks, p => Assert.True(p.Start >= secondLevel + ClipPicker.StartGuardSeconds - 0.01));
    }

    [Fact]
    public void NothingFromBeforeTheBufferWasRunning()
    {
        var sim = new Sim(startBudget: 2.0);
        sim.Picker.BufferSince = 45.0;
        sim.Play(30, 2);
        sim.Play(10, 8);
        sim.Play(120, 2);

        Assert.All(sim.Picks, p => Assert.True(p.Start >= 45.0 + ClipPicker.BufferStartMarginSeconds - 0.01));
    }

    [Fact]
    public void ShortBuffer_StillWorksWithLessHindsight()
    {
        var sim = new Sim();
        sim.Picker.BufferSeconds = 20.0;
        sim.Play(30, 2);
        sim.Play(10, 8);
        sim.Play(60, 2);

        var (_, start, pick) = Assert.Single(sim.Picks);
        Assert.InRange(start, 29.0, 31.0);
        Assert.True(pick.SecondsAgo <= 20.0 - ClipPicker.HeadMarginSeconds + 0.6);
    }

    [Fact]
    public void BufferTooShortForAClip_PicksNothing()
    {
        var sim = new Sim(startBudget: 2.0);
        sim.Picker.BufferSeconds = 10.0;
        sim.Play(30, 2);
        sim.Play(10, 8);
        sim.Play(60, 2);

        Assert.Empty(sim.Picks);
    }

    [Fact]
    public void ALongFrame_DoesNotBreakAnything()
    {
        var sim = new Sim();
        sim.Play(30, 3);
        Assert.Null(sim.Picker.Advance(sim.Now + 500.0, playing: true, armed: true));
        Assert.Null(sim.Picker.Advance(sim.Now + 400.0, playing: true, armed: true));
    }

    [Theory]
    [InlineData("flat")]
    [InlineData("bursty")]
    [InlineData("waves")]
    [InlineData("paused")]
    public void HalfAnHourOfPlay_GivesAboutOneClipPerTwoMinutes(string shape)
    {
        const double total = 1800.0;
        var sim = new Sim();
        var random = new Lcg(12345);
        var pauses = new List<(double From, double To)>();
        while (sim.Played < total)
        {
            switch (shape)
            {
                case "flat":
                    sim.Play(5, 3.5 + random.Next());
                    break;
                case "bursty":
                    sim.Play(25 + (20 * random.Next()), 2.0 + random.Next());
                    sim.Play(6 + (6 * random.Next()), 7.0 + (3 * random.Next()));
                    break;
                case "waves":
                    sim.Play(5, 4.0 + (3.0 * Math.Sin(sim.Now / 17.0)) + random.Next());
                    break;
                default:
                    sim.Play(20 + (60 * random.Next()), 2.0 + (5 * random.Next()));
                    var from = sim.Now;
                    sim.Pause(2 + (80 * random.Next()));
                    pauses.Add((from, sim.Now));
                    break;
            }
        }

        // The budget allows 1 to start with plus one per 120 s of play; a song this varied should use most of it.
        Assert.InRange(sim.Picks.Count, 11, 16);

        var ordered = sim.Picks.OrderBy(p => p.Start).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var (at, start, pick) = ordered[i];
            Assert.Equal(Clip, pick.Seconds);
            Assert.True(pick.Hits >= ClipPicker.MinHits);
            Assert.InRange(pick.SecondsAgo, Clip, UsableBuffer + 0.6);
            Assert.True(start + pick.Seconds <= at + 0.01);
            if (i > 0)
            {
                Assert.True(start >= ordered[i - 1].Start + Clip + ClipPicker.MinGapSeconds - 0.01, "clips too close together");
            }

            foreach (var (from, to) in pauses)
            {
                Assert.True(
                    start + pick.Seconds <= from - ClipPicker.EndGuardSeconds + 0.01 || start >= to + ClipPicker.StartGuardSeconds - 0.01,
                    $"stretch {start:0.0}-{start + pick.Seconds:0.0} touches the pause {from:0.0}-{to:0.0}");
            }
        }
    }

    [Fact]
    public void HeartRateClimb_MakesAnOrdinaryStretchAClip()
    {
        var sim = new Sim(startBudget: 0.3);

        // Flat notes throughout. Heart: steady at 150, then up to 172 between 200 s and 215 s.
        sim.HeartAt = time => time < 200 ? 150 : time < 215 ? 150 + (int)((time - 200) * 22 / 15) : 172;
        sim.Play(300, 3);

        var heart = Assert.Single(sim.Picks, p => p.Pick.Reason == ClipReason.HeartRate);
        Assert.InRange(heart.Start, 195.0, 212.0);
        Assert.True(heart.Pick.Interest > 1.9);
    }

    [Fact]
    public void HeartRate_RampFromRestAtTheStart_IsNotAClimb()
    {
        var sim = new Sim(startBudget: 2.0);
        sim.HeartAt = time => time < 40 ? 95 + (int)(time * 1.5) : 155;
        sim.Play(140, 3);

        Assert.DoesNotContain(sim.Picks, p => p.Pick.Reason == ClipReason.HeartRate);
    }

    [Fact]
    public void HeartRate_LongWarmUp_IsNotAClimb_EvenWhenJudgedLater()
    {
        // As seen in the first VR session: the heart rate is still coming up from rest a minute and a
        // half into the first song, and a stretch from then is only judged 50 s later.
        var sim = new Sim(startBudget: 2.0);
        sim.HeartAt = time => time < 60 ? 100 + (int)(time * 17 / 60) : time < 75 ? 117 : time < 90 ? 117 + (int)((time - 75) * 12 / 15) : 126;
        sim.Play(240, 3);

        Assert.DoesNotContain(sim.Picks, p => p.Pick.Reason == ClipReason.HeartRate);
        Assert.NotEmpty(sim.Picks);
    }

    [Fact]
    public void HeartRate_OneLongClimb_IsOneClip()
    {
        // As seen in VR (clips at 61m43s and 62m14s of one song): a climb that lasts 45 s credits two
        // neighbouring stretches. One clip per climb is enough.
        var sim = new Sim(startBudget: 2.0);
        sim.HeartAt = time => time < 300 ? 140 : time < 345 ? 140 + (int)((time - 300) * 36 / 45) : 176;
        sim.Play(450, 3);

        Assert.Single(sim.Picks, p => p.Pick.Reason == ClipReason.HeartRate);
    }

    [Fact]
    public void HeartRate_TwoSeparateClimbs_AreTwoClips()
    {
        var sim = new Sim(startBudget: 2.0);
        sim.HeartAt = time => time < 300 ? 140 : time < 315 ? 140 + (int)((time - 300) * 20 / 15) : time < 420 ? 160 : time < 435 ? 160 + (int)((time - 420) * 20 / 15) : 180;
        sim.Play(540, 3);

        Assert.Equal(2, sim.Picks.Count(p => p.Pick.Reason == ClipReason.HeartRate));
    }

    [Fact]
    public void HeartRate_BackUpAfterARest_IsNotAClimb()
    {
        var sim = new Sim(startBudget: 2.0);

        // Steady 160, a rest in the menu, then a new song where it comes back from 110 to 160.
        sim.HeartAt = time => time < 200 ? 160 : time < 240 ? 110 + (int)((time - 200) * 50 / 40) : 160;
        sim.Play(200, 3);
        sim.Picker.Finish(sim.Now, armed: false);
        sim.Picker.StartLevel(sim.Now);
        sim.Play(150, 3);

        Assert.DoesNotContain(sim.Picks, p => p.Pick.Reason == ClipReason.HeartRate);
    }

    [Fact]
    public void HeartRate_IsIgnoredWhenSwitchedOff_AndWithoutAStrap()
    {
        var off = new Sim(startBudget: 0.3);
        off.Settings.UseHeartRate = false;
        off.HeartAt = time => time < 200 ? 150 : time < 215 ? 150 + (int)((time - 200) * 22 / 15) : 172;
        off.Play(300, 3);
        Assert.DoesNotContain(off.Picks, p => p.Pick.Reason == ClipReason.HeartRate);

        // No readings at all: the note picking works on its own.
        var none = new Sim();
        none.Play(30, 2);
        none.Play(10, 8);
        none.Play(80, 2);
        Assert.Single(none.Picks);
    }

    [Fact]
    public void StaleHeartRate_DoesNotCount()
    {
        var sim = new Sim(startBudget: 0.3);

        // The strap drops out at 195 s and comes back at 230 s with a much higher reading.
        sim.HeartAt = time => time < 195 ? 150 : time < 230 ? 0 : 175;
        sim.Play(300, 3);

        Assert.DoesNotContain(sim.Picks, p => p.Pick.Reason == ClipReason.HeartRate && p.Start < 220.0);
    }

    [Fact]
    public void HeartRate_ReadingsWhilePaused_DoNotMoveTheAverage()
    {
        // Resting in the pause menu for minutes must not make coming back up look like a climb above "average".
        var sim = new Sim(startBudget: 2.0);
        sim.HeartAt = time => time < 200 ? 160 : time < 500 ? 100 : time < 530 ? 100 + (int)((time - 500) * 2) : 160;
        sim.Play(200, 3);
        sim.Pause(300);
        sim.Play(150, 3);

        Assert.DoesNotContain(sim.Picks, p => p.Pick.Reason == ClipReason.HeartRate);
    }

    [Fact]
    public void HeartRate_StrapPutOnMidSong_CountsOnlyOnceThereAreReadings()
    {
        var sim = new Sim(startBudget: 2.0);
        sim.HeartAt = time => time < 150 ? 0 : 150;
        sim.Play(300, 3);

        Assert.DoesNotContain(sim.Picks, p => p.Pick.Reason == ClipReason.HeartRate);
        Assert.NotEmpty(sim.Picks);
    }

    [Fact]
    public void Target_SetsTheRate()
    {
        var sim = new Sim(startBudget: 0.0);
        sim.Settings.TargetSecondsPerClip = 60.0;
        sim.Play(60, 0);
        Assert.Equal(1.0, sim.Picker.Budget, 2);

        // Nonsense values do not divide by zero or flood.
        sim.Settings.TargetSecondsPerClip = 0.0;
        sim.Play(10, 0);
        Assert.Equal(2.0, sim.Picker.Budget, 2);
    }

    /// <summary>Drives a picker with a note rate, in 50 ms frames.</summary>
    private sealed class Sim
    {
        private const double Frame = 0.05;
        private double _noteDebt;
        private double _missDebt;
        private double _nextHeart;

        public Sim(double startBudget = ClipPicker.StartBudget, double levelStart = 0.0)
        {
            Picker = new ClipPicker(Settings, startBudget);
            Now = levelStart;
            _nextHeart = levelStart;
            Picker.StartLevel(levelStart);
        }

        public ClipSettings Settings { get; } = new();

        public ClipPicker Picker { get; }

        public double Now { get; private set; }

        public double Played { get; private set; }

        public bool Armed { get; set; } = true;

        /// <summary>Beats per minute by time; 0 = no reading.</summary>
        public Func<double, int>? HeartAt { get; set; }

        /// <summary>When each pick was made, where its stretch starts, and the pick.</summary>
        public List<(double At, double Start, ClipPick Pick)> Picks { get; } = new();

        /// <param name="missShare">Share of the notes that are missed instead of hit.</param>
        public void Play(double seconds, double notesPerSecond, double missShare = 0.0)
        {
            for (var left = seconds; left > Frame / 2; left -= Frame)
            {
                Step(playing: true);
                Played += Frame;
                _noteDebt += Math.Max(0.0, notesPerSecond) * Frame;
                while (_noteDebt >= 1.0)
                {
                    _noteDebt -= 1.0;
                    _missDebt += missShare;
                    if (_missDebt >= 1.0)
                    {
                        _missDebt -= 1.0;
                        Picker.NoteMissed(Now);
                    }
                    else
                    {
                        Picker.NoteHit(Now);
                    }
                }
            }
        }

        public void Pause(double seconds)
        {
            for (var left = seconds; left > Frame / 2; left -= Frame)
            {
                Step(playing: false);
            }
        }

        private void Step(bool playing)
        {
            Now += Frame;
            if (HeartAt != null && Now >= _nextHeart)
            {
                _nextHeart += 1.0;
                var bpm = HeartAt(Now);
                if (bpm > 0)
                {
                    Picker.HeartRate(Now, bpm);
                }
            }

            var pick = Picker.Advance(Now, playing, Armed);
            if (pick != null)
            {
                Picks.Add((Now, Now - pick.SecondsAgo, pick));
            }
        }
    }

    /// <summary>A fixed pseudo-random sequence in [0, 1), the same on every runtime.</summary>
    private sealed class Lcg
    {
        private uint _state;

        public Lcg(uint seed)
        {
            _state = seed;
        }

        public double Next()
        {
            _state = (_state * 1664525u) + 1013904223u;
            return (_state >> 8) / (double)(1 << 24);
        }
    }
}
