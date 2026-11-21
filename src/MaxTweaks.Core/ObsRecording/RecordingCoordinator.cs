using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MaxTweaks.Core.ObsRecording;

/// <summary>What the coordinator needs from the outside world. Every member may be called from any thread.</summary>
public interface IRecordingHost
{
    void Info(string message);

    void Warn(string message);

    /// <summary>A short message for the player (a toast, if those are on).</summary>
    void Notify(string message);

    /// <summary><see cref="RecordingCoordinator.Status"/> changed. Must not block.</summary>
    void StatusChanged();

    bool FileExists(string path);

    /// <summary>Renames a file; throws IOException while another program still has it open.</summary>
    void MoveFile(string from, string to);

    Task Delay(TimeSpan time, CancellationToken cancellation);
}

/// <summary>
/// Starts an OBS recording when a level starts, stops it when the level ends and renames the saved
/// file after the song. All work runs one step at a time on the thread pool, in the order the
/// calls and OBS's events arrive, so callers (the game's main thread) never wait on the network.
///
/// Rules:
/// - Only a recording this class started is ever stopped or renamed. If OBS is already recording
///   when a level starts, that recording is left alone. A recording of ours that ends without our
///   asking (stopped in OBS, or an OBS error) is reported and keeps OBS's name.
/// - The file name OBS reports is not trusted: it is renamed only when OBS runs on this PC and the
///   name is a full local path to a recording (see RecordingFileName.IsLocalRecordingPath).
/// - The file is renamed after OBS reports the output as stopped (RecordStateChanged with
///   OBS_WEBSOCKET_OUTPUT_STOPPED and the path); if that never arrives the path from the StopRecord
///   response is used after <see cref="StopTimeout"/>.
/// - A level that starts while the previous file is still being finished (a restart) waits for it,
///   because OBS refuses to start while the output is active.
/// - One connection stays open between levels and is reopened when it dropped. A problem is
///   reported (warning plus notification) once until something works again, not once per level.
/// </summary>
public sealed class RecordingCoordinator
{
    public const int RenameAttempts = 20;

    public const string NotConnectedYet = "Not connected yet";

    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan RenameRetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly IObsConnector _connector;
    private readonly IRecordingHost _host;
    private readonly SerialQueue _queue;
    private readonly bool _windowsPaths;
    private readonly object _backgroundGate = new();
    private readonly List<Task> _background = new();

    // Everything below is touched only by queued work, one step at a time.
    private IObsConnection? _connection;
    private RecordingLabel? _active;
    private RecordingLabel? _pendingStart;
    private StopWait? _stopWait;
    private string? _lastProblem;
    private string? _givenUpOn;
    private string? _lastQuiet;
    private bool _closing;

    private volatile string _status = NotConnectedYet;
    private volatile bool _disposed;
    private volatile bool _connected;
    private volatile TaskCompletionSource<bool>? _stopSettled;
    private int _saved;
    private int _probeQueued;

    /// <param name="windowsPaths">
    /// Which rules a file name reported by OBS must pass before it is renamed
    /// (<see cref="RecordingFileName.IsLocalRecordingPath"/>); null = those of the system this runs on.
    /// </param>
    public RecordingCoordinator(IObsConnector connector, IRecordingHost host, bool? windowsPaths = null)
    {
        _connector = connector;
        _host = host;
        _windowsPaths = windowsPaths ?? Path.DirectorySeparatorChar == '\\';
        _queue = new SerialQueue(e => _host.Warn("unexpected error: " + e));
    }

    /// <summary>One line for the settings page, e.g. "Connected to OBS 31.0.0".</summary>
    public string Status => _status;

    /// <summary>The connection to OBS is open right now.</summary>
    public bool Connected => _connected;

    /// <summary>
    /// Recordings of this mod that OBS has finished since this object was made (the game start),
    /// renamed or not. Recordings somebody else started are not counted.
    /// </summary>
    public int SavedCount => Volatile.Read(ref _saved);

