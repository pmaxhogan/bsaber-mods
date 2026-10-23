using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MaxTweaks.Core.Chapters.Bookmarks;

/// <summary>The bookmarks of one difficulty.</summary>
public sealed class DifficultyBookmarks
{
    public DifficultyBookmarks(string characteristic, string difficulty, IReadOnlyList<Bookmark> bookmarks)
    {
        Characteristic = characteristic;
        Difficulty = difficulty;
        Bookmarks = bookmarks;
    }

    public string Characteristic { get; }

    public string Difficulty { get; }

    public IReadOnlyList<Bookmark> Bookmarks { get; }
}

/// <summary>A chapter produced from bookmarks; positions in beats, open end means "until the song ends".</summary>
public sealed class GeneratedChapter
{
    public GeneratedChapter(double startBeat, double? endBeat, string label, IReadOnlyList<string>? difficulties)
    {
        StartBeat = startBeat;
        EndBeat = endBeat;
        Label = label;
        Difficulties = difficulties;
    }

    public double StartBeat { get; }

    public double? EndBeat { get; }

    public string Label { get; }

    public IReadOnlyList<string>? Difficulties { get; }
}

/// <summary>
/// Builds starter chapters from editor bookmarks: each bookmark starts a chapter and the next one ends
/// it; the last chapter has no end (it runs to the song end at play time). Difficulties with identical
/// bookmark lists are grouped. If every difficulty that has bookmarks agrees, the chapters carry no
/// "difficulties" filter (so they also show on difficulties without bookmarks: the song structure is
/// the same). Otherwise each group's chapters are limited to that group's difficulties, written as
/// "Difficulty" when the map has one characteristic and "Characteristic/Difficulty" otherwise.
/// </summary>
public static class BookmarkChapterBuilder
{
    public static List<GeneratedChapter> Build(IReadOnlyList<DifficultyBookmarks> difficulties, List<string> notes)
    {
        var groups = new List<(string Key, IReadOnlyList<Bookmark> Bookmarks, List<DifficultyBookmarks> Members)>();
        foreach (var entry in difficulties)
        {
            if (entry.Bookmarks.Count == 0)
            {
                notes.Add($"{entry.Characteristic}/{entry.Difficulty}: no bookmarks");
                continue;
            }

            var key = Key(entry.Bookmarks);
            var index = groups.FindIndex(g => g.Key == key);
            if (index < 0)
            {
                groups.Add((key, entry.Bookmarks, new List<DifficultyBookmarks> { entry }));
            }
            else
            {
                groups[index].Members.Add(entry);
            }
        }

        var result = new List<GeneratedChapter>();
        if (groups.Count == 0)
        {
            return result;
        }

        var qualify = difficulties.Select(d => DifficultyFilter.Normalize(d.Characteristic)).Distinct().Count() > 1;
        if (groups.Count > 1 && difficulties.Any(d => d.Bookmarks.Count == 0))
        {
            notes.Add("bookmarks differ between difficulties: difficulties without bookmarks get no chapters");
        }

        foreach (var group in groups)
        {
            IReadOnlyList<string>? filter = groups.Count == 1
                ? null
                : group.Members.Select(m => qualify ? $"{m.Characteristic}/{m.Difficulty}" : m.Difficulty).ToList();
            AddChapters(group.Bookmarks, filter, result, notes);
        }

        return result;
    }

    /// <summary>Serializes chapters as chapters.json text (two-space indent, LF line endings).</summary>
    public static string ToJson(IReadOnlyList<GeneratedChapter> chapters)
    {
        var array = new JArray();
        foreach (var chapter in chapters)
        {
            var obj = new JObject
            {
                ["label"] = chapter.Label,
                ["startBeat"] = chapter.StartBeat,
            };
            if (chapter.EndBeat.HasValue)
            {
                obj["endBeat"] = chapter.EndBeat.Value;
            }

            if (chapter.Difficulties != null)
            {
                obj["difficulties"] = new JArray(chapter.Difficulties.Cast<object>().ToArray());
            }

            array.Add(obj);
        }

        var root = new JObject { ["chapters"] = array };
        var builder = new StringBuilder();
        using (var writer = new JsonTextWriter(new System.IO.StringWriter(builder, CultureInfo.InvariantCulture)))
        {
            writer.Formatting = Formatting.Indented;
            writer.Indentation = 2;
            root.WriteTo(writer);
        }

        return builder.ToString().Replace("\r\n", "\n") + "\n";
    }

    private static void AddChapters(IReadOnlyList<Bookmark> bookmarks, IReadOnlyList<string>? filter, List<GeneratedChapter> result, List<string> notes)
    {
        for (var i = 0; i < bookmarks.Count; i++)
        {
            var start = bookmarks[i].Beat;
            double? end = i + 1 < bookmarks.Count ? bookmarks[i + 1].Beat : null;
            var label = bookmarks[i].Name.Length > 0 ? bookmarks[i].Name : $"Chapter {i + 1}";
            if (end.HasValue && end.Value <= start)
            {
                notes.Add($"bookmark '{label}' at beat {start.ToString(CultureInfo.InvariantCulture)} shares its beat with the next one, skipped");
                continue;
            }

            result.Add(new GeneratedChapter(start, end, label, filter));
        }
    }

    private static string Key(IReadOnlyList<Bookmark> bookmarks)
    {
        var builder = new StringBuilder();
        foreach (var bookmark in bookmarks)
        {
            builder.Append(bookmark.Beat.ToString("R", CultureInfo.InvariantCulture)).Append('\u0001').Append(bookmark.Name).Append('\u0002');
        }

        return builder.ToString();
    }
}
