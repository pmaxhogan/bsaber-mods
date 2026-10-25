using System;
using System.Collections.Generic;
using MaxTweaks.Core.ActivePause;
using MaxTweaks.Core.ActivePause.Flags;
using MaxTweaks.Core.Chapters;
using UnityEngine;

namespace MaxTweaks.Features.ActivePause;

/// <summary>
/// Notes the player selected for a flag, by map data (<see cref="FlagNote"/>), so a note stays
/// selected when a seek or resume respawns it. Targets: colour notes, bombs, chain heads and chain
/// links (walls and arcs are not selectable). Picking is a ray test against a sphere of
/// <see cref="HitRadius"/> around each live note's visual centre; the nearest hit wins.
/// </summary>
internal sealed class NoteSelection
{
    public const float HitRadius = 0.28f;

    // Spec vocabulary per NoteCutDirection value (Up..None), built once.
    private static readonly string[] CutDirectionIds = BuildCutDirectionIds();

    private readonly List<FlagNote> _selected = new();
    private readonly HashSet<FlagNote> _selectedSet = new();

    /// <summary>Selected notes in selection order.</summary>
    public IReadOnlyList<FlagNote> Selected => _selected;

    public int Count => _selected.Count;

    /// <summary>The note the saber points at (only while active-paused), or null.</summary>
    public NoteController? HoveredNote { get; private set; }

    public FlagNote? Hovered => HoveredNote != null ? ToFlagNote(HoveredNote, null) : null;

    public static bool IsSelectable(IBeatmapObjectController item, out NoteController note)
    {
        note = (item as NoteController)!;
        return note != null && note.gameObject.activeInHierarchy && !note.hidden && note.noteData != null;
    }

    /// <summary>Nearest selectable note hit by <paramref name="ray"/>, or null.</summary>
    public static NoteController? HitTest(Ray ray, IReadOnlyList<IBeatmapObjectController> live)
    {
        NoteController? best = null;
        var bestDistance = float.MaxValue;
        var o = ray.origin;
        var d = ray.direction;
        for (var i = 0; i < live.Count; i++)
        {
            if (!IsSelectable(live[i], out var note))
            {
                continue;
            }

            var c = note.noteTransform.position;
            if (RayMath.RaySphere(o.x, o.y, o.z, d.x, d.y, d.z, c.x, c.y, c.z, HitRadius, out var distance)
                && distance < bestDistance)
            {
                best = note;
                bestDistance = distance;
            }
        }

        return best;
    }

    public void SetHovered(NoteController? note) => HoveredNote = note;

    public bool IsSelected(NoteController note) => _selected.Count > 0 && _selectedSet.Contains(ToFlagNote(note, null));

    public bool IsSelected(FlagNote note) => _selectedSet.Contains(note);

    /// <summary>Selects or deselects <paramref name="note"/>; returns its key and whether it is now selected.</summary>
    public (FlagNote Note, bool Selected) Toggle(NoteController note, BpmTimeline? tempo)
    {
        var key = ToFlagNote(note, tempo);
        if (_selectedSet.Remove(key))
        {
            _selected.Remove(key);
            return (key, false);
        }

        _selectedSet.Add(key);
        _selected.Add(key);
        return (key, true);
    }

    public void Clear()
    {
        _selected.Clear();
        _selectedSet.Clear();
        HoveredNote = null;
    }

    /// <summary>The map data of <paramref name="note"/> in the flags.json vocabulary.</summary>
    public static FlagNote ToFlagNote(NoteController note, BpmTimeline? tempo)
    {
        var data = note.noteData;
        var kind = data.gameplayType switch
        {
            NoteData.GameplayType.Bomb => FlagNoteKind.Bomb,
            NoteData.GameplayType.BurstSliderHead => FlagNoteKind.ChainHead,
            NoteData.GameplayType.BurstSliderElement => FlagNoteKind.ChainLink,
            _ => FlagNoteKind.Note,
        };
        var color = data.colorType switch
        {
            ColorType.ColorA => FlagNote.Red,
            ColorType.ColorB => FlagNote.Blue,
            _ => FlagNote.NoColor,
        };
        var direction = (int)data.cutDirection;
        var cut = direction >= 0 && direction < CutDirectionIds.Length ? CutDirectionIds[direction] : FlagNote.SnakeCase(data.cutDirection.ToString());
        var time = data.time;
        var beat = tempo != null ? tempo.SecondsToBeat(time) : time * StepMath.FallbackBpm / 60f;
        return new FlagNote(kind, beat, time, data.lineIndex, (int)data.noteLineLayer, color, cut);
    }

    private static string[] BuildCutDirectionIds()
    {
        var values = (NoteCutDirection[])Enum.GetValues(typeof(NoteCutDirection));
        var max = 0;
        foreach (var value in values)
        {
            max = Math.Max(max, (int)value);
        }

        var ids = new string[max + 1];
        foreach (var value in values)
        {
            ids[(int)value] = FlagNote.SnakeCase(value.ToString());
        }

        return ids;
    }
}
