using System.IO;
using MaxTweaks.Common;
using MaxTweaks.Core;
using MaxTweaks.Core.Chapters;

namespace MaxTweaks.Features.Chapters;

/// <summary>Finds and parses chapters.json next to the played custom level's Info.dat.</summary>
internal static class ChaptersLoader
{
    public const string FileName = "chapters.json";

    /// <returns>The parsed document, or null if the level is not custom or has no chapters file.</returns>
    public static ChaptersDocument? Load(BeatmapLevel? level)
    {
        var folder = CustomLevelFolders.TryGetFolder(level);
        if (folder == null || !SafeMapPaths.TryResolveInside(folder, FileName, out var path) || !File.Exists(path))
        {
            return null;
        }

        var document = ChaptersParser.Parse(File.ReadAllText(path));
        foreach (var warning in document.Warnings)
        {
            Plugin.Log.Warn($"[Chapters] {path}: {warning}");
        }

        Plugin.Log.Info($"[Chapters] loaded {document.Chapters.Count} chapter(s) from {path}");
        return document;
    }
}
