using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxTweaks.Core.ObsRecording;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MaxTweaks.Core.Tests.ObsRecording;

internal static class ObsTestData
{
    public const string Stopped = "OBS_WEBSOCKET_OUTPUT_STOPPED";

    public static ObsResponse Ok(string type, JObject? data = null) => new(type, "1", true, 100, null, data);

    public static ObsResponse Fail(string type, int code, string? comment = null) => new(type, "1", false, code, comment, null);

    public static ObsResponse StopOk(string? path) =>
        Ok("StopRecord", path == null ? null : new JObject { ["outputPath"] = path });

    public static ObsEvent RecordState(string state, string? path = null) => new(
        "RecordStateChanged",
        new JObject { ["outputActive"] = false, ["outputState"] = state, ["outputPath"] = path });

    public static ObsEvent StoppedEvent(string? path) => RecordState(Stopped, path);

    /// <summary>Polls until the condition holds; fails the test after the timeout.</summary>
    public static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 10000)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.ElapsedMilliseconds < timeoutMs, "timed out waiting for " + what);
            await Task.Delay(10);
        }
    }
}

/// <summary>A scripted, in-memory WebSocket.</summary>
internal sealed class FakeSocket : IObsSocket
{
    private readonly SemaphoreSlim _available = new(0);
    private readonly ConcurrentQueue<object?> _incoming = new();
    private readonly List<string> _sent = new();

    public int? CloseCode { get; set; }

    public Uri? ConnectedTo { get; private set; }

    public bool Disposed { get; private set; }

    public Exception? ConnectError { get; set; }

    public bool HangConnect { get; set; }

    public Exception? SendError { get; set; }

    /// <summary>Messages to enqueue in reply to each sent message.</summary>
    public Func<string, IEnumerable<string?>>? OnSend { get; set; }

    public IReadOnlyList<string> Sent
    {
        get
        {
            lock (_sent)
            {
                return _sent.ToArray();
            }
        }
    }

    public Task ConnectAsync(Uri uri, CancellationToken cancellation)
    {
        ConnectedTo = uri;
        if (ConnectError != null)
        {
            return Task.FromException(ConnectError);
        }

        return HangConnect ? Task.Delay(Timeout.Infinite, CancellationToken.None) : Task.CompletedTask;
    }

    public Task SendAsync(string text, CancellationToken cancellation)
    {
        if (SendError != null)
        {
            return Task.FromException(SendError);
        }

        lock (_sent)
        {
            _sent.Add(text);
        }

        foreach (var reply in OnSend?.Invoke(text) ?? Array.Empty<string?>())
        {
            Enqueue(reply);
        }

        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellation)
    {
        await _available.WaitAsync(cancellation);
        _incoming.TryDequeue(out var next);
        if (next is Exception error)
        {
            throw error;
        }

        return (string?)next;
    }

    /// <summary>Queues a message from "OBS"; null means the peer closed the connection.</summary>
    public void Enqueue(string? message)
    {
        _incoming.Enqueue(message);
        _available.Release();
    }

    public void EnqueueError(Exception error)
    {
        _incoming.Enqueue(error);
        _available.Release();
    }

    public void Dispose() => Disposed = true;

    public static string Hello(string? challenge = null, string? salt = null)
    {
        var d = new JObject { ["obsStudioVersion"] = "31.0.0", ["obsWebSocketVersion"] = "5.5.4", ["rpcVersion"] = 1 };
        if (challenge != null)
        {
            d["authentication"] = new JObject { ["challenge"] = challenge, ["salt"] = salt };
        }

        return new JObject { ["op"] = 0, ["d"] = d }.ToString();
    }

    public static string Identified() => "{\"op\":2,\"d\":{\"negotiatedRpcVersion\":1}}";

