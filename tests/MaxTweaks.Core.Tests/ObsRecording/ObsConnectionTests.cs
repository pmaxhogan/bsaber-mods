using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxTweaks.Core.ObsRecording;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MaxTweaks.Core.Tests.ObsRecording;

public class ObsConnectionTests
{
    private static readonly ObsHello Hello = new("31.0.0", "5.5.4", 1, null, null);

    private static string IdOf(string sent) => (string)JObject.Parse(sent)["d"]!["requestId"]!;

    private static string TypeOf(string sent) => (string)JObject.Parse(sent)["d"]!["requestType"]!;

    private static ObsConnection Open(FakeSocket socket, TimeSpan? timeout = null)
    {
        var connection = new ObsConnection(socket, Hello, timeout ?? TimeSpan.FromSeconds(5));
        connection.Start();
        connection.Start();
        return connection;
    }

    [Fact]
    public async Task Request_GetsItsResponse()
    {
        var socket = new FakeSocket();
        socket.OnSend = sent => new[] { FakeSocket.Response(IdOf(sent), TypeOf(sent), data: new JObject { ["obsVersion"] = "31.0.0" }) };
        using var connection = Open(socket);

        var response = await connection.RequestAsync("GetVersion", CancellationToken.None);

        Assert.True(response.Ok);
        Assert.Equal("GetVersion", response.RequestType);
        Assert.Equal("31.0.0", response.GetString("obsVersion"));
        Assert.Equal("31.0.0", connection.ObsVersion);
        Assert.Equal("5.5.4", connection.WebSocketVersion);
        Assert.True(connection.IsOpen);
    }

    [Fact]
    public async Task Responses_AreMatchedById_EvenOutOfOrder()
    {
        var socket = new FakeSocket();
        using var connection = Open(socket);

        var first = connection.RequestAsync("GetRecordStatus", CancellationToken.None);
        var second = connection.RequestAsync("GetVersion", CancellationToken.None);
        await ObsTestData.WaitFor(() => socket.Sent.Count == 2, "both requests sent");
        var ids = socket.Sent.ToDictionary(TypeOf, IdOf);
        Assert.NotEqual(ids["GetRecordStatus"], ids["GetVersion"]);

        socket.Enqueue(FakeSocket.Response("no-such-id", "Stray"));
        socket.Enqueue("this is not json");
        socket.Enqueue(FakeSocket.Response(ids["GetVersion"], "GetVersion"));
        socket.Enqueue(FakeSocket.Response(ids["GetRecordStatus"], "GetRecordStatus", ok: false, code: 501));

        Assert.Equal("GetVersion", (await second).RequestType);
        var status = await first;
        Assert.Equal("GetRecordStatus", status.RequestType);
        Assert.False(status.Ok);
        Assert.Equal(501, status.Code);
    }

    [Fact]
    public async Task Events_AreHandedOn_AndASubscriberFailureDoesNotEndTheSession()
    {
        var socket = new FakeSocket();
        using var connection = Open(socket);
        var seen = new List<string>();
        connection.EventReceived += e =>
        {
            lock (seen)
            {
                seen.Add(e.Type + ":" + e.GetString("outputState"));
            }

            if (e.Type == "Boom")
            {
                throw new InvalidOperationException("subscriber bug");
            }
        };

        socket.Enqueue(FakeSocket.Event("Boom", new JObject()));
        socket.Enqueue(FakeSocket.Event("RecordStateChanged", new JObject { ["outputState"] = "OBS_WEBSOCKET_OUTPUT_STOPPED" }));

        await ObsTestData.WaitFor(() => { lock (seen) { return seen.Count == 2; } }, "both events");
        Assert.Equal("RecordStateChanged:OBS_WEBSOCKET_OUTPUT_STOPPED", seen[1]);
        Assert.True(connection.IsOpen);
    }

