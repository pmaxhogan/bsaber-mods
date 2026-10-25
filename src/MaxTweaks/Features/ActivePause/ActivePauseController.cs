using System;
using MaxTweaks.Common;
using MaxTweaks.Common.Playback;
using MaxTweaks.Common.Pointing;
using MaxTweaks.Configuration;
using MaxTweaks.Core.ActivePause;
using MaxTweaks.Core.Chapters;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

namespace MaxTweaks.Features.ActivePause;

/// <summary>
/// Practice gameplay: X pauses the song in place without the pause menu ("active pause"), shows the
/// twelve seek buttons (<see cref="StepButtonPanel"/>) and ghosts every beatmap object
/// (<see cref="ObjectGhoster"/>); X again resumes immediately (no countdown, objects stay where they
/// are). Point a saber at a button and press A (FPFC: camera ray and Enter) to step; each step seeks
/// with <see cref="PlaybackSeeker"/> and then spawns the objects of the new position at once, frozen
/// and ghosted, so the pattern there can be studied before resuming (also after a Chapters skip;
/// not on Noodle maps, whose objects appear on resume). Objects spawned while paused are respawned
/// in place right before resuming so their cut sounds are scheduled with fresh timing.
///
/// Pause mechanics: IGamePause.Pause() (what PauseController.Pause does minus the menu: song clock,
/// callbacks updater, score, energy, head-in-wall and the audio listener) plus
/// BeatmapObjectManager.PauseAllBeatmapObjects, so IGamePause.isPaused is true and Difficulty switch
/// and Chapters keep working. GamePause hides the sabers (SaberManager.disableSabers); they are shown
/// again for aiming and the CuttingManager is disabled instead, so nothing can be cut (the wall
/// sparkle/rumble manager too, as it tests sabers against walls by itself). Resume reverses
/// it through IGamePause.WillResume()/Resume(). PauseController's own state stays Playing throughout:
/// if its pause menu opens (menu button, focus loss) active pause is dropped and the game owns the
/// pause, resuming through Continue as usual (its GamePause.Pause call is a no-op then; Continue's
/// GamePause.Resume does the rest).
///
/// Log lines for QA: "[ActivePause] on at 42.317s", "[ActivePause] off at 42.317s",
/// "[ActivePause] step +1b: 42.317s -> 42.857s", "[ActivePause] dropped: pause menu opened".
/// </summary>
internal sealed class ActivePauseController : IInitializable, ITickable, IDisposable
{
    private const string Feature = "ActivePause";

    private readonly ActivePauseConfig _config;
    private readonly GameplayCoreSceneSetupData _setupData;
    private readonly ControllerInput _input;
    private readonly PlaybackSeeker _seeker;
    private readonly IGamePause _gamePause;
    private readonly AudioTimeSyncController _audioTimeSyncController;
    private readonly BeatmapObjectManager _beatmapObjectManager;
    private readonly BeatmapCallbacksController _callbacksController;
    private readonly IReadonlyBeatmapData _beatmapData;
    private readonly SaberManager? _saberManager;
    private readonly PauseController? _pauseController;
    private readonly BpmController.InitData? _bpmInitData;
    private readonly ActiveBeatmap? _activeBeatmap;

    private readonly ActivePauseToggle _toggle = new();
    private readonly ObjectGhoster _ghoster = new();
    private StepButtonPanel? _panel;
    private BpmTimeline? _tempo;
    private CuttingManager? _cuttingManager;
    private ObstacleSaberSparkleEffectManager? _sparkleManager;
    private Camera? _camera;
    private bool _cuttingDisabled;
    private bool _sparklesDisabled;
    private bool _seekedWhileActive;
    private bool _loggedNoodle;
    private bool _failed;

