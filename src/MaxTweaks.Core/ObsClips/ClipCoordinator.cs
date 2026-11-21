using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxTweaks.Core.ObsRecording;

namespace MaxTweaks.Core.ObsClips;

/// <summary>What the clip coordinator needs from the outside world. Every member may be called from any thread.</summary>
public interface IClipHost : IRecordingHost
{
    /// <summary>Seconds on a steady clock: the one <see cref="ClipRequest.DecidedAt"/> is read from.</summary>
    double Now { get; }

    /// <summary>Whether the uncut replay stays next to its clip.</summary>
    bool KeepFullReplay { get; }

    /// <summary>
    /// Cuts <paramref name="seconds"/> starting <paramref name="secondsFromEnd"/> before the end of
    /// <paramref name="source"/> into <paramref name="target"/> (see <see cref="ClipCut"/>). Never
    /// throws, and leaves no half-written target behind when it fails.
    /// </summary>
    Task<ClipCutResult> CutAsync(string source, string target, double secondsFromEnd, double seconds);

    /// <summary>Deletes a file; throws IOException while another program still has it open.</summary>
    void DeleteFile(string path);

    /// <summary>A requested clip was not saved, so what it cost can be given back. Must not block.</summary>
    void SaveRefused();
}

/// <summary>One clip to save: a stretch of the recent past and what to name it after.</summary>
public sealed class ClipRequest
{
    public ClipRequest(RecordingLabel label, string? extra, double decidedAt, double secondsAgo, double seconds)
    {
        Label = label;
        Extra = string.IsNullOrWhiteSpace(extra) ? "clip" : extra!.Trim();
        DecidedAt = decidedAt;
        SecondsAgo = secondsAgo;
        Seconds = seconds;
    }

    public RecordingLabel Label { get; }

    /// <summary>The last part of the file name, e.g. "1m23s notes". Never empty.</summary>
    public string Extra { get; }

    /// <summary><see cref="IClipHost.Now"/> when the stretch was picked.</summary>
    public double DecidedAt { get; }

    /// <summary>How long before <see cref="DecidedAt"/> the stretch started.</summary>
    public double SecondsAgo { get; }

    public double Seconds { get; }

    public override string ToString() => Label + " " + Extra;
}

/// <summary>
/// Keeps OBS's replay buffer running and turns a <see cref="ClipRequest"/> into a file: asks OBS to
/// save the buffer, waits for the saved file, cuts the requested stretch out of it with ffmpeg and
/// deletes the rest. All work runs one step at a time on the thread pool, like
/// <see cref="RecordingCoordinator"/>, so callers (the game's main thread) never wait on OBS.
///
/// Rules:
/// - The replay buffer is started when a level starts and it is not running yet. A buffer that was
///   already running is used and left running; only one this class started is stopped again (when
///   the feature is switched off or the game quits).
/// - Only a replay this class asked for is ever cut, renamed or deleted: a ReplayBufferSaved event
///   that nobody here is waiting for (the player's own hotkey) is ignored.
/// - The file name OBS reports is not trusted: nothing is touched unless OBS runs on this PC and
///   the name is a full local path to a recording (RecordingFileName.IsLocalRecordingPath).
/// - The buffer must hold at least <see cref="MinBufferSeconds"/>; its length is read from OBS's
///   profile. Shorter, switched off in OBS, or unreadable: reported once, and no clips.
/// - When the cut is not possible (no ffmpeg, or it fails) the whole replay is kept, named like the
///   clip with " uncut" at the end.
/// - One save at a time: a request that arrives while OBS is still saving is refused.
/// </summary>
public sealed class ClipCoordinator
{
    public const int MinBufferSeconds = 20;
    public const int RecommendedBufferSeconds = 60;
    public const int CutAttempts = 3;
    public const int FileAttempts = 20;

    public const string NotConnectedYet = "Not connected yet";

    private const string UnknownBuffer = "replay buffer unknown";

    public static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly IObsConnector _connector;
    private readonly IClipHost _host;
    private readonly SerialQueue _queue;
    private readonly bool _windowsPaths;
    private readonly object _backgroundGate = new();
    private readonly List<Task> _background = new();

