using System;
using System.Collections.Generic;
using MaxTweaks.Core.Playback;
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
/// <item><see cref="DespawnObject"/>: the same for one object (deferred despawns, e.g. sliding notes).</item>
/// <item><see cref="ResyncCallbacks"/>: repositions every beatmap callback cursor for a new audio time
/// and sets the callbacks controller's object filter time, so objects resume from the given
/// objects-from time while events keep firing on time. Lighting/BPM/NJS events between the old and
/// new position are replayed forwards (or reverted when going backwards, the way the game itself
/// rewinds) so the environment matches the new time. Also repositions Noodle Extensions' own object
/// cursor when it is present.</item>
/// <item><see cref="SeekAudio"/>: moves the audio clock (AudioTimeSyncController.SeekTo) honouring the
/// practice speed (timeScale). When paused it also re-syncs PracticePlugin's pause-time position so it
/// does not seek back on Continue (<see cref="PracticePluginSync"/>).</item>
/// <item><see cref="SeekTo(float, float, string)"/>: the full jump = disable submission + despawn +
/// audio seek + resync. Use this unless you need the pieces separately (e.g. a difficulty swap that
/// replaces the beatmap data and resyncs without moving the audio).</item>
/// </list>
///
/// Object filter: BeatmapCallbacksController.ManualUpdate only fires object callbacks for objects at
/// or after its _startFilterTime (the practice start; PracticePlugin also raises it on its seeks).
/// <see cref="ResyncCallbacks"/> sets it to the objects-from time, which both allows backward seeks
/// before the practice start and drops the objects of a lead-in or a clear gap.
///
/// Paused vs playing: when the game is paused (pause menu), the callbacks updater is disabled by the
/// game, so objects for the new position spawn on resume with fresh audio-DSP timing. When playing,
/// they spawn in this frame's callbacks LateUpdate.
///
/// Recipe adapted from the BeatLeader replayer (Replayer/BeatmapTimeController.cs Rewind) and
/// PracticePlugin 9.x (SongSeekBeatmapHandler) for 1.40.8: audio seek via
/// AudioTimeSyncController.SeekTo((t - startSongTime) / timeScale), callback cursors via
/// CallbacksInTime.lastProcessedNode, BeatmapCallbacksController._prevSongTime reset.
///
/// Known limits: walls/arcs that started before the objects-from time are not recreated (they would
/// have to spawn mid-way through).
/// </summary>
internal class PlaybackSeeker
{
    /// <summary>Submission source name used for every seek.</summary>
    public const string SubmissionSource = "MaxTweaks";

    /// <summary>Seeks never land closer than this to the song end (the game ends the level at songEndTime - 0.2).</summary>
    public const float EndMargin = 0.3f;

    /// <summary>The game finishes the level at songEndTime - this (GameSongController.LateUpdate).</summary>
    private const float GameFinishMargin = 0.2f;

    private readonly AudioTimeSyncController _audioTimeSyncController;
    private readonly BeatmapCallbacksController _callbacksController;
    private readonly BeatmapObjectManager _beatmapObjectManager;
    private readonly NoteCutSoundEffectManager? _noteCutSoundEffectManager;
    private readonly Submission? _submission;
    private readonly ScoreController? _scoreController;
    private readonly StandardLevelGameplayManager? _gameplayManager;
    private readonly PracticePluginSync _practicePluginSync;
    private readonly NoodleCallbacksSync _noodleSync;

    public PlaybackSeeker(
        AudioTimeSyncController audioTimeSyncController,
        BeatmapCallbacksController callbacksController,
        BeatmapObjectManager beatmapObjectManager,
        [InjectOptional] NoteCutSoundEffectManager? noteCutSoundEffectManager,
        DiContainer container,
        [InjectOptional] Submission? submission,
        [InjectOptional] IScoreController? scoreController,
        [InjectOptional] StandardLevelGameplayManager? gameplayManager)
    {
        _practicePluginSync = new PracticePluginSync(container);
        _noodleSync = new NoodleCallbacksSync(container);
        _audioTimeSyncController = audioTimeSyncController;
        _callbacksController = callbacksController;
        _beatmapObjectManager = beatmapObjectManager;
        _noteCutSoundEffectManager = noteCutSoundEffectManager;
        _submission = submission;
        _scoreController = scoreController as ScoreController;
        _gameplayManager = gameplayManager;
    }

    /// <summary>Raised after every successful <see cref="SeekTo(float, float, string)"/> with the new audio time.</summary>
    public event Action<float>? DidSeek;

