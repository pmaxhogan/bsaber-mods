using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MaxTweaks.Core.ObsRecording;

/// <summary>A WebSocket that carries whole text messages; the seam the OBS client is tested through.</summary>
public interface IObsSocket : IDisposable
{
    /// <summary>The close code the peer sent, once <see cref="ReceiveAsync"/> returned null.</summary>
    int? CloseCode { get; }

    Task ConnectAsync(Uri uri, CancellationToken cancellation);

    /// <summary>Sends one text message. Not safe to call concurrently.</summary>
    Task SendAsync(string text, CancellationToken cancellation);

    /// <summary>The next text message, or null once the peer closed the connection.</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellation);
}

/// <summary><see cref="IObsSocket"/> over System.Net.WebSockets.ClientWebSocket (in Unity's Mono too).</summary>
public sealed class ClientWebSocketObsSocket : IObsSocket
{
    private readonly ClientWebSocket _socket = new();
    private readonly byte[] _buffer = new byte[8192];

    public int? CloseCode { get; private set; }

    public Task ConnectAsync(Uri uri, CancellationToken cancellation) => _socket.ConnectAsync(uri, cancellation);

    public Task SendAsync(string text, CancellationToken cancellation)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellation);
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellation)
    {
        using var message = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(new ArraySegment<byte>(_buffer), cancellation).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                CloseCode = (int?)result.CloseStatus;
                return null;
            }

            message.Write(_buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }

            // A binary message: not part of the JSON protocol, skip it.
            message.SetLength(0);
        }
    }

    public void Dispose()
    {
        try
        {
            _socket.Abort();
        }
        catch (Exception)
        {
            // Already closed or never connected.
        }

        _socket.Dispose();
    }
}
