using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MaxTweaks.Core.ObsRecording;

/// <summary>The server's first message: versions, and the challenge when a password is required.</summary>
public sealed class ObsHello
{
    public ObsHello(string obsVersion, string webSocketVersion, int rpcVersion, string? challenge, string? salt)
    {
        ObsVersion = obsVersion;
        WebSocketVersion = webSocketVersion;
        RpcVersion = rpcVersion;
        Challenge = challenge;
        Salt = salt;
    }

    public string ObsVersion { get; }

    public string WebSocketVersion { get; }

    public int RpcVersion { get; }

    public string? Challenge { get; }

    public string? Salt { get; }

    public bool AuthenticationRequired => Challenge != null && Salt != null;
}

/// <summary>An event pushed by OBS (OpCode 5).</summary>
public sealed class ObsEvent
{
    public ObsEvent(string type, JObject? data)
    {
        Type = type;
        Data = data;
    }

    public string Type { get; }

    public JObject? Data { get; }

    public string? GetString(string name) => ObsProtocol.StringOf(Data, name);

    public bool GetBool(string name) => ObsProtocol.BoolOf(Data, name);
}

/// <summary>The answer to one request (OpCode 7).</summary>
public sealed class ObsResponse
{
    public ObsResponse(string requestType, string requestId, bool ok, int code, string? comment, JObject? data)
    {
        RequestType = requestType;
        RequestId = requestId;
        Ok = ok;
        Code = code;
        Comment = comment;
        Data = data;
    }

    public string RequestType { get; }

    public string RequestId { get; }

    /// <summary>requestStatus.result: true only for RequestStatus::Success.</summary>
    public bool Ok { get; }

    /// <summary>requestStatus.code, e.g. 100 success, 500 output running, 501 output not running.</summary>
    public int Code { get; }

    public string? Comment { get; }

    public JObject? Data { get; }

    public string? GetString(string name) => ObsProtocol.StringOf(Data, name);

    public bool GetBool(string name) => ObsProtocol.BoolOf(Data, name);

    /// <summary>"code 500" or "code 500: comment", for log lines.</summary>
    public string Describe() => string.IsNullOrEmpty(Comment) ? "code " + Code : "code " + Code + ": " + Comment;
}

/// <summary>One parsed message from OBS; a payload is set for the op codes this mod reads.</summary>
public sealed class ObsMessage
{
    public ObsMessage(int op, ObsHello? hello, ObsEvent? obsEvent, ObsResponse? response)
    {
        Op = op;
        Hello = hello;
        Event = obsEvent;
        Response = response;
    }

    public int Op { get; }

    public ObsHello? Hello { get; }

    public ObsEvent? Event { get; }

    public ObsResponse? Response { get; }
}

/// <summary>
/// The slice of the obs-websocket 5.x protocol (rpcVersion 1) this mod speaks: sign-in, plain
/// requests and events. Messages are JSON text frames shaped {"op": number, "d": object}.
/// Reference: obs-websocket docs/generated/protocol.md.
/// </summary>
public static class ObsProtocol
{
    public const int OpHello = 0;
    public const int OpIdentify = 1;
    public const int OpIdentified = 2;
    public const int OpEvent = 5;
    public const int OpRequest = 6;
    public const int OpRequestResponse = 7;

    public const int RpcVersion = 1;

    /// <summary>EventSubscription::Outputs: the category RecordStateChanged belongs to.</summary>
    public const int EventSubscriptionOutputs = 1 << 6;

    /// <summary>RequestStatus::OutputRunning: StartRecord while the record output is active.</summary>
    public const int StatusOutputRunning = 500;

    /// <summary>RequestStatus::OutputNotRunning: StopRecord while nothing is recording.</summary>
    public const int StatusOutputNotRunning = 501;

    /// <summary>WebSocketCloseCode::AuthenticationFailed.</summary>
    public const int CloseAuthenticationFailed = 4009;

    public const string StartRecord = "StartRecord";
    public const string StopRecord = "StopRecord";
    public const string GetRecordStatus = "GetRecordStatus";
    public const string GetVersion = "GetVersion";

    public const string RecordStateChanged = "RecordStateChanged";
    public const string OutputState = "outputState";
    public const string OutputPath = "outputPath";
    public const string OutputActive = "outputActive";
    public const string OutputStopped = "OBS_WEBSOCKET_OUTPUT_STOPPED";
    public const string OutputStarted = "OBS_WEBSOCKET_OUTPUT_STARTED";