    /// <summary>
    /// Connects now if not connected, without notifying the player when that fails. Cheap to call
    /// again and again: one at a time, and a failure is logged once per reason.
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
                await EnsureConnectedAsync(announce: false).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _probeQueued, 0);
            }
        });
    }

    public void LevelStarted(RecordingLabel label) => Post(() => HandleLevelStartedAsync(label));

    /// <param name="label">What to name the file after (the difficulty can change during a level).</param>
    public void LevelEnded(RecordingLabel label) => Post(() => HandleLevelEndedAsync(label));

    /// <summary>Closes the connection unless a recording of ours is running or being finished.</summary>
    public void Disconnect() => Post(HandleDisconnectAsync);

    /// <summary>
    /// Game is quitting: stops a recording of ours, waits at most <paramref name="budget"/> for OBS to
    /// finish the file and for the rename, then closes the connection. Later calls are ignored.
    /// </summary>
    public async Task ShutdownAsync(TimeSpan budget)
    {
        if (_disposed)
        {
            return;
        }

        var clock = Stopwatch.StartNew();
        var stop = _queue.Post(async () =>
        {
            _closing = true;
            _pendingStart = null;
            if (_active != null)
            {
                var label = _active;
                _active = null;
                await StopAsync(label).ConfigureAwait(false);
            }
        });
        _disposed = true;
        if (await Within(stop, budget - clock.Elapsed).ConfigureAwait(false))
        {
            var settled = _stopSettled?.Task;
            if (settled == null || await Within(settled, budget - clock.Elapsed).ConfigureAwait(false))
            {
                await Within(Task.WhenAll(SnapshotBackground()), budget - clock.Elapsed).ConfigureAwait(false);
            }
        }

        await Within(
            _queue.Post(() =>
            {
                DropConnection();
                return Task.CompletedTask;
            }),
            TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
    }

    /// <summary>Completes when no queued step and no rename is running any more (for tests and tools).</summary>
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

    private static bool SamePath(string a, string b) =>
        string.Equals(a.Replace('\\', '/'), b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

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

    private void Post(Func<Task> work)
    {
        if (!_disposed)
        {
            _queue.Post(work);
        }
    }

    private async Task HandleLevelStartedAsync(RecordingLabel label)
    {
        if (_closing)
        {
            return;
        }

        if (_active != null)
        {
            // A start without an end in between: finish the old file first.
            var previous = _active;
            _active = null;
            await StopAsync(previous).ConfigureAwait(false);
        }

        if (_stopWait != null)
        {
            _pendingStart = label;
            _host.Info($"start for {label} waits until OBS has finished the previous file");
            return;
        }

        await StartAsync(label).ConfigureAwait(false);
    }

    private async Task HandleLevelEndedAsync(RecordingLabel label)
    {
        if (_pendingStart != null)
        {
            _pendingStart = null;
            _host.Info("level ended before its recording could start");
            return;
        }

        if (_active == null)
        {
            return;
        }

        _active = null;
        await StopAsync(label).ConfigureAwait(false);
    }

    private Task HandleDisconnectAsync()
    {
        _pendingStart = null;
        if (_active == null && _stopWait == null)
        {
            DropConnection();
            _lastProblem = null;
            _lastQuiet = null;
            SetStatus(NotConnectedYet);
        }

        return Task.CompletedTask;
    }

    private async Task StartAsync(RecordingLabel label)
    {
        var connection = await EnsureConnectedAsync(announce: true).ConfigureAwait(false);
        if (connection == null)
        {
            return;
        }

        ObsResponse response;
        try
        {
            response = await connection.RequestAsync(ObsProtocol.StartRecord, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Problem("could not start the recording: " + Reason(e));
            return;
        }

        if (response.Ok)
        {
            _active = label;
            _lastProblem = null;
            _host.Info($"record started for {label}");
            SetStatus("Recording " + label);
        }
        else if (response.Code == ObsProtocol.StatusOutputRunning)
        {
            Problem("OBS is already recording; that recording is left alone, so levels get no file of their own");
        }
        else
        {
            Problem($"OBS did not start recording ({response.Describe()})");
        }
    }

    private async Task StopAsync(RecordingLabel label)
    {
        var connection = await EnsureConnectedAsync(announce: false).ConfigureAwait(false);
        if (connection == null)
        {
            Problem("could not reach OBS to stop the recording; stop it in OBS");
            return;
        }

        ObsResponse response;
        try
        {
            response = await connection.RequestAsync(ObsProtocol.StopRecord, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Problem("could not stop the recording: " + Reason(e));
            SetConnectedStatus();
            return;
        }

        if (response.Ok)
        {
            var wait = new StopWait(label, response.GetString(ObsProtocol.OutputPath), connection.IsThisPc);
            _stopWait = wait;
            _stopSettled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _host.Info($"stop requested for {label}; waiting for OBS to finish the file");
            SetStatus("Saving the recording");
            _ = StopTimeoutAsync(wait);
        }
        else if (response.Code == ObsProtocol.StatusOutputNotRunning)
        {
            _host.Info("OBS was not recording any more (stopped in OBS?); nothing to rename");
            SetConnectedStatus();
        }
        else
        {
            Problem($"OBS did not stop recording ({response.Describe()})");
            SetConnectedStatus();
        }
    }

    private async Task StopTimeoutAsync(StopWait wait)
    {
        try
        {
            await _host.Delay(StopTimeout, wait.Cancel.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return;
        }

        _ = _queue.Post(() =>
        {
            if (!ReferenceEquals(_stopWait, wait))
            {
                return Task.CompletedTask;
            }

            _host.Warn($"OBS did not report the recording as finished within {StopTimeout.TotalSeconds:0} s");
            _givenUpOn = wait.Path;
            return FinishStopAsync(wait.Path);
        });
    }

    private void OnEvent(ObsEvent obsEvent)
    {
        if (obsEvent.Type != ObsProtocol.RecordStateChanged || obsEvent.GetString(ObsProtocol.OutputState) != ObsProtocol.OutputStopped)
        {
            return;
        }

        var path = obsEvent.GetString(ObsProtocol.OutputPath);
        _ = _queue.Post(() => HandleStoppedAsync(path));
    }

    private Task HandleStoppedAsync(string? path)
    {
        var wait = _stopWait;
        if (wait == null)
        {
            if (_active != null && (path == null || _givenUpOn == null || !SamePath(path, _givenUpOn)))
            {
                // Ours, but nobody asked for the stop: stopped by hand in OBS, or OBS gave up (disk
                // full, encoder error). The level's end still sends StopRecord, which then answers 501.
                Problem("OBS stopped recording before the level ended; that file keeps OBS's name");
            }

            // Otherwise the player's own recording, or the late report for a file we gave up waiting on.
            return Task.CompletedTask;
        }

        if (path != null && wait.Path != null && !SamePath(path, wait.Path))
        {
            _host.Info("ignored a finished file that is not the one being waited for");
            return Task.CompletedTask;
        }

        return FinishStopAsync(path ?? wait.Path);
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
        _host.Info("disconnected: " + reason);
        SetStatus("Not connected: " + reason);
        if (_stopWait == null)
        {
            return Task.CompletedTask;
        }

        _host.Warn("OBS disconnected before reporting the recording as finished");
        return FinishStopAsync(_stopWait.Path);
    }

    /// <summary>The stop being waited for is over: rename in the background and start a queued level.</summary>
    private async Task FinishStopAsync(string? path)
    {
        var wait = _stopWait!;
        _stopWait = null;
        wait.Cancel.Cancel();
        Track(RenameAsync(path, wait.Label, wait.OnThisPc));
        Interlocked.Increment(ref _saved);
        _stopSettled?.TrySetResult(true);
        SetConnectedStatus();

        var next = _pendingStart;
        _pendingStart = null;
        if (next != null && !_closing)
        {
            await StartAsync(next).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Renames the finished file. The path comes from OBS, so it is not trusted: nothing is touched
    /// unless OBS runs on this PC and the path passes <see cref="RecordingFileName.IsLocalRecordingPath"/>.
    /// </summary>
    private async Task RenameAsync(string? path, RecordingLabel label, bool onThisPc)
    {
        try
        {
            if (!onThisPc)
            {
                _host.Info($"OBS runs on another PC, so the recording of {label} keeps OBS's name");
                return;
            }

            if (string.IsNullOrEmpty(path))
            {
                _host.Warn($"OBS did not report the file name of the recording of {label}; it keeps its name");
                return;
            }

            if (!RecordingFileName.IsLocalRecordingPath(path, _windowsPaths, out var why))
            {
                _host.Warn($"not renaming the recording of {label}: {why} ({Shown(path!)})");
                return;
            }

            for (var attempt = 1; ; attempt++)
            {
                var target = RecordingFileName.Build(path!, label.SongName, label.Difficulty, _host.FileExists);
                try
                {
                    if (!SamePath(target, path!))
                    {
                        _host.MoveFile(path!, target);
                    }

                    _host.Info("saved " + target);
                    _host.Notify("Recording saved: " + RecordingFileName.NameOf(target));
                    return;
                }
                catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException)
                {
                    _host.Warn($"recording not found at {path}; not renamed");
                    return;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    if (attempt >= RenameAttempts)
                    {
                        _host.Warn($"could not rename {path}: {e.Message}");
                        _host.Notify("OBS recording: could not rename " + RecordingFileName.NameOf(path!));
                        return;
                    }
                }

                await _host.Delay(RenameRetryDelay, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            _host.Warn($"rename of {path} failed: {e.Message}");
        }
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
            SetConnectedStatus();
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
        _host.Notify("OBS recording: " + message);
    }

    private void SetConnectedStatus()
    {
        if (_connection != null && _connection.IsOpen)
        {
            SetStatus("Connected to OBS " + _connection.ObsVersion);
        }
    }

    private void SetStatus(string status)
    {
        if (_status == status)
        {
            return;
        }

        _status = status;
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

    /// <summary>A StopRecord that OBS accepted and whose file is not reported as finished yet.</summary>
    private sealed class StopWait
    {
        public StopWait(RecordingLabel label, string? path, bool onThisPc)
        {
            Label = label;
            Path = path;
            OnThisPc = onThisPc;
        }

        public RecordingLabel Label { get; }

        /// <summary>The OBS that accepted the stop runs on this PC, so its file can be renamed here.</summary>
        public bool OnThisPc { get; }

        /// <summary>The file name from the StopRecord response.</summary>
        public string? Path { get; }

        public CancellationTokenSource Cancel { get; } = new();
    }
}

/// <summary>Runs posted work one at a time, in order, on the thread pool; a failing step is reported and skipped.</summary>
internal sealed class SerialQueue
{
    private readonly object _gate = new();
    private readonly Action<Exception> _onError;
    private Task _tail = Task.CompletedTask;

    public SerialQueue(Action<Exception> onError)
    {
        _onError = onError;
    }

    public Task Tail
    {
        get
        {
            lock (_gate)
            {
                return _tail;
            }
        }
    }

    public Task Post(Func<Task> work)
    {
        lock (_gate)
        {
            _tail = _tail.ContinueWith(_ => RunAsync(work), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            return _tail;
        }
    }

    private async Task RunAsync(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            try
            {
                _onError(e);
            }
            catch (Exception)
            {
                // Reporting failed too; keep the queue alive.
            }
        }
    }
}
