using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using IPA.Loader;
using IPA.Utilities;
using MaxTweaks.Common;
using MaxTweaks.Common.Toasts;
using MaxTweaks.Configuration;
using MaxTweaks.Core.HeartRateBridge;
using UnityEngine;
using Zenject;

namespace MaxTweaks.Features.HeartRateBridge;

/// <summary>
/// App lifetime: keeps the Bluetooth heart-rate bridge (tools/hr-osc-bridge, shipped as
/// UserData/MaxTweaks/hr_osc_bridge.py) running as a hidden child process while the feature is on,
/// so HRCounter gets the strap's readings over OSC without anything started by hand. The process is
/// tied to the game through a job object and told the game's pid, so it never outlives the game.
/// Its output is relayed to the game log; connect/disconnect lines become toasts.
/// </summary>
internal sealed class HeartRateBridgeService : IInitializable, ITickable, IDisposable
{
    private const string Feature = "HeartRateBridge";
    private const string ScriptFileName = "hr_osc_bridge.py";
    private const float PollSeconds = 1f;
    private const double ReadingLogSeconds = 60;

    private readonly HeartRateBridgeConfig _config;
    private readonly IToastService _toasts;
    private readonly BridgeRestartPolicy _restarts = new();
    private readonly ConcurrentQueue<string> _output = new();

    private ChildProcessJob? _job;
    private Process? _process;
    private DateTime _startedUtc;
    private float _pollTimer;
    private double _nextStartRealtime;
    private double _lastReadingLogRealtime = double.NegativeInfinity;
    private string? _permanentFailure;
    private string? _lastStatus;
    private bool _wasEnabled;

    public HeartRateBridgeService(HeartRateBridgeConfig config, IToastService toasts)
    {
        _config = config;
        _toasts = toasts;
    }

    /// <summary>Raised on the main thread whenever <see cref="StatusText"/> may have changed.</summary>
    public event Action? StatusChanged;

    public bool IsRunning => _process != null && !_process.HasExited;

    public bool StrapConnected { get; private set; }

    public int LastBpm { get; private set; }

    /// <summary>One-line state for the settings page.</summary>
    public string StatusText
    {
        get
        {
            if (_permanentFailure != null)
            {
                return "Not running: " + _permanentFailure;
            }

            if (!_config.Enabled)
            {
                return "Off";
            }

            if (!IsRunning)
            {
                return HRCounterLoaded() || !_config.OnlyWithHRCounter ? "Starting..." : "Waiting: HRCounter is not installed";
            }

            return StrapConnected ? "Strap connected, " + LastBpm + " bpm" : "Running, looking for a strap";
        }
    }

