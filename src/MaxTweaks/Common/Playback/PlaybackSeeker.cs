using System;
using System.Collections.Generic;
using SiraUtil.Submissions;
using UnityEngine;
using Zenject;

namespace MaxTweaks.Common.Playback;

/// <summary>
/// Moves the running song to another time in place (no scene reload) and keeps the beatmap in step.
/// Shared by features that jump around inside a song (Chapters skip, Difficulty switch).
///
/// Binding: player container only, and only in practice mode. Several features may want it, so bind
/// it through <see cref="PlaybackInstaller.BindPlaybackSeeker"/>, which binds it once per container.
///
/// Primitives (all main-thread, call from Update/Tick or UI callbacks):
/// <list type="bullet">
/// <item><see cref="DespawnAll"/>: removes every live note, bomb, wall, arc and chain and stops their
/// scheduled cut sounds. No miss/cut events fire, so score, combo and energy are untouched.</item>
/// <item><see cref="ResyncCallbacks"/>: repositions every beatmap callback cursor so the next callback
/// update spawns objects from the given time onwards. Lighting/BPM/NJS events between the old and new
/// position are replayed forwards (or reverted when going backwards, the way the game itself rewinds)
/// so the environment matches the new time.</item>
/// <item><see cref="SeekAudio"/>: moves the audio clock (AudioTimeSyncController.SeekTo) honouring the
/// practice speed (timeScale). When paused it also re-syncs PracticePlugin's pause-time position so it
/// does not seek back on Continue (<see cref="PracticePluginSync"/>).</item>
/// <item><see cref="SeekTo(float, float, string)"/>: the full jump = disable submission + despawn +
/// audio seek + resync. Use this unless you need the pieces separately (e.g. a difficulty swap that
/// replaces the beatmap data between despawn and resync).</item>
/// </list>
///
/// Paused vs playing: when the game is paused (pause menu), the callbacks updater is disabled by the
/// game, so objects for the new position spawn on resume with fresh audio-DSP timing. When playing,
/// they spawn in this frame's callbacks LateUpdate.
///
/// Recipe adapted from the BeatLeader replayer (Replayer/BeatmapTimeController.cs Rewind) for 1.40.8:
/// audio seek via AudioTimeSyncController.SeekTo((t - startSongTime) / timeScale), callback cursors
/// via CallbacksInTime.lastProcessedNode, and BeatmapCallbacksController._prevSongTime reset.
///
/// Known limits: walls/arcs that started before the objects-from time are not recreated (they would
/// have to spawn mid-way through); Noodle Extensions keeps its own callback cursor that is not
/// repositioned.
/// </summary>
internal class PlaybackSeeker
{
    /// <summary>Submission source name used for every seek.</summary>
    public const string SubmissionSource = "MaxTweaks";

    /// <summary>Seeks never land closer than this to the song end (the game ends the level at songEndTime - 0.2).</summary>
    public const float EndMargin = 0.3f;

    private readonly AudioTimeSyncController _audioTimeSyncController;
    private readonly BeatmapCallbacksController _callbacksController;
    private readonly BeatmapObjectManager _beatmapObjectManager;
    private readonly IReadonlyBeatmapData _beatmapData;
    private readonly NoteCutSoundEffectManager? _noteCutSoundEffectManager;
    private readonly Submission? _submission;
    private readonly PracticePluginSync _practicePluginSync;

    public PlaybackSeeker(
        AudioTimeSyncController audioTimeSyncController,
        BeatmapCallbacksController callbacksController,
        BeatmapObjectManager beatmapObjectManager,
        IReadonlyBeatmapData beatmapData,
        [InjectOptional] NoteCutSoundEffectManager? noteCutSoundEffectManager,
        DiContainer container,
        [InjectOptional] Submission? submission)
    {
        _practicePluginSync = new PracticePluginSync(container);
        _audioTimeSyncController = audioTimeSyncController;
        _callbacksController = callbacksController;
        _beatmapObjectManager = beatmapObjectManager;
        _beatmapData = beatmapData;
        _noteCutSoundEffectManager = noteCutSoundEffectManager;
        _submission = submission;
    }

    /// <summary>Raised after every successful <see cref="SeekTo(float, float, string)"/> with the new audio time.</summary>
    public event Action<float>? DidSeek;

    public float SongTime => _audioTimeSyncController.songTime;

    public float SongEndTime => _audioTimeSyncController.songEndTime;

    /// <summary>True while the song clock runs or is paused (not before start, not after stop/fail).</summary>
    public bool CanSeek => _audioTimeSyncController.isReady && _audioTimeSyncController.state != AudioTimeSyncController.State.Stopped;

    /// <summary>
    /// How long before its time an object is spawned (the largest callback ahead-time, i.e. the spawn
    /// controller's move + half-jump duration). A lead-in of at least this long lets the first object
    /// after a seek fly in from the far end instead of popping up close to the player.
    /// </summary>
    public float SpawnAheadTime
    {
        get
        {
            var max = 0f;
            foreach (var pair in _callbacksController._callbacksInTimes)
            {
                max = Mathf.Max(max, pair.Key);
            }

            return max;
        }
    }

