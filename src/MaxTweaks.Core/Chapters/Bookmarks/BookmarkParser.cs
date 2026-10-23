using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MaxTweaks.Core.Chapters.Bookmarks;

/// <summary>An editor bookmark: a named position in beats.</summary>
public sealed class Bookmark
{
    public Bookmark(double beat, string name)
    {
        Beat = beat;
        Name = name;
    }

    public double Beat { get; }

    public string Name { get; }
}

/// <summary>A v4 official-editor bookmark file: which difficulty it belongs to and its bookmarks.</summary>
public sealed class BookmarkFile
{
    public BookmarkFile(string? characteristic, string? difficulty, IReadOnlyList<Bookmark> bookmarks)
    {
        Characteristic = characteristic;
        Difficulty = difficulty;
        Bookmarks = bookmarks;
    }

    public string? Characteristic { get; }

    public string? Difficulty { get; }

    public IReadOnlyList<Bookmark> Bookmarks { get; }
}

/// <summary>
/// Reads editor bookmarks (formats per ChroMapper's V2/V3/V4 loaders):
/// <list type="bullet">
/// <item>v2 difficulty: <c>_customData._bookmarks: [{ _time, _name, _color }]</c></item>
/// <item>v3 difficulty: <c>customData.bookmarks: [{ b, n, c }]</c> (ChroMapper's v2-to-v3 converter
/// copies v2 entries verbatim, so <c>_time/_name</c> keys are accepted here too)</item>
/// <item>v4: <c>Bookmarks/*.dat</c> next to Info.dat:
/// <c>{ characteristic, difficulty, bookmarks: [{ beat, label, text }] }</c></item>
/// </list>
/// Entries without a usable beat are dropped; returned lists are sorted by beat (stable).
/// </summary>
public static class BookmarkParser
{
    /// <returns>The bookmarks, or null if the file has none or cannot be parsed.</returns>
    public static List<Bookmark>? FromDifficultyJson(string? json)
    {
        var root = TryRead(json) as JObject;
        if (root == null)
        {
            return null;
        }

        foreach (var customKey in new[] { "customData", "_customData" })
        {
            if (ChaptersParser.GetProperty(root, customKey) is not JObject custom)
            {
                continue;
            }

            foreach (var bookmarksKey in new[] { "bookmarks", "_bookmarks" })
            {
                if (ChaptersParser.GetProperty(custom, bookmarksKey) is JArray array)
                {
                    var list = ReadEntries(array);
                    if (list.Count > 0)
                    {
                        return list;
                    }
                }
            }
        }

        return null;
    }

    /// <returns>The parsed file, or null if it is not a bookmark file.</returns>
    public static BookmarkFile? FromBookmarkFileJson(string? json)
    {
        if (TryRead(json) is not JObject root || ChaptersParser.GetProperty(root, "bookmarks") is not JArray array)
        {
            return null;
        }

        return new BookmarkFile(
            ReadString(root, "characteristic"),
            ReadString(root, "difficulty"),
            ReadEntries(array));
    }

    private static List<Bookmark> ReadEntries(JArray array)
    {
        var list = new List<(Bookmark Bookmark, int Order)>();
        foreach (var token in array)
        {
            if (token is not JObject entry)
            {
                continue;
            }

            var beat = ReadNumber(entry, "b") ?? ReadNumber(entry, "_time") ?? ReadNumber(entry, "beat");
            if (!beat.HasValue || beat.Value < 0)
            {
                continue;
            }

            var name = ReadString(entry, "n") ?? ReadString(entry, "_name") ?? ReadString(entry, "label") ?? ReadString(entry, "text") ?? string.Empty;
            list.Add((new Bookmark(beat.Value, name.Trim()), list.Count));
        }

        list.Sort((x, y) => x.Bookmark.Beat != y.Bookmark.Beat ? x.Bookmark.Beat.CompareTo(y.Bookmark.Beat) : x.Order.CompareTo(y.Order));
        var result = new List<Bookmark>(list.Count);
        foreach (var item in list)
        {
            result.Add(item.Bookmark);
        }

        return result;
    }

    private static double? ReadNumber(JObject entry, string name)
    {
        var token = ChaptersParser.GetProperty(entry, name);
        if (token == null)
        {
            return null;
        }

        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            var value = ChaptersParser.ToDouble(token);
            return double.IsNaN(value) || double.IsInfinity(value) ? null : value;
        }

        return token.Type == JTokenType.String &&
               double.TryParse((string?)token, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
               !double.IsNaN(parsed) && !double.IsInfinity(parsed)
            ? parsed
            : null;
    }

    private static string? ReadString(JObject entry, string name)
    {
        var token = ChaptersParser.GetProperty(entry, name);
        if (token == null || token.Type != JTokenType.String)
        {
            return null;
        }

        var value = ((string?)token)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    internal static JToken? TryRead(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return ChaptersParser.ReadToken(json!);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
