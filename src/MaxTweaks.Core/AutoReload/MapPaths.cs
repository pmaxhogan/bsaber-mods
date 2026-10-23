using System;
using System.Collections.Generic;

namespace MaxTweaks.Core.AutoReload;

/// <summary>
/// String-only path helpers for map folders. Paths are compared case-insensitively (Windows) and
/// both '\' and '/' are treated as separators, so the logic behaves the same on the Linux CI runner.
/// Nothing here touches the file system.
/// </summary>
public static class MapPaths
{
    public const char Separator = '\\';

    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>Unifies separators to '\' and trims trailing separators (keeps a bare drive root like "C:\").</summary>
    public static string Normalize(string path)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var unified = path.Replace('/', Separator);
        var end = unified.Length;
        while (end > 0 && unified[end - 1] == Separator)
        {
            end--;
        }

        if (end == 2 && end < unified.Length && unified[1] == ':')
        {
            return unified.Substring(0, 2) + Separator;
        }

        return end == unified.Length ? unified : unified.Substring(0, end);
    }

    /// <summary>Normalized "root\name" for a map folder directly under a root.</summary>
    public static string FolderKey(string root, string folderName)
    {
        var normalizedRoot = Normalize(root);
        return normalizedRoot[normalizedRoot.Length - 1] == Separator
            ? normalizedRoot + folderName
            : normalizedRoot + Separator + folderName;
    }

    /// <summary>
    /// Maps a changed path under <paramref name="root"/> to the map folder it belongs to. Only the map
    /// folder itself ("root\Map") and files directly inside it ("root\Map\Info.dat") count: SongCore
    /// reads nothing deeper, and editors write autosaves/backups into subfolders, which must not
    /// trigger reloads. Returns false for the root itself, deeper paths, paths outside the root and
    /// folders in <paramref name="ignoredFolderNames"/> (e.g. SongCore's "Cache" under CustomWIPLevels).
    /// </summary>
    public static bool TryGetMapFolderName(
        string root,
        string changedPath,
        IReadOnlyCollection<string>? ignoredFolderNames,
        out string folderName)
    {
        folderName = string.Empty;
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(changedPath))
        {
            return false;
        }

        var normalizedRoot = Normalize(root);
        var path = Normalize(changedPath);
        var prefixLength = normalizedRoot[normalizedRoot.Length - 1] == Separator
            ? normalizedRoot.Length
            : normalizedRoot.Length + 1;

        if (path.Length <= prefixLength
            || !path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || path[prefixLength - 1] != Separator)
        {
            return false;
        }

        var relative = path.Substring(prefixLength);
        var parts = relative.Split(Separator);
        if (parts.Length > 2 || parts[0].Length == 0)
        {
            return false;
        }

        if (ignoredFolderNames != null)
        {
            foreach (var ignored in ignoredFolderNames)
            {
                if (Comparer.Equals(ignored, parts[0]))
                {
                    return false;
                }
            }
        }

        folderName = parts[0];
        return true;
    }
}
