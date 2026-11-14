using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxTweaks.Core.ObsRecording;
using Newtonsoft.Json.Linq;

namespace MaxTweaks.Core.Tests.ObsRecording;

/// <summary>
/// A local WebSocket server that behaves like OBS's obs-websocket 5 for the requests this mod uses:
/// sign-in with a password, StartRecord/StopRecord with the right error codes, a real file that is
/// held open while "recording", and RecordStateChanged events (STOPPED only after the file is
/// closed). StartRecord while the previous output is still stopping is refused with 500, as in OBS.
/// </summary>
internal sealed class FakeObsServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly string _folder;
    private readonly string? _password;
    private readonly List<string> _requests = new();
    private FileStream? _file;
    private string? _path;
    private bool _active;
    private int _recordings;

    public FakeObsServer(string folder, string? password)
    {
        _folder = folder;
        _password = password;
        (_listener, Port) = Listen();
        _ = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    /// <summary>How long "OBS" needs to finish the file after StopRecord.</summary>
    public TimeSpan FinishDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Keeps the finished file locked for this long after reporting it stopped.</summary>
    public TimeSpan HoldAfterStopped { get; set; } = TimeSpan.Zero;

    public bool OutputActive
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    public int RecordingsStarted
    {
        get
        {
            lock (_gate)
            {
                return _recordings;
            }
        }
    }

    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>The player's own recording, started in OBS before any level.</summary>
    public string StartOwnRecording()
    {
        lock (_gate)
        {
            return BeginRecording();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (Exception)
        {
            // Already stopped.
        }

        lock (_gate)
        {
            _file?.Dispose();
            _file = null;
        }
    }

    private static (HttpListener Listener, int Port) Listen()
    {
        for (var attempt = 0; ; attempt++)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/");
            try
            {
                listener.Start();
                return (listener, port);
            }
            catch (HttpListenerException) when (attempt < 10)
            {
                listener.Close();
            }
        }
    }

    private static async Task<string?> ReceiveAsync(WebSocket socket, CancellationToken cancellation)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellation);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(message.ToArray());
            }
        }
    }

    private static JObject Message(int op, JObject d) => new() { ["op"] = op, ["d"] = d };

    private static JObject RecordState(bool active, string state, string? path) => Message(5, new JObject
    {
        ["eventType"] = "RecordStateChanged",
        ["eventIntent"] = 64,
        ["eventData"] = new JObject { ["outputActive"] = active, ["outputState"] = state, ["outputPath"] = path },
    });

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
                _ = Task.Run(() => ServeAsync(socket));
            }
            catch (Exception)
            {
                return;
            }
        }
    }

    private async Task ServeAsync(WebSocket socket)
    {
        using var sendLock = new SemaphoreSlim(1, 1);

        async Task Send(JObject message)
        {
            var bytes = Encoding.UTF8.GetBytes(message.ToString(Newtonsoft.Json.Formatting.None));
            await sendLock.WaitAsync(_stop.Token);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _stop.Token);
            }
            finally
            {
                sendLock.Release();
            }
        }

        try
        {
            var challenge = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            var salt = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            var hello = new JObject { ["obsStudioVersion"] = "31.0.0", ["obsWebSocketVersion"] = "5.5.4", ["rpcVersion"] = 1 };
            if (_password != null)
            {
                hello["authentication"] = new JObject { ["challenge"] = challenge, ["salt"] = salt };
            }

            await Send(Message(0, hello));

            var identifyText = await ReceiveAsync(socket, _stop.Token);
            if (identifyText == null)
            {
                return;
            }

            var identify = JObject.Parse(identifyText);
            var given = (string?)identify["d"]?["authentication"];
            if ((int?)identify["op"] != 1 || (_password != null && given != ObsAuth.Compute(_password, salt, challenge)))
            {
                await socket.CloseAsync((WebSocketCloseStatus)4009, "Authentication failed.", _stop.Token);
                return;
            }

            await Send(Message(2, new JObject { ["negotiatedRpcVersion"] = 1 }));

            while (true)
            {
                var text = await ReceiveAsync(socket, _stop.Token);
                if (text == null)
                {
                    return;
                }

                var d = (JObject)JObject.Parse(text)["d"]!;
                var type = (string)d["requestType"]!;
                var id = (string)d["requestId"]!;
                lock (_gate)
                {
                    _requests.Add(type);
                }

                var code = 100;
                JObject? data = null;
                Func<Task>? after = null;
                switch (type)
                {
                    case "GetVersion":
                        data = new JObject
                        {
                            ["obsVersion"] = "31.0.0",
                            ["obsWebSocketVersion"] = "5.5.4",
                            ["rpcVersion"] = 1,
                            ["padding"] = new string('x', 20000),
                        };
                        break;
                    case "GetRecordStatus":
                        data = new JObject { ["outputActive"] = OutputActive, ["outputPaused"] = false };
                        break;
                    case "StartRecord":
                        lock (_gate)
                        {
                            if (_active)
                            {
                                code = 500;
                            }
                            else
                            {
                                BeginRecording();
                            }
                        }

                        if (code == 100)
                        {
                            after = () => Send(RecordState(true, "OBS_WEBSOCKET_OUTPUT_STARTED", null));
                        }

                        break;
                    case "StopRecord":
                        string? path;
                        lock (_gate)
                        {
                            path = _active && _file != null ? _path : null;
                        }

                        if (path == null)
                        {
                            code = 501;
                        }
                        else
                        {
                            data = new JObject { ["outputPath"] = path };
                            after = () =>
                            {
                                _ = Task.Run(() => FinishRecordingAsync(path, Send));
                                return Send(RecordState(true, "OBS_WEBSOCKET_OUTPUT_STOPPING", null));
                            };
                        }

                        break;
                    default:
                        code = 204;
                        break;
                }

                var response = new JObject
                {
                    ["requestType"] = type,
                    ["requestId"] = id,
                    ["requestStatus"] = new JObject { ["result"] = code == 100, ["code"] = code },
                };
                if (data != null)
                {
                    response["responseData"] = data;
                }

                await Send(Message(7, response));
                if (after != null)
                {
                    await after();
                }
            }
        }
        catch (Exception)
        {
            // The client went away or the test is over.
        }
        finally
        {
            socket.Dispose();
        }
    }

    /// <summary>Caller holds the gate. Creates the file and keeps it open, like OBS while recording.</summary>
    private string BeginRecording()
    {
        _recordings++;
        var name = "2026-01-02 10-0" + _recordings.ToString(CultureInfo.InvariantCulture) + "-00.mp4";
        var path = Path.Combine(_folder, name);
        _file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        _file.Write(new byte[] { 1, 2, 3 }, 0, 3);
        _file.Flush();
        _path = path.Replace('\\', '/');
        _active = true;
        return _path;
    }

    private async Task FinishRecordingAsync(string path, Func<JObject, Task> send)
    {
        try
        {
            await Task.Delay(FinishDelay, _stop.Token);
            FileStream? hold = null;
            lock (_gate)
            {
                _file?.Dispose();
                _file = null;
                if (HoldAfterStopped > TimeSpan.Zero)
                {
                    hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                }

                _active = false;
            }

            await send(RecordState(false, "OBS_WEBSOCKET_OUTPUT_STOPPED", path));
            if (hold != null)
            {
                await Task.Delay(HoldAfterStopped, _stop.Token);
                hold.Dispose();
            }
        }
        catch (Exception)
        {
            // The test is over.
        }
    }
}
