using System.Collections.Generic;

namespace MaxTweaks.Core.Chapters;

/// <summary>
/// Turns the parsed file into the chapters that apply to one played difficulty: filters by
/// "difficulties", converts beats with the map's tempo map, fills a missing end with the song end and
/// validates. Invalid chapters are skipped with a warning; nothing here throws on bad data.
/// </summary>
public static class ChapterResolver
{
    /// <summary>Chapters shorter than this are rejected.</summary>
    public const float MinDurationSeconds = 1f;

    /// <param name="document">Parsed chapters.json.</param>
    /// <param name="tempo">Tempo map of the played difficulty; null if unknown (beat chapters are then skipped).</param>
    /// <param name="characteristic">Serialized characteristic name of the played map.</param>
    /// <param name="difficulty">Difficulty name of the played map.</param>
    /// <param name="songEndSeconds">Song end in seconds; non-finite or non-positive if unknown.</param>
    /// <param name="warnings">Receives one line per skipped or adjusted chapter.</param>
    public static List<Chapter> Resolve(
        ChaptersDocument document,
        BpmTimeline? tempo,
        string characteristic,
        string difficulty,
        float songEndSeconds,
        List<string> warnings)
    {
        var songEndKnown = songEndSeconds > 0 && !float.IsNaN(songEndSeconds) && !float.IsInfinity(songEndSeconds);
        var result = new List<Chapter>();
        foreach (var spec in document.Chapters)
        {
            if (!DifficultyFilter.Matches(spec.Difficulties, characteristic, difficulty))
            {
                continue;
            }

            var label = spec.Label ?? $"Chapter {spec.Index + 1}";
            var where = $"chapter #{spec.Index + 1} ('{label}')";

            float start;
            float? end;
            if (spec.UsesBeats)
            {
                if (tempo == null)
                {
                    warnings.Add($"{where}: uses beats but the map BPM is unknown, skipped");
                    continue;
                }

                start = tempo.BeatToSeconds(spec.StartBeat!.Value);
                end = spec.EndBeat.HasValue ? tempo.BeatToSeconds(spec.EndBeat.Value) : null;
            }
            else
            {
                start = spec.Start!.Value;
                end = spec.End;
            }

            if (!end.HasValue)
            {
                if (!songEndKnown)
                {
                    warnings.Add($"{where}: has no end and the song length is unknown, skipped");
                    continue;
                }

                end = songEndSeconds;
            }

            if (songEndKnown && start >= songEndSeconds)
            {
                warnings.Add($"{where}: starts at {TimeText.Format(start)}, after the song ends ({TimeText.Format(songEndSeconds)}), skipped");
                continue;
            }

            if (end.Value <= start)
            {
                warnings.Add($"{where}: end {TimeText.Format(end.Value)} is not after start {TimeText.Format(start)}, skipped");
                continue;
            }

            if (songEndKnown && end.Value > songEndSeconds)
            {
                end = songEndSeconds;
            }

            if (end.Value - start < MinDurationSeconds)
            {
                warnings.Add($"{where}: shorter than {MinDurationSeconds:0.#}s ({end.Value - start:0.###}s), skipped");
                continue;
            }

            result.Add(new Chapter(spec.Index, label, start, end.Value));
        }

        return result;
    }
}