    [Fact]
    public async Task Request_TimesOut_WhenObsDoesNotAnswer()
    {
        var socket = new FakeSocket();
        using var connection = Open(socket, TimeSpan.FromMilliseconds(50));

        var error = await Assert.ThrowsAsync<ObsException>(() => connection.RequestAsync("StartRecord", CancellationToken.None));

        Assert.Contains("did not answer StartRecord", error.Message);
        Assert.True(connection.IsOpen);
    }

    [Fact]
    public async Task Request_CanBeCancelled()
    {
        var socket = new FakeSocket();
        using var connection = Open(socket);
        using var cancel = new CancellationTokenSource();

        var request = connection.RequestAsync("StartRecord", cancel.Token);
        await ObsTestData.WaitFor(() => socket.Sent.Count == 1, "request sent");
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    [Fact]
    public async Task PeerClose_EndsTheSession_AndFailsWaitingRequests()
    {
        var socket = new FakeSocket();
        var connection = Open(socket);
        var closed = new List<string>();
        connection.Closed += (sender, reason) =>
        {
            Assert.Same(connection, sender);
            lock (closed)
            {
                closed.Add(reason);
            }
        };

        var waiting = connection.RequestAsync("StopRecord", CancellationToken.None);
        await ObsTestData.WaitFor(() => socket.Sent.Count == 1, "request sent");
        socket.Enqueue(null);

        var error = await Assert.ThrowsAsync<ObsException>(() => waiting);
        Assert.Equal("OBS closed the connection", error.Message);
        await ObsTestData.WaitFor(() => { lock (closed) { return closed.Count == 1; } }, "closed event");
        Assert.False(connection.IsOpen);
        Assert.True(socket.Disposed);

        var later = await Assert.ThrowsAsync<ObsException>(() => connection.RequestAsync("StartRecord", CancellationToken.None));
        Assert.Equal("not connected to OBS", later.Message);

        connection.Dispose();
        Assert.Single(closed);
    }

    [Fact]
    public async Task ReceiveFailure_EndsTheSession_WithTheCause()
    {
        var socket = new FakeSocket();
        var connection = Open(socket);
        string? reason = null;
        connection.Closed += (_, r) => reason = r;

        socket.EnqueueError(new IOException("reset by peer"));

        await ObsTestData.WaitFor(() => reason != null, "closed event");
        Assert.Contains("lost", reason);
        Assert.Contains("reset by peer", reason);
        Assert.False(connection.IsOpen);
    }

    [Fact]
    public async Task Dispose_ClosesOnce_AndTellsSubscribers()
    {
        var socket = new FakeSocket();
        var connection = Open(socket);
        var reasons = new List<string>();
        connection.Closed += (_, r) =>
        {
            lock (reasons)
            {
                reasons.Add(r);
            }

            throw new InvalidOperationException("subscriber bug");
        };

        connection.Dispose();
        connection.Dispose();

        Assert.False(connection.IsOpen);
        Assert.True(socket.Disposed);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        lock (reasons)
        {
            Assert.Equal(new[] { "closed by MaxTweaks" }, reasons);
        }
    }

    [Fact]
    public async Task SendFailure_IsReportedAsObsException()
    {
        var socket = new FakeSocket { SendError = new IOException("pipe broken") };
        using var connection = Open(socket);

        var error = await Assert.ThrowsAsync<ObsException>(() => connection.RequestAsync("StartRecord", CancellationToken.None));

        Assert.Contains("could not send StartRecord", error.Message);
        Assert.Contains("pipe broken", error.Message);
        Assert.IsType<IOException>(error.InnerException);
    }
}

public class ObsConnectorTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(150);

    private static ObsConnector Connector(FakeSocket socket, string password = "", bool knownOff = false, TimeSpan? connectTimeout = null) =>
        new(() => new ObsEndpoint("127.0.0.1", 4455, password, knownOff), () => socket, connectTimeout, TimeSpan.FromSeconds(5));