    // Everything below is touched only by queued work, one step at a time.
    private IObsConnection? _connection;
    private SaveWait? _pending;
    private string? _lastProblem;
    private string? _lastQuiet;
    private bool _closing;
    private bool _startedByUs;
    private bool _bufferActive;
    private int _savedThisLevel;

    private volatile string _status = NotConnectedYet;
    private volatile bool _disposed;
    private volatile bool _ready;
    private volatile bool _connected;
    private volatile string? _trouble;
    private volatile int _bufferSeconds;
    private volatile TaskCompletionSource<bool>? _saveSettled;
    private double _bufferSince = double.NegativeInfinity;
    private int _toolMissingReported;
    private int _saved;
    private int _probeQueued;

    /// <param name="windowsPaths">See <see cref="RecordingCoordinator"/>; null = the rules of the system this runs on.</param>
    public ClipCoordinator(IObsConnector connector, IClipHost host, bool? windowsPaths = null)
    {
        _connector = connector;
        _host = host;
        _windowsPaths = windowsPaths ?? Path.DirectorySeparatorChar == '\\';
        _queue = new SerialQueue(e => _host.Warn("unexpected error: " + e));
    }

    /// <summary>One line for the settings page.</summary>
    public string Status => _status;

    /// <summary>OBS is connected and its replay buffer is running and long enough: a clip can be saved now.</summary>
    public bool Ready => _ready;

    /// <summary>The length of OBS's replay buffer (0 while unknown).</summary>
    public int BufferSeconds => _bufferSeconds;

    /// <summary><see cref="IClipHost.Now"/> since when the buffer is known to be running.</summary>
    public double BufferSince => Volatile.Read(ref _bufferSince);

    /// <summary>The connection to OBS is open right now (the replay buffer may still be off).</summary>
    public bool Connected => _connected;

    /// <summary>
    /// A few words on why no clip can be saved although OBS is connected ("replay buffer off"), or
    /// null when nothing is wrong: ready, or waiting for the next level to start the buffer. Short
    /// enough for the status line; <see cref="Status"/> has the whole sentence.
    /// </summary>
    public string? Trouble => _trouble;

    /// <summary>
    /// Replays OBS accepted to save for this mod since this object was made (the game start): the
    /// same count as the "clips from this song" toasts added up.
    /// </summary>
    public int SavedCount => Volatile.Read(ref _saved);

    /// <summary>
    /// Connects now if not connected and looks at the replay buffer, without starting it or
    /// notifying the player. Cheap to call again and again: one at a time, and what it finds is
    /// logged once per finding.
    /// </summary>
    public void Probe()
    {
        if (_disposed || Interlocked.Exchange(ref _probeQueued, 1) != 0)
        {
            return;
        }

        _queue.Post(async () =>
        {
            try
            {
                // A buffer that is running and fine needs no second look.
                if (!_closing && !(_ready && _connection != null && _connection.IsOpen))
                {
                    await EnsureBufferAsync(announce: false, start: false).ConfigureAwait(false);
                }
            }
            finally
            {
                Volatile.Write(ref _probeQueued, 0);
            }
        });
    }

    /// <summary>A level starts: connect if needed and start the replay buffer if it is not running.</summary>
    public void LevelStarted() => Post(() =>
    {
        _savedThisLevel = 0;
        return _closing ? Task.CompletedTask : EnsureBufferAsync(announce: true, start: true);
    });

    /// <summary>The level is over: tells the player how many clips it gave.</summary>
    public void LevelEnded() => Post(() =>
    {
        var saved = _savedThisLevel;
        _savedThisLevel = 0;
        if (saved > 0)
        {
            _host.Notify(saved == 1 ? "OBS clips: 1 clip from this song" : $"OBS clips: {saved.ToString(CultureInfo.InvariantCulture)} clips from this song");
        }

        return Task.CompletedTask;
    });

    public void Save(ClipRequest request) => Post(() => HandleSaveAsync(request));

