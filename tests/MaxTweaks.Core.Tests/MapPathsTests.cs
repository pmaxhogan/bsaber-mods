using System;
using System.Collections.Generic;
using System.IO;
using MaxTweaks.Core.Chapters.Bookmarks;
using Xunit;

namespace MaxTweaks.Core.Tests;

public class MapPathsTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "maps", "abc (Song - Mapper)");

    [Theory]
    [InlineData("ExpertPlusStandard.dat")]
    [InlineData("Bookmarks/x.json")]
    [InlineData("Bookmarks\\x.json")]
    [InlineData("sub/../Hard.dat")]
    public void InsideNames_Resolve(string name)
    {
        Assert.True(MapPaths.TryResolveInside(Folder, name, out var full));
        Assert.StartsWith(Path.GetFullPath(Folder) + Path.DirectorySeparatorChar, full, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("..\\..\\x")]
    [InlineData("../x.dat")]
    [InlineData("../abc (Song - Mapper)2/x.dat")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("\\\\server\\share\\x")]
    [InlineData("C:x.dat")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EscapingOrEmptyNames_AreRejected(string? name)
    {
        Assert.False(MapPaths.TryResolveInside(Folder, name, out var full));
        Assert.Equal(string.Empty, full);
    }

    [Fact]
    public void MissingFolder_IsRejected()
    {
        Assert.False(MapPaths.TryResolveInside(null, "x.dat", out _));
        Assert.False(MapPaths.TryResolveInside(" ", "x.dat", out _));
    }

    [Fact]
    public void TrailingSeparatorOnFolder_IsFine()
    {
        Assert.True(MapPaths.TryResolveInside(Folder + Path.DirectorySeparatorChar, "Info.dat", out var full));
        Assert.EndsWith("Info.dat", full);
    }
}

public sealed class MapBookmarkReaderPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "maxtweaks-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void DifficultyFileOutsideTheMap_IsNotRead()
    {
        var map = Path.Combine(_root, "map");
        Directory.CreateDirectory(map);
        File.WriteAllText(Path.Combine(_root, "secret.dat"), """{ "_customData": { "_bookmarks": [ { "_time": 1, "_name": "leak" } ] } }""");
        File.WriteAllText(Path.Combine(map, "Info.dat"), """
            { "_difficultyBeatmapSets": [ { "_beatmapCharacteristicName": "Standard", "_difficultyBeatmaps": [
              { "_difficulty": "Hard", "_beatmapFilename": "../secret.dat" } ] } ] }
            """);

        var notes = new List<string>();
        var result = MapBookmarkReader.Read(map, notes);
        Assert.Empty(Assert.Single(result).Bookmarks);
        Assert.Contains(notes, n => n.Contains("outside the map folder"));
    }
}
