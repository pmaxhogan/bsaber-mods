using System;
using System.Collections.Generic;
using MaxTweaks.Common.Playback;
using MaxTweaks.Core.DifficultySwitch;
using MaxTweaks.Common;
using UnityEngine;
using Zenject;

namespace MaxTweaks.Features.DifficultySwitch;

/// <summary>
/// Clears the live objects of the old difficulty after a switch. Notes, bombs and chain elements
/// slide vertically out of view (<see cref="SlideOut"/>) and are then despawned; walls and arcs (and
/// everything when SlideOldNotes is off or the game is paused) are despawned at once.
///
/// A sliding note is paused (NoteController.Pause(true) disables the component, so its movement no
/// longer writes the transform and it can never reach the miss marker) and made uncuttable
/// (CuttableBySaber.canBeCut = false; the game resets that itself when a pooled note is reused), so
/// no cut or miss event can fire. It is despawned through the beatmap object manager without events
/// once the slide ends, or immediately if anything re-enables it (resuming from the pause menu calls
/// Pause(false) on every live object). Pending cut sounds of the old notes are stopped and the
/// ScoreController's pending-note list is cleared (see PlaybackSeeker.ForgetPendingNotes).
/// Pausing during a slide despawns the sliding notes at once (IGamePause.didPauseEvent): otherwise a
/// resume could re-enable a note that then passes the miss marker before the next Tick.
/// </summary>
internal sealed class NoteSlider : IInitializable, IDisposable
{
    private readonly PlaybackSeeker _seeker;
    private readonly IGamePause? _gamePause;
    private readonly List<Sliding> _sliding = new(64);
    private readonly List<IBeatmapObjectController> _snapshot = new(64);
    private readonly List<CuttableBySaber> _cuttables = new(8);

    public NoteSlider(PlaybackSeeker seeker, [InjectOptional] IGamePause? gamePause)
    {
        _seeker = seeker;
        _gamePause = gamePause;
    }

    public void Initialize()
    {
        if (_gamePause != null)
        {
            _gamePause.didPauseEvent += HandleDidPause;
        }
    }

    public int Count => _sliding.Count;

    /// <summary>Starts clearing every live object (slides when <paramref name="slide"/>).</summary>
    public void ClearLiveObjects(bool slide, SlideDirection direction)
    {
        FinishAll();
        _seeker.StopCutSounds();

        _snapshot.Clear();
        _snapshot.AddRange(_seeker.LiveObjects);
        foreach (var item in _snapshot)
        {
            if (slide && item is NoteController note && note != null && note.noteData != null)
            {
                StartSlide(note, direction);
            }
            else
            {
                _seeker.DespawnObject(item);
            }
        }

        _snapshot.Clear();
        _seeker.ForgetPendingNotes();
    }

    /// <summary>Advances the slides; call every frame.</summary>
    public void Tick(float deltaTime)
    {
        for (var i = _sliding.Count - 1; i >= 0; i--)
        {
            var entry = _sliding[i];
            var note = entry.Note;
            if (note == null || note.noteData != entry.Data || !_seeker.IsLive(note))
            {
                // Despawned (and maybe reused) by someone else, e.g. a Chapters skip.
                _sliding.RemoveAt(i);
                continue;
            }

            entry.Elapsed += deltaTime;
            _sliding[i] = entry;
            if (note.enabled || SlideOut.IsFinished(entry.Elapsed, SlideOut.DurationSeconds))
            {
                _sliding.RemoveAt(i);
                _seeker.DespawnObject(note);
                continue;
            }

            var offset = SlideOut.Offset(entry.Elapsed, SlideOut.DurationSeconds, SlideOut.DistanceMeters, entry.Direction);
            note.transform.position = entry.Start + new Vector3(0f, offset, 0f);
        }
    }

    /// <summary>Despawns every sliding note now.</summary>
    public void FinishAll()
    {
        for (var i = _sliding.Count - 1; i >= 0; i--)
        {
            var entry = _sliding[i];
            if (entry.Note != null && entry.Note.noteData == entry.Data && _seeker.IsLive(entry.Note))
            {
                _seeker.DespawnObject(entry.Note);
            }
        }

        _sliding.Clear();
    }

    public void Dispose()
    {
        if (_gamePause != null)
        {
            _gamePause.didPauseEvent -= HandleDidPause;
        }

        _sliding.Clear();
    }

    private void HandleDidPause()
    {
        if (_sliding.Count > 0)
        {
            Guard.Run("DifficultySwitch", FinishAll);
        }
    }

    private void StartSlide(NoteController note, SlideDirection direction)
    {
        note.Pause(true);
        _cuttables.Clear();
        note.GetComponentsInChildren(true, _cuttables);
        foreach (var cuttable in _cuttables)
        {
            cuttable.canBeCut = false;
        }

        _cuttables.Clear();
        _sliding.Add(new Sliding
        {
            Note = note,
            Data = note.noteData,
            Start = note.transform.position,
            Direction = direction,
            Elapsed = 0f,
        });
    }

    private struct Sliding
    {
        public NoteController Note;
        public NoteData Data;
        public Vector3 Start;
        public SlideDirection Direction;
        public float Elapsed;
    }
}
