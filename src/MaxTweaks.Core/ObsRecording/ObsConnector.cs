using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace MaxTweaks.Core.ObsRecording;

/// <summary>
/// Opens a WebSocket to OBS and signs in (Hello, Identify with the authentication string,
/// Identified), subscribing to output events only. Every failure becomes an
/// <see cref="ObsException"/> that says what to fix; none of them contains the password.
/// </summary>
public sealed class ObsConnector : IObsConnector
{
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(5);

    private readonly Func<ObsEndpoint> _endpoint;
    private readonly Func<IObsSocket> _socketFactory;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _requestTimeout;

    /// <param name="endpoint">Called for every attempt, so changed settings apply to the next one.</param>
    /// <param name="socketFactory">A fresh, unconnected socket per attempt.</param>
    public ObsConnector(Func<ObsEndpoint> endpoint, Func<IObsSocket> socketFactory, TimeSpan? connectTimeout = null, TimeSpan? requestTimeout = null)
    {
        _endpoint = endpoint;
        _socketFactory = socketFactory;
        _connectTimeout = connectTimeout ?? DefaultConnectTimeout;
        _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
    }

    public async Task<IObsConnection> ConnectAsync(CancellationToken cancellation)
    {
        var endpoint = _endpoint();
        var socket = _socketFactory();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(_connectTimeout);

            try
            {
                await Bounded(socket.ConnectAsync(endpoint.Uri, timeout.Token), timeout.Token).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellation.IsCancellationRequested)
            {
                throw new ObsException(endpoint.ServerKnownOff
                    ? "the OBS WebSocket server is switched off (in OBS: Tools > WebSocket Server Settings > Enable WebSocket server)"
                    : $"OBS is not reachable at {endpoint.Address} (is OBS running, with Tools > WebSocket Server Settings > Enable WebSocket server?)");
            }

            var hello = await ReceiveAsync(socket, endpoint, timeout.Token, cancellation).ConfigureAwait(false);
            if (hello?.Hello == null)
            {
                throw new ObsException($"{endpoint.Address} did not answer like OBS (obs-websocket 5 is needed, OBS 28 or newer)");
            }

            string? authentication = null;
            if (hello.Hello.AuthenticationRequired)
            {
                if (endpoint.Password.Length == 0)
                {
                    throw new ObsException("OBS asks for its WebSocket password and none is known (set ObsRecording.Password in UserData/MaxTweaks.json)");
                }

                authentication = ObsAuth.Compute(endpoint.Password, hello.Hello.Salt!, hello.Hello.Challenge!);
            }

            try
            {
                await Bounded(socket.SendAsync(ObsProtocol.Identify(authentication, ObsProtocol.EventSubscriptionOutputs), timeout.Token), timeout.Token).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellation.IsCancellationRequested)
            {
                throw new ObsException($"OBS at {endpoint.Address} stopped answering during sign-in");
            }

            var identified = await ReceiveAsync(socket, endpoint, timeout.Token, cancellation).ConfigureAwait(false);
            if (identified == null)
            {
                throw new ObsException(socket.CloseCode == ObsProtocol.CloseAuthenticationFailed
                    ? "OBS rejected the WebSocket password (check ObsRecording.Password, or clear it to use the one OBS has stored)"
                    : "OBS closed the connection during sign-in" + (socket.CloseCode is { } code ? " (close code " + code.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty));
            }

            if (identified.Op != ObsProtocol.OpIdentified)
            {
                throw new ObsException($"{endpoint.Address} did not complete the OBS sign-in");
            }

            return new ObsConnection(socket, hello.Hello, _requestTimeout);
        }
        catch (Exception)
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>The next parseable message, or null when the peer closed the connection.</summary>
    private static async Task<ObsMessage?> ReceiveAsync(IObsSocket socket, ObsEndpoint endpoint, CancellationToken timeout, CancellationToken cancellation)
    {
        while (true)
        {
            string? text;
            try
            {
                text = await Bounded(socket.ReceiveAsync(timeout), timeout).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellation.IsCancellationRequested)
            {
                throw new ObsException($"OBS at {endpoint.Address} stopped answering during sign-in");
            }

            if (text == null)
            {
                return null;
            }

            if (ObsProtocol.TryParse(text, out var message))
            {
                return message;
            }
        }
    }

    /// <summary>
    /// Awaits <paramref name="task"/> but gives up when <paramref name="cancellation"/> fires even if
    /// the task itself ignores the token (socket implementations differ in how promptly they do).
    /// </summary>
    private static async Task<T> Bounded<T>(Task<T> task, CancellationToken cancellation)
    {
        var gaveUp = new TaskCompletionSource<bool>();
        using (cancellation.Register(() => gaveUp.TrySetResult(true)))
        {
            if (await Task.WhenAny(task, gaveUp.Task).ConfigureAwait(false) != task)
            {
                // The abandoned task ends when the caller disposes the socket; keep its failure observed.
                _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                throw new OperationCanceledException(cancellation);
            }
        }

        return await task.ConfigureAwait(false);
    }

    private static async Task Bounded(Task task, CancellationToken cancellation)
    {
        await Bounded(Wrap(task), cancellation).ConfigureAwait(false);
    }

    private static async Task<bool> Wrap(Task task)
    {
        await task.ConfigureAwait(false);
        return true;
    }
}