    private static JObject SentIdentify(FakeSocket socket) => (JObject)JObject.Parse(socket.Sent[0])["d"]!;

    [Fact]
    public async Task NoPasswordNeeded_SignsIn_SubscribedToOutputEventsOnly()
    {
        var socket = new FakeSocket();
        socket.Enqueue(FakeSocket.Hello());
        socket.OnSend = _ => new[] { FakeSocket.Identified() };

        using var connection = await Connector(socket).ConnectAsync(CancellationToken.None);

        Assert.Equal("ws://127.0.0.1:4455/", socket.ConnectedTo!.ToString());
        Assert.Equal("31.0.0", connection.ObsVersion);
        Assert.Equal("5.5.4", connection.WebSocketVersion);
        Assert.True(connection.IsOpen);
        var identify = SentIdentify(socket);
        Assert.Equal(1, (int)identify["rpcVersion"]!);
        Assert.Equal(64, (int)identify["eventSubscriptions"]!);
        Assert.Null(identify["authentication"]);
        Assert.False(socket.Disposed);
    }

    [Fact]
    public async Task PasswordNeeded_SendsTheAuthenticationString_NotThePassword()
    {
        var socket = new FakeSocket();
        socket.Enqueue("garbage before hello");
        socket.Enqueue(FakeSocket.Hello("Y2hhbGxlbmdl", "c2FsdA=="));
        socket.OnSend = _ => new[] { FakeSocket.Identified() };

        using var connection = await Connector(socket, "made-up-password").ConnectAsync(CancellationToken.None);

        Assert.Equal("WLuYeg6cAJXmlwSGrkbRg/wl8aau0+egBiW8gU2ZhKg=", (string?)SentIdentify(socket)["authentication"]);
        Assert.DoesNotContain("made-up-password", socket.Sent[0]);
    }

    [Fact]
    public async Task PasswordNeeded_ButNoneKnown_SaysWhereToSetIt()
    {
        var socket = new FakeSocket();
        socket.Enqueue(FakeSocket.Hello("ch", "sa"));

        var error = await Assert.ThrowsAsync<ObsException>(() => Connector(socket).ConnectAsync(CancellationToken.None));

        Assert.Contains("ObsRecording.Password", error.Message);
        Assert.Empty(socket.Sent);
        Assert.True(socket.Disposed);
    }

    [Fact]
    public async Task WrongPassword_IsNamed_WithoutLeakingIt()
    {
        var socket = new FakeSocket { CloseCode = 4009 };
        socket.Enqueue(FakeSocket.Hello("ch", "sa"));
        socket.OnSend = _ => new string?[] { null };

        var error = await Assert.ThrowsAsync<ObsException>(() => Connector(socket, "wrong-made-up").ConnectAsync(CancellationToken.None));

        Assert.Contains("rejected the WebSocket password", error.Message);
        Assert.DoesNotContain("wrong-made-up", error.Message);
        Assert.True(socket.Disposed);
    }

    [Fact]
    public async Task ClosedDuringSignIn_ForAnotherReason_GivesTheCloseCode()
    {
        var socket = new FakeSocket { CloseCode = 4010 };
        socket.Enqueue(FakeSocket.Hello());
        socket.OnSend = _ => new string?[] { null };

        var error = await Assert.ThrowsAsync<ObsException>(() => Connector(socket).ConnectAsync(CancellationToken.None));

        Assert.Equal("OBS closed the connection during sign-in (close code 4010)", error.Message);
    }

    [Fact]
    public async Task ClosedDuringSignIn_WithoutCode()
    {
        var socket = new FakeSocket();
        socket.Enqueue(FakeSocket.Hello());
        socket.OnSend = _ => new string?[] { null };

        var error = await Assert.ThrowsAsync<ObsException>(() => Connector(socket).ConnectAsync(CancellationToken.None));

        Assert.Equal("OBS closed the connection during sign-in", error.Message);
    }