    public ActivePauseController(
        ActivePauseConfig config,
        GameplayCoreSceneSetupData setupData,
        ControllerInput input,
        PlaybackSeeker seeker,
        IGamePause gamePause,
        AudioTimeSyncController audioTimeSyncController,
        BeatmapObjectManager beatmapObjectManager,
        BeatmapCallbacksController callbacksController,
        IReadonlyBeatmapData beatmapData,
        [InjectOptional] SaberManager? saberManager,
        [InjectOptional] PauseController? pauseController,
        [InjectOptional] BpmController.InitData? bpmInitData,
        [InjectOptional] ActiveBeatmap? activeBeatmap)
    {
        _config = config;
        _setupData = setupData;
        _input = input;
        _seeker = seeker;
        _gamePause = gamePause;
        _audioTimeSyncController = audioTimeSyncController;
        _beatmapObjectManager = beatmapObjectManager;
        _callbacksController = callbacksController;
        _beatmapData = beatmapData;
        _saberManager = saberManager;
        _pauseController = pauseController;
        _bpmInitData = bpmInitData;
        _activeBeatmap = activeBeatmap;
    }

    private float SongTime => _audioTimeSyncController.songTime;

    public void Initialize()
    {
        Guard.Run(Feature, BuildTempo);
        if (_pauseController != null)
        {
            _pauseController.didPauseEvent += HandlePauseMenuOpened;
        }
        else
        {
            Plugin.Log.Warn($"[{Feature}] PauseController not found; opening the pause menu while active-paused is not handled");
        }

        if (_activeBeatmap != null)
        {
            _activeBeatmap.Changed += HandleDifficultyChanged;
        }

        _seeker.DidSeek += HandleDidSeek;
    }

    public void Tick()
    {
        if (_failed)
        {
            return;
        }

        try
        {
            if (_input.GetButtonDown(ControllerButton.X))
            {
                Toggle();
            }

            if (_toggle.IsActive)
            {
                if (_config.GhostNotes)
                {
                    _ghoster.Apply(_seeker.LiveObjects);
                }

                UpdatePointing();
            }
            else
            {
                // Covers every resume path, including the game's own Continue after the pause menu
                // took over an active pause (the music source can be stopped by then; see the seeker).
                _seeker.EnsureAudioPlaying("tick");
            }
        }
        catch (Exception e)
        {
            // Never spam the log every frame, and never leave the game stuck paused: resume and stop.
            _failed = true;
            Plugin.Log.Error($"[{Feature}] disabled for this song: {e}");
            if (_toggle.IsActive)
            {
                _toggle.Reset();
                Guard.Run(Feature, Exit);
            }
        }
    }

    public void Dispose()
    {
        if (_pauseController != null)
        {
            _pauseController.didPauseEvent -= HandlePauseMenuOpened;
        }

        if (_activeBeatmap != null)
        {
            _activeBeatmap.Changed -= HandleDifficultyChanged;
        }

        _seeker.DidSeek -= HandleDidSeek;
        _panel?.Dispose();
        _panel = null;
    }

    private void BuildTempo()
    {
        _tempo = null;
        if (!(BeatmapTempo.StartBpm(_setupData, _bpmInitData) > 0f))
        {
            Plugin.Log.Warn($"[{Feature}] no usable BPM; beat steps move {StepMath.FormatTime(60f / StepMath.FallbackBpm)} per beat without snapping");
            return;
        }

        _tempo = BeatmapTempo.Build(_beatmapData, _setupData, _bpmInitData);
    }

    // Difficulty switch replaced the beatmap data in place (possibly while active-paused): its BPM
    // events may differ.
    private void HandleDifficultyChanged(BeatmapKey oldKey, BeatmapKey newKey) => Guard.Run(Feature, BuildTempo);

    private void Toggle()
    {
        switch (_toggle.OnTogglePressed(_gamePause.isPaused, _seeker.CanSeek))
        {
            case ToggleAction.Activate:
                Enter();
                break;
            case ToggleAction.Deactivate:
                Exit();
                break;
            default:
                Plugin.Log.Debug($"[{Feature}] X ignored: {_toggle.LastRefusal}");
                break;
        }
    }

