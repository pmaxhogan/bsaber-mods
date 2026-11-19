using System;
using MaxTweaks.Common;
using MaxTweaks.Common.HeartRate;
using MaxTweaks.Common.Playback;
using MaxTweaks.Configuration;
using MaxTweaks.Core.ObsClips;
using MaxTweaks.Core.ObsRecording;
using Zenject;

namespace MaxTweaks.Features.ObsClips;

/// <summary>
/// Player container: feeds the app-lifetime <see cref="ObsClipsService"/> with what happens in this
/// level (notes hit and missed, heart rate, whether the song is running) and hands it the song name,
/// difficulty and song time for the clips it picks.
/// - A note counts as hit when it was cut correctly; a bad cut, a miss and a cut bomb count against
///   the stretch. A bomb that passes is nothing.
/// - "Running" is the song clock playing and the game not paused (the pause menu and this mod's
///   active pause both pause the game), so pauses never end up in a clip.
/// </summary>
internal sealed class ObsClipsLevelHook : IInitializable, ITickable, IDisposable
{
    private const string Feature = "ObsClips";

    private readonly ObsClipsService _service;
    private readonly GameplayCoreSceneSetupData _setupData;
    private readonly AudioTimeSyncController _audio;
    private readonly HeartRateFeed _feed;
    private readonly BeatmapObjectManager? _objects;
    private readonly IGamePause? _pause;
    private readonly ActiveBeatmap? _activeBeatmap;
    private readonly SongTimeTrail _trail = new();
    private readonly Action _advance;
    private bool _started;

    public ObsClipsLevelHook(
        ObsClipsService service,
        GameplayCoreSceneSetupData setupData,
        AudioTimeSyncController audio,
        HeartRateFeed feed,
        [InjectOptional] BeatmapObjectManager? objects,
        [InjectOptional] IGamePause? pause,
        [InjectOptional] ActiveBeatmap? activeBeatmap)
    {
        _service = service;
        _setupData = setupData;
        _audio = audio;
        _feed = feed;
        _objects = objects;
        _pause = pause;
        _activeBeatmap = activeBeatmap;

        // Kept, so the per-frame call does not allocate a delegate.
        _advance = Advance;
    }

    public void Initialize() => Guard.Run(Feature, () =>
    {
        _started = true;
        _service.LevelStarted();
        _feed.Reading += HandleHeartRate;
        if (_objects != null)
        {
            _objects.noteWasCutEvent += HandleNoteCut;
            _objects.noteWasMissedEvent += HandleNoteMissed;
        }
        else
        {
            Plugin.Log.Warn($"[{Feature}] no note events in this scene: only the heart rate can make a clip");
        }
    });

    public void Tick()
    {
        if (_started)
        {
            Guard.Run(Feature, _advance);
        }
    }

    public void Dispose() => Guard.Run(Feature, () =>
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _feed.Reading -= HandleHeartRate;
        if (_objects != null)
        {
            _objects.noteWasCutEvent -= HandleNoteCut;
            _objects.noteWasMissedEvent -= HandleNoteMissed;
        }

        if (_service.LevelEnding() is { } clip)
        {
            Save(clip);
        }

        _service.LevelEnded();
    });

    private void Advance()
    {
        var playing = _audio.state == AudioTimeSyncController.State.Playing && !(_pause != null && _pause.isPaused);
        if (playing)
        {
            _trail.Add(_service.Now, _audio.songTime);
        }

        if (_service.Advance(playing) is { } clip)
        {
            Save(clip);
        }
    }

    private void Save(PickedClip clip)
    {
        var songTime = _trail.TryAt(clip.StartedAt, out var seconds) ? seconds : 0.0;
        var reason = clip.Pick.Reason == ClipReason.HeartRate ? "heart rate" : "notes";
        _service.Save(clip, Label(), SongTimeTrail.Text(songTime) + " " + reason);
    }

    private void HandleNoteCut(NoteController note, in NoteCutInfo info)
    {
        // Runs inside the game's cut handling: never let a failure out.
        try
        {
            if (note.noteData.colorType != ColorType.None && info.allIsOK)
            {
                _service.NoteHit();
            }
            else
            {
                _service.NoteMissed();
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Debug($"[{Feature}] note cut: {e.Message}");
        }
    }

    private void HandleNoteMissed(NoteController note)
    {
        try
        {
            if (note.noteData.colorType == ColorType.None)
            {
                return;
            }

            if (_service.MissesCountAsHits)
            {
                _service.NoteHit();
            }
            else
            {
                _service.NoteMissed();
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Debug($"[{Feature}] note missed: {e.Message}");
        }
    }

    private void HandleHeartRate(int bpm) => _service.HeartRate(bpm);

    /// <summary>Read when a clip is saved: the practice-mode difficulty switch can change the difficulty mid-song.</summary>
    private RecordingLabel Label()
    {
        var key = _activeBeatmap?.Key ?? _setupData.beatmapKey;
        return new RecordingLabel(
            _setupData.beatmapLevel?.songName,
            RecordingFileName.DifficultyLabel(
                key.difficulty.ToString(),
                key.beatmapCharacteristic?.serializedName,
                practice: _setupData.practiceSettings != null));
    }
}