    public static string Response(string id, string type, bool ok = true, int code = 100, JObject? data = null)
    {
        var d = new JObject
        {
            ["requestType"] = type,
            ["requestId"] = id,
            ["requestStatus"] = new JObject { ["result"] = ok, ["code"] = code },
        };
        if (data != null)
        {
            d["responseData"] = data;
        }

        return new JObject { ["op"] = 7, ["d"] = d }.ToString();
    }

    public static string Event(string type, JObject data) =>
        new JObject { ["op"] = 5, ["d"] = new JObject { ["eventType"] = type, ["eventIntent"] = 64, ["eventData"] = data } }.ToString();
}

/// <summary>A signed-in session whose answers the test scripts.</summary>
internal sealed class FakeConnection : IObsConnection
{
    private readonly List<string> _requests = new();

    public event Action<ObsEvent>? EventReceived;

    public event Action<IObsConnection, string>? Closed;

    public string ObsVersion => "31.0.0";

    public string WebSocketVersion => "5.5.4";

    public bool IsOpen { get; private set; } = true;

    public bool IsThisPc { get; set; } = true;

    public bool Started { get; private set; }

    public bool Disposed { get; private set; }

    /// <summary>The file name StopRecord answers with.</summary>
    public string? StopPath { get; set; } = "C:/Videos/2026-01-02 10-00-00.mp4";

    /// <summary>Overrides the default answers (StartRecord ok, StopRecord ok with <see cref="StopPath"/>).</summary>
    public Func<string, ObsResponse>? Respond { get; set; }

    /// <summary>Like <see cref="Respond"/>, for tests that care about the request data; wins when set.</summary>
    public Func<string, JObject?, ObsResponse>? RespondWithData { get; set; }

    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToArray();
            }
        }
    }

    public bool HasSubscribers => EventReceived != null || Closed != null;

    public void Start() => Started = true;

    public Task<ObsResponse> RequestAsync(string requestType, CancellationToken cancellation) =>
        RequestAsync(requestType, null, cancellation);

    public Task<ObsResponse> RequestAsync(string requestType, JObject? requestData, CancellationToken cancellation)
    {
        lock (_requests)
        {
            _requests.Add(requestType);
        }

        try
        {
            var response = RespondWithData != null
                ? RespondWithData(requestType, requestData)
                : Respond != null
                ? Respond(requestType)
                : requestType == "StopRecord" ? ObsTestData.StopOk(StopPath) : ObsTestData.Ok(requestType);
            return Task.FromResult(response);
        }
        catch (Exception e)
        {
            return Task.FromException<ObsResponse>(e);
        }
    }

    public void Raise(ObsEvent obsEvent) => EventReceived?.Invoke(obsEvent);

    /// <summary>The session ends from OBS's side.</summary>
    public void Drop(string reason)
    {
        IsOpen = false;
        Closed?.Invoke(this, reason);
    }

    public void Dispose()
    {
        Disposed = true;
        IsOpen = false;
    }
}

internal sealed class FakeConnector : IObsConnector
{
    private readonly List<FakeConnection> _connections = new();

    public int Attempts { get; private set; }

    /// <summary>When set, connecting fails with this.</summary>
    public Exception? Error { get; set; }

    /// <summary>Applied to every new connection before it is handed out.</summary>
    public Action<FakeConnection>? Configure { get; set; }

    public IReadOnlyList<FakeConnection> Connections
    {
        get
        {
            lock (_connections)
            {
                return _connections.ToArray();
            }
        }
    }

    public FakeConnection Last => Connections[Connections.Count - 1];

    public Task<IObsConnection> ConnectAsync(CancellationToken cancellation)
    {
        Attempts++;
        if (Error != null)
        {
            return Task.FromException<IObsConnection>(Error);
        }

        var connection = new FakeConnection();
        Configure?.Invoke(connection);
        lock (_connections)
        {
            _connections.Add(connection);
        }

        return Task.FromResult<IObsConnection>(connection);
    }
}

