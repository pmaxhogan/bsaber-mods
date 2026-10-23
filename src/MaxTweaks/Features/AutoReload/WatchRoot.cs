using System.Collections.Generic;

namespace MaxTweaks.Features.AutoReload;

/// <summary>
/// One folder of map folders. SongCore keys its loaded maps by the path it scanned (for CustomLevels
/// that is the junction path inside the game folder), while the watcher watches the junction's
/// resolved target. Events are translated from <see cref="WatchPath"/> to <see cref="SongCorePath"/>.
/// </summary>
internal sealed class WatchRoot
{
    public WatchRoot(string name, string songCorePath, string watchPath, IReadOnlyCollection<string> ignoredFolderNames)
    {
        Name = name;
        SongCorePath = songCorePath;
        WatchPath = watchPath;
        IgnoredFolderNames = ignoredFolderNames;
    }

    public string Name { get; }

    /// <summary>Normalized path as SongCore spells it (prefix of its level dictionary keys).</summary>
    public string SongCorePath { get; }

    /// <summary>Normalized path that is actually watched (junctions resolved).</summary>
    public string WatchPath { get; }

    /// <summary>Folder names directly under the root that never count as maps (e.g. "Cache").</summary>
    public IReadOnlyCollection<string> IgnoredFolderNames { get; }

    public override string ToString() => SongCorePath == WatchPath
        ? $"{Name} ({WatchPath})"
        : $"{Name} ({SongCorePath} -> {WatchPath})";
}
