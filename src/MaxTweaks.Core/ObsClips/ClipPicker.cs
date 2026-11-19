using System;
using System.Collections.Generic;

namespace MaxTweaks.Core.ObsClips;

/// <summary>Why a stretch of play was picked.</summary>
public enum ClipReason
{
    /// <summary>More notes hit than in the rest of the song.</summary>
    Notes,

    /// <summary>The heart rate climbed quickly, above its recent level.</summary>
    HeartRate,
}

/// <summary>A stretch of play worth keeping, relative to the moment it was picked.</summary>
public sealed class ClipPick
{
    public ClipPick(double secondsAgo, double seconds, ClipReason reason, double interest, int hits)
    {
        SecondsAgo = secondsAgo;
        Seconds = seconds;
        Reason = reason;
        Interest = interest;
        Hits = hits;
    }

    /// <summary>How long before the pick the stretch started.</summary>
    public double SecondsAgo { get; }

    /// <summary>How long the stretch is.</summary>
    public double Seconds { get; }

    public ClipReason Reason { get; }

    /// <summary>1.0 = as many notes hit as the song's average stretch; a heart-rate climb adds to it.</summary>
    public double Interest { get; }

    /// <summary>Notes hit in the stretch.</summary>
    public int Hits { get; }
}

/// <summary>What the player can change about picking (read live, so a change applies at once).</summary>
public sealed class ClipSettings
{
    /// <summary>Length of one clip.</summary>
    public double ClipSeconds { get; set; } = 10.0;

    /// <summary>One clip per this much unpaused play, on average.</summary>
    public double TargetSecondsPerClip { get; set; } = 120.0;

    public bool UseHeartRate { get; set; } = true;
}

/// <summary>
/// Decides which stretches of a song are worth a clip, at about one clip per
/// <see cref="ClipSettings.TargetSecondsPerClip"/> of unpaused play.
///
/// It relies on OBS's replay buffer holding the last <see cref="BufferSeconds"/> of video, so a
/// stretch does not have to be judged the moment it ends: every stretch still in the buffer stays a
/// candidate, and the best one is only claimed just before it would scroll out of the buffer (or
/// when the song ends). A better stretch that comes later therefore replaces it.
///
/// - A stretch is <see cref="ClipSettings.ClipSeconds"/> long and is scored twice a second by the
///   notes hit in it, times the share of notes that were hit (misses and bad cuts count against
///   it). Its interest is that score over the song's average so far, so 1.0 is an ordinary stretch
///   on any map, plus up to <see cref="HeartMaxPoints"/> for a heart rate that climbed during it.
/// - Budget: one clip's worth per TargetSecondsPerClip of play, starting at <see cref="StartBudget"/>
///   and capped at <see cref="MaxBudget"/>. A clip costs 1. The bar a stretch must clear falls as
///   the budget builds (<see cref="Bar"/>): only something exceptional is clipped early, and after a
///   dull spell the best of the last minute is. A candidate with better ones still to come must
///   clear the bar of the budget left after those.
/// - The buffer also holds what is not play: the menu before the song, a pause, the previous song.
///   Only a stretch that lies entirely inside uninterrupted play of the current song is a
///   candidate, and not one that starts within <see cref="StartGuardSeconds"/> of the song starting
///   or resuming (the cut may begin a little before the stretch) or that a pause follows at once.
///   Stretches within <see cref="MinGapSeconds"/> of an earlier clip (so two clips never show the
///   same moment) or from before the buffer was running are never picked.
///
/// All times are seconds on one steady clock that keeps running during pauses (the video does).
/// Not thread-safe: drive it from one thread.
/// </summary>
public sealed class ClipPicker
{
    public const double StartBudget = 1.0;
    public const double MaxBudget = 2.0;

    /// <summary>Below this nothing is clipped, however good.</summary>
    public const double MinBudget = 0.25;

    /// <summary>The oldest part of the buffer is not relied on: the saved file starts at a keyframe and the save takes a moment.</summary>
    public const double HeadMarginSeconds = 6.0;

    /// <summary>A candidate is claimed when it is this close to scrolling out of the usable buffer.</summary>
    public const double ExpiryMarginSeconds = 1.5;

    /// <summary>Video from the first moments after the buffer started is not relied on either.</summary>
    public const double BufferStartMarginSeconds = 2.0;