    private void Enter()
    {
        _seekedWhileActive = false;
        _gamePause.Pause();
        _beatmapObjectManager.PauseAllBeatmapObjects(pause: true);
        ShowSabersWithoutCutting();
        if (_config.GhostNotes)
        {
            _ghoster.Apply(_seeker.LiveObjects);
        }

        _panel ??= new StepButtonPanel(EyeHeight(), HandleButtonClicked);
        _panel.Show();
        Plugin.Log.Info($"[{Feature}] on at {StepMath.FormatTime(SongTime)}");
    }

    // Resuming comes first and the cosmetic cleanup after it, each guarded, so a failure in the
    // cleanup can never leave the game paused without a menu.
    private void Exit()
    {
        Guard.Run(Feature, RespawnIfSeeked);
        _beatmapObjectManager.PauseAllBeatmapObjects(pause: false);
        _gamePause.WillResume();
        _gamePause.Resume();
        Guard.Run(Feature, () => _seeker.EnsureAudioPlaying("active pause resume"));
        Guard.Run(Feature, RestoreCutting);
        Guard.Run(Feature, () => _panel?.Hide());
        Guard.Run(Feature, _ghoster.RestoreAll);
        Plugin.Log.Info($"[{Feature}] off at {StepMath.FormatTime(SongTime)}");
    }

    // Objects spawned while paused (after a step or another feature's seek) had their cut sounds
    // scheduled against the DSP clock at spawn time; after a pause longer than a moment those are
    // expired and the cut-sound manager will not schedule them again (not newer than the last note).
    // Seek in place while still paused instead: they despawn and respawn on the next update after
    // resume with fresh timing, at the same positions.
    private void RespawnIfSeeked()
    {
        if (!_seekedWhileActive)
        {
            return;
        }

        _seekedWhileActive = false;
        _ghoster.RestoreAll();
        var now = SongTime;
        _seeker.SeekTo(now, now, "Active pause resume");
        Plugin.Log.Info($"[{Feature}] objects respawn on resume at {StepMath.FormatTime(now)}");
    }

    // PauseController.Pause ran while we were active (menu button, focus loss): it showed the menu,
    // hid and paused every object; its GamePause.Pause call was a no-op. Hand the pause over: the
    // game resumes it through Continue.
    private void HandlePauseMenuOpened() => Guard.Run(Feature, () =>
    {
        if (!_toggle.OnPauseMenuOpened())
        {
            return;
        }

        _panel?.Hide();
        _ghoster.RestoreAll();
        if (_saberManager != null)
        {
            _saberManager.disableSabers = true; // what GamePause.Pause would have done
        }

        RestoreCutting();

        // The game resumes through Continue: objects spawned by a step must respawn then too.
        RespawnIfSeeked();
        Plugin.Log.Info($"[{Feature}] dropped: pause menu opened");
    });

    private void ShowSabersWithoutCutting()
    {
        _cuttingManager ??= Object.FindObjectOfType<CuttingManager>();
        if (_cuttingManager == null || _saberManager == null)
        {
            Plugin.Log.Warn($"[{Feature}] CuttingManager or SaberManager not found; sabers stay hidden while active-paused");
            return;
        }

        _cuttingManager.enabled = false;
        _cuttingDisabled = true;

        // The wall sparkle/rumble manager tests the sabers against walls by itself every frame: a
        // visible saber inside a frozen wall would sparkle and rumble the whole time.
        _sparkleManager ??= Object.FindObjectOfType<ObstacleSaberSparkleEffectManager>();
        if (_sparkleManager != null)
        {
            foreach (var effect in _sparkleManager._effects)
            {
                if (effect != null && effect.IsEmitting())
                {
                    effect.StopEmission();
                }
            }

            _sparkleManager.enabled = false;
            _sparklesDisabled = true;
        }

        _saberManager.disableSabers = false;
    }

    private void RestoreCutting()
    {
        if (_cuttingDisabled && _cuttingManager != null)
        {
            _cuttingManager.enabled = true;
        }

        if (_sparklesDisabled && _sparkleManager != null)
        {
            _sparkleManager.enabled = true;
        }

        _cuttingDisabled = false;
        _sparklesDisabled = false;
    }

