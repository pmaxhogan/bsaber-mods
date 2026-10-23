using System;
using System.IO;

namespace MaxTweaks.Core;

/// <summary>
/// Resolves file names taken from map data (Info.dat entries, bookmark names, ...) against the map
/// folder. Downloaded maps are untrusted: a name must not be rooted and must not escape the folder
/// with "..". Every map-relative file read goes through <see cref="TryResolveInside"/>.
/// </summary>
public static class SafeMapPaths
{
    /// <returns>True with the full path if <paramref name="name"/> stays inside <paramref name="mapFolder"/>.</returns>
    public static bool TryResolveInside(string? mapFolder, string? name, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(mapFolder) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            // Map JSON is authored on Windows: treat '\' as a separator on every OS, so "..\x" and
            // "\\server\share" are recognised (and rejected) even where '\' is a plain filename char.
            name = name!.Replace('\\', '/');
            if (Path.IsPathRooted(name) || name.StartsWith("/", StringComparison.Ordinal) || name.IndexOf(':') >= 0)
            {
                return false;
            }

            var root = Path.GetFullPath(mapFolder!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(root, name));
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || candidate.Length == root.Length)
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