    /// <summary>
    /// A stretch must start this long after the song started or resumed: the cut keeps a little
    /// video in front of the stretch (ClipCut.LeadSeconds, and back to the keyframe before that).
    /// </summary>
    public const double StartGuardSeconds = 3.0;

    /// <summary>A stretch that ended this shortly before a pause is dropped: the cut keeps a little video after it.</summary>
    public const double EndGuardSeconds = 1.0;

    /// <summary>Two clips are at least this far apart, end to start.</summary>
    public const double MinGapSeconds = 10.0;

    /// <summary>No clip before this much of the song was played: the average is not worth much yet.</summary>
    public const double MinLevelSeconds = 20.0;

    /// <summary>A stretch with fewer notes hit is never a clip.</summary>
    public const int MinHits = 5;

    public const double HeartMaxPoints = 1.5;

    private const double SampleStepSeconds = 0.5;
    private const double BarAtMinBudget = 1.9;
    private const double BarSlope = 0.72;

    // Heart rate: the climb is measured from the start of the stretch to a little after its end
    // (the heart lags the effort), and only the part above the recent average counts, so the ramp
    // from rest at the start of a song is not a climb. The first two minutes of readings of a
    // session only calibrate: a stretch that started during them gets nothing for its heart rate,
    // however late it is judged (that is the warm-up).
    private const double HeartLagSeconds = 5.0;
    private const double HeartFreshSeconds = 5.0;
    private const double HeartBpmPerPoint = 10.0;
    private const double HeartAverageSeconds = 120.0;
    private const double HeartCalibrationSeconds = 120.0;
    private const double HeartReasonPoints = 0.75;

    private readonly ClipSettings _settings;
    private readonly Queue<double> _hits = new();
    private readonly Queue<double> _misses = new();
    private readonly List<HeartReading> _heart = new();
    private readonly List<Sample> _samples = new();
    private readonly List<Span> _taken = new();
    private readonly List<Candidate> _ranked = new();
    private readonly List<double> _reserved = new();

    private bool _inLevel;
    private bool _playing;
    private double _now;
    private double _playingSince;
    private double _nextSample;
    private double _levelPlaySeconds;
    private double _noteSum;
    private int _noteCount;
    private double _heartAverage;
    private double _heartAverageAt;
    private double _heartSeconds;
    private double _heartCalibratedAt = double.PositiveInfinity;

    public ClipPicker(ClipSettings settings, double startBudget = StartBudget)
    {
        _settings = settings;
        Budget = Math.Max(0.0, Math.Min(MaxBudget, startBudget));
    }

    /// <summary>Clips' worth of budget; see the class remarks.</summary>
    public double Budget { get; private set; }

    /// <summary>How much video OBS's replay buffer holds.</summary>
    public double BufferSeconds { get; set; } = 60.0;

    /// <summary>Since when (on the picker's clock) the buffer is known to be running.</summary>
    public double BufferSince { get; set; } = double.NegativeInfinity;

    /// <summary>What a stretch's interest must reach with this much budget.</summary>
    public static double Bar(double budget) =>
        budget < MinBudget ? double.PositiveInfinity : BarAtMinBudget - (BarSlope * (budget - MinBudget));

    /// <summary>A song starts: its notes and average start from nothing; the budget carries over.</summary>
    public void StartLevel(double now)
    {
        _hits.Clear();
        _misses.Clear();
        _heart.Clear();
        _samples.Clear();
        _taken.Clear();
        _inLevel = true;
        _playing = false;
        _now = now;
        _playingSince = double.PositiveInfinity;
        _nextSample = now + SampleStepSeconds;
        _levelPlaySeconds = 0.0;
        _noteSum = 0.0;
        _noteCount = 0;
    }

    /// <summary>A note was cut correctly.</summary>
    public void NoteHit(double now)
    {
        if (_inLevel)
        {
            _hits.Enqueue(now);
        }
    }

    /// <summary>A note was missed or cut badly.</summary>
    public void NoteMissed(double now)
    {
        if (_inLevel)
        {
            _misses.Enqueue(now);
        }
    }

    /// <summary>A heart-rate reading (about one a second while a strap is connected).</summary>
    public void HeartRate(double now, int bpm)
    {
        if (!_inLevel || bpm <= 0)
        {
            return;
        }

        _heart.Add(new HeartReading(now, bpm));
        if (!_playing)
        {
            return;
        }

        if (_heartAverage <= 0.0)
        {
            _heartAverage = bpm;
        }
        else
        {
            var gap = Math.Max(0.0, Math.Min(HeartFreshSeconds, now - _heartAverageAt));
            _heartAverage += (bpm - _heartAverage) * (1.0 - Math.Exp(-gap / HeartAverageSeconds));
            _heartSeconds += gap;
            if (_heartSeconds >= HeartCalibrationSeconds && double.IsPositiveInfinity(_heartCalibratedAt))
            {
                _heartCalibratedAt = now;
            }
        }

        _heartAverageAt = now;
    }