    /// <summary>The feature was switched off: stops a buffer of ours and closes the connection.</summary>
    public void Disconnect() => Post(async () =>
    {
        await StopOwnBufferAsync().ConfigureAwait(false);
        DropConnection();
        _lastProblem = null;
        _lastQuiet = null;
        SetReady(false);
        SetStatus(NotConnectedYet);
    });

    /// <summary>
    /// Game is quitting: waits at most <paramref name="budget"/> for a save in flight and its cut,
    /// stops a buffer of ours, then closes the connection. Later calls are ignored.
    /// </summary>
    public async Task ShutdownAsync(TimeSpan budget)
    {
        if (_disposed)
        {
            return;
        }

        var clock = Stopwatch.StartNew();
        var closing = _queue.Post(() =>
        {
            _closing = true;
            return Task.CompletedTask;
        });
        _disposed = true;
        if (await Within(closing, budget - clock.Elapsed).ConfigureAwait(false))
        {
            var settled = _saveSettled?.Task;
            if (settled != null)
            {
                await Within(settled, budget - clock.Elapsed).ConfigureAwait(false);
            }

            await Within(_queue.Post(StopOwnBufferAsync), budget - clock.Elapsed).ConfigureAwait(false);
            await Within(Task.WhenAll(SnapshotBackground()), budget - clock.Elapsed).ConfigureAwait(false);
        }

        await Within(
            _queue.Post(() =>
            {
                DropConnection();
                return Task.CompletedTask;
            }),
            TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
    }

    /// <summary>Completes when no queued step and no cut is running any more (for tests and tools).</summary>
    public async Task WhenIdleAsync()
    {
        while (true)
        {
            var tail = _queue.Tail;
            await tail.ConfigureAwait(false);
            await Task.WhenAll(SnapshotBackground()).ConfigureAwait(false);
            if (ReferenceEquals(tail, _queue.Tail) && SnapshotBackground().Length == 0)
            {
                return;
            }
        }
    }

    private static string Reason(Exception e) => e is ObsException ? e.Message : e.GetType().Name + ": " + e.Message;

    /// <summary>A refused path for a log line: control characters replaced, cut to a sane length.</summary>
    private static string Shown(string path)
    {
        var text = path.Length > 200 ? path.Substring(0, 200) + "..." : path;
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(c < ' ' || c == '\u007f' ? '?' : c);
        }

        return builder.ToString();
    }

    private static string Sentence(string message) =>
        message.Length == 0 ? message : char.ToUpperInvariant(message[0]) + message.Substring(1);

    private static async Task<bool> Within(Task task, TimeSpan time)
    {
        if (task.IsCompleted)
        {
            return true;
        }

        if (time <= TimeSpan.Zero)
        {
            return false;
        }

        using var cancel = new CancellationTokenSource();
        var first = await Task.WhenAny(task, Task.Delay(time, cancel.Token)).ConfigureAwait(false);
        cancel.Cancel();
        return first == task;
    }

    private static async Task<string?> ParameterAsync(IObsConnection connection, string category, string name)
    {
        var response = await connection
            .RequestAsync(ObsProtocol.GetProfileParameter, ObsProtocol.ProfileParameter(category, name), CancellationToken.None)
            .ConfigureAwait(false);
        return response.Ok
            ? response.GetString(ObsProtocol.ParameterValue) ?? response.GetString(ObsProtocol.DefaultParameterValue)
            : null;
    }

    /// <summary>The "Maximum Replay Time" of the output mode OBS is in, from its current profile.</summary>
    private static async Task<int?> BufferSecondsAsync(IObsConnection connection)
    {
        var mode = await ParameterAsync(connection, "Output", "Mode").ConfigureAwait(false);
        var category = string.Equals(mode, "Advanced", StringComparison.OrdinalIgnoreCase) ? "AdvOut" : "SimpleOutput";
        var text = await ParameterAsync(connection, category, "RecRBTime").ConfigureAwait(false);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0 ? seconds : null;
    }

