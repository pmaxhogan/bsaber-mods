using System;
using MaxTweaks.Core.HideOst;
using Xunit;

namespace MaxTweaks.Core.Tests.HideOst;

public class OfficialSongPolicyTests
{
    private const string Custom = "custom_level_0123456789ABCDEF0123456789ABCDEF01234567";

    [Theory]
    [InlineData("custom_level_0123456789ABCDEF0123456789ABCDEF01234567", true)]
    [InlineData("custom_level_0123456789ABCDEF0123456789ABCDEF01234567 WIP", true)]
    [InlineData("custom_level_", true)]
    [InlineData("100Bills", false)]
    [InlineData("BeatSaber", false)]
    [InlineData("Custom_Level_abc", false)]
    [InlineData("xcustom_level_abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCustomLevelId_UsesOrdinalPrefix(string? id, bool expected)
    {
        Assert.Equal(expected, OfficialSongPolicy.IsCustomLevelId(id));
    }

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    public void ShouldHide_RequiresEveryCondition(bool enabled, bool hidden, bool solo, bool customs, bool expected)
    {
        Assert.Equal(expected, OfficialSongPolicy.ShouldHide(enabled, hidden, solo, customs));
    }

    [Fact]
    public void FilterCategories_RemovesMusicPacksAndKeepsOrder()
    {
        var input = new[] { SongCategory.MusicPacks, SongCategory.CustomSongs, SongCategory.Favorites, SongCategory.All };
        Assert.Equal(
            new[] { SongCategory.CustomSongs, SongCategory.Favorites, SongCategory.All },
            OfficialSongPolicy.FilterCategories(input));
    }

    [Fact]
    public void FilterCategories_WithoutMusicPacks_IsUnchangedCopy()
    {
        var input = new[] { SongCategory.Favorites, SongCategory.All };
        var result = OfficialSongPolicy.FilterCategories(input);
        Assert.Equal(input, result);
        Assert.NotSame(input, result);
    }

    [Fact]
    public void FilterCategories_NeverReturnsEmpty()
    {
        var input = new[] { SongCategory.MusicPacks };
        Assert.Equal(input, OfficialSongPolicy.FilterCategories(input));
        Assert.Empty(OfficialSongPolicy.FilterCategories(Array.Empty<SongCategory>()));
    }

    [Fact]
    public void FilterCategories_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => OfficialSongPolicy.FilterCategories(null!));
    }

    [Theory]
    [InlineData(SongCategory.MusicPacks, SongCategory.CustomSongs)]
    [InlineData(SongCategory.CustomSongs, SongCategory.CustomSongs)]
    [InlineData(SongCategory.Favorites, SongCategory.Favorites)]
    [InlineData(SongCategory.All, SongCategory.All)]
    [InlineData(SongCategory.None, SongCategory.None)]
    public void CoerceStartCategory_MapsMusicPacksToCustomSongs(SongCategory requested, SongCategory expected)
    {
        Assert.Equal(expected, OfficialSongPolicy.CoerceStartCategory(requested));
    }

    [Fact]
    public void CoerceCategory_FallsBackToFirstThenNone()
    {
        Assert.Equal(
            SongCategory.Favorites,
            OfficialSongPolicy.CoerceCategory(SongCategory.MusicPacks, new[] { SongCategory.Favorites, SongCategory.All }));
        Assert.Equal(
            SongCategory.None,
            OfficialSongPolicy.CoerceCategory(SongCategory.MusicPacks, Array.Empty<SongCategory>()));
        Assert.Equal(
            SongCategory.MusicPacks,
            OfficialSongPolicy.CoerceCategory(SongCategory.MusicPacks, new[] { SongCategory.MusicPacks, SongCategory.All }));
        Assert.Throws<ArgumentNullException>(() => OfficialSongPolicy.CoerceCategory(SongCategory.All, null!));
    }

