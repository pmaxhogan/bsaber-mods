using System.Text.RegularExpressions;

namespace MaxTweaks.Core.HeartRateBridge;

public enum BridgeEvent
{
    Other,
    Scanning,
    Connected,
    Disconnected,
    Reading,
    Error,
}

/// <summary>Classifies one line of the bridge script's output (tools/hr-osc-bridge/hr_osc_bridge.py).</summary>
public static class BridgeOutput
{
    private static readonly Regex Timestamp = new(@"^\d\d:\d\d:\d\d\s+", RegexOptions.Compiled);
    private static readonly Regex ReadingLine = new(@"^(\d+) bpm\b", RegexOptions.Compiled);

    /// <summary>Strips the script's HH:MM:SS prefix; the game log has its own timestamps.</summary>
    public static string Strip(string? line) => Timestamp.Replace(line ?? string.Empty, string.Empty).TrimEnd();

    public static BridgeEvent Classify(string? line, out int bpm)
    {
        bpm = 0;
        var text = Strip(line);
        var reading = ReadingLine.Match(text);
        if (reading.Success && int.TryParse(reading.Groups[1].Value, out bpm))
        {
            return BridgeEvent.Reading;
        }

        if (text.StartsWith("connected, bridging"))
        {
            return BridgeEvent.Connected;
        }

        if (text.StartsWith("strap disconnected"))
        {
            return BridgeEvent.Disconnected;
        }

        if (text.StartsWith("scanning "))
        {
            return BridgeEvent.Scanning;
        }

        if (text.StartsWith("connection failed") || text.StartsWith("Traceback") || text.Contains("Error"))
        {
            return BridgeEvent.Error;
        }

        return BridgeEvent.Other;
    }
}
