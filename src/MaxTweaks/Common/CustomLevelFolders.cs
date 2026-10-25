namespace MaxTweaks.Common;

/// <summary>Map folder (the one holding Info.dat) of a custom level loaded by SongCore.</summary>
internal static class CustomLevelFolders
{
    /// <returns>The folder path, or null for OST/DLC levels and levels SongCore did not load.</returns>
    public static string? TryGetFolder(BeatmapLevel? level)
    {
        var loader = SongCore.Loader.CustomLevelLoader;
        if (level == null || loader == null)
        {
            return null;
        }

        return loader._loadedBeatmapSaveData.TryGetValue(level.levelID, out var saveData)
            ? saveData.customLevelFolderInfo.folderPath
            : null;
    }
}
