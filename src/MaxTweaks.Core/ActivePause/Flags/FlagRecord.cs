using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace MaxTweaks.Core.ActivePause.Flags;

/// <summary>One saved flag: a moment of the map, its feedback labels and the selected notes.</summary>
public sealed class FlagRecord
{
    public string Id { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public string Characteristic { get; set; } = "Standard";

    public string Difficulty { get; set; } = string.Empty;

    /// <summary>Song seconds.</summary>
    public float SongTime { get; set; }

    /// <summary>Map beat at <see cref="SongTime"/>.</summary>
    public float Beat { get; set; }

    /// <summary>Practice speed multiplier.</summary>
    public float Speed { get; set; } = 1f;

    public IReadOnlyList<string> Labels { get; set; } = Array.Empty<string>();

    public IReadOnlyList<FlagNote> Notes { get; set; } = Array.Empty<FlagNote>();

    /// <summary>Screenshot path relative to the map folder ("flags/&lt;id&gt;.png"), or null.</summary>
    public string? Screenshot { get; set; }

    /// <summary>
    /// The JSON object this record was read from (null for a new record). A parsed record is written
    /// back exactly as read, so keys this version does not know survive.
    /// </summary>
    internal JObject? Source { get; set; }
}