    private void Post(Func<Task> work)
    {
        if (!_disposed)
        {
            _queue.Post(work);
        }
    }

    /// <summary>
    /// Connects if needed and brings the replay buffer to "running and long enough", or reports why
    /// it is not. Sets <see cref="Ready"/> either way.
    /// </summary>
    private async Task EnsureBufferAsync(bool announce, bool start)
    {
        var connection = await EnsureConnectedAsync(announce).ConfigureAwait(false);
        if (connection == null)
        {
            SetReady(false);
            return;
        }

        try
        {
            var status = await connection.RequestAsync(ObsProtocol.GetReplayBufferStatus, CancellationToken.None).ConfigureAwait(false);
            if (!status.Ok)
            {
                NotReady(
                    status.Code == ObsProtocol.StatusInvalidResourceState
                        ? "the replay buffer is switched off in OBS (Settings > Output > Replay Buffer > Enable Replay Buffer)"
                        : $"OBS did not report its replay buffer ({status.Describe()})",
                    announce,
                    status.Code == ObsProtocol.StatusInvalidResourceState ? "replay buffer off" : UnknownBuffer);
                return;
            }

            var seconds = await BufferSecondsAsync(connection).ConfigureAwait(false);
            if (seconds == null)
            {
                NotReady("could not read the replay buffer length from OBS", announce, UnknownBuffer);
                return;
            }

            if (seconds < MinBufferSeconds)
            {
                NotReady(
                    $"OBS's replay buffer is {seconds.Value.ToString(CultureInfo.InvariantCulture)} s long; clips need {MinBufferSeconds} s or more, {RecommendedBufferSeconds} s is best"
                    + " (OBS: Settings > Output > Replay Buffer > Maximum Replay Time, then stop and start the replay buffer)",
                    announce,
                    "replay buffer too short");
                return;
            }

            var length = seconds.Value.ToString(CultureInfo.InvariantCulture);
            if (!status.GetBool(ObsProtocol.OutputActive))
            {
                _bufferActive = false;
                if (!start)
                {
                    SetReady(false);
                    _trouble = null;
                    _bufferSeconds = seconds.Value;
                    SetStatus($"Connected to OBS {connection.ObsVersion}; the replay buffer ({length} s) starts with the next level");
                    return;
                }

                var started = await connection.RequestAsync(ObsProtocol.StartReplayBuffer, CancellationToken.None).ConfigureAwait(false);
                if (started.Ok)
                {
                    _startedByUs = true;
                    _host.Info($"replay buffer started ({length} s)");
                }
                else if (started.Code != ObsProtocol.StatusOutputRunning)
                {
                    NotReady($"OBS did not start the replay buffer ({started.Describe()})", announce, "replay buffer not started");
                    return;
                }

                MarkBufferRunning();
            }
            else if (!_bufferActive)
            {
                _host.Info($"replay buffer is already running ({length} s); it is used and left running");
                MarkBufferRunning();
            }

            _bufferSeconds = seconds.Value;
            _lastProblem = null;
            _lastQuiet = null;
            _trouble = null;
            SetReady(true);
            SetStatus($"Connected to OBS {connection.ObsVersion}; replay buffer running ({length} s)");
        }
        catch (Exception e)
        {
            NotReady("could not check the replay buffer: " + Reason(e), announce, UnknownBuffer);
        }
    }

    private void MarkBufferRunning()
    {
        _bufferActive = true;
        Volatile.Write(ref _bufferSince, _host.Now);
    }

    private void NotReady(string message, bool announce, string trouble)
    {
        _trouble = trouble;
        SetReady(false);
        SetStatus(Sentence(message));
        if (announce)
        {
            Problem(message);
        }
        else
        {
            Quiet("not ready: " + message);
        }
    }

