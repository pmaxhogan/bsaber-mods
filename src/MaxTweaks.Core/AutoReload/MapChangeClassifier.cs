namespace MaxTweaks.Core.AutoReload;

/// <summary>What is on disk for a changed map folder when the batch settles.</summary>
public enum MapFolderState
{
    /// <summary>The folder does not exist (deleted or renamed away), or is hidden.</summary>
    Missing,

    /// <summary>The folder exists but has no Info.dat yet (copy/extraction in progress, or not a map).</summary>
    NoInfoDat,

    /// <summary>The folder exists and has an Info.dat.</summary>
    Ready,
}

public enum MapChangeKind
{
    /// <summary>Nothing to do (e.g. a folder appeared and vanished again before it was ever loaded).</summary>
    None,

    /// <summary>New map folder: an incremental refresh picks it up.</summary>
    Added,

    /// <summary>
    /// Already-loaded map whose files changed. SongCore's incremental refresh skips folders it has
    /// loaded, so the old entry must be dropped first to make it load again.
    /// </summary>
    Updated,

    /// <summary>Loaded map whose folder is gone.</summary>
    Removed,

    /// <summary>Folder exists without Info.dat: wait and look again later.</summary>
    AwaitingInfoDat,
}

public static class MapChangeClassifier
{
    public static MapChangeKind Classify(MapFolderState state, bool isLoaded)
    {
        switch (state)
        {
            case MapFolderState.Missing:
                return isLoaded ? MapChangeKind.Removed : MapChangeKind.None;
            case MapFolderState.NoInfoDat:
                // Even for a loaded map: editors may delete and rewrite Info.dat when saving. Waiting
                // is safe; the old version stays playable until the new Info.dat lands.
                return MapChangeKind.AwaitingInfoDat;
            default:
                return isLoaded ? MapChangeKind.Updated : MapChangeKind.Added;
        }
    }
}
