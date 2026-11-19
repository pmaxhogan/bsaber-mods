using System;
using System.Globalization;

namespace MaxTweaks.Core.ObsClips;

/// <summary>How cutting a clip out of a saved replay went.</summary>
public enum ClipCutOutcome
{
    Ok,

    /// <summary>ffmpeg could not be started (not installed, or the configured path is wrong).</summary>
    ToolMissing,

    Failed,
}

public sealed class ClipCutResult
{
    public static readonly ClipCutResult Done = new(ClipCutOutcome.Ok, string.Empty);

    public ClipCutResult(ClipCutOutcome outcome, string message)
    {
        Outcome = outcome;
        Message = message;
    }

    public ClipCutOutcome Outcome { get; }

    /// <summary>What went wrong, for the log.</summary>
    public string Message { get; }
}

/// <summary>
/// The ffmpeg call that cuts a clip out of a replay OBS saved. The streams are copied, not encoded
/// again, so it takes a fraction of a second and costs the game nothing; the price is that a copy
/// can only start on a keyframe, so the clip starts up to one keyframe interval (OBS: 2 s unless
/// changed) before the asked-for moment. The position is counted back from the end of the file,
/// because the end is the moment the replay was saved while its start depends on OBS.
/// </summary>
public static class ClipCut
{
    /// <summary>Kept in front of the picked stretch.</summary>
    public const double LeadSeconds = 0.5;

    /// <summary>Kept after the picked stretch.</summary>
    public const double TailSeconds = 0.5;

    private static readonly string[] FastStartExtensions = { ".mp4", ".mov", ".m4v" };

    /// <summary>
    /// Arguments for ffmpeg: copy <paramref name="seconds"/> of video and audio starting
    /// <paramref name="secondsFromEnd"/> before the end of <paramref name="source"/> into
    /// <paramref name="target"/> (overwritten when it exists).
    /// </summary>
    public static string Arguments(string source, string target, double secondsFromEnd, double seconds)
    {
        var fromEnd = Math.Max(0.1, secondsFromEnd);
        var length = Math.Max(0.1, seconds);
        var arguments = "-hide_banner -loglevel error -nostdin -y"
            + " -sseof -" + Number(fromEnd)
            + " -i " + CommandLine.Quote(source)
            + " -t " + Number(length)
            + " -map 0:v? -map 0:a? -c copy -avoid_negative_ts make_zero";
        if (IsFastStart(target))
        {
            arguments += " -movflags +faststart";
        }

        return arguments + " " + CommandLine.Quote(target);
    }

    private static bool IsFastStart(string path)
    {
        foreach (var extension in FastStartExtensions)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