    public void Initialize()
    {
        _wasEnabled = _config.Enabled;
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

    private void NotifyStatus()
    {
        Guard.Run(Feature, () => StatusChanged?.Invoke());
    }

    public void Dispose()
    {
        Guard.Run(Feature, () => Stop("game closing"));
        _job?.Dispose();
        _job = null;
    }

    private void Poll()
    {
        PollCore();
        // Compare with the last text handed out, not a snapshot from before PollCore: the config can
        // change between polls (settings toggle, file reload), which already changes StatusText.
        var status = StatusText;
        if (status != _lastStatus)
        {
            _lastStatus = status;
            NotifyStatus();
        }
    }

    private void PollCore()
    {
        DrainOutput();

        if (_config.Enabled != _wasEnabled)
        {
            // Toggled in the settings: forget earlier failures and backoff so a fix (uv installed,
            // script restored) is picked up at once.
            _wasEnabled = _config.Enabled;
            _permanentFailure = null;
            _restarts.Reset();
            _nextStartRealtime = 0;
        }

        var wanted = _config.Enabled && _permanentFailure == null && (!_config.OnlyWithHRCounter || HRCounterLoaded());
        if (_process != null && _process.HasExited)
        {
            var code = _process.ExitCode;
            var ran = (DateTime.UtcNow - _startedUtc).TotalSeconds;
            _process.Dispose();
            _process = null;
            StrapConnected = false;
            var delay = _restarts.DelayAfterExit(code, ran);
            _nextStartRealtime = Time.realtimeSinceStartupAsDouble + delay;
            if (code == BridgeCommandLine.AlreadyRunningExitCode)
            {
                Plugin.Log.Info($"[{Feature}] another bridge is already running; checking again in {delay:0} s");
            }
            else
            {
                Plugin.Log.Warn($"[{Feature}] bridge exited with code {code} after {ran:0} s; restart in {delay:0} s");
            }
        }

        if (wanted && _process == null && Time.realtimeSinceStartupAsDouble >= _nextStartRealtime)
        {
            Start();
        }
        else if (!wanted && _process != null)
        {
            Stop(_config.Enabled ? "HRCounter not loaded" : "disabled");
        }
    }

    private static bool HRCounterLoaded() => PluginManager.GetPluginFromId("HRCounter") != null;

    private string ResolveScriptPath()
    {
        var configured = (_config.ScriptPath ?? string.Empty).Trim();
        return configured.Length > 0 ? configured : Path.Combine(UnityGame.UserDataPath, "MaxTweaks", ScriptFileName);
    }

    private void Start()
    {
        var script = ResolveScriptPath();
        if (!File.Exists(script))
        {
            Fail($"bridge script not found at {script} (it ships in the release zip as UserData/MaxTweaks/{ScriptFileName})");
            return;
        }

        var uv = string.IsNullOrWhiteSpace(_config.UvPath) ? "uv" : _config.UvPath.Trim();
        var info = new ProcessStartInfo(uv, BridgeCommandLine.Arguments(script, _config.ExtraArguments, Process.GetCurrentProcess().Id))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(script) ?? UnityGame.UserDataPath,
        };
        info.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

        var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => Enqueue(e.Data);
        process.ErrorDataReceived += (_, e) => Enqueue(e.Data);
        try
        {
            process.Start();
        }
        catch (Win32Exception e)
        {
            process.Dispose();
            Fail($"cannot start '{uv}': {e.Message} (install uv from https://docs.astral.sh/uv/ or set UvPath)");
            return;
        }

        try
        {
            _job ??= new ChildProcessJob();
            _job.Add(process);
        }
        catch (Exception e)
        {
            // Not fatal: the script also watches the game's pid and exits when it is gone.
            Plugin.Log.Warn($"[{Feature}] could not tie the bridge to the game process: {e.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;
        _startedUtc = DateTime.UtcNow;
        StrapConnected = false;
        Plugin.Log.Info($"[{Feature}] started pid {process.Id}: {uv} {info.Arguments}");
    }

    private void Stop(string reason)
    {
        var process = _process;
        _process = null;
        StrapConnected = false;
        if (process == null)
        {
            return;
        }

        try
        {
            // Killing uv alone would orphan its Python children (they hold the guard port, so the
            // next start would see "already running"): terminate the whole job instead. A fresh job
            // is created for the next start. Kill() stays as the fallback without a job.
            if (_job != null)
            {
                _job.TerminateAll();
                _job.Dispose();
                _job = null;
            }

            if (!process.HasExited)
            {
                process.Kill();
            }

            process.WaitForExit(2000);
            Plugin.Log.Info($"[{Feature}] stopped ({reason})");
        }
        catch (Exception e)
        {
            Plugin.Log.Warn($"[{Feature}] stop failed: {e.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    private void Fail(string reason)
    {
        _permanentFailure = reason;
        Plugin.Log.Error($"[{Feature}] {reason}");
        if (_config.ShowToasts)
        {
            _toasts.Show("Heart rate bridge: " + reason, 8f);
        }
    }

    private void Enqueue(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            _output.Enqueue(line!);
        }
    }

    private void DrainOutput()
    {
        while (_output.TryDequeue(out var line))
        {
            var text = BridgeOutput.Strip(line);
            switch (BridgeOutput.Classify(line, out var bpm))
            {
                case BridgeEvent.Reading:
                    LastBpm = bpm;
                    var now = Time.realtimeSinceStartupAsDouble;
                    if (now - _lastReadingLogRealtime >= ReadingLogSeconds)
                    {
                        _lastReadingLogRealtime = now;
                        Plugin.Log.Debug($"[{Feature}] {text}");
                    }

                    break;
                case BridgeEvent.Connected:
                    StrapConnected = true;
                    Plugin.Log.Info($"[{Feature}] strap connected");
                    if (_config.ShowToasts)
                    {
                        _toasts.Show("Heart rate strap connected");
                    }

                    break;
                case BridgeEvent.Disconnected:
                    StrapConnected = false;
                    Plugin.Log.Info($"[{Feature}] strap disconnected");
                    if (_config.ShowToasts)
                    {
                        _toasts.Show("Heart rate strap disconnected");
                    }

                    break;
                case BridgeEvent.Error:
                    Plugin.Log.Warn($"[{Feature}] {text}");
                    break;
                default:
                    Plugin.Log.Debug($"[{Feature}] {text}");
                    break;
            }
        }
    }
}
