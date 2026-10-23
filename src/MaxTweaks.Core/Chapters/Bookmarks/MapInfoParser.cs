using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace MaxTweaks.Core.Chapters.Bookmarks;

/// <summary>One playable difficulty listed in Info.dat.</summary>
public sealed class MapDifficulty
{
    public MapDifficulty(string characteristic, string difficulty, string? beatmapFileName)
    {
        Characteristic = characteristic;
        Difficulty = difficulty;
        BeatmapFileName = beatmapFileName;
    }

    /// <summary>Serialized characteristic name, e.g. "Standard".</summary>
    public string Characteristic { get; }

    /// <summary>Difficulty name, e.g. "ExpertPlus".</summary>
    public string Difficulty { get; }

    /// <summary>Difficulty file relative to the map folder, if listed.</summary>
    public string? BeatmapFileName { get; }
}

/// <summary>Lists the difficulties of an Info.dat (v2/v3 "_difficultyBeatmapSets" or v4 "difficultyBeatmaps").</summary>
public static class MapInfoParser
{
    /// <returns>The difficulties, or null if the text is not a recognisable Info.dat.</returns>
    public static List<MapDifficulty>? Parse(string? json)
    {
        if (BookmarkParser.TryRead(json) is not JObject root)
        {
            return null;
        }

        var result = new List<MapDifficulty>();
        if (ChaptersParser.GetProperty(root, "_difficultyBeatmapSets") is JArray sets)
        {
            foreach (var setToken in sets)
            {
                if (setToken is not JObject set ||
                    ChaptersParser.GetProperty(set, "_difficultyBeatmaps") is not JArray beatmaps)
                {
                    continue;
                }

                var characteristic = Text(set, "_beatmapCharacteristicName") ?? "Standard";
                foreach (var beatmapToken in beatmaps)
                {
                    if (beatmapToken is JObject beatmap && Text(beatmap, "_difficulty") is { } difficulty)
                    {
                        result.Add(new MapDifficulty(characteristic, difficulty, Text(beatmap, "_beatmapFilename")));
                    }
                }
            }

            return result;
        }

        if (ChaptersParser.GetProperty(root, "difficultyBeatmaps") is JArray v4)
        {
            foreach (var beatmapToken in v4)
            {
                if (beatmapToken is JObject beatmap && Text(beatmap, "difficulty") is { } difficulty)
                {
                    result.Add(new MapDifficulty(
                        Text(beatmap, "characteristic") ?? "Standard",
                        difficulty,
                        Text(beatmap, "beatmapDataFilename")));
                }
            }

            return result;
        }

        return null;
    }

    private static string? Text(JObject obj, string name)
    {
        var token = ChaptersParser.GetProperty(obj, name);
        var value = token != null && token.Type == JTokenType.String ? ((string?)token)?.Trim() : null;
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
