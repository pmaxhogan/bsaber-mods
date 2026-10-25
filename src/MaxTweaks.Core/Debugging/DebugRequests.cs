using System;
using System.Collections.Generic;

namespace MaxTweaks.Core.Debugging;

/// <summary>Same numeric values as the game's BeatmapDifficulty enum.</summary>
public enum DebugDifficulty
{
    Easy = 0,
    Normal = 1,
    Hard = 2,
    Expert = 3,
    ExpertPlus = 4,
}

/// <summary>Buttons the debug channel can press (mirrors MaxTweaks.Common.ControllerButton).</summary>
public enum DebugButton
{
    A,
    B,
    X,
    Y,
    LeftTrigger,
    RightTrigger,
}

public enum DebugButtonAction
{
    Down,
    Up,
    Press,
}

/// <summary>Arguments of <c>play</c> / <c>select-level</c>: level query, difficulty, characteristic, practice.</summary>
public sealed class PlayRequest
{
    public string Level { get; set; } = string.Empty;

    public DebugDifficulty Difficulty { get; set; } = DebugDifficulty.Expert;

    public string Characteristic { get; set; } = "Standard";

    public bool Practice { get; set; }

    public float StartSeconds { get; set; }

    public float Speed { get; set; } = 1f;
}

public sealed class ButtonRequest
{
    public DebugButton Button { get; set; }

    public DebugButtonAction Action { get; set; }

    /// <summary>Hold time for Press (default 0.15 s) or auto-release time for Down (0 = until "up").</summary>
    public float Seconds { get; set; }
}

public sealed class StickRequest
{
    public float X { get; set; }

    public float Y { get; set; }

    /// <summary>0 = hold until the next stick command (e.g. "stick 0 0").</summary>
    public float Seconds { get; set; }
}

public static class DebugRequestParser
{
    public const float DefaultPressSeconds = 0.15f;

    public static bool TryParseDifficulty(string text, out DebugDifficulty difficulty)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "easy":
            case "e":
                difficulty = DebugDifficulty.Easy;
                return true;
            case "normal":
            case "n":
                difficulty = DebugDifficulty.Normal;
                return true;
            case "hard":
            case "h":
                difficulty = DebugDifficulty.Hard;
                return true;
            case "expert":
            case "x":
                difficulty = DebugDifficulty.Expert;
                return true;
            case "expertplus":
            case "expert+":
            case "ex+":
            case "e+":
            case "x+":
                difficulty = DebugDifficulty.ExpertPlus;
                return true;
            default:
                difficulty = default;
                return false;
        }
    }

    /// <summary>
    /// Grammar: <c>&lt;level...&gt; &lt;difficulty&gt; [characteristic] [practice &lt;start&gt; [speed]]</c>.
    /// The level may be several unquoted words; it is everything before the difficulty.
    /// </summary>
    public static bool TryParsePlay(IReadOnlyList<string> args, out PlayRequest request, out string error)
    {
        request = new PlayRequest();
        error = string.Empty;
        var head = new List<string>(args);

        var practiceIndex = head.FindLastIndex(t => string.Equals(t, "practice", StringComparison.OrdinalIgnoreCase));
        if (practiceIndex >= 0)
        {
            var tail = head.GetRange(practiceIndex + 1, head.Count - practiceIndex - 1);
            head.RemoveRange(practiceIndex, head.Count - practiceIndex);
            request.Practice = true;
            if (tail.Count > 2)
            {
                error = "practice takes at most <startSeconds> [speed]";
                return false;
            }

            if (tail.Count >= 1)
            {
                if (!DebugCommandParser.TryParseSeconds(tail[0], out var start))
                {
                    error = $"bad practice start time '{tail[0]}' (use seconds or m:ss)";
                    return false;
                }

                request.StartSeconds = start;
            }

            if (tail.Count == 2)
            {
                if (!DebugCommandParser.TryParseFloat(tail[1], out var speed) || speed < 0.5f || speed > 2f)
                {
                    error = $"bad practice speed '{tail[1]}' (0.5 - 2.0)";
                    return false;
                }

                request.Speed = speed;
            }
        }

        if (head.Count >= 2 && TryParseDifficulty(head[head.Count - 1], out var diff))
        {
            request.Difficulty = diff;
            head.RemoveAt(head.Count - 1);
        }
        else if (head.Count >= 3 && TryParseDifficulty(head[head.Count - 2], out diff))
        {
            request.Difficulty = diff;
            request.Characteristic = head[head.Count - 1];
            head.RemoveRange(head.Count - 2, 2);
        }
        else
        {
            error = "usage: <level> <easy|normal|hard|expert|expertplus> [characteristic] [practice <start> [speed]]";
            return false;
        }

        request.Level = string.Join(" ", head).Trim();
        if (request.Level.Length == 0)
        {
            error = "missing level";
            return false;
        }

        return true;
    }

    /// <summary>Grammar: <c>&lt;A|B|X|Y|LT|RT&gt; &lt;down|up|press&gt; [seconds]</c> (LT/RT = left/right trigger).</summary>
    public static bool TryParseButton(IReadOnlyList<string> args, out ButtonRequest request, out string error)
    {
        request = new ButtonRequest();
        error = string.Empty;
        if (args.Count < 2 || args.Count > 3)
        {
            error = "usage: button <A|B|X|Y|LT|RT> <down|up|press> [seconds]";
            return false;
        }

        switch (args[0].ToUpperInvariant())
        {
            case "A": request.Button = DebugButton.A; break;
            case "B": request.Button = DebugButton.B; break;
            case "X": request.Button = DebugButton.X; break;
            case "Y": request.Button = DebugButton.Y; break;
            case "LT": request.Button = DebugButton.LeftTrigger; break;
            case "RT": request.Button = DebugButton.RightTrigger; break;
            default:
                error = $"unknown button '{args[0]}'";
                return false;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "down": request.Action = DebugButtonAction.Down; break;
            case "up": request.Action = DebugButtonAction.Up; break;
            case "press": request.Action = DebugButtonAction.Press; break;
            default:
                error = $"unknown action '{args[1]}'";
                return false;
        }

        request.Seconds = request.Action == DebugButtonAction.Press ? DefaultPressSeconds : 0f;
        if (args.Count == 3)
        {
            if (request.Action == DebugButtonAction.Up)
            {
                error = "up takes no duration";
                return false;
            }

            if (!DebugCommandParser.TryParseFloat(args[2], out var seconds) || seconds <= 0f)
            {
                error = $"bad duration '{args[2]}'";
                return false;
            }

            request.Seconds = seconds;
        }

        return true;
    }

    /// <summary>Grammar: <c>&lt;x&gt; &lt;y&gt; [seconds]</c>, values clamped to [-1, 1].</summary>
    public static bool TryParseStick(IReadOnlyList<string> args, out StickRequest request, out string error)
    {
        request = new StickRequest();
        error = string.Empty;
        if (args.Count < 2 || args.Count > 3
            || !DebugCommandParser.TryParseFloat(args[0], out var x)
            || !DebugCommandParser.TryParseFloat(args[1], out var y))
        {
            error = "usage: stick <x> <y> [seconds]";
            return false;
        }

        request.X = Math.Max(-1f, Math.Min(1f, x));
        request.Y = Math.Max(-1f, Math.Min(1f, y));
        if (args.Count == 3)
        {
            if (!DebugCommandParser.TryParseFloat(args[2], out var seconds) || seconds <= 0f)
            {
                error = $"bad duration '{args[2]}'";
                return false;
            }

            request.Seconds = seconds;
        }

        return true;
    }
}