    /// <summary>
    /// Full jump: audio continues from <paramref name="audioTime"/>, beatmap objects resume from
    /// <paramref name="objectsFromTime"/> (pass a later time than the audio for a clean lead-in, or the
    /// same time). Disables score submission for the rest of the song with <paramref name="reason"/>.
    /// Both times are clamped to [0, song end - <see cref="EndMargin"/>] and objects never start
    /// before the audio.
    /// </summary>
    /// <returns>False if the song clock cannot be moved right now.</returns>
    public bool SeekTo(float audioTime, float objectsFromTime, string reason)
    {
        if (!CanSeek || float.IsNaN(audioTime) || float.IsNaN(objectsFromTime))
        {
            return false;
        }

        var latest = Mathf.Max(0f, SongEndTime - EndMargin);
        audioTime = Mathf.Clamp(audioTime, 0f, latest);
        objectsFromTime = Mathf.Clamp(objectsFromTime, audioTime, latest);

        DisableSubmission(reason);
        DespawnAll();
        SeekAudio(audioTime);
        ResyncCallbacks(objectsFromTime);

        Plugin.Log.Info($"[Playback] seek to {audioTime:0.000}s (objects from {objectsFromTime:0.000}s): {reason}");
        DidSeek?.Invoke(audioTime);
        return true;
    }

    /// <summary>Same as <see cref="SeekTo(float, float, string)"/> with objects resuming at the audio time.</summary>
    public bool SeekTo(float time, string reason) => SeekTo(time, time, reason);

    /// <summary>Disables score submission for the rest of this song (SiraUtil Submission ticket).</summary>
    public void DisableSubmission(string reason)
    {
        _submission?.DisableScoreSubmission(SubmissionSource, reason);
    }

    /// <summary>Removes every live beatmap object and silences pending cut sounds.</summary>
    public void DespawnAll()
    {
        StopCutSounds();

        // Despawn removes the controller from this list, so walk it backwards by index.
        var objects = _beatmapObjectManager._allBeatmapObjects;
        for (var i = objects.Count - 1; i >= 0; i--)
        {
            if (i >= objects.Count)
            {
                continue;
            }

            var item = objects[i];

            // Objects are paused while the pause menu is up; a pooled object must not come back paused.
            item.Pause(false);
            switch (item)
            {
                case NoteController note:
                    _beatmapObjectManager.Despawn(note);
                    break;
                case ObstacleController obstacle:
                    _beatmapObjectManager.Despawn(obstacle);
                    break;
                case SliderController slider:
                    _beatmapObjectManager.Despawn(slider);
                    break;
                default:
                    // Unknown controller type (another mod's): leave it alone.
                    break;
            }
        }
    }

    /// <summary>
    /// Moves the audio clock to <paramref name="time"/> (song seconds). Does not touch beatmap
    /// objects or callbacks; pair it with <see cref="DespawnAll"/> and <see cref="ResyncCallbacks"/>.
    /// </summary>
    public void SeekAudio(float time)
    {
        var atsc = _audioTimeSyncController;
        atsc.SeekTo((time - atsc.startSongTime) / atsc.timeScale);

        // SeekTo stores the expected sample position; a rounding difference of one sample would make
        // Update think the clip looped and add a whole clip length to the song time.
        atsc._prevAudioSamplePos = -1;

        if (atsc.state == AudioTimeSyncController.State.Paused)
        {
            Guard.Run("Playback", () => _practicePluginSync.AfterPausedSeek(atsc._audioSource));
        }
    }

    /// <summary>
    /// Points every callback cursor at the last beatmap item before <paramref name="time"/>, so the
    /// next callback update processes items from <paramref name="time"/> onwards. Events passed on the
    /// way are replayed (forwards) or reverted to their previous state (backwards); objects passed are
    /// skipped.
    /// </summary>
    public void ResyncCallbacks(float time)
    {
        var items = _beatmapData.allBeatmapDataItems;
        foreach (var pair in _callbacksController._callbacksInTimes)
        {
            var callbacks = pair.Value;
            var node = callbacks.lastProcessedNode;
            if (node == null || node.Value.time < time)
            {
                var next = node == null ? items.First : node.Next;
                while (next != null && next.Value.time < time)
                {
                    if (next.Value.type == BeatmapDataItem.BeatmapDataItemType.BeatmapEvent)
                    {
                        callbacks.CallCallbacks(next.Value);
                    }

                    node = next;
                    next = next.Next;
                }
            }
            else
            {
                while (node != null && node.Value.time >= time)
                {
                    if (node.Value is BeatmapEventData eventData)
                    {
                        var previous = eventData.previousSameTypeEventData ?? eventData.GetDefault(eventData);
                        if (previous != null)
                        {
                            callbacks.CallCallbacks(previous);
                        }
                    }

                    node = node.Previous;
                }
            }

            callbacks.lastProcessedNode = node;
        }

        // Force the forward branch on the next ManualUpdate even if the new time is earlier.
        _callbacksController._prevSongTime = float.MinValue;
    }

    /// <summary>Stops scheduled note-cut sounds and resets the per-colour duplicate guard.</summary>
    public void StopCutSounds()
    {
        var manager = _noteCutSoundEffectManager;
        if (manager == null)
        {
            return;
        }

        var container = manager._noteCutSoundEffectPoolContainer;
        if (container != null)
        {
            // StopPlayingAndFinish despawns the effect from activeItems synchronously; the guard
            // prevents an endless loop should that ever change.
            List<NoteCutSoundEffect> active = container.activeItems;
            for (var guard = active.Count + 8; active.Count > 0 && guard > 0; guard--)
            {
                active[active.Count - 1].StopPlayingAndFinish();
            }
        }

        // Without this, after a backward seek every note earlier than the last scheduled one would
        // get no cut sound (HandleNoteWasSpawned skips notes older than _prevNoteATime/_prevNoteBTime).
        manager._prevNoteATime = -1f;
        manager._prevNoteBTime = -1f;
    }
}
