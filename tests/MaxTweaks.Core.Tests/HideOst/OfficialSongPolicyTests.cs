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
    public void ClassifyPack_AllCustomOrEmpty_IsKeptAsIs()
    {
        Assert.Equal(PackVerdict.KeepAsIs, OfficialSongPolicy.ClassifyPack(new[] { Custom, Custom }));
        Assert.Equal(PackVerdict.KeepAsIs, OfficialSongPolicy.ClassifyPack(Array.Empty<string?>()));
    }

    [Fact]
    public void ClassifyPack_Mixed_KeepsCustomOnly()
    {
        Assert.Equal(PackVerdict.KeepCustomOnly, OfficialSongPolicy.ClassifyPack(new[] { "100Bills", Custom }));
    }

    [Fact]
    public void ClassifyPack_NoCustom_IsDropped()
    {
        Assert.Equal(PackVerdict.Drop, OfficialSongPolicy.ClassifyPack(new string?[] { "100Bills", null }));
    }

    [Fact]
    public void ClassifyPack_Generic_UsesSelector()
    {
        var levels = new[] { new Level("BeatSaber"), new Level(Custom) };
        Assert.Equal(PackVerdict.KeepCustomOnly, OfficialSongPolicy.ClassifyPack(levels, l => l.Id));
        Assert.Throws<ArgumentNullException>(() => OfficialSongPolicy.ClassifyPack<Level>(null!, l => l.Id));
        Assert.Throws<ArgumentNullException>(() => OfficialSongPolicy.ClassifyPack(levels, null!));
    }

    [Fact]
    public void KeepCustom_KeepsOrderAndDropsOfficial()
    {
        var a = new Level(Custom + "A");
        var b = new Level("Escape");
        var c = new Level(Custom + "C");
        var result = OfficialSongPolicy.KeepCustom(new[] { a, b, c }, l => l.Id);
        Assert.Equal(new[] { a, c }, result);
        Assert.Empty(OfficialSongPolicy.KeepCustom(new[] { b }, l => l.Id));
        Assert.Throws<ArgumentNullException>(() => OfficialSongPolicy.KeepCustom<Level>(null!, l => l.Id));
        Assert.Throws<ArgumentNullException>(() => OfficialSongPolicy.KeepCustom(new[] { a }, null!));
    }

    [Fact]
    public void SongCategory_MirrorsGameEnumOrder()
    {
        // Documents the 1.40.8 order; the plugin maps by name, so this is a reminder, not a contract.
        Assert.Equal(
            new[] { "None", "MusicPacks", "CustomSongs", "Favorites", "All" },
            Enum.GetNames(typeof(SongCategory)));
    }

    private sealed record Level(string? Id);
}