    [Fact]
    public void KeepCustomLevels_AllCustom_ReturnsSameInstance()
    {
        var levels = new[] { new Level(Custom + "A"), new Level(Custom + "B") };
        Assert.Same(levels, OfficialSongPolicy.KeepCustomLevels(levels, l => l.Id));
    }

    [Fact]
    public void KeepCustomLevels_Empty_ReturnsSameInstance()
    {
        var levels = Array.Empty<Level>();
        Assert.Same(levels, OfficialSongPolicy.KeepCustomLevels(levels, l => l.Id));
    }

    [Fact]
    public void KeepCustomLevels_Mixed_KeepsCustomInOrder()
    {
        var a = new Level(Custom + "A");
        var b = new Level("Escape");
        var c = new Level(Custom + "C");
        var d = new Level(null);
        var input = new[] { b, a, d, c };
        var result = OfficialSongPolicy.KeepCustomLevels(input, l => l.Id);
        Assert.Equal(new[] { a, c }, result);
        Assert.NotSame(input, result);
        Assert.Equal(new[] { b, a, d, c }, input);
    }

    [Fact]
    public void KeepCustomLevels_NoCustom_ReturnsEmpty()
    {
        var result = OfficialSongPolicy.KeepCustomLevels(new[] { new Level("100Bills"), new Level("BeatSaber") }, l => l.Id);
        Assert.Empty(result);
    }

    [Fact]
    public void KeepCustomLevels_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => OfficialSongPolicy.KeepCustomLevels<Level>(null!, l => l.Id));
        Assert.Throws<ArgumentNullException>(() => OfficialSongPolicy.KeepCustomLevels(new[] { new Level(Custom) }, null!));
    }

    [Theory]
    [InlineData(SongCategory.None, SongCategory.None)]
    [InlineData(SongCategory.MusicPacks, SongCategory.CustomSongs)]
    [InlineData(SongCategory.CustomSongs, SongCategory.CustomSongs)]
    [InlineData(SongCategory.Favorites, SongCategory.Favorites)]
    [InlineData(SongCategory.All, SongCategory.All)]
    public void CoerceCategory_AgainstHiddenTabSet(SongCategory requested, SongCategory expected)
    {
        var hiddenTabs = OfficialSongPolicy.FilterCategories(
            new[] { SongCategory.MusicPacks, SongCategory.CustomSongs, SongCategory.Favorites, SongCategory.All });
        Assert.Equal(expected, OfficialSongPolicy.CoerceCategory(requested, hiddenTabs));
    }

    [Theory]
    [InlineData(SongCategory.None)]
    [InlineData(SongCategory.MusicPacks)]
    [InlineData(SongCategory.CustomSongs)]
    [InlineData(SongCategory.Favorites)]
    [InlineData(SongCategory.All)]
    public void CoerceCategory_FullTabSet_IsIdentity(SongCategory requested)
    {
        var allTabs = new[] { SongCategory.MusicPacks, SongCategory.CustomSongs, SongCategory.Favorites, SongCategory.All };
        Assert.Equal(requested, OfficialSongPolicy.CoerceCategory(requested, allTabs));
    }

    [Fact]
    public void SongCategory_MatchesDecompiledGameEnum()
    {
        // Beat Saber 1.40.8 Main.dll, SelectLevelCategoryViewController.LevelCategory (ilspycmd):
        //   enum LevelCategory { None, MusicPacks, CustomSongs, Favorites, All }  => 0, 1, 2, 3, 4
        // BetterSongList and PlaylistManager also hard-code CustomSongs as (int)2 in their patches.
        Assert.Equal(0, (int)SongCategory.None);
        Assert.Equal(1, (int)SongCategory.MusicPacks);
        Assert.Equal(2, (int)SongCategory.CustomSongs);
        Assert.Equal(3, (int)SongCategory.Favorites);
        Assert.Equal(4, (int)SongCategory.All);
        Assert.Equal(5, Enum.GetValues(typeof(SongCategory)).Length);
    }

    private sealed record Level(string? Id);
}
