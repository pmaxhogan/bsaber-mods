using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace MaxTweaks.Core.ObsRecording;

/// <summary>A failure talking to OBS whose message is fit to show the player (never contains the password).</summary>
public sealed class ObsException : Exception
{
    public ObsException(string message)
        : base(message)
    {
    }

    public ObsException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>A signed-in obs-websocket session.</summary>
public interface IObsConnection : IDisposable
{
    string ObsVersion { get; }

    string WebSocketVersion { get; }

    bool IsOpen { get; }

    /// <summary>
    /// The OBS on the other end runs on this PC (see <see cref="ObsEndpoint.IsThisPc"/>), so the
    /// file names it reports are files on this PC. False: they are on another machine.
    /// </summary>
    bool IsThisPc { get; }

    /// <summary>Raised from the receive loop's thread for every event OBS sends.</summary>
    event Action<ObsEvent>? EventReceived;

    /// <summary>Raised once when the session ends, with a reason for the log.</summary>
    event Action<IObsConnection, string>? Closed;

    /// <summary>Starts reading messages. Call after subscribing to the events so none is missed.</summary>
    void Start();

    /// <summary>Sends a request and waits for its response; throws <see cref="ObsException"/> when that fails.</summary>
    Task<ObsResponse> RequestAsync(string requestType, CancellationToken cancellation);
}

/// <summary>Opens signed-in sessions.</summary>
public interface IObsConnector
{
    /// <summary>Throws <see cref="ObsException"/> with the reason when OBS cannot be reached or refuses.</summary>
    Task<IObsConnection> ConnectAsync(CancellationToken cancellation);
}

/// <summary>
/// <see cref="IObsConnection"/> over an <see cref="IObsSocket"/> that has already been identified:
/// one receive loop matches responses to requests by id and hands events on.
/// </summary>
public sealed class ObsConnection : IObsConnection
{
    private readonly IObsSocket _socket;
    private readonly TimeSpan _requestTimeout;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ObsResponse>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _nextId;
    private int _started;
    private int _closed;

    public ObsConnection(IObsSocket socket, ObsHello hello, TimeSpan requestTimeout, bool isThisPc)
    {
        _socket = socket;
        _requestTimeout = requestTimeout;
        ObsVersion = hello.ObsVersion;
        WebSocketVersion = hello.WebSocketVersion;
        IsThisPc = isThisPc;
    }

    public event Action<ObsEvent>? EventReceived;

    public event Action<IObsConnection, string>? Closed;

    public string ObsVersion { get; }

    public string WebSocketVersion { get; }

    public bool IsThisPc { get; }

    public bool IsOpen => Volatile.Read(ref _closed) == 0;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            Task.Run(ReceiveLoopAsync);
        }
    }

    public async Task<ObsResponse> RequestAsync(string requestType, CancellationToken cancellation)
    {
        if (!IsOpen)
        {
            throw new ObsException("not connected to OBS");
        }

        var id = Interlocked.Increment(ref _nextId).ToString(CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<ObsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            await _sendLock.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(ObsProtocol.Request(requestType, id), cancellation).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var first = await Task.WhenAny(completion.Task, Task.Delay(_requestTimeout, timer.Token)).ConfigureAwait(false);
            timer.Cancel();
            if (first != completion.Task)
            {
                cancellation.ThrowIfCancellationRequested();
                throw new ObsException($"OBS did not answer {requestType} within {_requestTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s");
            }

            return await completion.Task.ConfigureAwait(false);
        }
        catch (ObsException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new ObsException($"could not send {requestType} to OBS: {e.Message}", e);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public void Dispose() => Close("closed by MaxTweaks");

    private async Task ReceiveLoopAsync()
    {
        var reason = "OBS closed the connection";
        try
        {
            while (true)
            {
                var text = await _socket.ReceiveAsync(_lifetime.Token).ConfigureAwait(false);
                if (text == null)
                {
                    break;
                }

                if (!ObsProtocol.TryParse(text, out var message))
                {
                    continue;
                }

                if (message.Response != null)
                {
                    if (_pending.TryRemove(message.Response.RequestId, out var completion))
                    {
                        completion.TrySetResult(message.Response);
                    }
                }
                else if (message.Event != null)
                {
                    RaiseEvent(message.Event);
                }
            }
        }
        catch (OperationCanceledException)
        {
            reason = "closed by MaxTweaks";
        }
        catch (Exception e)
        {
            reason = "the connection to OBS was lost (" + e.Message + ")";
        }

        Close(reason);
    }

    private void RaiseEvent(ObsEvent obsEvent)
    {
        try
        {
            EventReceived?.Invoke(obsEvent);
        }
        catch (Exception)
        {
            // A subscriber's failure must not end the receive loop.
        }
    }

    private void Close(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        _socket.Dispose();
        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var completion))
            {
                completion.TrySetException(new ObsException(reason));
            }
        }

        try
        {
            Closed?.Invoke(this, reason);
        }
        catch (Exception)
        {
            // Nothing useful to do with a subscriber's failure here.
        }
    }
}
