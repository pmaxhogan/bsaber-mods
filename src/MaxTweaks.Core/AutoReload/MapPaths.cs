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

    /// <summary>Active-pause flags file next to Info.dat.</summary>
    public const string FlagsFileName = "flags.json";

    /// <summary>Active-pause flag screenshots folder next to Info.dat.</summary>
    public const string FlagsFolderName = "flags";

    /// <summary>
    /// True for files MaxTweaks itself writes next to a map that SongCore never reads, so writing
    /// them is not a change to the map: active-pause flags ("flags.json", its temp and corrupt
    /// copies "flags.json.*", and the "flags" screenshot folder and anything inside it). The path is
    /// relative to the map folder; '\' and '/' both separate, case-insensitive.
    /// </summary>
    public static bool IsSidecarOnly(string relativePathInsideMapFolder)
    {
        if (string.IsNullOrEmpty(relativePathInsideMapFolder))
        {
            return false;
        }

        var path = relativePathInsideMapFolder.Replace('/', Separator).TrimStart(Separator);
        var slash = path.IndexOf(Separator);
        var first = slash < 0 ? path : path.Substring(0, slash);
        if (Comparer.Equals(first, FlagsFolderName))
        {
            return true;
        }

        return slash < 0
            && (Comparer.Equals(first, FlagsFileName)
                || first.StartsWith(FlagsFileName + ".", StringComparison.OrdinalIgnoreCase));
    }

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
    /// trigger reloads. Returns false for the root itself, deeper paths, paths outside the root,
    /// MaxTweaks' own sidecar files (<see cref="IsSidecarOnly"/>) and folders in
    /// <paramref name="ignoredFolderNames"/> (e.g. SongCore's "Cache" under CustomWIPLevels).
    /// </summary>
    public static bool TryGetMapFolderName(
        string root,
        string changedPath,
        IReadOnlyCollection<string>? ignoredFolderNames,
        out string folderName)
    {
        return TryGetMapFolderName(root, changedPath, ignoredFolderNames, null, out folderName);
    }

    /// <summary>
    /// As above, and additionally rejects "root\Map\X" when <paramref name="isDirectory"/> says X is a
    /// directory: a write into an editor's "Map\autosaves\" folder raises a change event for the
    /// autosaves folder itself, which must not count as a change to the map.
    /// </summary>
    public static bool TryGetMapFolderName(
        string root,
        string changedPath,
        IReadOnlyCollection<string>? ignoredFolderNames,
        Func<string, bool>? isDirectory,
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

        if (parts.Length == 2 && isDirectory != null && isDirectory(changedPath))
        {
            return false;
        }

        if (parts.Length == 2 && IsSidecarOnly(parts[1]))
        {
            return false;
        }

        folderName = parts[0];
        return true;
    }
}
