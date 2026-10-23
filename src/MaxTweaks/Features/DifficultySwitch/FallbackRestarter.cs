using System;
using MaxTweaks.Common.Playback;
using UnityEngine;
using Zenject;

namespace MaxTweaks.Features.DifficultySwitch;

/// <summary>
/// Fallback when the in-place swap is not possible or failed: restart the level on the target
/// difficulty in practice mode at the current song time and speed (the audio restarts; the owner
/// accepted that for this case only).
///
/// The game's restart (StandardLevelRestartController.RestartLevel -> MenuTransitionsHelper
/// HandleMainGameSceneDidFinish -> GameScenesManager.ReplaceScenes) reuses the SAME
/// StandardLevelScenesTransitionSetupDataSO and never calls its Init again. Mods hook Init, not the
/// restart: Heck's StandardModuleActivator prefix switches the Noodle/Chroma modules (and their
/// patches) on or off for the map, and Init itself resolves the environment and colour scheme. So the
/// restart first runs Init again with the arguments of the original level start
/// (<see cref="TransitionInitCapture"/>), only the difficulty key and the practice settings (current
/// time and speed) replaced, then calls RestartLevel; the new scenes start exactly as if the level had
/// been launched on that difficulty (a Noodle-required target gets Noodle, another environment gets
/// its environment), and any half-applied in-place state of the old scene is discarded with it.
/// The running scene is paused first, so nothing of it updates while the mods' state already belongs
/// to the target during the transition fade. If RestartLevel throws, Init runs again with the original
/// arguments.
///
/// Without a captured start (capture patch failed, or the level was started without Init) the
/// restart can only retarget the existing setup data in place (<see cref="Retarget"/>); that is
/// refused when the difficulty needs different mods or another environment.
///
/// A switch disables score submission for the rest of that song; the restarted scene is a new
/// container, so the request is carried over in <see cref="PendingSubmissionDisable"/> and consumed by
/// the next DifficultySwitchController of the same level.
/// </summary>
internal sealed class FallbackRestarter
{
    private readonly GameplayCoreSceneSetupData _setupData;
    private readonly AudioTimeSyncController _audioTimeSyncController;
    private readonly ILevelRestartController? _restartController;
    private readonly StandardLevelScenesTransitionSetupDataSO? _transitionSetupData;
    private readonly PauseMenuManager? _pauseMenuManager;
    private readonly PauseMenuManager.InitData? _pauseMenuInitData;
    private readonly IGamePause? _gamePause;

    public FallbackRestarter(
        GameplayCoreSceneSetupData setupData,
        AudioTimeSyncController audioTimeSyncController,
        [InjectOptional] ILevelRestartController? restartController,
        [InjectOptional] StandardLevelScenesTransitionSetupDataSO? transitionSetupData,
        [InjectOptional] PauseMenuManager? pauseMenuManager,
        [InjectOptional] PauseMenuManager.InitData? pauseMenuInitData,
        [InjectOptional] IGamePause? gamePause)
    {
        _gamePause = gamePause;
        _setupData = setupData;
        _audioTimeSyncController = audioTimeSyncController;
        _restartController = restartController;
        _transitionSetupData = transitionSetupData;
        _pauseMenuManager = pauseMenuManager;
        _pauseMenuInitData = pauseMenuInitData;
    }

    /// <summary>Level id whose next start should disable submission (set by a fallback restart).</summary>
    public static string? PendingSubmissionDisable { get; set; }

    /// <summary>True when the restart path is available in this scene.</summary>
    public bool IsAvailable => _restartController != null && TransitionSetupData != null;

    private StandardLevelScenesTransitionSetupDataSO? TransitionSetupData =>
        _transitionSetupData != null
            ? _transitionSetupData
            : (_restartController as StandardLevelRestartController)?._standardLevelSceneSetupData;