    [Fact]
    public async Task ConnectRefused_SaysObsIsNotReachable()
    {
        var socket = new FakeSocket { ConnectError = new IOException("connection refused") };

        var error = await Assert.ThrowsAsync<ObsException>(() => Connector(socket).ConnectAsync(CancellationToken.None));

        Assert.Contains("OBS is not reachable at 127.0.0.1:4455", error.Message);
        Assert.True(socket.Disposed);
    }

    [Fact]
    public async Task ConnectRefused_WhenObsFileSaysServerOff_SaysSo()
    {
        var socket = new FakeSocket { ConnectError = new IOException("connection refused") };

        var error = await Assert.ThrowsAsync<ObsException>(() => Connector(socket, knownOff: true).ConnectAsync(CancellationToken.None));

        Assert.Contains("WebSocket server is switched off", error.Message);
    }

    [Fact]
    public async Task ConnectThatHangs_GivesUpAfterTheTimeout()
    {
        var socket = new FakeSocket { HangConnect = true };

        var error = await Assert.ThrowsAsync<ObsException>(() => Connector(socket, connectTimeout: Short).ConnectAsync(CancellationToken.None));

        Assert.Contains("not reachable", error.Message);
        Assert.True(socket.Disposed);
    }

    [Fact]
    public async Task NoHello_GivesUpAfterTheTimeout()
    {
        var socket = new FakeSocket();

        var error = await Assert.ThrowsAsync<ObsException>(() => Connector(socket, connectTimeout: Short).ConnectAsync(CancellationToken.None));

        Assert.Contains("stopped answering during sign-in", error.Message);
    }

    [Fact]
    public async Task FirstMessageIsNotHello_IsNotObs()
    {
        var socket = new FakeSocket();
        socket.Enqueue(FakeSocket.Identified());

        var error = await Assert.ThrowsAsync<ObsException>(() => Connector(socket).ConnectAsync(CancellationToken.None));

        Assert.Contains("did not answer like OBS", error.Message);
    }

    [Fact]
    public async Task ClosedBeforeHello_IsNotObs()
    {
        var socket = new FakeSocket();
        socket.Enqueue(null);

        var error = await Assert.ThrowsAsync<ObsException>(() => Connector(socket).ConnectAsync(CancellationToken.None));

        Assert.Contains("did not answer like OBS", error.Message);
    }

    [Fact]
    public async Task IdentifyCannotBeSent_StoppedAnswering()
    {
        var socket = new FakeSocket { SendError = new IOException("gone") };
        socket.Enqueue(FakeSocket.Hello());

        var error = await Assert.ThrowsAsync<ObsException>(() => Connector(socket).ConnectAsync(CancellationToken.None));

        Assert.Contains("stopped answering during sign-in", error.Message);
    }

    [Fact]
    public async Task AnswerToIdentifyIsNotIdentified_DidNotComplete()
    {
        var socket = new FakeSocket();
        socket.Enqueue(FakeSocket.Hello());
        socket.OnSend = _ => new[] { FakeSocket.Hello() };

        var error = await Assert.ThrowsAsync<ObsException>(() => Connector(socket).ConnectAsync(CancellationToken.None));

        Assert.Contains("did not complete the OBS sign-in", error.Message);
    }

    [Fact]
    public async Task CallerCancellation_IsNotTurnedIntoAnObsError()
    {
        var socket = new FakeSocket { HangConnect = true };
        using var cancel = new CancellationTokenSource();

        var attempt = Connector(socket).ConnectAsync(cancel.Token);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        Assert.True(socket.Disposed);
    }

    [Fact]
    public void Defaults_AreShort()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), ObsConnector.DefaultConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), ObsConnector.DefaultRequestTimeout);
        var error = new ObsException("outer", new IOException("inner"));
        Assert.Equal("inner", error.InnerException!.Message);
    }
}
