using System;
using System.Collections.Generic;
using System.IO;

namespace MaxTweaks.Core.Chapters.Bookmarks;

/// <summary>
/// Collects the bookmarks of every difficulty of a map folder: Info.dat lists the difficulties, each
/// difficulty file may carry v2/v3 bookmarks in its custom data, and v4 official-editor files in
/// Bookmarks/ are matched by their own "characteristic"/"difficulty" fields. When both exist for a
/// difficulty, the difficulty file wins.
/// </summary>
public static class MapBookmarkReader
{
    public static List<DifficultyBookmarks> Read(string mapFolder, List<string> notes)
    {
        var infoPath = FindInfo(mapFolder) ?? throw new FileNotFoundException($"no Info.dat in '{mapFolder}'");
        var difficulties = MapInfoParser.Parse(File.ReadAllText(infoPath))
            ?? throw new InvalidDataException($"'{infoPath}' is not a recognisable Info.dat");

        var bookmarkFiles = SafeMapPaths.TryResolveInside(mapFolder, "Bookmarks", out var bookmarkFolder)
            ? ReadBookmarkFolder(bookmarkFolder, notes)
            : new List<BookmarkFile>();
        var result = new List<DifficultyBookmarks>();
        foreach (var difficulty in difficulties)
        {
            IReadOnlyList<Bookmark>? bookmarks = null;
            if (difficulty.BeatmapFileName != null)
            {
                if (!SafeMapPaths.TryResolveInside(mapFolder, difficulty.BeatmapFileName, out var path))
                {
                    notes.Add($"{difficulty.Characteristic}/{difficulty.Difficulty}: file name '{difficulty.BeatmapFileName}' points outside the map folder, ignored");
                }
                else if (File.Exists(path))
                {
                    bookmarks = BookmarkParser.FromDifficultyJson(File.ReadAllText(path));
                }
                else
                {
                    notes.Add($"{difficulty.Characteristic}/{difficulty.Difficulty}: file '{difficulty.BeatmapFileName}' not found");
                }
            }

            if (bookmarks == null)
            {
                foreach (var file in bookmarkFiles)
                {
                    if (Same(file.Characteristic ?? "Standard", difficulty.Characteristic) && Same(file.Difficulty, difficulty.Difficulty))
                    {
                        bookmarks = file.Bookmarks;
                        break;
                    }
                }
            }

            result.Add(new DifficultyBookmarks(difficulty.Characteristic, difficulty.Difficulty, bookmarks ?? Array.Empty<Bookmark>()));
        }

        return result;
    }

    private static string? FindInfo(string mapFolder)
    {
        if (!Directory.Exists(mapFolder))
        {
            return null;
        }

        foreach (var path in Directory.GetFiles(mapFolder))
        {
            if (string.Equals(Path.GetFileName(path), "Info.dat", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return null;
    }

    private static List<BookmarkFile> ReadBookmarkFolder(string folder, List<string> notes)
    {
        var files = new List<BookmarkFile>();
        if (!Directory.Exists(folder))
        {
            return files;
        }

        var paths = Directory.GetFiles(folder);
        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var file = BookmarkParser.FromBookmarkFileJson(File.ReadAllText(path));
            if (file == null || file.Difficulty == null)
            {
                notes.Add($"Bookmarks/{Path.GetFileName(path)}: not a bookmark file with a difficulty, ignored");
                continue;
            }

            files.Add(file);
        }

        return files;
    }

    private static bool Same(string? a, string? b) => DifficultyFilter.Normalize(a) == DifficultyFilter.Normalize(b);
}
