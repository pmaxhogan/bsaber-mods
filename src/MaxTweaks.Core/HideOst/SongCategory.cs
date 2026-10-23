namespace MaxTweaks.Core.HideOst;

/// <summary>
/// Game-free mirror of the song list category tabs. Decompiled from Beat Saber 1.40.8 Main.dll:
/// <code>
/// public enum SelectLevelCategoryViewController.LevelCategory
/// { None, MusicPacks, CustomSongs, Favorites, All }   // = 0, 1, 2, 3, 4
/// </code>
/// The values below are pinned to the same numbers (unit tested). The plugin still maps between the
/// two by name with a switch, never by casting, so a reordered game enum cannot silently misroute.
/// </summary>
public enum SongCategory
{
    None = 0,
    MusicPacks = 1,
    CustomSongs = 2,
    Favorites = 3,
    All = 4,
}
