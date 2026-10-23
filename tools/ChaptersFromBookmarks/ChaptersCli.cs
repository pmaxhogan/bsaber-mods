using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MaxTweaks.Core;
using MaxTweaks.Core.Chapters.Bookmarks;

namespace ChaptersFromBookmarks;

/// <summary>
/// The command-line logic, separated from Main so tests can run it with captured output.
/// chapters-from-bookmarks &lt;map folder&gt; [--out &lt;file&gt;] [--stdout] [--force]
/// </summary>
public static class ChaptersCli
{
    public const int ExitOk = 0;
    public const int ExitUsage = 1;
    public const int ExitMapUnreadable = 2;
    public const int ExitNoBookmarks = 3;
    public const int ExitOutputExists = 4;
    public const int ExitWriteFailed = 5;

    public const string Usage =
        "usage: chapters-from-bookmarks <map folder> [--out <file>] [--stdout] [--force]\n" +
        "  Builds a starter chapters.json from the map's editor bookmarks (v2 _bookmarks,\n" +
        "  v3 customData.bookmarks, v4 Bookmarks/*.dat). Each bookmark starts a chapter and the\n" +
        "  next one ends it; the last chapter runs to the song end.\n" +
        "  --out <file>  write here instead of <map folder>/chapters.json\n" +
        "  --stdout      print the JSON instead of writing a file\n" +
        "  --force       overwrite an existing output file\n" +
        "  exit codes: 0 written, 1 usage, 2 map unreadable, 3 no bookmarks, 4 output exists, 5 write failed";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        string? folder = null;
        string? outPath = null;
        bool toStdout = false, force = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    output.WriteLine(Usage);
                    return ExitOk;
                case "-o" or "--out" when i + 1 < args.Length:
                    outPath = args[++i];
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
                        error.WriteLine($"unexpected argument '{args[i]}'\n{Usage}");
                        return ExitUsage;
                    }

                    folder = args[i];
                    break;
            }
        }

        if (folder == null)
        {
            error.WriteLine(Usage);
            return ExitUsage;
        }

        var notes = new List<string>();
        List<DifficultyBookmarks> difficulties;
        try
        {
            difficulties = MapBookmarkReader.Read(folder, notes);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            error.WriteLine($"cannot read map: {e.Message}");
            return ExitMapUnreadable;
        }

        var chapters = BookmarkChapterBuilder.Build(difficulties, notes);
        foreach (var note in notes)
        {
            error.WriteLine($"note: {note}");
        }

        if (chapters.Count == 0)
        {
            error.WriteLine("no bookmarks found, nothing written");
            return ExitNoBookmarks;
        }

        var json = BookmarkChapterBuilder.ToJson(chapters);
        if (toStdout)
        {
            output.Write(json);
            return ExitOk;
        }

        string path;
        if (outPath != null)
        {
            path = Path.GetFullPath(outPath);
        }
        else if (!SafeMapPaths.TryResolveInside(folder, "chapters.json", out path))
        {
            error.WriteLine("cannot build the output path");
            return ExitMapUnreadable;
        }

        return Write(path, json, force, chapters.Count, output, error);
    }

    private static int Write(string path, string json, bool force, int count, TextWriter output, TextWriter error)
    {
        // CreateNew fails atomically if the file exists, so there is no check-then-write race.
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json);
        try
        {
            using (var stream = new FileStream(path, force ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        catch (IOException) when (!force && File.Exists(path))
        {
            error.WriteLine($"'{path}' already exists; use --force to overwrite");
            return ExitOutputExists;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            error.WriteLine($"cannot write '{path}': {e.Message}");
            return ExitWriteFailed;
        }

        output.WriteLine($"wrote {count} chapter(s) to {path}");
        return ExitOk;
    }
}
