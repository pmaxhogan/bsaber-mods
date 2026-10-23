using System.Collections.Generic;
using System.Text;

namespace MaxTweaks.Core.AutoReload;

/// <summary>
/// Outcome of one reload, computed by comparing the loaded map folders before and after the refresh
/// (so maps found only through a rescan are counted too), plus the folders this reload expected to
/// (re)load.
/// </summary>
public sealed class ReloadSummary
{
    public ReloadSummary(int added, int updated, int removed, int failed)
    {
        Added = added;
        Updated = updated;
        Removed = removed;
        Failed = failed;
    }

    public int Added { get; }

    public int Updated { get; }

    public int Removed { get; }

    /// <summary>Folders that should have (re)loaded but did not (broken Info.dat, missing audio...).</summary>
    public int Failed { get; }

    public bool IsEmpty => Added == 0 && Updated == 0 && Removed == 0 && Failed == 0;

    /// <param name="loadedBefore">Loaded map folder keys before the reload (before dropping updated maps).</param>
    /// <param name="loadedAfter">Loaded map folder keys after the refresh finished.</param>
    /// <param name="updated">Loaded folders that were dropped so they would load again.</param>
    /// <param name="expectedAdded">New folders with an Info.dat that the refresh should have loaded.</param>
    public static ReloadSummary Compute(
        IEnumerable<string> loadedBefore,
        IEnumerable<string> loadedAfter,
        IEnumerable<string> updated,
        IEnumerable<string> expectedAdded)
    {
        var before = new HashSet<string>(loadedBefore, MapPaths.Comparer);
        var after = new HashSet<string>(loadedAfter, MapPaths.Comparer);
        var updatedSet = new HashSet<string>(updated, MapPaths.Comparer);

        int added = 0, updatedCount = 0, removed = 0, failed = 0;
        foreach (var key in after)
        {
            if (!before.Contains(key))
            {
                added++;
            }
        }

        foreach (var key in before)
        {
            if (!after.Contains(key) && !updatedSet.Contains(key))
            {
                removed++;
            }
        }

        foreach (var key in updatedSet)
        {
            if (after.Contains(key))
            {
                updatedCount++;
            }
            else
            {
                failed++;
            }
        }

        foreach (var key in new HashSet<string>(expectedAdded, MapPaths.Comparer))
        {
            if (!after.Contains(key) && !updatedSet.Contains(key))
            {
                failed++;
            }
        }

        return new ReloadSummary(added, updatedCount, removed, failed);
    }

    /// <summary>"3 maps added, 1 updated, 2 removed, 1 failed to load"; null when nothing changed.</summary>
    public string? ToToastText()
    {
        if (IsEmpty)
        {
            return null;
        }

        var builder = new StringBuilder();
        Append(builder, Added, "added");
        Append(builder, Updated, "updated");
        Append(builder, Removed, "removed");
        Append(builder, Failed, "failed to load");
        return builder.ToString();
    }

    public override string ToString() => ToToastText() ?? "no changes";

    private static void Append(StringBuilder builder, int count, string verb)
    {
        if (count == 0)
        {
            return;
        }

        if (builder.Length == 0)
        {
            builder.Append(count).Append(count == 1 ? " map " : " maps ").Append(verb);
        }
        else
        {
            builder.Append(", ").Append(count).Append(' ').Append(verb);
        }
    }
}
