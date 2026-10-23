using System;
using System.Collections.Generic;
using System.Linq;

namespace MaxTweaks.Core.Debugging;

/// <summary>A level the debug channel can pick: its id, its folder name (custom levels) and song name.</summary>
public sealed class LevelCandidate
{
    public LevelCandidate(string levelId, string? folderName, string songName)
    {
        LevelId = levelId;
        FolderName = folderName;
        SongName = songName;
    }

    public string LevelId { get; }

    public string? FolderName { get; }

    public string SongName { get; }

    public override string ToString() => FolderName != null ? $"{LevelId} ({FolderName})" : $"{LevelId} ({SongName})";
}

/// <summary>
/// Resolves a free-text level query. Tiers, first tier with any match wins: exact level id,
/// exact folder name, exact song name, folder-name substring, song-name substring (all
/// case-insensitive). More than one match in the winning tier is ambiguous.
/// </summary>
public static class LevelMatcher
{
    public static IReadOnlyList<LevelCandidate> Match(IEnumerable<LevelCandidate> candidates, string query)
    {
        var all = candidates.ToList();
        var q = query.Trim();
        var tiers = new Func<LevelCandidate, bool>[]
        {
            c => string.Equals(c.LevelId, q, StringComparison.OrdinalIgnoreCase),
            c => c.FolderName != null && string.Equals(c.FolderName, q, StringComparison.OrdinalIgnoreCase),
            c => string.Equals(c.SongName, q, StringComparison.OrdinalIgnoreCase),
            c => c.FolderName != null && c.FolderName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0,
            c => c.SongName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0,
        };

        foreach (var tier in tiers)
        {
            // Distinct by id: the same level can be listed from several sources.
            var hits = all.Where(tier).GroupBy(c => c.LevelId).Select(g => g.First()).ToList();
            if (hits.Count > 0)
            {
                return hits;
            }
        }

        return Array.Empty<LevelCandidate>();
    }
}
