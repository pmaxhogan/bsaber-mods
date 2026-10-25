using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MaxTweaks.Core.ActivePause.Flags;

/// <summary>flags.json could not be read as a flag document (malformed JSON, wrong shape).</summary>
public sealed class FlagDocumentException : Exception
{
    public FlagDocumentException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// The flags.json sidecar next to a map's Info.dat (format in docs/flags.md):
/// <c>{ "version": 1, "levelId": ..., "songName": ..., "flags": [ ... ] }</c>.
///
/// The mod only appends. A parsed document keeps every key it does not know, at the document level
/// and inside each existing flag (existing flags, even ones it cannot read, are written back exactly
/// as read), so files edited by hand or by newer tools survive a save.
/// </summary>
public sealed class FlagDocument
{
    public const int CurrentVersion = 1;

    private readonly JObject _root;

    // Every entry of the "flags" array in order (unknown shapes included); appended flags at the end.
    private readonly List<JToken?> _entries = new();

    private FlagDocument(JObject root)
    {
        _root = root;
    }

    public int Version { get; set; } = CurrentVersion;

    public string LevelId { get; set; } = string.Empty;

    public string SongName { get; set; } = string.Empty;

    /// <summary>The flags that could be read, in file order (entries of unknown shape are skipped here but kept on save).</summary>
    public List<FlagRecord> Flags { get; } = new();

    public static FlagDocument Empty(string levelId, string songName) =>
        new(new JObject())
        {
            LevelId = levelId ?? string.Empty,
            SongName = songName ?? string.Empty,
        };