    /// <summary>
    /// Time passed. Call every frame of a song, paused or not.
    /// </summary>
    /// <param name="playing">The song is running: not paused, not in a menu.</param>
    /// <param name="armed">A clip could be saved right now (OBS is connected, the buffer runs). Budget builds either way.</param>
    /// <returns>A stretch to save now, or null. The budget for it is already spent.</returns>
    public ClipPick? Advance(double now, bool playing, bool armed)
    {
        if (!_inLevel || now < _now)
        {
            return null;
        }

        var elapsed = now - _now;
        _now = now;
        if (playing && _playing)
        {
            _levelPlaySeconds += elapsed;
            Budget = Math.Min(MaxBudget, Budget + (elapsed / Math.Max(10.0, _settings.TargetSecondsPerClip)));
        }

        if (playing && !_playing)
        {
            _playingSince = now;
        }
        else if (!playing && _playing)
        {
            // Paused: the video right after the newest stretches is the pause menu.
            _samples.RemoveAll(sample => sample.End > now - EndGuardSeconds);
        }

        _playing = playing;

        if (now - _nextSample > BufferSeconds)
        {
            // A very long frame or a suspended PC: nothing in between is worth sampling.
            _nextSample = now;
        }

        var sampled = false;
        while (_nextSample <= now)
        {
            TakeSample(_nextSample);
            _nextSample += SampleStepSeconds;
            sampled = true;
        }

        if (!sampled)
        {
            return null;
        }

        Forget(now);
        return Evaluate(now, finish: false, armed);
    }

    /// <summary>The song is over (finished, failed, quit or restarted): the last chance for its best stretch.</summary>
    public ClipPick? Finish(double now, bool armed)
    {
        if (!_inLevel)
        {
            return null;
        }

        _now = Math.Max(_now, now);
        var pick = Evaluate(_now, finish: true, armed);
        _inLevel = false;
        _playing = false;
        return pick;
    }

    /// <summary>A picked clip could not be saved: its budget comes back. The stretch is not offered again.</summary>
    public void Refund()
    {
        Budget = Math.Min(MaxBudget, Budget + 1.0);
    }

    private static bool Overlap(double startA, double endA, double startB, double endB) => startA < endB && startB < endA;

    private void TakeSample(double end)
    {
        var clip = _settings.ClipSeconds;
        var start = end - clip;
        while (_hits.Count > 0 && _hits.Peek() <= start)
        {
            _hits.Dequeue();
        }

        while (_misses.Count > 0 && _misses.Peek() <= start)
        {
            _misses.Dequeue();
        }

        // Only a stretch of uninterrupted play is a candidate: a pause would be in the video.
        if (!_playing || start < _playingSince + StartGuardSeconds)
        {
            return;
        }

        var hits = CountUpTo(_hits, end);
        var misses = CountUpTo(_misses, end);
        var score = hits == 0 ? 0.0 : (double)hits * hits / (hits + misses);
        _samples.Add(new Sample(end, hits, score));
        _noteSum += score;
        _noteCount++;
    }

    private static int CountUpTo(Queue<double> times, double end)
    {
        var count = 0;
        foreach (var time in times)
        {
            if (time > end)
            {
                break;
            }

            count++;
        }

        return count;
    }

    private void Forget(double now)
    {
        var oldest = now - BufferSeconds - 1.0;
        var drop = 0;
        while (drop < _samples.Count && _samples[drop].End < oldest)
        {
            drop++;
        }

        _samples.RemoveRange(0, drop);
        _taken.RemoveAll(span => span.End + MinGapSeconds < oldest);

        var oldestHeart = oldest - _settings.ClipSeconds - HeartFreshSeconds;
        drop = 0;
        while (drop < _heart.Count && _heart[drop].Time < oldestHeart)
        {
            drop++;
        }

        _heart.RemoveRange(0, drop);
    }