    private void UpdatePointing()
    {
        var panel = _panel;
        if (panel == null)
        {
            return;
        }

        var hit = -1;
        Ray ray;
        if (_input.IsFpfc)
        {
            if (PointerRays.TryFromCamera(ref _camera, out ray))
            {
                hit = panel.HitTest(ray);
            }
        }
        else if (_saberManager != null)
        {
            // A is on the right controller, so the right saber wins when both point at buttons.
            if (PointerRays.TryFromSaber(_saberManager.rightSaber, out ray))
            {
                hit = panel.HitTest(ray);
            }

            if (hit < 0 && PointerRays.TryFromSaber(_saberManager.leftSaber, out ray))
            {
                hit = panel.HitTest(ray);
            }
        }

        panel.SetRayHover(hit);
        if (hit >= 0 && _input.GetButtonDown(ControllerButton.A))
        {
            Step(hit);
        }
    }

    private void HandleButtonClicked(int index)
    {
        if (_toggle.IsActive && !_failed)
        {
            Step(index);
        }
    }

    private void Step(int index)
    {
        var step = ActivePauseStep.All[index];
        var from = SongTime;
        var to = StepMath.ClampedTarget(from, step, _tempo, _seeker.SongEndTime, PlaybackSeeker.EndMargin);

        // Every live object is despawned by the seek; give the pooled visuals their dissolve back first.
        _ghoster.RestoreAll();
        if (!_seeker.SeekTo(to, to, "Active pause step"))
        {
            Plugin.Log.Warn($"[{Feature}] step {step.Label} refused at {StepMath.FormatTime(from)}: song cannot be seeked now");
            return;
        }

        // HandleDidSeek has spawned the objects at the new time.
        Plugin.Log.Info($"[{Feature}] step {step.Label}: {StepMath.FormatTime(from)} -> {StepMath.FormatTime(to)}");
    }

    // Any seek while active (a step, a Chapters skip): show the objects of the new position now.
    private void HandleDidSeek(float audioTime) => Guard.Run(Feature, () =>
    {
        if (!_toggle.IsActive || _failed)
        {
            return;
        }

        _seekedWhileActive = true;
        _ghoster.RestoreAll();
        SpawnAtCurrentTime();
        if (_config.GhostNotes)
        {
            _ghoster.Apply(_seeker.LiveObjects);
        }
    });

    // The callbacks updater is paused with the game: run one callbacks pass for the frozen song time
    // so the objects there spawn now instead of on resume, place each once and pause it like the rest.
    private void SpawnAtCurrentTime()
    {
        if (_seeker.HasNoodleCallbacks)
        {
            // Noodle spawns from its own callbacks manager, which this pass does not drive.
            if (!_loggedNoodle)
            {
                _loggedNoodle = true;
                Plugin.Log.Info($"[{Feature}] objects appear on resume (Noodle)");
            }

            return;
        }

        _callbacksController.ManualUpdate(SongTime);
        var live = _seeker.LiveObjects;
        for (var i = 0; i < live.Count; i++)
        {
            switch (live[i])
            {
                case NoteController note:
                    note.ManualUpdate();
                    break;
                case ObstacleController obstacle:
                    obstacle.ManualUpdate();
                    break;
                case SliderController slider:
                    slider.ManualUpdate();
                    break;
            }
        }

        _beatmapObjectManager.PauseAllBeatmapObjects(pause: true);

        // The spawn above scheduled a cut sound per note (PlayScheduled, each holding an audio
        // voice). They are useless here: these objects are respawned with fresh timing on resume
        // (RespawnIfSeeked). Left alone, a burst of steps exhausts Unity's audio channels and Unity
        // then stops the PAUSED music source, after which no resume path can restart the song.
        _seeker.StopCutSounds();
    }

    private float EyeHeight() => Mathf.Clamp(_setupData.playerSpecificSettings.playerHeight - 0.1f, 1.2f, 2.1f);
}
