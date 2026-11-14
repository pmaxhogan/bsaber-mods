using MaxTweaks.Core.ObsRecording;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MaxTweaks.Core.Tests.ObsRecording;

public class ObsProtocolTests
{
    // Expected values computed with an independent implementation (Python hashlib) of the recipe in
    // obs-websocket's protocol.md; the first uses that document's example password, salt and challenge.
    [Theory]
    [InlineData("supersecretpassword", "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY=", "1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=")]
    [InlineData("made-up-password", "c2FsdA==", "Y2hhbGxlbmdl", "WLuYeg6cAJXmlwSGrkbRg/wl8aau0+egBiW8gU2ZhKg=")]
    public void Auth_FollowsTheProtocolRecipe(string password, string salt, string challenge, string expected)
    {
        Assert.Equal(expected, ObsAuth.Compute(password, salt, challenge));
    }

    [Fact]
    public void Auth_NullPassword_IsTreatedAsEmpty()
    {
        Assert.Equal(ObsAuth.Compute(string.Empty, "s", "c"), ObsAuth.Compute(null, "s", "c"));
    }

    [Fact]
    public void Identify_WithAuthentication()
    {
        var root = JObject.Parse(ObsProtocol.Identify("abc=", ObsProtocol.EventSubscriptionOutputs));
        Assert.Equal(1, (int)root["op"]!);
        Assert.Equal(1, (int)root["d"]!["rpcVersion"]!);
        Assert.Equal("abc=", (string?)root["d"]!["authentication"]);
        Assert.Equal(64, (int)root["d"]!["eventSubscriptions"]!);
    }

    [Fact]
    public void Identify_WithoutAuthentication_OmitsTheField()
    {
        var root = JObject.Parse(ObsProtocol.Identify(null, 64));
        Assert.Null(root["d"]!["authentication"]);
    }

    [Fact]
    public void Request_HasTypeAndId_OnOneLine()
    {
        var text = ObsProtocol.Request("StartRecord", "7");
        Assert.DoesNotContain("\n", text);
        var root = JObject.Parse(text);
        Assert.Equal(6, (int)root["op"]!);
        Assert.Equal("StartRecord", (string?)root["d"]!["requestType"]);
        Assert.Equal("7", (string?)root["d"]!["requestId"]);
    }

    [Fact]
    public void Parse_Hello_WithAuthentication()
    {
        const string json = """
            {"op":0,"d":{"obsStudioVersion":"30.2.2","obsWebSocketVersion":"5.5.2","rpcVersion":1,
            "authentication":{"challenge":"ch","salt":"sa"}}}
            """;
        Assert.True(ObsProtocol.TryParse(json, out var message));
        Assert.Equal(ObsProtocol.OpHello, message.Op);
        Assert.NotNull(message.Hello);
        Assert.Equal("30.2.2", message.Hello!.ObsVersion);
        Assert.Equal("5.5.2", message.Hello.WebSocketVersion);
        Assert.Equal(1, message.Hello.RpcVersion);
        Assert.Equal("ch", message.Hello.Challenge);
        Assert.Equal("sa", message.Hello.Salt);
        Assert.True(message.Hello.AuthenticationRequired);
        Assert.Null(message.Event);
        Assert.Null(message.Response);
    }

    [Fact]
    public void Parse_Hello_WithoutAuthentication()
    {
        Assert.True(ObsProtocol.TryParse("{\"op\":0,\"d\":{\"rpcVersion\":1}}", out var message));
        Assert.False(message.Hello!.AuthenticationRequired);
        Assert.Equal("?", message.Hello.ObsVersion);
        Assert.Equal("?", message.Hello.WebSocketVersion);
    }

    [Fact]
    public void Parse_Identified()
    {
        Assert.True(ObsProtocol.TryParse("{\"op\":2,\"d\":{\"negotiatedRpcVersion\":1}}", out var message));
        Assert.Equal(ObsProtocol.OpIdentified, message.Op);
        Assert.Null(message.Hello);
    }

    [Fact]
    public void Parse_RecordStateChangedEvent()
    {
        const string json = """
            {"op":5,"d":{"eventType":"RecordStateChanged","eventIntent":64,"eventData":
            {"outputActive":false,"outputState":"OBS_WEBSOCKET_OUTPUT_STOPPED","outputPath":"C:/v/a.mp4"}}}
            """;
        Assert.True(ObsProtocol.TryParse(json, out var message));
        var obsEvent = message.Event!;
        Assert.Equal(ObsProtocol.RecordStateChanged, obsEvent.Type);
        Assert.Equal(ObsProtocol.OutputStopped, obsEvent.GetString(ObsProtocol.OutputState));
        Assert.Equal("C:/v/a.mp4", obsEvent.GetString(ObsProtocol.OutputPath));
        Assert.False(obsEvent.GetBool(ObsProtocol.OutputActive));
    }

