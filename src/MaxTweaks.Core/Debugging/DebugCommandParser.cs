using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MaxTweaks.Core.Debugging;

/// <summary>One parsed line of the debug command file: optional "#id", a verb and its arguments.</summary>
public sealed class DebugCommandLine
{
    public DebugCommandLine(string? id, string verb, IReadOnlyList<string> args, string raw)
    {
        Id = id;
        Verb = verb;
        Args = args;
        Raw = raw;
    }

    /// <summary>The token after '#' at the start of the line (used by scripts to match the ack), or null.</summary>
    public string? Id { get; }

    /// <summary>Lower-cased command name, e.g. "play".</summary>
    public string Verb { get; }

    public IReadOnlyList<string> Args { get; }

    public string Raw { get; }
}

/// <summary>
/// Game-free parsing for the --debug command channel (see docs/qa.md). Lines look like
/// <c>#42 play "849a (Sandstorm - Skyler Wallace)" expert practice 20</c>.
/// </summary>
public static class DebugCommandParser
{
    /// <summary>Returns null for blank lines and comments (starting with "//" or ";").</summary>
    public static DebugCommandLine? ParseLine(string? line)
    {
        if (line == null)
        {
            return null;
        }

        var trimmed = line.Trim().TrimStart((char)0xFEFF).Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith(";", StringComparison.Ordinal))
        {
            return null;
        }

        var tokens = Tokenize(trimmed);
        string? id = null;
        var start = 0;
        if (tokens.Count > 0 && tokens[0].Length > 1 && tokens[0][0] == '#')
        {
            id = tokens[0].Substring(1);
            start = 1;
        }

        if (tokens.Count <= start)
        {
            return new DebugCommandLine(id, string.Empty, Array.Empty<string>(), trimmed);
        }

        var args = new List<string>();
        for (var i = start + 1; i < tokens.Count; i++)
        {
            args.Add(tokens[i]);
        }

        return new DebugCommandLine(id, tokens[start].ToLowerInvariant(), args, trimmed);
    }

    /// <summary>Splits on whitespace; double quotes group words ("a b"), and \" is a literal quote inside quotes.</summary>
    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '\\' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>Parses seconds as "12.5" or "m:ss(.fff)" (invariant culture).</summary>
    public static bool TryParseSeconds(string text, out float seconds)
    {
        seconds = 0f;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var colon = text.IndexOf(':');
        if (colon < 0)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds >= 0f;
        }

        if (!int.TryParse(text.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !float.TryParse(text.Substring(colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var secs)
            || secs < 0f || secs >= 60f)
        {
            return false;
        }

        seconds = (minutes * 60f) + secs;
        return true;
    }

    public static bool TryParseFloat(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