    /// <summary>True when Noodle Extensions spawns this map's objects from its own callbacks manager.</summary>
    public bool HasNoodleCallbacks => _noodleSync.IsActive;

    public float SongTime => _audioTimeSyncController.songTime;

    public float SongEndTime => _audioTimeSyncController.songEndTime;

    /// <summary>True while the song clock is paused (pause menu).</summary>
    public bool IsPaused => _audioTimeSyncController.state == AudioTimeSyncController.State.Paused;

    /// <summary>Song seconds per real second (practice speed).</summary>
    public float TimeScale => _audioTimeSyncController.timeScale;

    /// <summary>
    /// True while the song clock runs or is paused, before the game's own finish point, and the level
    /// has not failed or finished (not before start, not after stop/fail/finish).
    /// </summary>
    public bool CanSeek
    {
        get
        {
            var atsc = _audioTimeSyncController;
            if (!atsc.isReady || atsc.state == AudioTimeSyncController.State.Stopped)
            {
                return false;
            }

            if (atsc.songTime >= atsc.songEndTime - GameFinishMargin)
            {
                return false;
            }

            if (_gameplayManager != null)
            {
                var state = _gameplayManager._gameState;
                if (state == StandardLevelGameplayManager.GameState.Failed || state == StandardLevelGameplayManager.GameState.Finished)
                {
                    return false;
                }
            }

            return true;
        }
    }

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
        ResyncCallbacks(audioTime, objectsFromTime);

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