/// <summary>Records what the coordinator tells the outside world, with an in-memory "disk".</summary>
internal sealed class FakeHost : IRecordingHost
{
    private readonly object _gate = new();
    private readonly List<string> _infos = new();
    private readonly List<string> _warnings = new();
    private readonly List<string> _notes = new();
    private readonly List<(string From, string To)> _moves = new();
    private readonly List<TimeSpan> _delays = new();
    private readonly List<TaskCompletionSource<bool>> _heldTimeouts = new();
    private readonly HashSet<string> _files = new();
    private readonly TaskCompletionSource<bool> _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _release = new(false);
    private volatile string? _blockOn;

    public int StatusChanges { get; private set; }

    /// <summary>Called with the attempt number (1-based); a returned exception is thrown by MoveFile.</summary>
    public Func<int, Exception?>? MoveError { get; set; }

    public bool ThrowOnNextInfo { get; set; }

    public int MoveAttempts { get; private set; }

    public IReadOnlyList<string> Infos => Copy(_infos);

    public IReadOnlyList<string> Warnings => Copy(_warnings);

    public IReadOnlyList<string> Notes => Copy(_notes);

    public IReadOnlyList<(string From, string To)> Moves => Copy(_moves);

    public IReadOnlyList<TimeSpan> Delays => Copy(_delays);

    public void AddFile(string path)
    {
        lock (_gate)
        {
            _files.Add(path);
        }
    }

    public void Info(string message)
    {
        lock (_gate)
        {
            if (ThrowOnNextInfo)
            {
                ThrowOnNextInfo = false;
                throw new InvalidOperationException("log is broken");
            }

            _infos.Add(message);
        }

        if (_blockOn != null && message.Contains(_blockOn))
        {
            _blockOn = null;
            _blocked.TrySetResult(true);
            _release.Wait(TimeSpan.FromSeconds(10));
        }
    }

    /// <summary>
    /// Makes the coordinator's current step hang inside the next Info call containing the text, so a
    /// test can queue more work behind it; resolves once the step is hanging.
    /// </summary>
    public Task BlockOnInfo(string text)
    {
        _blockOn = text;
        return _blocked.Task;
    }

    public void ReleaseInfo() => _release.Set();

    public void Warn(string message)
    {
        lock (_gate)
        {
            _warnings.Add(message);
        }
    }

    public void Notify(string message)
    {
        lock (_gate)
        {
            _notes.Add(message);
        }
    }

    public void StatusChanged()
    {
        lock (_gate)
        {
            StatusChanges++;
        }
    }

    public bool FileExists(string path)
    {
        lock (_gate)
        {
            return _files.Contains(path);
        }
    }

    public void MoveFile(string from, string to)
    {
        lock (_gate)
        {
            MoveAttempts++;
            var error = MoveError?.Invoke(MoveAttempts);
            if (error != null)
            {
                throw error;
            }

            _moves.Add((from, to));
            _files.Remove(from);
            _files.Add(to);
        }
    }

    /// <summary>Short waits (rename retries) pass at once; the stop timeout waits for <see cref="FireStopTimeout"/>.</summary>
    public Task Delay(TimeSpan time, CancellationToken cancellation)
    {
        lock (_gate)
        {
            _delays.Add(time);
            if (time < RecordingCoordinator.StopTimeout)
            {
                return Task.CompletedTask;
            }

            var held = new TaskCompletionSource<bool>();
            cancellation.Register(() => held.TrySetCanceled());
            _heldTimeouts.Add(held);
            return held.Task;
        }
    }

    /// <summary>Lets every pending stop timeout elapse; returns how many were still pending.</summary>
    public int FireStopTimeout()
    {
        TaskCompletionSource<bool>[] held;
        lock (_gate)
        {
            held = _heldTimeouts.ToArray();
            _heldTimeouts.Clear();
        }

        return held.Count(h => h.TrySetResult(true));
    }

    private IReadOnlyList<T> Copy<T>(List<T> list)
    {
        lock (_gate)
        {
            return list.ToArray();
        }
    }
}
