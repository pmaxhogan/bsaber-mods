using System.Collections.Generic;
using System.Globalization;

namespace MaxTweaks.Core.Debugging;

/// <summary>What a UI query matches on: a hover hint's text, a label's text, or a GameObject name.</summary>
public enum UiMatchKind
{
    Hint,
    Text,
    Name,
}

/// <summary>Arguments of <c>find-hint|find-text|find-name</c> and <c>click-hint|click-text|click-name</c>.</summary>
public sealed class UiQueryRequest
{
    public UiMatchKind Kind { get; set; }

    /// <summary>Case-insensitive substring to look for.</summary>
    public string Needle { get; set; } = string.Empty;

    /// <summary>Which match to click when several match (0-based, from the find listing).</summary>
    public int Index { get; set; }

    public bool Click { get; set; }
}

public static class UiRequestParser
{
    public static readonly IReadOnlyList<string> QueryVerbs = new[]
    {
        "find-hint", "find-text", "find-name", "click-hint", "click-text", "click-name",
    };

    /// <summary>
    /// Grammar: <c>find-&lt;kind&gt; &lt;needle&gt;</c> or <c>click-&lt;kind&gt; &lt;needle&gt; [index]</c>.
    /// Quote a needle with spaces.
    /// </summary>
    public static bool TryParseQuery(string verb, IReadOnlyList<string> args, out UiQueryRequest request, out string error)
    {
        request = new UiQueryRequest();
        error = string.Empty;
        var dash = verb.IndexOf('-');
        var action = dash > 0 ? verb.Substring(0, dash) : string.Empty;
        var kind = dash > 0 ? verb.Substring(dash + 1) : string.Empty;
        if (action != "find" && action != "click")
        {
            error = $"unknown UI verb '{verb}'";
            return false;
        }

        request.Click = action == "click";
        switch (kind)
        {
            case "hint": request.Kind = UiMatchKind.Hint; break;
            case "text": request.Kind = UiMatchKind.Text; break;
            case "name": request.Kind = UiMatchKind.Name; break;
            default:
                error = $"unknown UI verb '{verb}'";
                return false;
        }

        var maxArgs = request.Click ? 2 : 1;
        if (args.Count < 1 || args.Count > maxArgs || args[0].Length == 0)
        {
            error = request.Click ? $"usage: {verb} <text> [index]" : $"usage: {verb} <text>";
            return false;
        }

        request.Needle = args[0];
        if (args.Count == 2)
        {
            if (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                error = $"bad index '{args[1]}' (0, 1, 2, ...)";
                return false;
            }

            request.Index = index;
        }

        return true;
    }

    /// <summary>Grammar: <c>patches &lt;Type.FullName&gt; &lt;method&gt;</c>.</summary>
    public static bool TryParsePatches(IReadOnlyList<string> args, out string typeName, out string methodName, out string error)
    {
        typeName = methodName = error = string.Empty;
        if (args.Count != 2 || args[0].Length == 0 || args[1].Length == 0)
        {
            error = "usage: patches <TypeName> <MethodName>";
            return false;
        }

        typeName = args[0];
        methodName = args[1];
        return true;
    }

    /// <summary>Grammar: <c>textinfo &lt;text&gt;</c>, one non-empty argument (quote spaces).</summary>
    public static bool TryParseSingle(string verb, IReadOnlyList<string> args, out string value, out string error)
    {
        value = error = string.Empty;
        if (args.Count != 1 || args[0].Length == 0)
        {
            error = $"usage: {verb} <text>";
            return false;
        }

        value = args[0];
        return true;
    }
}
