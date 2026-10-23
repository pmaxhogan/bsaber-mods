namespace MaxTweaks.Core.HideOst;

/// <summary>
/// Game-free mirror of the song list category tabs (SelectLevelCategoryViewController.LevelCategory
/// in Beat Saber 1.40.8: None, MusicPacks, CustomSongs, Favorites, All). The plugin maps between the
/// two by name with a switch, never by casting, so a reordered game enum cannot silently misroute.
/// </summary>
public enum SongCategory
{
    None,
    MusicPacks,
    CustomSongs,
    Favorites,
    All,
}
