using System.Collections.Generic;

namespace MaxTweaks.Core.AutoReload;

/// <summary>
/// Pure helpers for the polling fallback: a snapshot maps each map folder name under a root to a
/// stamp derived from its top-level files (times, sizes, names). Comparing two snapshots yields the
/// folders that appeared, vanished or changed.
/// </summary>
public static class FolderSnapshot
{
    private const ulong FnvOffset = 14695981039346656037UL;

    /// <summary>Starting value for <see cref="Mix"/>.</summary>
    public const long Empty = 0;

    /// <summary>
    /// Folds one value into a stamp. Order-independent (files may be listed in any order): each value
    /// is scrambled and then added.
    /// </summary>
    public static long Mix(long stamp, long value)
    {
        unchecked
        {
            var x = (ulong)value ^ FnvOffset;
            x ^= x >> 33;
            x *= 0xff51afd7ed558ccdUL;
            x ^= x >> 33;
            x *= 0xc4ceb9fe1a85ec53UL;
            x ^= x >> 33;
            return stamp + (long)x;
        }
    }

    /// <summary>Stable (process-independent) hash of a file name, case-insensitive.</summary>
    public static long NameHash(string name)
    {
        unchecked
        {
            var hash = FnvOffset;
            foreach (var c in name)
            {
                hash ^= char.ToUpperInvariant(c);
                hash *= 1099511628211UL;
            }

            return (long)hash;
        }
    }

    /// <summary>Folder names that were added, removed or whose stamp changed.</summary>
    public static List<string> Diff(IReadOnlyDictionary<string, long> previous, IReadOnlyDictionary<string, long> current)
    {
        var changed = new List<string>();
        foreach (var entry in current)
        {
            if (!previous.TryGetValue(entry.Key, out var stamp) || stamp != entry.Value)
            {
                changed.Add(entry.Key);
            }
        }

        foreach (var entry in previous)
        {
            if (!current.ContainsKey(entry.Key))
            {
                changed.Add(entry.Key);
            }
        }

        return changed;
    }
}
