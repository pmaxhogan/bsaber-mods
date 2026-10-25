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
/// </summary>
internal sealed class ObjectGhoster
{
    /// <summary>Cutout while ghosted (0 = solid, 1 = gone).</summary>
    public const float GhostCutout = 0.75f;

    private readonly Dictionary<IBeatmapObjectController, Entry[]> _tracked = new();

    public int TrackedCount => _tracked.Count;

    /// <summary>Ghosts every object in <paramref name="live"/> (new ones are tracked first).</summary>
    public void Apply(IReadOnlyList<IBeatmapObjectController> live)
    {
        for (var i = 0; i < live.Count; i++)
        {
            var item = live[i];
            if (!_tracked.TryGetValue(item, out var entries))
            {
                entries = Track(item);
                _tracked[item] = entries;
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
