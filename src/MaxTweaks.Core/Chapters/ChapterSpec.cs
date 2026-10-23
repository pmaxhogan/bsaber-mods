using System.Collections.Generic;

namespace MaxTweaks.Core.Chapters;

/// <summary>
/// One chapter as written in chapters.json, before beats are converted and the song length is known.
/// Exactly one of the two position styles is set: seconds (<see cref="Start"/>/<see cref="End"/>) or
/// beats (<see cref="StartBeat"/>/<see cref="EndBeat"/>). A missing end means "until the song ends".
/// </summary>
public sealed class ChapterSpec
{
    public ChapterSpec(int index, string? label, float? start, float? end, float? startBeat, float? endBeat, IReadOnlyList<string>? difficulties)
    {
        Index = index;
        Label = label;
        Start = start;
        End = end;
        StartBeat = startBeat;
        EndBeat = endBeat;
        Difficulties = difficulties;
    }

    /// <summary>Zero-based position in the file's "chapters" array (used in log messages and as a tie-break).</summary>
    public int Index { get; }

    public string? Label { get; }

    public float? Start { get; }

    public float? End { get; }

    public float? StartBeat { get; }

    public float? EndBeat { get; }

    /// <summary>Difficulty filter entries as written, or null when the chapter applies to every difficulty.</summary>
    public IReadOnlyList<string>? Difficulties { get; }

    public bool UsesBeats => StartBeat.HasValue;
}

/// <summary>The parsed content of a chapters.json file plus everything that was skipped and why.</summary>
public sealed class ChaptersDocument
{
    public ChaptersDocument(IReadOnlyList<ChapterSpec> chapters, IReadOnlyList<string> warnings)
    {
        Chapters = chapters;
        Warnings = warnings;
    }

    public IReadOnlyList<ChapterSpec> Chapters { get; }

    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>A chapter resolved for one difficulty: times in song seconds, half-open [Start, End).</summary>
public sealed class Chapter
{
    public Chapter(int index, string label, float start, float end)
    {
        Index = index;
        Label = label;
        Start = start;
        End = end;
    }

    /// <summary>Position in the source file.</summary>
    public int Index { get; }

    public string Label { get; }

    public float Start { get; }

    public float End { get; }

    public float Duration => End - Start;

    public bool Contains(float time) => time >= Start && time < End;

    /// <summary>Progress through the chapter in [0, 1].</summary>
    public float Progress(float time)
    {
        if (time <= Start)
        {
            return 0f;
        }

        return time >= End ? 1f : (time - Start) / (End - Start);
    }

    public override string ToString() => $"#{Index} '{Label}' [{TimeText.Format(Start)}, {TimeText.Format(End)})";
}