    /// <exception cref="FlagDocumentException">Malformed JSON, a root that is not an object, or "flags" that is not an array.</exception>
    public static FlagDocument Parse(string json)
    {
        if (json == null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        JToken token;
        try
        {
            using var reader = new JsonTextReader(new StringReader(json))
            {
                // Keep date strings and numbers as written: existing flags are written back as read.
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
            };
            token = JToken.ReadFrom(reader);
            if (reader.Read())
            {
                throw new FlagDocumentException("unexpected content after the JSON value");
            }
        }
        catch (JsonException e)
        {
            throw new FlagDocumentException("not valid JSON: " + e.Message, e);
        }

        if (token is not JObject root)
        {
            throw new FlagDocumentException($"the root is a {token.Type}, expected an object");
        }

        var document = new FlagDocument(root)
        {
            Version = root["version"] is JValue { Type: JTokenType.Integer } version ? version.Value<int>() : CurrentVersion,
            LevelId = AsString(root["levelId"]) ?? string.Empty,
            SongName = AsString(root["songName"]) ?? string.Empty,
        };

        var flags = root["flags"];
        if (flags == null || flags.Type == JTokenType.Null)
        {
            return document;
        }

        if (flags is not JArray array)
        {
            throw new FlagDocumentException($"\"flags\" is a {flags.Type}, expected an array");
        }

        foreach (var entry in array)
        {
            document._entries.Add(entry);
            if (entry is JObject obj)
            {
                document.Flags.Add(ReadFlag(obj));
            }
        }

        return document;
    }

    public void Append(FlagRecord flag)
    {
        if (flag == null)
        {
            throw new ArgumentNullException(nameof(flag));
        }

        Flags.Add(flag);
        _entries.Add(null); // written from the record
    }

    /// <summary>Indented JSON, LF line endings, invariant culture.</summary>
    public string Serialize()
    {
        var root = (JObject)_root.DeepClone();
        root["version"] = Version;
        root["levelId"] = LevelId;
        root["songName"] = SongName;

        var array = new JArray();
        var appended = 0;
        var newFlags = new List<FlagRecord>();
        foreach (var flag in Flags)
        {
            if (flag.Source == null)
            {
                newFlags.Add(flag);
            }
        }

        foreach (var entry in _entries)
        {
            array.Add(entry != null ? entry.DeepClone() : WriteFlag(newFlags[appended++]));
        }

        root["flags"] = array;
        var sw = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        using (var writer = new JsonTextWriter(sw) { Formatting = Formatting.Indented, Indentation = 2 })
        {
            root.WriteTo(writer);
        }

        return sw.ToString() + "\n";
    }

    private static string? AsString(JToken? token) =>
        token is JValue { Type: JTokenType.String } value ? (string?)value.Value : null;

    private static float AsFloat(JToken? token) =>
        token is JValue { Type: JTokenType.Float or JTokenType.Integer } value
            ? Convert.ToSingle(value.Value, CultureInfo.InvariantCulture)
            : 0f;

    private static int AsInt(JToken? token) =>
        token is JValue { Type: JTokenType.Integer or JTokenType.Float } value
            ? (int)Math.Round(Convert.ToDouble(value.Value, CultureInfo.InvariantCulture))
            : 0;

    private static FlagRecord ReadFlag(JObject obj)
    {
        var labels = new List<string>();
        if (obj["labels"] is JArray labelArray)
        {
            foreach (var label in labelArray)
            {
                var text = AsString(label);
                if (text != null)
                {
                    labels.Add(text);
                }
            }
        }

        var notes = new List<FlagNote>();
        if (obj["notes"] is JArray noteArray)
        {
            foreach (var item in noteArray)
            {
                if (item is JObject note && FlagNote.TryParseKind(AsString(note["kind"]), out var kind))
                {
                    notes.Add(new FlagNote(
                        kind,
                        AsFloat(note["beat"]),
                        AsFloat(note["time"]),
                        AsInt(note["lineIndex"]),
                        AsInt(note["lineLayer"]),
                        AsString(note["color"]) ?? FlagNote.NoColor,
                        AsString(note["cutDirection"]) ?? FlagNote.NoDirection));
                }
            }
        }

        var created = DateTime.MinValue;
        var createdText = AsString(obj["createdAt"]);
        if (createdText != null)
        {
            DateTime.TryParse(createdText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out created);
        }

        return new FlagRecord
        {
            Id = AsString(obj["id"]) ?? string.Empty,
            CreatedAtUtc = created,
            Characteristic = AsString(obj["characteristic"]) ?? string.Empty,
            Difficulty = AsString(obj["difficulty"]) ?? string.Empty,
            SongTime = AsFloat(obj["songTime"]),
            Beat = AsFloat(obj["beat"]),
            Speed = AsFloat(obj["speed"]),
            Labels = labels,
            Notes = notes,
            Screenshot = AsString(obj["screenshot"]),
            Source = obj,
        };
    }

    private static JObject WriteFlag(FlagRecord flag)
    {
        var notes = new JArray();
        foreach (var note in flag.Notes)
        {
            notes.Add(new JObject
            {
                ["kind"] = FlagNote.KindId(note.Kind),
                ["beat"] = Round(note.Beat),
                ["time"] = Round(note.Time),
                ["lineIndex"] = note.LineIndex,
                ["lineLayer"] = note.LineLayer,
                ["color"] = note.Color,
                ["cutDirection"] = note.CutDirection,
            });
        }

        return new JObject
        {
            ["id"] = flag.Id,
            ["createdAt"] = flag.CreatedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["characteristic"] = flag.Characteristic,
            ["difficulty"] = flag.Difficulty,
            ["songTime"] = Round(flag.SongTime),
            ["beat"] = Round(flag.Beat),
            ["speed"] = Round(flag.Speed),
            ["labels"] = new JArray(flag.Labels),
            ["notes"] = notes,
            ["screenshot"] = flag.Screenshot != null ? new JValue(flag.Screenshot) : JValue.CreateNull(),
        };
    }

    // Milliseconds / thousandths are plenty; a float keeps "42.317" short in the file (a double
    // widened from a float would print 42.31700134277344).
    private static JValue Round(float value) => new((float)Math.Round(value, 3, MidpointRounding.AwayFromZero));
}