    private ClipPick? Evaluate(double now, bool finish, bool armed)
    {
        if (!armed || Budget < MinBudget || _levelPlaySeconds < MinLevelSeconds || _noteCount == 0 || _noteSum <= 0.0)
        {
            return null;
        }

        var clip = _settings.ClipSeconds;
        var mean = _noteSum / _noteCount;
        var edge = now - (BufferSeconds - HeadMarginSeconds);
        var oldestStart = Math.Max(edge, BufferSince + BufferStartMarginSeconds);

        _ranked.Clear();
        foreach (var sample in _samples)
        {
            var start = sample.End - clip;
            if (start < oldestStart || sample.End > now || sample.Hits < MinHits || IsTaken(start, sample.End))
            {
                continue;
            }

            var heart = HeartPoints(start, sample.End, now);
            _ranked.Add(new Candidate(sample, (sample.Score / mean) + heart, heart));
        }

        // Best first; of equals the older one, which runs out of time sooner.
        _ranked.Sort((a, b) =>
        {
            var byInterest = b.Interest.CompareTo(a.Interest);
            return byInterest != 0 ? byInterest : a.Sample.End.CompareTo(b.Sample.End);
        });

        _reserved.Clear();
        foreach (var candidate in _ranked)
        {
            var end = candidate.Sample.End;
            if (OverlapsReserved(end, clip))
            {
                continue;
            }

            // Every better stretch still in the buffer has first call on the budget.
            var budget = Budget - _reserved.Count;
            if (budget < MinBudget)
            {
                break;
            }

            var due = finish ? _reserved.Count == 0 : end - clip <= edge + ExpiryMarginSeconds;
            if (due && candidate.Interest >= Bar(budget))
            {
                Budget -= 1.0;
                _taken.Add(new Span(end - clip, end));
                return new ClipPick(
                    now - (end - clip),
                    clip,
                    candidate.Heart >= HeartReasonPoints ? ClipReason.HeartRate : ClipReason.Notes,
                    candidate.Interest,
                    candidate.Sample.Hits);
            }

            if (finish)
            {
                break;
            }

            _reserved.Add(end);
        }

        return null;
    }

    private bool IsTaken(double start, double end)
    {
        foreach (var span in _taken)
        {
            if (Overlap(start, end, span.Start - MinGapSeconds, span.End + MinGapSeconds))
            {
                return true;
            }
        }

        return false;
    }

    private bool OverlapsReserved(double end, double clip)
    {
        foreach (var reserved in _reserved)
        {
            if (Math.Abs(end - reserved) < clip + MinGapSeconds)
            {
                return true;
            }
        }

        return false;
    }

    private double HeartPoints(double start, double end, double now)
    {
        if (!_settings.UseHeartRate || start < _heartCalibratedAt)
        {
            return 0.0;
        }

        if (!TryBpmAt(start, out var before) || !TryBpmAt(Math.Min(end + HeartLagSeconds, now), out var after))
        {
            return 0.0;
        }

        var climb = after - Math.Max(before, _heartAverage);
        return Math.Max(0.0, Math.Min(HeartMaxPoints, climb / HeartBpmPerPoint));
    }

    /// <summary>The last reading at or before <paramref name="time"/>, unless it is too old to stand for that moment.</summary>
    private bool TryBpmAt(double time, out double bpm)
    {
        for (var i = _heart.Count - 1; i >= 0; i--)
        {
            var reading = _heart[i];
            if (reading.Time > time)
            {
                continue;
            }

            bpm = reading.Bpm;
            return time - reading.Time <= HeartFreshSeconds;
        }

        bpm = 0.0;
        return false;
    }

    private readonly struct Sample
    {
        public Sample(double end, int hits, double score)
        {
            End = end;
            Hits = hits;
            Score = score;
        }

        /// <summary>The stretch is the ClipSeconds up to this time.</summary>
        public double End { get; }

        public int Hits { get; }

        public double Score { get; }
    }

    private readonly struct Candidate
    {
        public Candidate(Sample sample, double interest, double heart)
        {
            Sample = sample;
            Interest = interest;
            Heart = heart;
        }

        public Sample Sample { get; }

        public double Interest { get; }

        public double Heart { get; }
    }

    private readonly struct Span
    {
        public Span(double start, double end)
        {
            Start = start;
            End = end;
        }

        public double Start { get; }

        public double End { get; }
    }

    private readonly struct HeartReading
    {
        public HeartReading(double time, int bpm)
        {
            Time = time;
            Bpm = bpm;
        }

        public double Time { get; }

        public int Bpm { get; }
    }
}
