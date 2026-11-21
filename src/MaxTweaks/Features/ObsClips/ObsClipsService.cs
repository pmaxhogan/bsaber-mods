using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MaxTweaks.Common;
using MaxTweaks.Common.Debugging;
using MaxTweaks.Common.Obs;
using MaxTweaks.Common.Toasts;
using MaxTweaks.Configuration;
using MaxTweaks.Core.ObsClips;
using MaxTweaks.Core.ObsRecording;
using MaxTweaks.Core.ObsStatus;
using UnityEngine;
using Zenject;

namespace MaxTweaks.Features.ObsClips;

/// <summary>A stretch the picker chose, with the clock time it was chosen at.</summary>
internal readonly struct PickedClip
{
    public PickedClip(ClipPick pick, double decidedAt)
    {
        Pick = pick;
        DecidedAt = decidedAt;
    }

    public ClipPick Pick { get; }

    /// <summary><see cref="ObsClipsService.Now"/> when it was picked.</summary>
    public double DecidedAt { get; }

    /// <summary><see cref="ObsClipsService.Now"/> at the start of the stretch.</summary>
    public double StartedAt => DecidedAt - Pick.SecondsAgo;
}

/// <summary>
/// App lifetime: saves short clips of the best moments of play through OBS's replay buffer.
/// - Core <see cref="ClipPicker"/> decides which stretches are worth it (about one per
///   ObsClips.SecondsPerClip of unpaused play). Its budget lives here, so it carries over from song
///   to song; the per-level hook feeds it notes, heart rate and time.
/// - Core <see cref="ClipCoordinator"/> keeps the replay buffer running, asks OBS to save it and has
///   the picked stretch cut out with ffmpeg (<see cref="FfmpegCutter"/>).
/// It has its own connection to OBS (the same address as OBS recording), so the two features do not
/// depend on each other. Everything that talks to OBS or to the disk runs on the thread pool.
/// </summary>
internal sealed class ObsClipsService : IInitializable, ITickable, IDisposable, IClipHost, IObsClipsDebug
{
    private const string Feature = "ObsClips";
    private const float PollSeconds = 1f;

    // While not connected in the menu (or connected with a replay buffer that cannot be used), look
    // again this often, so that an OBS started or fixed after the game is found without playing a
    // level first. Quiet: no toast, one log line per reason.
    private const float RetrySeconds = 5f;

    private static readonly TimeSpan QuitBudget = TimeSpan.FromSeconds(2.5);

    private readonly ObsClipsConfig _config;
    private readonly IToastService _toasts;
    private readonly ObsStatusBoard _board;
    private readonly ClipSettings _settings = new();
    private readonly ClipPicker _picker;
    private readonly ClipCoordinator _coordinator;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private float _pollTimer;
    private float _retryTimer;
    private bool _inLevel;
    private bool _wasEnabled;
    private int _statusDirty;
    private int _refunds;

    public ObsClipsService(ObsClipsConfig config, ObsRecordingConfig obs, IToastService toasts, ObsStatusBoard board)
    {
        _config = config;
        _toasts = toasts;
        _board = board;
        _picker = new ClipPicker(_settings);
        _coordinator = new ClipCoordinator(
            new ObsConnector(() => ObsEndpointSource.Resolve(obs, Feature), () => new ClientWebSocketObsSocket()),
            this);
    }

    /// <summary>Raised on the main thread whenever <see cref="StatusText"/> may have changed.</summary>
    public event Action? StatusChanged;

    /// <summary>One-line state for the settings page.</summary>
    public string StatusText => _config.Enabled ? _coordinator.Status : "Off";

    /// <summary>Seconds on the clock every clip time is measured on. Any thread.</summary>
    public double Now => _clock.Elapsed.TotalSeconds;

    bool IClipHost.KeepFullReplay => _config.KeepFullReplay;

    /// <summary>Debug channel only: a missed note counts as a hit (FPFC hits nothing).</summary>
    public bool MissesCountAsHits { get; private set; }

    private bool Armed => _config.Enabled && _coordinator.Ready;

    public void Initialize()
    {
        ObsClipsDebugHook.Current = this;
        _wasEnabled = _config.Enabled;
        if (_wasEnabled)
        {
            // Connect early so the settings page shows the state; the buffer starts with the first level.
            Guard.Run(Feature, _coordinator.Probe);
        }
    }

    public void Tick()
    {
        _pollTimer += Time.unscaledDeltaTime;
        if (_pollTimer < PollSeconds)
        {
            return;
        }

        _pollTimer = 0f;
        Guard.Run(Feature, Poll);
    }

    public void Dispose()
    {
        if (ReferenceEquals(ObsClipsDebugHook.Current, this))
        {
            ObsClipsDebugHook.Current = null;
        }

        // Quitting: give a save in flight a moment and stop a replay buffer this mod started.
        // Bounded; continuations run on the thread pool.
        Guard.Run(Feature, () => _coordinator.ShutdownAsync(QuitBudget).Wait(QuitBudget + TimeSpan.FromMilliseconds(500)));

        // An internal restart of the game ends here too: hand the count to the next service.
        ObsSessionTotals.AddClips(_coordinator.SavedCount);
    }

    // --- Called by the per-level hook, on the main thread. ---

    public void LevelStarted()
    {
        _inLevel = true;
        ApplySettings();
        _picker.StartLevel(Now);
        _coordinator.LevelStarted();
    }

    public void NoteHit() => _picker.NoteHit(Now);

    public void NoteMissed() => _picker.NoteMissed(Now);