    private async Task HandleSaveAsync(ClipRequest request)
    {
        if (_pending != null)
        {
            _host.Info($"clip of {request} skipped: OBS is still saving the previous one");
            _host.SaveRefused();
            return;
        }

        var connection = _connection;
        if (connection == null || !connection.IsOpen || !_ready)
        {
            _host.Info($"clip of {request} skipped: OBS is not ready");
            _host.SaveRefused();
            return;
        }

        ObsResponse response;
        var before = _host.Now;
        try
        {
            response = await connection.RequestAsync(ObsProtocol.SaveReplayBuffer, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Problem("could not save the replay: " + Reason(e));
            _host.SaveRefused();
            return;
        }

        if (response.Ok)
        {
            // The video in the file ends somewhere between the request and its answer.
            var wait = new SaveWait(request, (before + _host.Now) / 2.0, connection.IsThisPc);
            _pending = wait;
            _saveSettled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _savedThisLevel++;
            Interlocked.Increment(ref _saved);
            _lastProblem = null;
            _host.Info($"replay save requested for {request} ({request.Seconds.ToString("0.#", CultureInfo.InvariantCulture)} s from {request.SecondsAgo.ToString("0.#", CultureInfo.InvariantCulture)} s ago)");
            _ = SaveTimeoutAsync(wait);
        }
        else if (response.Code == ObsProtocol.StatusOutputNotRunning)
        {
            _bufferActive = false;
            SetReady(false);
            _host.Info("the replay buffer was not running any more (stopped in OBS?); starting it again");
            _host.SaveRefused();
            if (!_closing)
            {
                await EnsureBufferAsync(announce: true, start: true).ConfigureAwait(false);
            }
        }
        else
        {
            Problem($"OBS did not save the replay ({response.Describe()})");
            _host.SaveRefused();
        }
    }

    private async Task SaveTimeoutAsync(SaveWait wait)
    {
        try
        {
            await _host.Delay(SaveTimeout, wait.Cancel.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        _ = _queue.Post(() =>
        {
            if (ReferenceEquals(_pending, wait))
            {
                _host.Warn($"OBS did not report the replay for {wait.Request} as saved within {SaveTimeout.TotalSeconds:0} s; if a file was written it is left as it is");
                SettlePending();
            }

            return Task.CompletedTask;
        });
    }

    private void SettlePending()
    {
        var wait = _pending;
        _pending = null;
        wait?.Cancel.Cancel();
        _saveSettled?.TrySetResult(true);
    }

    private void OnEvent(ObsEvent obsEvent)
    {
        if (obsEvent.Type == ObsProtocol.ReplayBufferSaved)
        {
            var path = obsEvent.GetString(ObsProtocol.SavedReplayPath);
            _ = _queue.Post(() => HandleSavedAsync(path));
        }
        else if (obsEvent.Type == ObsProtocol.ReplayBufferStateChanged)
        {
            var state = obsEvent.GetString(ObsProtocol.OutputState);
            if (state == ObsProtocol.OutputStopped || state == ObsProtocol.OutputStarted)
            {
                _ = _queue.Post(() => HandleBufferStateAsync(state == ObsProtocol.OutputStarted));
            }
        }
    }

    private Task HandleSavedAsync(string? path)
    {
        var wait = _pending;
        if (wait == null)
        {
            // Not asked for by this mod (the player's own save hotkey): not ours to touch.
            return Task.CompletedTask;
        }

        // Track before settling, so a shutdown that waits for the save also waits for the cut.
        Track(ProcessAsync(path, wait));
        SettlePending();
        return Task.CompletedTask;
    }

    private Task HandleBufferStateAsync(bool started)
    {
        if (started)
        {
            // Started by this mod (already known) or by hand in OBS (take a fresh look, never start).
            return _bufferActive || _closing ? Task.CompletedTask : EnsureBufferAsync(announce: false, start: false);
        }

        if (_bufferActive)
        {
            _host.Info("OBS stopped the replay buffer; it is started again when the next level starts");
        }

        _bufferActive = false;
        _startedByUs = false;
        SetReady(false);
        if (_connection != null && _connection.IsOpen)
        {
            SetStatus($"Connected to OBS {_connection.ObsVersion}; the replay buffer starts with the next level");
        }

        return Task.CompletedTask;
    }

    private void OnClosed(IObsConnection connection, string reason) =>
        _ = _queue.Post(() => HandleClosedAsync(connection, reason));

    private Task HandleClosedAsync(IObsConnection connection, string reason)
    {
        if (!ReferenceEquals(_connection, connection))
        {
            return Task.CompletedTask;
        }

        DropConnection();
        _bufferActive = false;
        SetReady(false);
        _host.Info("disconnected: " + reason);
        SetStatus("Not connected: " + reason);
        if (_pending != null)
        {
            _host.Warn($"OBS disconnected before the replay for {_pending.Request} was saved");
            SettlePending();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Cuts the clip out of the saved replay and removes the rest. The path comes from OBS, so it is
    /// not trusted: nothing is touched unless OBS runs on this PC and the path passes
    /// <see cref="RecordingFileName.IsLocalRecordingPath"/>.
    /// </summary>
    private async Task ProcessAsync(string? path, SaveWait wait)
    {
        var request = wait.Request;
        try
        {
            if (!wait.OnThisPc)
            {
                _host.Info($"OBS runs on another PC, so the replay for {request} stays there uncut");
                return;
            }

            if (string.IsNullOrEmpty(path))
            {
                _host.Warn($"OBS did not report the file name of the replay for {request}; it is left as it is");
                return;
            }

            if (!RecordingFileName.IsLocalRecordingPath(path, _windowsPaths, out var why))
            {
                _host.Warn($"not touching the replay for {request}: {why} ({Shown(path!)})");
                return;
            }

            var source = path!;
            var label = request.Label;
            var target = RecordingFileName.Build(source, label.SongName, label.Difficulty, _host.FileExists, request.Extra);
            var fromEnd = request.SecondsAgo + Math.Max(0.0, wait.SnapshotAt - request.DecidedAt) + ClipCut.LeadSeconds;
            var seconds = request.Seconds + ClipCut.LeadSeconds + ClipCut.TailSeconds;

            ClipCutResult result;
            for (var attempt = 1; ; attempt++)
            {
                result = await _host.CutAsync(source, target, fromEnd, seconds).ConfigureAwait(false);
                if (result.Outcome != ClipCutOutcome.Failed || attempt >= CutAttempts)
                {
                    break;
                }

                await _host.Delay(RetryDelay, CancellationToken.None).ConfigureAwait(false);
            }

            if (result.Outcome == ClipCutOutcome.Ok)
            {
                _host.Info("saved clip " + target);
                if (!_host.KeepFullReplay)
                {
                    await DeleteAsync(source).ConfigureAwait(false);
                }

                return;
            }

            if (result.Outcome == ClipCutOutcome.ToolMissing)
            {
                if (Interlocked.Exchange(ref _toolMissingReported, 1) == 0)
                {
                    _host.Warn("ffmpeg could not be started (" + result.Message + "); clips are kept as whole replays until it can");
                    _host.Notify("OBS clips: ffmpeg was not found, so clips are kept as whole replays (install ffmpeg or set ObsClips.FfmpegPath)");
                }
            }
            else
            {
                _host.Warn($"could not cut the clip out of {RecordingFileName.NameOf(source)}: {result.Message}");
            }

            var whole = RecordingFileName.Build(source, label.SongName, label.Difficulty, _host.FileExists, request.Extra + " uncut");
            await MoveAsync(source, whole).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _host.Warn($"clip of {request} failed: {e.Message}");
        }
    }

    private async Task DeleteAsync(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _host.DeleteFile(path);
                return;
            }
            catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                if (attempt >= FileAttempts)
                {
                    _host.Warn($"could not delete the whole replay {path}: {e.Message}");
                    return;
                }
            }

            await _host.Delay(RetryDelay, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task MoveAsync(string from, string to)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _host.MoveFile(from, to);
                _host.Info("kept the whole replay as " + to);
                return;
            }
            catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException)
            {
                _host.Warn($"replay not found at {from}; not renamed");
                return;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                if (attempt >= FileAttempts)
                {
                    _host.Warn($"could not rename {from}: {e.Message}");
                    return;
                }
            }

            await _host.Delay(RetryDelay, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Stops the replay buffer if this class started it and it has not been stopped since.</summary>
    private async Task StopOwnBufferAsync()
    {
        if (!_startedByUs)
        {
            return;
        }

        _startedByUs = false;
        var connection = _connection;
        if (connection == null || !connection.IsOpen)
        {
            return;
        }

        try
        {
            var response = await connection.RequestAsync(ObsProtocol.StopReplayBuffer, CancellationToken.None).ConfigureAwait(false);
            if (response.Ok)
            {
                _host.Info("replay buffer stopped (this mod had started it)");
            }
        }
        catch (Exception e)
        {
            _host.Info("could not stop the replay buffer: " + Reason(e));
        }

        _bufferActive = false;
        SetReady(false);
    }

    private async Task<IObsConnection?> EnsureConnectedAsync(bool announce)
    {
        if (_connection != null && _connection.IsOpen)
        {
            return _connection;
        }

        DropConnection();
        try
        {
            var connection = await _connector.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            connection.EventReceived += OnEvent;
            connection.Closed += OnClosed;
            _connection = connection;
            _connected = true;
            _lastQuiet = null;
            connection.Start();
            _host.Info($"connected to OBS {connection.ObsVersion} (obs-websocket {connection.WebSocketVersion})");
            return connection;
        }
        catch (Exception e)
        {
            var reason = Reason(e);
            SetStatus("Not connected: " + reason);
            if (announce)
            {
                Problem(reason);
            }
            else
            {
                Quiet("not connected: " + reason);
            }

            return null;
        }
    }

    private void DropConnection()
    {
        var connection = _connection;
        _connection = null;
        _connected = false;
        _trouble = null;
        if (connection == null)
        {
            return;
        }

        connection.EventReceived -= OnEvent;
        connection.Closed -= OnClosed;
        try
        {
            connection.Dispose();
        }
        catch (Exception)
        {
            // Closing a broken connection may fail; it is gone either way.
        }
    }

    /// <summary>Logs a quiet (not announced) state once, not again on every retry.</summary>
    private void Quiet(string message)
    {
        if (message == _lastQuiet)
        {
            return;
        }

        _lastQuiet = message;
        _host.Info(message);
    }

    /// <summary>Warns and notifies, unless this same problem was the last thing reported.</summary>
    private void Problem(string message)
    {
        if (message == _lastProblem)
        {
            return;
        }

        _lastProblem = message;
        _host.Warn(message);
        _host.Notify("OBS clips: " + message);
    }

    private void SetReady(bool ready)
    {
        if (_ready == ready)
        {
            return;
        }

        _ready = ready;
        NotifyStatusChanged();
    }

    private void SetStatus(string status)
    {
        if (_status == status)
        {
            return;
        }

        _status = status;
        NotifyStatusChanged();
    }

    private void NotifyStatusChanged()
    {
        try
        {
            _host.StatusChanged();
        }
        catch (Exception)
        {
            // The status line is cosmetic.
        }
    }

    private void Track(Task task)
    {
        lock (_backgroundGate)
        {
            _background.RemoveAll(t => t.IsCompleted);
            _background.Add(task);
        }
    }

    private Task[] SnapshotBackground()
    {
        lock (_backgroundGate)
        {
            _background.RemoveAll(t => t.IsCompleted);
            return _background.ToArray();
        }
    }

    /// <summary>A SaveReplayBuffer that OBS accepted and whose file is not reported yet.</summary>
    private sealed class SaveWait
    {
        public SaveWait(ClipRequest request, double snapshotAt, bool onThisPc)
        {
            Request = request;
            SnapshotAt = snapshotAt;
            OnThisPc = onThisPc;
        }

        public ClipRequest Request { get; }

        /// <summary><see cref="IClipHost.Now"/> at the moment the saved video ends.</summary>
        public double SnapshotAt { get; }

        /// <summary>The OBS that saved the replay runs on this PC, so its file can be cut here.</summary>
        public bool OnThisPc { get; }

        public CancellationTokenSource Cancel { get; } = new();
    }
}
