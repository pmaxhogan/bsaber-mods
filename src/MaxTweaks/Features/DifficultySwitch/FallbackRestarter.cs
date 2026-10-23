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
/// StandardLevelScenesTransitionSetupDataSO and its scene setup data objects, and reloads the beatmap
/// through GameplayCoreSceneSetupData.LoadTransformedBeatmapDataAsync before the new scenes start.
/// So we retarget those objects in place before restarting: GameplayCoreSceneSetupData.beatmapKey,
/// beatmapBasicData and practiceSettings, StandardGameplaySceneSetupData.beatmapKey, and the SO's
/// beatmapKey/practiceSettings. Mutating the existing objects (instead of building new ones) keeps
/// every mod's subclass/patch intact (Heck replaces the core setup data with its own subclass). The
/// new scene therefore starts as if the level had been launched on that difficulty, so any
/// half-applied in-place state of the old scene is discarded with it.
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

    public FallbackRestarter(
        GameplayCoreSceneSetupData setupData,
        AudioTimeSyncController audioTimeSyncController,
        [InjectOptional] ILevelRestartController? restartController,
        [InjectOptional] StandardLevelScenesTransitionSetupDataSO? transitionSetupData)
    {
        _setupData = setupData;
        _audioTimeSyncController = audioTimeSyncController;
        _restartController = restartController;
        _transitionSetupData = transitionSetupData;
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
    public void Restart(BeatmapKey target, float songTime)
    {
        var restart = _restartController ?? throw new InvalidOperationException("no level restart controller");
        var transition = TransitionSetupData ?? throw new InvalidOperationException("no standard level transition setup data");
        var setup = _setupData;
        var basic = setup.beatmapLevel.GetDifficultyBeatmapData(target.beatmapCharacteristic, target.difficulty)
            ?? throw new InvalidOperationException($"no basic data for {target.difficulty}");

        var atsc = _audioTimeSyncController;
        var latest = Mathf.Max(0f, atsc.songEndTime - 1f);
        var start = Mathf.Clamp(songTime, 0f, latest);
        var speed = atsc.timeScale > 0f ? atsc.timeScale : setup.practiceSettings?.songSpeedMul ?? 1f;
        var practice = new PracticeSettings(start, speed);

        ReadonlyFields.Set(setup, nameof(GameplayCoreSceneSetupData.beatmapKey), target);
        ReadonlyFields.Set(setup, nameof(GameplayCoreSceneSetupData.beatmapBasicData), basic);
        ReadonlyFields.Set(setup, nameof(GameplayCoreSceneSetupData.practiceSettings), practice);
        setup.ApplyDisableUpdateAlwaysConfiguration();

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
        ReadonlyFields.SetAutoProperty(transition, nameof(StandardLevelScenesTransitionSetupDataSO.practiceSettings), practice);

        // The scene replace is deferred, so setting this after the call is still in time; if the
        // restart throws, nothing lingers for the next start of this level.
        restart.RestartLevel();
        PendingSubmissionDisable = setup.beatmapLevel.levelID;
    }
}