    public void HeartRate(int bpm) => _picker.HeartRate(Now, bpm);

    /// <summary>Every frame of a level, paused or not. Returns a stretch to save now, if any.</summary>
    public PickedClip? Advance(bool playing)
    {
        ApplySettings();
        var now = Now;
        var pick = _picker.Advance(now, playing, Armed);
        return pick == null ? null : new PickedClip(pick, now);
    }

    /// <summary>The level scene is going away: the last chance for its best stretch.</summary>
    public PickedClip? LevelEnding()
    {
        ApplySettings();
        var now = Now;
        var pick = _picker.Finish(now, Armed);
        return pick == null ? null : new PickedClip(pick, now);
    }

    public void Save(PickedClip clip, RecordingLabel label, string extra)
    {
        var pick = clip.Pick;
        Plugin.Log.Info(string.Format(
            CultureInfo.InvariantCulture,
            "[{0}] picked {1} {2}: {3} hits, interest {4:0.00}, started {5:0.0} s ago, budget left {6:0.00}",
            Feature,
            label,
            extra,
            pick.Hits,
            pick.Interest,
            pick.SecondsAgo,
            _picker.Budget));
        _coordinator.Save(new ClipRequest(label, extra, clip.DecidedAt, pick.SecondsAgo, pick.Seconds));
    }

    public void LevelEnded()
    {
        _inLevel = false;
        _coordinator.LevelEnded();
    }

    // --- IClipHost: called from the thread pool. ---

    void IRecordingHost.Info(string message) => Plugin.Log.Info($"[{Feature}] {message}");

    void IRecordingHost.Warn(string message) => Plugin.Log.Warn($"[{Feature}] {message}");

    void IRecordingHost.Notify(string message)
    {
        if (_config.ShowToasts)
        {
            // Thread-safe: the toast service marshals to the main thread itself.
            _toasts.Show(message, 6f);
        }
    }

    void IRecordingHost.StatusChanged() => Interlocked.Exchange(ref _statusDirty, 1);

    bool IRecordingHost.FileExists(string path) => File.Exists(path);

    void IRecordingHost.MoveFile(string from, string to) => File.Move(from, to);

    Task IRecordingHost.Delay(TimeSpan time, CancellationToken cancellation) => Task.Delay(time, cancellation);

    Task<ClipCutResult> IClipHost.CutAsync(string source, string target, double secondsFromEnd, double seconds) =>
        FfmpegCutter.CutAsync(_config.FfmpegPath, source, target, secondsFromEnd, seconds);

    void IClipHost.DeleteFile(string path) => File.Delete(path);

    void IClipHost.SaveRefused() => Interlocked.Increment(ref _refunds);

    // --- Debug channel (IObsClipsDebug): the save path without waiting for the picker. ---

    public string ClipNow(double secondsAgo, double seconds)
    {
        if (!_config.Enabled)
        {
            throw new DebugCommandException("OBS clips is off");
        }

        if (!_coordinator.Ready)
        {
            throw new DebugCommandException("not ready: " + _coordinator.Status);
        }

        _coordinator.Save(new ClipRequest(new RecordingLabel("debug", null), "test", Now, secondsAgo, seconds));
        return string.Format(CultureInfo.InvariantCulture, "clip of {0:0.#} s from {1:0.#} s ago requested", seconds, secondsAgo);
    }

    public string CountMissesAsHits(bool on)
    {
        MissesCountAsHits = on;
        return on ? "misses count as hits until the game restarts" : "misses count as misses";
    }

    public string Describe() => string.Format(
        CultureInfo.InvariantCulture,
        "clips={0} buffer={1}s budget={2:0.00}{3}",
        !_config.Enabled ? "off" : _coordinator.Ready ? "ready" : "not-ready",
        _coordinator.BufferSeconds,
        _picker.Budget,
        MissesCountAsHits ? " missesAsHits" : string.Empty);

    private void ApplySettings()
    {
        _settings.ClipSeconds = Mathf.Clamp(_config.ClipSeconds, 3, 30);
        _settings.TargetSecondsPerClip = Mathf.Clamp(_config.SecondsPerClip, 20, 3600);
        _settings.UseHeartRate = _config.UseHeartRate;

        var buffer = _coordinator.BufferSeconds;
        if (buffer > 0)
        {
            _picker.BufferSeconds = buffer;
        }

        _picker.BufferSince = _coordinator.BufferSince;

        // A clip OBS did not save costs nothing.
        for (var refunds = Interlocked.Exchange(ref _refunds, 0); refunds > 0; refunds--)
        {
            _picker.Refund();
        }
    }

    private void Poll()
    {
        if (_config.Enabled != _wasEnabled)
        {
            _wasEnabled = _config.Enabled;
            if (_wasEnabled)
            {
                _coordinator.Probe();
            }
            else
            {
                _coordinator.Disconnect();
            }

            Interlocked.Exchange(ref _statusDirty, 1);
        }

        var trouble = _coordinator.Trouble;
        if (_wasEnabled && !_inLevel && (!_coordinator.Connected || trouble != null))
        {
            _retryTimer += PollSeconds;
            if (_retryTimer >= RetrySeconds)
            {
                _retryTimer = 0f;
                _coordinator.Probe();
            }
        }
        else
        {
            _retryTimer = 0f;
        }

        _board.SetClips(new ObsFeatureState(_wasEnabled, _coordinator.Connected, ObsSessionTotals.Clips + _coordinator.SavedCount, trouble));

        if (Interlocked.Exchange(ref _statusDirty, 0) != 0)
        {
            StatusChanged?.Invoke();
        }
    }
}
