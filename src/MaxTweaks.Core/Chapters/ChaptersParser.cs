using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MaxTweaks.Core.Chapters;

/// <summary>
/// Parses chapters.json. Never throws: malformed input yields an empty document with warnings, and
/// each bad entry is skipped with a warning naming its index.
/// <code>
/// { "chapters": [
///     { "start": "0:12.5", "end": "0:45", "label": "Intro" },
///     { "startBeat": 64, "endBeat": 128, "label": "Drop", "difficulties": ["Expert", "ExpertPlus"] },
///     { "start": 120, "label": "Outro" }   // no end: runs to the end of the song
/// ] }
/// </code>
/// A bare top-level array of chapters is accepted too. Property names are case-insensitive.
/// </summary>
public static class ChaptersParser
{
    public static ChaptersDocument Parse(string? json)
    {
        var warnings = new List<string>();
        var chapters = new List<ChapterSpec>();
        if (string.IsNullOrWhiteSpace(json))
        {
            warnings.Add("chapters file is empty");
            return new ChaptersDocument(chapters, warnings);
        }

        JToken root;
        try
        {
            root = ReadToken(json!);
        }
        catch (JsonException e)
        {
            warnings.Add($"chapters file is not valid JSON: {e.Message}");
            return new ChaptersDocument(chapters, warnings);
        }

        JArray? array = root as JArray;
        if (array == null && root is JObject obj)
        {
            array = GetProperty(obj, "chapters") as JArray;
        }

        if (array == null)
        {
            warnings.Add("chapters file has no \"chapters\" array");
            return new ChaptersDocument(chapters, warnings);
        }

        for (var i = 0; i < array.Count; i++)
        {
            var spec = ParseEntry(i, array[i], warnings);
            if (spec != null)
            {
                chapters.Add(spec);
            }
        }

        return new ChaptersDocument(chapters, warnings);
    }

