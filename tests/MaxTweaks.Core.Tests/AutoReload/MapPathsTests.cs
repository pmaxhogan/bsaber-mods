using System;
using MaxTweaks.Core.AutoReload;
using Xunit;

namespace MaxTweaks.Core.Tests.AutoReload;

public class MapPathsTests
{
    private const string Root = @"C:\BS\Beat Saber_Data\CustomLevels";

    [Theory]
    [InlineData(@"C:\a\b\", @"C:\a\b")]
    [InlineData(@"C:/a/b//", @"C:\a\b")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:", @"C:")]
    [InlineData(@"C:\a", @"C:\a")]
    [InlineData(@"", @"")]
    public void Normalize_UnifiesSeparatorsAndTrims(string input, string expected)
    {
        Assert.Equal(expected, MapPaths.Normalize(input));
    }

    [Fact]
    public void Normalize_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => MapPaths.Normalize(null!));
    }

    [Fact]
    public void FolderKey_JoinsWithSingleSeparator()
    {
        Assert.Equal(@"C:\x\Map", MapPaths.FolderKey(@"C:\x\", "Map"));
        Assert.Equal(@"C:\x\Map", MapPaths.FolderKey("C:/x", "Map"));
        Assert.Equal(@"C:\Map", MapPaths.FolderKey(@"C:\", "Map"));
    }

    [Theory]
    [InlineData(Root + @"\1a2b (Song - Mapper)", "1a2b (Song - Mapper)")]
    [InlineData(Root + @"\1a2b (Song - Mapper)\Info.dat", "1a2b (Song - Mapper)")]
    [InlineData(Root + @"\Map\ExpertPlusStandard.dat", "Map")]
    [InlineData(@"c:\bs\beat saber_data\customlevels\Map\song.ogg", "Map")]
    [InlineData(@"C:/BS/Beat Saber_Data/CustomLevels/Map/Info.dat", "Map")]
    public void TryGetMapFolderName_AcceptsFolderAndTopLevelFiles(string path, string expected)
    {
        Assert.True(MapPaths.TryGetMapFolderName(Root, path, null, out var name));
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData(Root)]
    [InlineData(Root + @"\")]
    [InlineData(Root + @"\Map\autosaves\Expert.dat")]
    [InlineData(Root + @"\Map\Backups\x\Info.dat")]
    [InlineData(@"C:\BS\Beat Saber_Data\CustomLevelsOther\Map")]
    [InlineData(@"C:\Elsewhere\Map")]
    [InlineData(@"C:\BS")]
    [InlineData("")]
    public void TryGetMapFolderName_RejectsRootDeepAndOutsidePaths(string path)
    {
        Assert.False(MapPaths.TryGetMapFolderName(Root, path, null, out var name));
        Assert.Equal(string.Empty, name);
    }

    [Fact]
    public void TryGetMapFolderName_RejectsEmptyRoot()
    {
        Assert.False(MapPaths.TryGetMapFolderName("", @"C:\x\Map", null, out _));
    }

    [Fact]
    public void TryGetMapFolderName_SkipsIgnoredFolders()
    {
        const string wip = @"C:\BS\Beat Saber_Data\CustomWIPLevels";
        var ignored = new[] { "Cache" };
        Assert.False(MapPaths.TryGetMapFolderName(wip, wip + @"\cache\song.zip", ignored, out _));
        Assert.False(MapPaths.TryGetMapFolderName(wip, wip + @"\Cache", ignored, out _));
        Assert.True(MapPaths.TryGetMapFolderName(wip, wip + @"\MyWip\Info.dat", ignored, out var name));
        Assert.Equal("MyWip", name);
    }

    [Fact]
    public void TryGetMapFolderName_SkipsSubfolderEntriesOfAMap()
    {
        // A write into Map\autosaves\ raises a change for the autosaves folder itself.
        const string autosaves = Root + @"\Map\autosaves";
        const string info = Root + @"\Map\Info.dat";
        static bool IsDirectory(string path) => path == autosaves;
        Assert.False(MapPaths.TryGetMapFolderName(Root, autosaves, null, IsDirectory, out var name));
        Assert.Equal(string.Empty, name);
        Assert.True(MapPaths.TryGetMapFolderName(Root, info, null, IsDirectory, out name));
        Assert.Equal("Map", name);

        // The map folder itself is a directory too, and must still count.
        Assert.True(MapPaths.TryGetMapFolderName(Root, Root + @"\Map", null, _ => true, out name));
        Assert.Equal("Map", name);

        // Without the predicate, or once the subfolder is gone, it counts as a change to the map.
        Assert.True(MapPaths.TryGetMapFolderName(Root, autosaves, null, out _));
        Assert.True(MapPaths.TryGetMapFolderName(Root, autosaves, null, _ => false, out _));
    }

    [Fact]
    public void TryGetMapFolderName_WorksWithDriveRoot()
    {
        Assert.True(MapPaths.TryGetMapFolderName(@"D:\", @"D:\Map\Info.dat", null, out var name));
        Assert.Equal("Map", name);
    }

    [Fact]
    public void Comparer_IsCaseInsensitive()
    {
        Assert.True(MapPaths.Comparer.Equals(@"C:\A\Map", @"c:\a\map"));
    }
}