    /// <summary>RequestStatus::InvalidResourceState: e.g. the replay buffer is not enabled in OBS's settings.</summary>
    public const int StatusInvalidResourceState = 604;

    public const string GetReplayBufferStatus = "GetReplayBufferStatus";
    public const string StartReplayBuffer = "StartReplayBuffer";
    public const string StopReplayBuffer = "StopReplayBuffer";
    public const string SaveReplayBuffer = "SaveReplayBuffer";
    public const string GetProfileParameter = "GetProfileParameter";

    public const string ReplayBufferStateChanged = "ReplayBufferStateChanged";
    public const string ReplayBufferSaved = "ReplayBufferSaved";
    public const string SavedReplayPath = "savedReplayPath";

    public const string ParameterCategory = "parameterCategory";
    public const string ParameterName = "parameterName";
    public const string ParameterValue = "parameterValue";
    public const string DefaultParameterValue = "defaultParameterValue";

    /// <summary>Identify (OpCode 1); authentication is omitted when the server asked for none.</summary>
    public static string Identify(string? authentication, int eventSubscriptions)
    {
        var data = new JObject { ["rpcVersion"] = RpcVersion };
        if (authentication != null)
        {
            data["authentication"] = authentication;
        }

        data["eventSubscriptions"] = eventSubscriptions;
        return Envelope(OpIdentify, data);
    }

    /// <summary>Request (OpCode 6); <paramref name="requestData"/> is left out when the request takes none.</summary>
    public static string Request(string requestType, string requestId, JObject? requestData = null)
    {
        var data = new JObject { ["requestType"] = requestType, ["requestId"] = requestId };
        if (requestData != null)
        {
            data["requestData"] = requestData;
        }

        return Envelope(OpRequest, data);
    }

    /// <summary>The request data of GetProfileParameter.</summary>
    public static JObject ProfileParameter(string category, string name) =>
        new() { [ParameterCategory] = category, [ParameterName] = name };

    /// <summary>Parses one message; false for anything that is not a JSON object with "op" and "d".</summary>
    public static bool TryParse(string? json, out ObsMessage message)
    {
        message = null!;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        JObject root;
        try
        {
            root = JObject.Parse(json!);
        }
        catch (JsonException)
        {
            return false;
        }

        if (root["op"] is not JValue { Type: JTokenType.Integer } opValue || root["d"] is not JObject d)
        {
            return false;
        }

        var op = ClampToInt((long)opValue);
        ObsHello? hello = null;
        ObsEvent? obsEvent = null;
        ObsResponse? response = null;
        switch (op)
        {
            case OpHello:
                var authentication = d["authentication"] as JObject;
                hello = new ObsHello(
                    StringOf(d, "obsStudioVersion") ?? "?",
                    StringOf(d, "obsWebSocketVersion") ?? "?",
                    IntOf(d, "rpcVersion"),
                    StringOf(authentication, "challenge"),
                    StringOf(authentication, "salt"));
                break;
            case OpEvent:
                obsEvent = new ObsEvent(StringOf(d, "eventType") ?? string.Empty, d["eventData"] as JObject);
                break;
            case OpRequestResponse:
                var status = d["requestStatus"] as JObject;
                response = new ObsResponse(
                    StringOf(d, "requestType") ?? string.Empty,
                    StringOf(d, "requestId") ?? string.Empty,
                    BoolOf(status, "result"),
                    IntOf(status, "code"),
                    StringOf(status, "comment"),
                    d["responseData"] as JObject);
                break;
        }

        message = new ObsMessage(op, hello, obsEvent, response);
        return true;
    }

    internal static string? StringOf(JObject? data, string name) =>
        data?[name] is JValue { Type: JTokenType.String } value ? (string?)value : null;

    internal static bool BoolOf(JObject? data, string name) =>
        data?[name] is JValue { Type: JTokenType.Boolean } value && (bool)value;

    private static int IntOf(JObject? data, string name) =>
        data?[name] is JValue { Type: JTokenType.Integer } value ? ClampToInt((long)value) : 0;

    private static int ClampToInt(long value) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, value));

    private static string Envelope(int op, JObject data) =>
        new JObject { ["op"] = op, ["d"] = data }.ToString(Formatting.None);
}