    internal static JToken ReadToken(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json))
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
        };
        var token = JToken.ReadFrom(reader);

        // Reject trailing garbage after the root value (JToken.ReadFrom stops at the root's end).
        while (reader.Read())
        {
            if (reader.TokenType != JsonToken.Comment)
            {
                throw new JsonReaderException($"unexpected content after the root value at line {reader.LineNumber}");
            }
        }

        return token;
    }

    /// <summary>
    /// Numeric token to double without throwing: Newtonsoft stores integers beyond Int64 as BigInteger,
    /// which Value&lt;double&gt;() cannot convert. Out-of-range values become infinity (callers reject them).
    /// </summary>
    internal static double ToDouble(JToken token)
    {
        if (token is not JValue { Value: { } raw })
        {
            return double.NaN;
        }

        try
        {
            return raw switch
            {
                double d => d,
                float f => f,
                long l => l,
                int i => i,
                decimal m => (double)m,
                IConvertible c => c.ToDouble(CultureInfo.InvariantCulture),

                // BigInteger (not IConvertible, and not referenced here to stay compatible with the
                // game's Mono profile): its text form parses to a double or to infinity.
                _ => double.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN,
            };
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
        {
            return double.NaN;
        }
    }

    internal static JToken? GetProperty(JObject obj, string name) => obj.GetValue(name, StringComparison.OrdinalIgnoreCase);

    private static ChapterSpec? ParseEntry(int index, JToken token, List<string> warnings)
    {
        var where = $"chapter #{index + 1}";
        if (token is not JObject entry)
        {
            warnings.Add($"{where}: not an object, skipped");
            return null;
        }

        var labelToken = GetProperty(entry, "label");
        string? label = null;
        if (labelToken != null && labelToken.Type != JTokenType.Null)
        {
            if (labelToken.Type != JTokenType.String)
            {
                warnings.Add($"{where}: \"label\" must be a string, skipped");
                return null;
            }

            label = ((string?)labelToken)?.Trim();
            if (label != null && label.Length == 0)
            {
                label = null;
            }
        }

        if (label != null)
        {
            where += $" ('{label}')";
        }

        var hasTime = Has(entry, "start") || Has(entry, "end");
        var hasBeats = Has(entry, "startBeat") || Has(entry, "endBeat");
        if (hasTime && hasBeats)
        {
            warnings.Add($"{where}: mixes start/end with startBeat/endBeat, use one style, skipped");
            return null;
        }

        float? start = null, end = null, startBeat = null, endBeat = null;
        if (hasBeats)
        {
            if (!TryReadNumber(entry, "startBeat", where, warnings, out startBeat) ||
                !TryReadNumber(entry, "endBeat", where, warnings, out endBeat))
            {
                return null;
            }

            if (!startBeat.HasValue)
            {
                warnings.Add($"{where}: missing \"startBeat\", skipped");
                return null;
            }
        }
        else
        {
            if (!TryReadTime(entry, "start", where, warnings, out start) ||
                !TryReadTime(entry, "end", where, warnings, out end))
            {
                return null;
            }

            if (!start.HasValue)
            {
                warnings.Add($"{where}: missing \"start\" (or \"startBeat\"), skipped");
                return null;
            }
        }

        if (!TryReadDifficulties(entry, where, warnings, out var difficulties))
        {
            return null;
        }

        return new ChapterSpec(index, label, start, end, startBeat, endBeat, difficulties);
    }

    private static bool Has(JObject entry, string name)
    {
        var token = GetProperty(entry, name);
        return token != null && token.Type != JTokenType.Null;
    }

    private static bool TryReadTime(JObject entry, string name, string where, List<string> warnings, out float? value)
    {
        value = null;
        var token = GetProperty(entry, name);
        if (token == null || token.Type == JTokenType.Null)
        {
            return true;
        }

        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            var number = ToDouble(token);
            if (!(number >= 0 && number <= float.MaxValue))
            {
                warnings.Add($"{where}: \"{name}\" must be a non-negative number of seconds, skipped");
                return false;
            }

            value = (float)number;
            return true;
        }

        if (token.Type == JTokenType.String && TimeText.TryParse((string?)token, out var seconds))
        {
            value = seconds;
            return true;
        }

        warnings.Add($"{where}: \"{name}\" must be seconds or \"m:ss(.fff)\", got {Describe(token)}, skipped");
        return false;
    }

    private static bool TryReadNumber(JObject entry, string name, string where, List<string> warnings, out float? value)
    {
        value = null;
        var token = GetProperty(entry, name);
        if (token == null || token.Type == JTokenType.Null)
        {
            return true;
        }

        double number;
        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            number = ToDouble(token);
        }
        else if (token.Type != JTokenType.String ||
                 !double.TryParse((string?)token, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            warnings.Add($"{where}: \"{name}\" must be a number of beats, got {Describe(token)}, skipped");
            return false;
        }

        if (!(number >= 0 && number <= float.MaxValue))
        {
            warnings.Add($"{where}: \"{name}\" must be a non-negative number of beats, skipped");
            return false;
        }

        value = (float)number;
        return true;
    }

    private static bool TryReadDifficulties(JObject entry, string where, List<string> warnings, out IReadOnlyList<string>? difficulties)
    {
        difficulties = null;
        var token = GetProperty(entry, "difficulties");
        if (token == null || token.Type == JTokenType.Null)
        {
            return true;
        }

        var list = new List<string>();
        if (token.Type == JTokenType.String)
        {
            list.Add((string)token!);
        }
        else if (token is JArray array)
        {
            foreach (var item in array)
            {
                if (item.Type != JTokenType.String)
                {
                    warnings.Add($"{where}: \"difficulties\" must contain strings, skipped");
                    return false;
                }

                list.Add((string)item!);
            }
        }
        else
        {
            warnings.Add($"{where}: \"difficulties\" must be a string or an array of strings, skipped");
            return false;
        }

        list.RemoveAll(string.IsNullOrWhiteSpace);
        if (list.Count == 0)
        {
            warnings.Add($"{where}: \"difficulties\" is empty, applying the chapter to every difficulty");
            return true;
        }

        difficulties = list;
        return true;
    }

    private static string Describe(JToken token)
    {
        var text = token.ToString(Formatting.None);
        return text.Length > 40 ? text.Substring(0, 40) + "..." : text;
    }
}