    /// <summary>Restarts on <paramref name="target"/> at <paramref name="songTime"/>; throws when it cannot.</summary>
    /// <param name="needsInit">
    /// The target may need different mods or another environment than the running scene, so the
    /// level start must be re-run (no retarget-only restart).
    /// </param>
    /// <returns>How the restart was set up (for the log).</returns>
    public string Restart(BeatmapKey target, float songTime, bool needsInit)
    {
        var restart = _restartController ?? throw new InvalidOperationException("no level restart controller");
        var transition = TransitionSetupData ?? throw new InvalidOperationException("no standard level transition setup data");

        var atsc = _audioTimeSyncController;
        var latest = Mathf.Max(0f, atsc.songEndTime - 1f);
        var start = Mathf.Clamp(songTime, 0f, latest);
        var speed = atsc.timeScale > 0f ? atsc.timeScale : _setupData.practiceSettings?.songSpeedMul ?? 1f;
        var practice = new PracticeSettings(start, speed);

        var capture = TransitionInitCapture.Last;
        var canInit = capture != null && ReferenceEquals(capture.Target, transition);
        if (!canInit && needsInit)
        {
            throw new InvalidOperationException("the level start was not captured, so a restart cannot switch mods or environment");
        }

        if (_gamePause != null && !_gamePause.isPaused)
        {
            _gamePause.Pause();
        }

        string how;
        if (canInit)
        {
            var original = capture!;
            original.Method.Invoke(transition, original.With(target, practice));
            try
            {
                restart.RestartLevel();
            }
            catch
            {
                RunInit(original, original.Args);
                throw;
            }

            how = "level start re-run for the target";
        }
        else
        {
            var oldKey = _setupData.beatmapKey;
            var oldPractice = _setupData.practiceSettings;
            Retarget(target, practice);
            try
            {
                restart.RestartLevel();
            }
            catch
            {
                try
                {
                    Retarget(oldKey, oldPractice);
                }
                catch (Exception e)
                {
                    Plugin.Log.Error($"[DifficultySwitch] restoring the setup data failed: {e}");
                }

                throw;
            }

            how = "setup data retargeted";
        }

        // The scene replace is deferred, so setting this after the call is still in time; if the
        // restart throws, nothing lingers for the next start of this level.
        PendingSubmissionDisable = _setupData.beatmapLevel.levelID;
        return how;
    }

    private static void RunInit(CapturedInit capture, object[] args)
    {
        try
        {
            capture.Method.Invoke(capture.Target, (object[])args.Clone());
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"[DifficultySwitch] restoring the level start failed: {e}");
        }
    }

    /// <summary>
    /// Points the scene's setup data at <paramref name="target"/>, so the pause-menu Restart, the
    /// pause-menu level bar and the results screen use it. Also used after an in-place switch (then
    /// <paramref name="practice"/> is null and the practice settings stay as they are; a Restart from
    /// the pause menu then starts the new difficulty at the original practice start time).
    ///
    /// Updated: GameplayCoreSceneSetupData.beatmapKey / beatmapBasicData (/ practiceSettings),
    /// StandardGameplaySceneSetupData.beatmapKey, the transition setup data's beatmapKey
    /// (/ practiceSettings), PauseMenuManager.InitData.beatmapKey plus a level bar refresh.
    /// Not updatable: the BeatmapKey struct and BeatmapBasicData bound by value in the GameCore
    /// container (GameplayCoreInstaller) for anything that injected them at scene start.
    /// </summary>
    public void Retarget(BeatmapKey target, PracticeSettings? practice)
    {
        var setup = _setupData;
        var basic = setup.beatmapLevel.GetDifficultyBeatmapData(target.beatmapCharacteristic, target.difficulty)
            ?? throw new InvalidOperationException($"no basic data for {target.difficulty}");

        ReadonlyFields.Set(setup, nameof(GameplayCoreSceneSetupData.beatmapKey), target);
        ReadonlyFields.Set(setup, nameof(GameplayCoreSceneSetupData.beatmapBasicData), basic);
        if (practice != null)
        {
            ReadonlyFields.Set(setup, nameof(GameplayCoreSceneSetupData.practiceSettings), practice);
        }

        setup.ApplyDisableUpdateAlwaysConfiguration();

        var transition = TransitionSetupData;
        if (transition != null)
        {
            var scenes = ReadonlyFields.Get<SceneSetupData[]>(transition, "_sceneSetupDataArray");
            if (scenes != null)
            {
                foreach (var scene in scenes)
                {
                    if (scene is StandardGameplaySceneSetupData gameplay)
                    {
                        ReadonlyFields.Set(gameplay, nameof(StandardGameplaySceneSetupData.beatmapKey), target);
                    }
                }
            }

            ReadonlyFields.SetAutoProperty(transition, nameof(StandardLevelScenesTransitionSetupDataSO.beatmapKey), target);
            if (practice != null)
            {
                ReadonlyFields.SetAutoProperty(transition, nameof(StandardLevelScenesTransitionSetupDataSO.practiceSettings), practice);
            }
        }

        if (_pauseMenuInitData != null)
        {
            ReadonlyFields.Set(_pauseMenuInitData, nameof(PauseMenuManager.InitData.beatmapKey), target);
            var levelBar = _pauseMenuManager != null ? _pauseMenuManager._levelBar : null;
            if (_pauseMenuInitData.showLevelBar && levelBar != null)
            {
                levelBar.Setup(setup.beatmapLevel, target.difficulty, target.beatmapCharacteristic);
            }
        }
    }
}
