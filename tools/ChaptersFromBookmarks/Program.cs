using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MaxTweaks.Core;
using MaxTweaks.Core.Chapters.Bookmarks;

namespace ChaptersFromBookmarks;

/// <summary>
/// chapters-from-bookmarks &lt;map folder&gt; [--out &lt;file&gt;] [--stdout] [--force]
/// Reads Info.dat + difficulty files (+ Bookmarks/ for v4) and writes chapters.json into the map
/// folder (or --out). An existing file is never overwritten without --force.
/// Exit codes: 0 written, 1 usage error, 2 map unreadable, 3 no bookmarks, 4 output exists.
/// </summary>
internal static class Program
{
    private const string Usage =
        "usage: chapters-from-bookmarks <map folder> [--out <file>] [--stdout] [--force]\n" +
        "  Builds a starter chapters.json from the map's editor bookmarks (v2 _bookmarks,\n" +
        "  v3 customData.bookmarks, v4 Bookmarks/*.dat). Each bookmark starts a chapter and the\n" +
        "  next one ends it; the last chapter runs to the song end.\n" +
        "  --out <file>  write here instead of <map folder>/chapters.json\n" +
        "  --stdout      print the JSON instead of writing a file\n" +
        "  --force       overwrite an existing output file";

    private static int Main(string[] args)
    {
        string? folder = null;
        string? output = null;
        bool toStdout = false, force = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    Console.WriteLine(Usage);
                    return 0;
                case "-o" or "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--stdout":
                    toStdout = true;
                    break;
                case "-f" or "--force":
                    force = true;
                    break;
                default:
                    if (args[i].StartsWith('-') || folder != null)
                    {
                        Console.Error.WriteLine($"unexpected argument '{args[i]}'\n{Usage}");
                        return 1;
                    }

                    folder = args[i];
                    break;
            }
        }

        if (folder == null)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var notes = new List<string>();
        List<DifficultyBookmarks> difficulties;
        try
        {
            difficulties = MapBookmarkReader.Read(folder, notes);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"cannot read map: {e.Message}");
            return 2;
        }

        var chapters = BookmarkChapterBuilder.Build(difficulties, notes);
        foreach (var note in notes)
        {
            Console.Error.WriteLine($"note: {note}");
        }

        if (chapters.Count == 0)
        {
            Console.Error.WriteLine("no bookmarks found, nothing written");
            return 3;
        }

        var json = BookmarkChapterBuilder.ToJson(chapters);
        if (toStdout)
        {
            Console.Out.Write(json);
            return 0;
        }

        string path;
        if (output != null)
        {
            path = Path.GetFullPath(output);
        }
        else if (!SafeMapPaths.TryResolveInside(folder, "chapters.json", out path))
        {
            Console.Error.WriteLine("cannot build the output path");
            return 2;
        }

        if (File.Exists(path) && !force)
        {
            Console.Error.WriteLine($"'{path}' already exists; use --force to overwrite");
            return 4;
        }

        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"wrote {chapters.Count} chapter(s) to {path}");
        return 0;
    }
}
