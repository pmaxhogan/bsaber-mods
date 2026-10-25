using System;
using System.Globalization;
using System.Text;

namespace MaxTweaks.Core.ActivePause.Flags;

/// <summary>What kind of beatmap object a flagged note is.</summary>
public enum FlagNoteKind
{
    Note,
    Bomb,
    ChainHead,
    ChainLink,
}

/// <summary>
/// The map data that identifies a note (kind, time, line index, layer, colour, cut direction), so
/// a note stays the same selection when a seek despawns and respawns it. Equality uses the time
/// rounded to milliseconds and ignores <see cref="Beat"/> (derived from the time).
/// </summary>
public sealed class FlagNote : IEquatable<FlagNote>
{
    public const string Red = "red";
    public const string Blue = "blue";
    public const string NoColor = "none";
    public const string NoDirection = "none";

    public FlagNote(FlagNoteKind kind, float beat, float time, int lineIndex, int lineLayer, string color, string cutDirection)
    {
        Kind = kind;
        Beat = beat;
        Time = time;
        LineIndex = lineIndex;
        LineLayer = lineLayer;
        Color = color ?? NoColor;
        CutDirection = cutDirection ?? NoDirection;
    }

    public FlagNoteKind Kind { get; }

    /// <summary>Map beat (BPM changes included).</summary>
    public float Beat { get; }

    /// <summary>Song seconds.</summary>
    public float Time { get; }

    public int LineIndex { get; }

    public int LineLayer { get; }

    /// <summary><c>red</c>, <c>blue</c> or <c>none</c> (bombs).</summary>
    public string Color { get; }

    /// <summary><c>up</c>, <c>down</c>, <c>left</c>, <c>right</c>, <c>up_left</c>, ..., <c>any</c>, <c>none</c>.</summary>
    public string CutDirection { get; }

    private long TimeKey => (long)Math.Round(Time * 1000.0);

    /// <summary><c>note</c>, <c>bomb</c>, <c>chain_head</c> or <c>chain_link</c>.</summary>
    public static string KindId(FlagNoteKind kind) => kind switch
    {
        FlagNoteKind.Note => "note",
        FlagNoteKind.Bomb => "bomb",
        FlagNoteKind.ChainHead => "chain_head",
        FlagNoteKind.ChainLink => "chain_link",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static bool TryParseKind(string? id, out FlagNoteKind kind)
    {
        switch (id)
        {
            case "note":
                kind = FlagNoteKind.Note;
                return true;
            case "bomb":
                kind = FlagNoteKind.Bomb;
                return true;
            case "chain_head":
                kind = FlagNoteKind.ChainHead;
                return true;
            case "chain_link":
                kind = FlagNoteKind.ChainLink;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    /// <summary>
    /// Snake case of an enum member name: "UpLeft" -> "up_left", "Any" -> "any"; empty -> "none".
    /// Used for the game's NoteCutDirection names.
    /// </summary>
    public static string SnakeCase(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return NoDirection;
        }

        var sb = new StringBuilder(name!.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    public bool Equals(FlagNote? other) =>
        other is not null
        && Kind == other.Kind
        && LineIndex == other.LineIndex
        && LineLayer == other.LineLayer
        && string.Equals(Color, other.Color, StringComparison.Ordinal)
        && string.Equals(CutDirection, other.CutDirection, StringComparison.Ordinal)
        && TimeKey == other.TimeKey;

    public override bool Equals(object? obj) => Equals(obj as FlagNote);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)Kind;
            hash = (hash * 397) ^ LineIndex;
            hash = (hash * 397) ^ LineLayer;
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Color);
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(CutDirection);
            hash = (hash * 397) ^ TimeKey.GetHashCode();
            return hash;
        }
    }

    public override string ToString() =>
        $"{KindId(Kind)} {Color} {CutDirection} at {Time.ToString("0.000", CultureInfo.InvariantCulture)}s ({LineIndex},{LineLayer})";
}
