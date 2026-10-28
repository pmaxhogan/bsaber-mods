using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaxTweaks.Features.ActivePause;

/// <summary>
/// Shows live beatmap objects (notes, bombs, chain elements, arcs, walls) mostly dissolved with the
/// game's own dissolve: every object's visuals carry CutoutEffect components (the "_Cutout" noise
/// dissolve that notes, arcs and walls animate when they despawn, set through a
/// MaterialPropertyBlock, so no shared material changes). Each tracked effect keeps the cutout it
/// had and gets it back on <see cref="RestoreAll"/>.
///
/// <see cref="Apply"/> is called every frame while active: it picks up objects spawned since (a step
/// while paused spawns the objects at the target) and re-applies the ghost where the game reset it
/// (a freshly instantiated pool object's CutoutAnimateEffect.Start sets 0 one frame after spawning).
/// Despawned objects need nothing: the game resets the dissolve when it reuses a pooled object.
///
/// Walls are tracked but never ghosted: a partly dissolved obstacle renders as a bright solid
/// slab (the dissolve burn fills its volume) that hides everything behind it, the flag panel
/// included. They keep the game's own translucent look. For a flag screenshot
/// (<see cref="BeginCapture"/>) the notes are shown solid and the walls fully dissolved for one
/// frame, so the pattern is what the image shows.
/// </summary>
internal sealed class ObjectGhoster
{
    /// <summary>Cutout while ghosted (0 = solid, 1 = gone).</summary>
    public const float GhostCutout = 0.55f;

    private readonly Dictionary<IBeatmapObjectController, Entry[]> _tracked = new();

    public int TrackedCount => _tracked.Count;

    /// <summary>
    /// Ghosts every object in <paramref name="live"/> (new ones are tracked first). Objects for which
    /// <paramref name="keepSolid"/> returns true (selected notes) get their previous cutout back
    /// instead, so they stand out at full opacity.
    /// </summary>
    public void Apply(IReadOnlyList<IBeatmapObjectController> live, Func<IBeatmapObjectController, bool>? keepSolid = null)
    {
        for (var i = 0; i < live.Count; i++)
        {
            var item = live[i];
            if (!_tracked.TryGetValue(item, out var entries))
            {
                entries = Track(item);
                _tracked[item] = entries;
            }

            if (item is ObstacleController)
            {
                continue;
            }

            if (keepSolid != null && keepSolid(item))
            {
                foreach (var entry in entries)
                {
                    if (entry.Effect != null && entry.Effect._cutout > entry.Previous && entry.Effect._cutout <= GhostCutout)
                    {
                        entry.Effect.SetCutout(entry.Previous);
                    }
                }

                continue;
            }

            foreach (var entry in entries)
            {
                // A dissolve already further along (a missed note fading out) is left alone.
                if (entry.Effect != null && entry.Effect._cutout < GhostCutout)
                {
                    entry.Effect.SetCutout(GhostCutout);
                }
            }
        }
    }

    /// <summary>
    /// For the flag screenshot frame: every ghosted object back to its previous cutout (solid) and
    /// every wall fully dissolved (invisible). <see cref="EndCapture"/> brings the walls back; the
    /// next <see cref="Apply"/> ghosts the rest again.
    /// </summary>
    public void BeginCapture()
    {
        foreach (var pair in _tracked)
        {
            var wall = pair.Key is ObstacleController;
            foreach (var entry in pair.Value)
            {
                if (entry.Effect == null)
                {
                    continue;
                }

                if (wall)
                {
                    entry.Effect.SetCutout(1f);
                }
                else if (entry.Effect._cutout > entry.Previous && entry.Effect._cutout <= GhostCutout)
                {
                    entry.Effect.SetCutout(entry.Previous);
                }
            }
        }
    }

    /// <summary>Walls back after the screenshot frame.</summary>
    public void EndCapture()
    {
        foreach (var pair in _tracked)
        {
            if (pair.Key is not ObstacleController)
            {
                continue;
            }

            foreach (var entry in pair.Value)
            {
                if (entry.Effect != null)
                {
                    entry.Effect.SetCutout(entry.Previous);
                }
            }
        }
    }

    /// <summary>Gives every tracked effect its previous cutout back and forgets them.</summary>
    public void RestoreAll()
    {
        foreach (var entries in _tracked.Values)
        {
            foreach (var entry in entries)
            {
                if (entry.Effect != null)
                {
                    entry.Effect.SetCutout(entry.Previous);
                }
            }
        }

        _tracked.Clear();
    }

    private static Entry[] Track(IBeatmapObjectController item)
    {
        if (item is not Component component || component == null)
        {
            return System.Array.Empty<Entry>();
        }

        var effects = component.GetComponentsInChildren<CutoutEffect>(true);
        var entries = new Entry[effects.Length];
        for (var i = 0; i < effects.Length; i++)
        {
            entries[i] = new Entry(effects[i], effects[i]._cutout);
        }

        return entries;
    }

    private readonly struct Entry
    {
        public Entry(CutoutEffect effect, float previous)
        {
            Effect = effect;
            Previous = previous;
        }

        public CutoutEffect Effect { get; }

        public float Previous { get; }
    }
}