    [Fact]
    public void Parse_Event_NullPathAndMissingData()
    {
        Assert.True(ObsProtocol.TryParse("{\"op\":5,\"d\":{\"eventType\":\"RecordStateChanged\",\"eventData\":{\"outputActive\":true,\"outputPath\":null}}}", out var started));
        Assert.Null(started.Event!.GetString("outputPath"));
        Assert.True(started.Event.GetBool("outputActive"));
        Assert.False(started.Event.GetBool("outputPath"));

        Assert.True(ObsProtocol.TryParse("{\"op\":5,\"d\":{\"eventType\":\"ExitStarted\"}}", out var bare));
        Assert.Null(bare.Event!.Data);
        Assert.Null(bare.Event.GetString("anything"));
        Assert.False(bare.Event.GetBool("anything"));
    }

    [Fact]
    public void Parse_Response_Success_WithData()
    {
        const string json = """
            {"op":7,"d":{"requestType":"StopRecord","requestId":"12","requestStatus":{"result":true,"code":100},
            "responseData":{"outputPath":"C:/v/b.mp4"}}}
            """;
        Assert.True(ObsProtocol.TryParse(json, out var message));
        var response = message.Response!;
        Assert.Equal("StopRecord", response.RequestType);
        Assert.Equal("12", response.RequestId);
        Assert.True(response.Ok);
        Assert.Equal(100, response.Code);
        Assert.Null(response.Comment);
        Assert.Equal("C:/v/b.mp4", response.GetString("outputPath"));
        Assert.Equal("code 100", response.Describe());
    }

    [Fact]
    public void Parse_Response_Failure_WithComment()
    {
        const string json = """
            {"op":7,"d":{"requestType":"StartRecord","requestId":"3",
            "requestStatus":{"result":false,"code":500,"comment":"output active"}}}
            """;
        Assert.True(ObsProtocol.TryParse(json, out var message));
        var response = message.Response!;
        Assert.False(response.Ok);
        Assert.Equal(ObsProtocol.StatusOutputRunning, response.Code);
        Assert.Equal("code 500: output active", response.Describe());
        Assert.Null(response.Data);
        Assert.Null(response.GetString("outputPath"));
        Assert.False(response.GetBool("outputActive"));
    }

    [Fact]
    public void Parse_Response_BoolField()
    {
        Assert.True(ObsProtocol.TryParse("{\"op\":7,\"d\":{\"requestType\":\"GetRecordStatus\",\"requestId\":\"1\",\"requestStatus\":{\"result\":true,\"code\":100},\"responseData\":{\"outputActive\":true}}}", out var message));
        Assert.True(message.Response!.GetBool(ObsProtocol.OutputActive));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"d\":{}}")]
    [InlineData("{\"op\":\"0\",\"d\":{}}")]
    [InlineData("{\"op\":0}")]
    [InlineData("{\"op\":0,\"d\":5}")]
    public void Parse_RejectsWhatIsNotAMessage(string? json)
    {
        Assert.False(ObsProtocol.TryParse(json, out _));
    }

    [Fact]
    public void Parse_UnknownOp_HasNoPayload()
    {
        Assert.True(ObsProtocol.TryParse("{\"op\":9,\"d\":{}}", out var message));
        Assert.Equal(9, message.Op);
        Assert.Null(message.Hello);
        Assert.Null(message.Event);
        Assert.Null(message.Response);
    }

    [Fact]
    public void Parse_OddFieldTypes_FallBackToDefaults()
    {
        Assert.True(ObsProtocol.TryParse("{\"op\":7,\"d\":{\"requestType\":5,\"requestStatus\":{\"result\":\"yes\",\"code\":\"x\"}}}", out var message));
        Assert.Equal(string.Empty, message.Response!.RequestType);
        Assert.Equal(string.Empty, message.Response.RequestId);
        Assert.False(message.Response.Ok);
        Assert.Equal(0, message.Response.Code);

        Assert.True(ObsProtocol.TryParse("{\"op\":99999999999,\"d\":{}}", out var huge));
        Assert.Equal(int.MaxValue, huge.Op);
    }
}
