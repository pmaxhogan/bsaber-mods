using System;
using System.Collections.Generic;
using System.Globalization;

namespace MaxTweaks.Core.ObsStatus;

/// <summary>What one OBS feature (OBS recording, OBS clips) knows about itself right now.</summary>
public readonly struct ObsFeatureState : IEquatable<ObsFeatureState>
{
    public ObsFeatureState(bool enabled, bool connected, int saved, string? trouble = null)
    {
        Enabled = enabled;
        Connected = connected;
        Saved = saved;
        Trouble = trouble;
    }

    /// <summary>The feature is switched on.</summary>
    public bool Enabled { get; }

    /// <summary>Its connection to OBS is open.</summary>
    public bool Connected { get; }

    /// <summary>Files it has saved since the game started (kept while the feature is off).</summary>
    public int Saved { get; }

    /// <summary>A few words on why it cannot work although OBS is connected, or null.</summary>
    public string? Trouble { get; }

    public static bool operator ==(ObsFeatureState left, ObsFeatureState right) => left.Equals(right);

    public static bool operator !=(ObsFeatureState left, ObsFeatureState right) => !left.Equals(right);

    public bool Equals(ObsFeatureState other) =>
        Enabled == other.Enabled && Connected == other.Connected && Saved == other.Saved && Trouble == other.Trouble;

    public override bool Equals(object? obj) => obj is ObsFeatureState other && Equals(other);

    public override int GetHashCode() =>
        (Enabled ? 1 : 0) ^ (Connected ? 2 : 0) ^ (Saved * 397) ^ (Trouble == null ? 0 : StringComparer.Ordinal.GetHashCode(Trouble));
}

/// <summary>
/// The one-line OBS status shown on the song selection screen: whether OBS is connected and how
/// many recordings and clips this session has saved. Rich text (TextMeshPro colour tags), ASCII.
/// </summary>
public static class ObsStatusLine
{
    public const string GoodColor = "#70e070";
    public const string BadColor = "#ff7070";
    public const string WarnColor = "#ffc050";

    private const string Separator = "   |   ";

    /// <summary>
    /// The line, or null when there is nothing to show (both features off). "OBS" and one coloured
    /// state, then one count per feature:
    /// - "connected" (green) when every enabled feature is connected, "not connected" (red) when
    ///   none is, "connecting" (amber) in between: each feature has its own connection and retries
    ///   on its own, so a mix lasts a few seconds.
    /// - All connected but one cannot work: its few words on why (amber) instead of "connected".
    /// - A feature that is off shows its count only when it has saved something this session.
    /// </summary>
    public static string? Build(ObsFeatureState recording, ObsFeatureState clips)
    {
        if (!recording.Enabled && !clips.Enabled)
        {
            return null;
        }

        var enabled = (recording.Enabled ? 1 : 0) + (clips.Enabled ? 1 : 0);
        var connected = (recording.Enabled && recording.Connected ? 1 : 0) + (clips.Enabled && clips.Connected ? 1 : 0);
        var trouble = Trouble(recording) ?? Trouble(clips);
        var parts = new List<string>
        {
            "OBS " + (connected == 0 ? Colored(BadColor, "not connected")
                : connected < enabled ? Colored(WarnColor, "connecting")
                : trouble != null ? Colored(WarnColor, Plain(trouble))
                : Colored(GoodColor, "connected")),
        };

        Add(parts, recording, "recording", "recordings");
        Add(parts, clips, "clip", "clips");
        return string.Join(Separator, parts);
    }

    private static string? Trouble(ObsFeatureState state) =>
        state.Enabled && state.Connected && !string.IsNullOrEmpty(state.Trouble) ? state.Trouble : null;

    private static void Add(List<string> parts, ObsFeatureState state, string one, string many)
    {
        if (state.Enabled || state.Saved != 0)
        {
            parts.Add(state.Saved.ToString(CultureInfo.InvariantCulture) + " " + (state.Saved == 1 ? one : many));
        }
    }

    private static string Colored(string color, string text) => "<color=" + color + ">" + text + "</color>";

    /// <summary>Keeps text that is not ours from being read as rich-text tags.</summary>
    private static string Plain(string text) => text.Replace("<", "(").Replace(">", ")");
}
