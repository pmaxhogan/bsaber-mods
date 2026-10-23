using System.Collections.Generic;
using System.Text;

namespace MaxTweaks.Core.Chapters;

/// <summary>
/// Matches a chapter's "difficulties" entries against the difficulty being played. Each entry is one of:
/// a difficulty ("Expert", "ExpertPlus", "Expert+"), a characteristic ("Lawless", "OneSaber"), or both
/// as "Characteristic/Difficulty" or "Characteristic:Difficulty" ("Standard/Expert+"). Comparison
/// ignores case, spaces, '-' and '_', and treats "+" as "Plus".
/// </summary>
public static class DifficultyFilter
{
    private static readonly HashSet<string> DifficultyNames = new() { "easy", "normal", "hard", "expert", "expertplus" };

    /// <param name="filters">The chapter's entries, or null for "every difficulty".</param>
    /// <param name="characteristic">Serialized characteristic name of the played map, e.g. "Standard".</param>
    /// <param name="difficulty">Difficulty name, e.g. "ExpertPlus".</param>
    public static bool Matches(IReadOnlyList<string>? filters, string characteristic, string difficulty)
    {
        if (filters == null || filters.Count == 0)
        {
            return true;
        }

        var playedCharacteristic = Normalize(characteristic);
        var playedDifficulty = Normalize(difficulty);
        foreach (var filter in filters)
        {
            if (EntryMatches(filter, playedCharacteristic, playedDifficulty))
            {
                return true;
            }
        }

        return false;
    }

    public static string Normalize(string? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 4);
        foreach (var c in value)
        {
            if (c == '+')
            {
                builder.Append("plus");
            }
            else if (c != ' ' && c != '-' && c != '_')
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static bool EntryMatches(string entry, string playedCharacteristic, string playedDifficulty)
    {
        var separator = entry.IndexOfAny(new[] { '/', ':' });
        if (separator >= 0)
        {
            return Normalize(entry.Substring(0, separator)) == playedCharacteristic &&
                   Normalize(entry.Substring(separator + 1)) == playedDifficulty;
        }

        var normalized = Normalize(entry);
        return DifficultyNames.Contains(normalized) ? normalized == playedDifficulty : normalized == playedCharacteristic;
    }
}
