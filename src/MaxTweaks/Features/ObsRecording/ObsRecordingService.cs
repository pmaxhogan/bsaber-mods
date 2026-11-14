using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MaxTweaks.Common;
using MaxTweaks.Common.Toasts;
using MaxTweaks.Configuration;
using MaxTweaks.Core.ObsRecording;
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

    private static readonly TimeSpan QuitBudget = TimeSpan.FromSeconds(2.5);

    private readonly ObsRecordingConfig _config;
    private readonly IToastService _toasts;
    private readonly RecordingCoordinator _coordinator;

    private float _pollTimer;
    private bool _wasEnabled;
    private int _statusDirty;

    public ObsRecordingService(ObsRecordingConfig config, IToastService toasts)
    {
        _config = config;
        _toasts = toasts;
        _coordinator = new RecordingCoordinator(new ObsConnector(ResolveEndpoint, () => new ClientWebSocketObsSocket()), this);
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
    }

    /// <summary>A level scene started (main thread). Returns at once.</summary>
    public void LevelStarted(RecordingLabel label) => _coordinator.LevelStarted(label);

    /// <summary>The level scene is going away: finish, fail, quit to menu or restart (main thread).</summary>
    public void LevelEnded(RecordingLabel label) => _coordinator.LevelEnded(label);

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

        if (Interlocked.Exchange(ref _statusDirty, 0) != 0)
        {
            StatusChanged?.Invoke();
        }
    }

    /// <summary>
    /// Called on the thread pool for every connection attempt. Port and password come from this
    /// mod's config when set, else from OBS's own WebSocket settings file on this PC. That file is
    /// only read, and only used (ObsEndpoint.Resolve enforces it too), when the host is this PC.
    /// </summary>
    private ObsEndpoint ResolveEndpoint()
    {
        var host = _config.Host;
        ObsServerSettings? obs = null;
        if (ObsEndpoint.IsThisPcHost(string.IsNullOrWhiteSpace(host) ? ObsEndpoint.DefaultHost : host))
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "obs-studio",
                    "plugin_config",
                    "obs-websocket",
                    "config.json");
                if (File.Exists(path))
                {
                    obs = ObsServerSettings.Parse(File.ReadAllText(path));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.Debug($"[{Feature}] could not read OBS's WebSocket settings: {e.Message}");
            }
        }

        return ObsEndpoint.Resolve(host, _config.Port, _config.Password, obs);
    }
}
