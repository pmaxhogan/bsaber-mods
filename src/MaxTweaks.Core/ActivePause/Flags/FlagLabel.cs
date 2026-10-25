using System;
using System.Collections.Generic;

namespace MaxTweaks.Core.ActivePause.Flags;

/// <summary>Column of a feedback label on the flag panel.</summary>
public enum FlagGroup
{
    Good,
    Bad,
    Note,
}

/// <summary>
/// One tap-to-toggle feedback label of an active-pause flag: a stable snake_case id (written to
/// flags.json, consumed by the automapper), the button text and its column.
/// </summary>
public sealed class FlagLabel
{
    private FlagLabel(string id, string text, FlagGroup group)
    {
        Id = id;
        Text = text;
        Group = group;
    }

    /// <summary>Every label in panel order: Good (5), Bad (8), Note (4).</summary>
    public static IReadOnlyList<FlagLabel> All { get; } = new[]
    {
        new FlagLabel("good_pattern", "Good pattern", FlagGroup.Good),
        new FlagLabel("good_difficulty", "Good difficulty", FlagGroup.Good),
        new FlagLabel("good_timing", "Good timing", FlagGroup.Good),
        new FlagLabel("good_flow", "Good flow", FlagGroup.Good),
        new FlagLabel("fun", "Fun", FlagGroup.Good),
        new FlagLabel("too_easy", "Too easy", FlagGroup.Bad),
        new FlagLabel("too_hard", "Too hard", FlagGroup.Bad),
        new FlagLabel("timed_poorly", "Timed poorly", FlagGroup.Bad),
        new FlagLabel("impossible_pattern", "Impossible pattern", FlagGroup.Bad),
        new FlagLabel("bad_flow", "Bad flow", FlagGroup.Bad),
        new FlagLabel("awkward_hands", "Awkward hands", FlagGroup.Bad),
        new FlagLabel("too_dense", "Too dense", FlagGroup.Bad),
        new FlagLabel("too_sparse", "Too sparse", FlagGroup.Bad),
        new FlagLabel("repetitive", "Repetitive", FlagGroup.Note),
        new FlagLabel("surprising", "Surprising", FlagGroup.Note),
        new FlagLabel("mismatches_music", "Doesn't match the music", FlagGroup.Note),
        new FlagLabel("check_other_difficulties", "Check other difficulties", FlagGroup.Note),
    };

    public string Id { get; }

    public string Text { get; }

    public FlagGroup Group { get; }

    /// <summary>The label with this id (exact, case-sensitive), or null.</summary>
    public static FlagLabel? Find(string? id)
    {
        if (id == null)
        {
            return null;
        }

        foreach (var label in All)
        {
            if (string.Equals(label.Id, id, StringComparison.Ordinal))
            {
                return label;
            }
        }

        return null;
    }

    public override string ToString() => Id;
}
