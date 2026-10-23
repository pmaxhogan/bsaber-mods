using System.Collections.Generic;
using System.Linq;

namespace MaxTweaks.Core.AutoReload;

/// <summary>
/// A settled set of changed map folders (normalized folder keys, see <see cref="MapPaths"/>).
/// <see cref="Rescan"/> means events were lost (watcher buffer overflow), so the folder list may be
/// incomplete and a library refresh must run even if no folder is listed.
/// </summary>
public sealed class ChangeBatch
{
    public ChangeBatch(IEnumerable<string> folders, bool rescan)
    {
        Folders = new HashSet<string>(folders, MapPaths.Comparer).ToList();
        Rescan = rescan;
    }

    public IReadOnlyList<string> Folders { get; }

    public bool Rescan { get; }

    public bool IsEmpty => Folders.Count == 0 && !Rescan;

    /// <summary>Union of two batches; either may be null.</summary>
    public static ChangeBatch? Merge(ChangeBatch? a, ChangeBatch? b)
    {
        if (a == null)
        {
            return b;
        }

        if (b == null)
        {
            return a;
        }

        return new ChangeBatch(a.Folders.Concat(b.Folders), a.Rescan || b.Rescan);
    }

    public bool Contains(string folderKey)
    {
        foreach (var folder in Folders)
        {
            if (MapPaths.Comparer.Equals(folder, folderKey))
            {
                return true;
            }
        }

        return false;
    }
}