            DespawnObject(objects[i]);
        }

        ForgetPendingNotes();
    }

    /// <summary>
    /// Despawns one live object (note, bomb, chain element, wall or arc) without miss/cut events.
    /// Unknown controller types (another mod's) and objects no longer live are left alone.
    /// Does not stop cut sounds; pair with <see cref="StopCutSounds"/> and <see cref="ForgetPendingNotes"/>.
    /// </summary>
    /// <returns>True if it was despawned.</returns>
    public bool DespawnObject(IBeatmapObjectController item)
    {
        // Objects are paused while the pause menu is up; a pooled object must not come back paused,
        // so unpause right before handing it back (only for the types we despawn).
        switch (item)
        {
            case NoteController note:
                note.Pause(false);
                _beatmapObjectManager.Despawn(note);
                return true;
            case ObstacleController obstacle:
                obstacle.Pause(false);
                _beatmapObjectManager.Despawn(obstacle);
                return true;
            case SliderController slider:
                slider.Pause(false);
                _beatmapObjectManager.Despawn(slider);
                return true;
            default:
                return false;
        }
    }

    /// <summary>True while <paramref name="item"/> is one of the live beatmap objects.</summary>
    public bool IsLive(IBeatmapObjectController item) => _beatmapObjectManager._allBeatmapObjects.Contains(item);

    /// <summary>The live objects list (do not modify; copy before despawning while iterating).</summary>
    public IReadOnlyList<IBeatmapObjectController> LiveObjects => _beatmapObjectManager._allBeatmapObjects;

    /// <summary>
    /// ScoreController keeps the time of every spawned, not yet cut/missed note and holds back scoring
    /// of later notes until those resolve. Notes despawned without events would stay there forever,
    /// so call this once every live note has been (or is being) despawned without events.
    /// </summary>
    public void ForgetPendingNotes()
    {
        _scoreController?._sortedNoteTimesWithoutScoringElements.Clear();
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

        // SeekTo sets _dspTimeOffset = dspTime - audioTime, which ignores the practice speed; Update
        // uses audioTime / timeScale. Cut sounds scheduled before the next audio Update (or while
        // paused) would be off at speed != 1.
        var source = atsc._audioSource;
        if (source != null && atsc.timeScale > 0f)
        {
            atsc._dspTimeOffset = AudioSettings.dspTime - (source.time / atsc.timeScale);
        }

        if (atsc.state == AudioTimeSyncController.State.Paused)
        {
            Guard.Run("Playback", () => _practicePluginSync.AfterPausedSeek(atsc._audioSource));
        }
    }

    /// <summary>
    /// Repositions every callback cursor for audio time <paramref name="audioTime"/>: each cursor
    /// (one per callback ahead-time) ends on the last beatmap item before
    /// <see cref="SeekMath.CursorTarget"/>: audioTime + aheadTime (the point the game itself would have
    /// reached), capped at max(audioTime, objectsFromTime) so objects from objectsFromTime on are never
    /// walked over. Events passed on the way are replayed (forwards) or
    /// reverted to their previous state (backwards). Objects before
    /// <paramref name="objectsFromTime"/> are dropped through the controller's object filter time.
    /// Cursors that point into another items list (the beatmap data was replaced in place) restart
    /// from the first item.
    /// </summary>
    public void ResyncCallbacks(float audioTime, float objectsFromTime)
    {
        var controller = _callbacksController;
        var items = controller._beatmapData.allBeatmapDataItems;
        foreach (var pair in controller._callbacksInTimes)
        {
            var callbacks = pair.Value;
            var target = SeekMath.CursorTarget(audioTime, pair.Key, objectsFromTime);
            var node = callbacks.lastProcessedNode;
            callbacks.beatmapEventDataForCallbacksAfterNodeRemoval = null;
            if (node != null && node.List != items)
            {
                node = null;
            }

            if (node == null || node.Value.time < target)
            {
                var next = node == null ? items.First : node.Next;
                while (next != null && next.Value.time < target)
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
                while (node != null && node.Value.time >= target)
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

        SetObjectFilterTime(objectsFromTime);

        // Force the forward branch on the next ManualUpdate even if the new time is earlier.
        controller._prevSongTime = float.MinValue;

        Guard.Run("Playback", () => _noodleSync.Resync(objectsFromTime));

        // Batched light appliers apply their pending changes on this event.
        Guard.Run("Playback", RaiseDidProcessAllCallbacks);
    }

    /// <summary>
    /// Walks every callback cursor back to before the first item, reverting each event passed to its
    /// previous state (or the event type's default), the way the game rewinds. Used before the beatmap
    /// data is replaced, so event types that only the old data uses (lights, rings, boost, BPM) return
    /// to their defaults instead of keeping the old difficulty's last state. Cursors end null; follow
    /// with <see cref="ResyncCallbacks"/>.
    /// </summary>
    public void RevertEventsToStart()
    {
        var controller = _callbacksController;
        var items = controller._beatmapData.allBeatmapDataItems;
        foreach (var pair in controller._callbacksInTimes)
        {
            var callbacks = pair.Value;
            var node = callbacks.lastProcessedNode;
            if (node != null && node.List != items)
            {
                node = null;
            }

            while (node != null)
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

            callbacks.lastProcessedNode = null;
        }

        controller._prevSongTime = float.MinValue;
    }

    /// <summary>Tells PracticePlugin (when installed) the new note jump speed/value after a difficulty switch.</summary>
    public void SyncPracticePluginNoteJump(float noteJumpSpeed, float noteJumpValue)
    {
        Guard.Run("Playback", () => _practicePluginSync.AfterDifficultySwap(noteJumpSpeed, noteJumpValue));
    }

    /// <summary>PracticePlugin (optional) sync helpers.</summary>
    public PracticePluginSync PracticePlugin => _practicePluginSync;

    /// <summary>Sets BeatmapCallbacksController._startFilterTime (objects before it never spawn).</summary>
    public void SetObjectFilterTime(float time)
    {
        ReadonlyFields.Set(_callbacksController, "_startFilterTime", time);
    }

    /// <summary>Current BeatmapCallbacksController._startFilterTime.</summary>
    public float ObjectFilterTime => _callbacksController._startFilterTime;

    /// <summary>
    /// Restarts the music source if the song clock says Playing but the AudioSource is not playing.
    /// Unity's voice management can stop a PAUSED AudioSource outright when the audio channels run
    /// out (seen in VR after a burst of active-pause steps: "Ran out of virtual channels" warnings,
    /// then on resume "AudioTimeSyncController: audio should be playing ... isPlaying=False
    /// audioTime=0"). AudioSource.UnPause does nothing for a stopped source and the game's Update
    /// then returns early every frame, so the song never moves again. Returns true if it restarted.
    /// </summary>
    public bool EnsureAudioPlaying(string reason)
    {
        var atsc = _audioTimeSyncController;
        var source = atsc._audioSource;
        if (atsc.state != AudioTimeSyncController.State.Playing || source == null || source.clip == null || source.isPlaying)
        {
            return false;
        }

        var songTime = atsc.songTime;
        if (songTime >= source.clip.length - 1f)
        {
            return false;
        }

        // Play from the song clock's position: the same mapping SeekTo uses (offset + latency).
        source.Play();
        SeekAudio(songTime);
        Plugin.Log.Warn($"[Playback] music source was stopped; restarted at {songTime.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}s ({reason})");
        return true;
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

    private void RaiseDidProcessAllCallbacks()
    {
        var handler = ReadonlyFields.Get<Action>(_callbacksController, "didProcessAllCallbacksThisFrameEvent");
        handler?.Invoke();
    }
}
