using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MaxTweaks.Common;
using MaxTweaks.Common.Obs;
using MaxTweaks.Common.Toasts;
using MaxTweaks.Configuration;
using MaxTweaks.Core.ObsRecording;
using MaxTweaks.Core.ObsStatus;
using UnityEngine;
using Zenject;

namespace MaxTweaks.Features.ObsRecording;

/// <summary>
/// App lifetime: owns the connection to OBS (obs-websocket 5, built into OBS 28+) and the Core
/// <see cref="RecordingCoordinator"/> that starts a recording per level, stops it and renames the
/// saved file. It lives in the app container because the rename finishes after the level scene is
/// gone. Everything that talks to OBS runs on the thread pool; the game's threads never wait on it
/// except for a short, bounded wait when the game quits in the middle of a level.
/// </summary>
internal sealed class ObsRecordingService : IInitializable, ITickable, IDisposable, IRecordingHost
{
    private const string Feature = "ObsRecording";
    private const float PollSeconds = 1f;

    // While not connected in the menu, look for OBS again this often, so that an OBS started after
    // the game is found without playing a level first. Quiet: no toast, one log line per reason.
    private const float RetrySeconds = 5f;

    private static readonly TimeSpan QuitBudget = TimeSpan.FromSeconds(2.5);

    private readonly ObsRecordingConfig _config;
    private readonly IToastService _toasts;
    private readonly ObsStatusBoard _board;
    private readonly RecordingCoordinator _coordinator;

    private float _pollTimer;
    private float _retryTimer;
    private bool _inLevel;
    private bool _wasEnabled;
    private int _statusDirty;

    public ObsRecordingService(ObsRecordingConfig config, IToastService toasts, ObsStatusBoard board)
    {
        _config = config;
        _toasts = toasts;
        _board = board;
        _coordinator = new RecordingCoordinator(new ObsConnector(() => ObsEndpointSource.Resolve(_config, Feature), () => new ClientWebSocketObsSocket()), this);
    }

    /// <summary>Raised on the main thread whenever <see cref="StatusText"/> may have changed.</summary>
    public event Action? StatusChanged;

    /// <summary>One-line state for the settings page.</summary>
    public string StatusText => _config.Enabled ? _coordinator.Status : "Off";

    public void Initialize()
    {
        _wasEnabled = _config.Enabled;
        if (_wasEnabled)
        {
            // Connect early so the settings page shows the state and the first level starts at once.
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
        // Quitting in the middle of a level: stop the recording and give OBS a moment to finish the
        // file so it can still be renamed. Bounded; continuations run on the thread pool.
        Guard.Run(Feature, () => _coordinator.ShutdownAsync(QuitBudget).Wait(QuitBudget + TimeSpan.FromMilliseconds(500)));

        // An internal restart of the game ends here too: hand the count to the next service.
        ObsSessionTotals.AddRecordings(_coordinator.SavedCount);
    }

    /// <summary>A level scene started (main thread). Returns at once.</summary>
    public void LevelStarted(RecordingLabel label)
    {
        _inLevel = true;
        _coordinator.LevelStarted(label);
    }

    /// <summary>The level scene is going away: finish, fail, quit to menu or restart (main thread).</summary>
    public void LevelEnded(RecordingLabel label)
    {
        _inLevel = false;
        _coordinator.LevelEnded(label);
    }

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

        if (_wasEnabled && !_inLevel && !_coordinator.Connected)
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

        _board.SetRecording(new ObsFeatureState(_wasEnabled, _coordinator.Connected, ObsSessionTotals.Recordings + _coordinator.SavedCount));

        if (Interlocked.Exchange(ref _statusDirty, 0) != 0)
        {
            StatusChanged?.Invoke();
        }
    }
}
