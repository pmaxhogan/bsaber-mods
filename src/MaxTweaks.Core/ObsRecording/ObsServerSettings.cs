using System;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MaxTweaks.Core.ObsRecording;

/// <summary>
/// OBS's own WebSocket server settings, as OBS stores them in
/// %APPDATA%/obs-studio/plugin_config/obs-websocket/config.json. Read so the mod needs no port or
/// password of its own on the PC that runs OBS. Missing or odd fields stay null.
/// </summary>
public sealed class ObsServerSettings
{
    public ObsServerSettings(bool? serverEnabled, int? port, bool? authRequired, string? password)
    {
        ServerEnabled = serverEnabled;
        Port = port;
        AuthRequired = authRequired;
        Password = password;
    }

    public bool? ServerEnabled { get; }

    public int? Port { get; }

    public bool? AuthRequired { get; }

    public string? Password { get; }

    /// <summary>Null when the text is not a JSON object.</summary>
    public static ObsServerSettings? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JObject root;
        try
        {
            root = JObject.Parse(json!);
        }
        catch (JsonException)
        {
            return null;
        }

        return new ObsServerSettings(
            BoolOf(root, "server_enabled"),
            root["server_port"] is JValue { Type: JTokenType.Integer } port && (long)port is >= 1 and <= 65535 ? (int)(long)port : null,
            BoolOf(root, "auth_required"),
            root["server_password"] is JValue { Type: JTokenType.String } password ? (string?)password : null);
    }

    private static bool? BoolOf(JObject root, string name) =>
        root[name] is JValue { Type: JTokenType.Boolean } value ? (bool)value : null;
}

/// <summary>Where and how to reach OBS, after combining the mod's config with OBS's own settings.</summary>
public sealed class ObsEndpoint
{
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 4455;

    public ObsEndpoint(string host, int port, string password, bool serverKnownOff)
    {
        Host = host;
        Port = port;
        Password = password;
        ServerKnownOff = serverKnownOff;
    }

    public string Host { get; }

    public int Port { get; }

    /// <summary>Empty when none is known. Never log this.</summary>
    public string Password { get; }

    /// <summary>OBS runs on this PC and its settings file says the WebSocket server is switched off.</summary>
    public bool ServerKnownOff { get; }

    /// <summary>
    /// OBS runs on the PC the game runs on: the host is a loopback address or "localhost". Any other
    /// name or address (this PC's own LAN address included) counts as another machine.
    /// </summary>
    public bool IsThisPc => IsThisPcHost(Host);

    public Uri Uri => new UriBuilder("ws", Host, Port).Uri;

    /// <summary>Host and port only: safe for logs and toasts.</summary>
    public string Address => Host + ":" + Port;

    /// <summary>
    /// The mod's own settings win when set (host not blank, port 1-65535, password not empty).
    /// What is not set comes from OBS's settings file, but only when the host is this PC: that file
    /// describes the OBS installed here, and its password must never be sent to another machine. For
    /// any other host the port defaults to 4455 and a password has to be configured explicitly.
    /// </summary>
    public static ObsEndpoint Resolve(string? host, int port, string? password, ObsServerSettings? obs)
    {
        var resolvedHost = string.IsNullOrWhiteSpace(host) ? DefaultHost : host!.Trim();
        var own = IsThisPcHost(resolvedHost) ? obs : null;
        var resolvedPort = port is >= 1 and <= 65535 ? port : own?.Port ?? DefaultPort;
        string resolvedPassword;
        if (!string.IsNullOrEmpty(password))
        {
            resolvedPassword = password!;
        }
        else if (own == null || own.AuthRequired == false)
        {
            resolvedPassword = string.Empty;
        }
        else
        {
            resolvedPassword = own.Password ?? string.Empty;
        }

        return new ObsEndpoint(resolvedHost, resolvedPort, resolvedPassword, own?.ServerEnabled == false);
    }

    /// <summary>See <see cref="IsThisPc"/>.</summary>
    public static bool IsThisPcHost(string? host)
    {
        var text = (host ?? string.Empty).Trim();
        if (string.Equals(text, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (text.Length > 2 && text[0] == '[' && text[text.Length - 1] == ']')
        {
            text = text.Substring(1, text.Length - 2);
        }

        // Only the plain written form counts ("127.0.0.1", "::1"): shorthand, octal or hex spellings
        // that some parsers read as loopback are treated as another machine, the safe direction.
        return IPAddress.TryParse(text, out var address)
            && IPAddress.IsLoopback(address)
            && string.Equals(address.ToString(), text, StringComparison.OrdinalIgnoreCase);
    }
}
