using System;
using System.Collections.Generic;
using MaxTweaks.Common;
using MaxTweaks.Configuration;
using MaxTweaks.Core.Blink;
using UnityEngine;
using Zenject;

namespace MaxTweaks.Features.Blink;

/// <summary>
/// Per-song driver: indexes the map once at load, then each frame feeds the song clock to the
/// Core scheduler and shows its opacity on the overlay. Allocation free per frame.
/// Hooks (Beat Saber 1.40.8, all bound by GameplayCoreInstaller in Main.dll):
/// IReadonlyBeatmapData (transformed map, items time-sorted in seconds), AudioTimeSyncController
/// (songTime / timeScale / songEndTime / state; Paused while the game is paused),
/// AudioTimeSyncController.InitData.startSongTime (practice start), IJumpOffsetYProvider (wall
/// height offset, same value BeatmapObjectSpawnMovementData.GetObstacleSpawnData adds) and
/// PlayerTransforms.headPseudoLocalPos for the live head position.
/// </summary>
internal class BlinkController : IInitializable, ITickable, IDisposable, IWallHeadTest
{
    private const string Feature = "Blink";

    /// <summary>Head half-size plus safety, metres.</summary>
    private const float HeadMargin = 0.15f;

    /// <summary>Eyes sit roughly this far below the configured player height.</summary>
    private const float EyeBelowHeight = 0.1f;

    /// <summary>Where a re-centred player's head is, across the lanes (play-space centre).</summary>
    private const float NeutralHeadX = 0f;

    /// <summary>Used when PlayerTransforms is unavailable: a centred standing player.</summary>
    private const float FallbackHeadY = 1.7f;

    private readonly BlinkConfig _config;
    private readonly BlinkCooldown _cooldown;
    private readonly BlinkAssets _assets;
    private readonly IReadonlyBeatmapData _beatmapData;
    private readonly AudioTimeSyncController _audio;
    private readonly AudioTimeSyncController.InitData _audioInit;
    private readonly IJumpOffsetYProvider _jumpOffset;
    private readonly GameplayCoreSceneSetupData _setupData;
    private readonly ControllerInput _input;
    private readonly PlayerTransforms? _player;
    private readonly Func<bool> _tick;

    private BlinkScheduler? _scheduler;
    private BlinkOverlay? _overlay;
    private BlinkSettings _settings;
    private ObstacleData[] _walls = Array.Empty<ObstacleData>();
    private bool[] _wallForcedHit = Array.Empty<bool>();
    private int _lineCount = 4;
    private float _standingY = FallbackHeadY;
    private Vector3 _head;
    private float _jumpOffsetY;
    private bool _wasBlinking;

    public BlinkController(
        BlinkConfig config,
        BlinkCooldown cooldown,
        BlinkAssets assets,
        IReadonlyBeatmapData beatmapData,
        AudioTimeSyncController audio,
        AudioTimeSyncController.InitData audioInit,
        IJumpOffsetYProvider jumpOffset,
        GameplayCoreSceneSetupData setupData,
        ControllerInput input,
        [InjectOptional] PlayerTransforms? player)
    {
        _config = config;
        _cooldown = cooldown;
        _assets = assets;
        _beatmapData = beatmapData;
        _audio = audio;
        _audioInit = audioInit;
        _jumpOffset = jumpOffset;
        _setupData = setupData;
        _input = input;
        _player = player;
        _tick = TickBody;
    }

    public void Initialize()
    {
        Guard.Run(Feature, () =>
        {
            _settings = new BlinkSettings(_config.QuietBeforeSeconds, _config.ClearAheadSeconds, _config.DurationSeconds, _config.CooldownSeconds);
            _lineCount = _beatmapData.numberOfLines;
            var playerHeight = _setupData.playerSpecificSettings?.playerHeight ?? 0f;
            _standingY = playerHeight > 0.5f ? playerHeight - EyeBelowHeight : FallbackHeadY;

            var timeline = BuildTimeline();
            _scheduler = new BlinkScheduler(timeline, _cooldown, _audioInit.startSongTime);
            _overlay = new BlinkOverlay(_assets, _config.VignetteStrength);
            Plugin.Log.Debug($"[{Feature}] indexed {timeline.NoteCount} notes/chains and {timeline.WallCount} walls; cooldown {_cooldown.PlayedSinceLastBlink:F0}/{_settings.Cooldown:F0}s played; head from {(_player != null ? "PlayerTransforms" : "fallback (no PlayerTransforms)")}, standing eye height {_standingY:F2} m");
        });
    }

    public void Tick()
    {
        if (_scheduler == null)
        {
            return;
        }

        // A failure inside the frame body would repeat (and log) every frame: after the first one
        // Guard has logged it, so switch Blink off for the rest of this song and hide the overlay.
        if (!Guard.Run(Feature, _tick, false))
        {
            _scheduler = null;
            Plugin.Log.Warn($"[{Feature}] disabled for the rest of this song after an error");
            Guard.Run(Feature, () => _overlay?.Dispose());
        }
    }

    public void Dispose()
    {
        Guard.Run(Feature, () =>
        {
            _overlay?.Dispose();
            _overlay = null;
            _scheduler = null;
        });
    }

    /// <summary>IWallHeadTest: would wall <paramref name="wallId"/> hit the head as it is now?</summary>
    public bool HitsHead(int wallId)
    {
        if (_wallForcedHit[wallId])
        {
            return true;
        }

        var wall = _walls[wallId];

        // 90/360 maps: bring the head into the wall's own (rotated) lane frame.
        var head = wall.rotation == 0 ? _head : Quaternion.Euler(0f, -wall.rotation, 0f) * _head;
        var zone = HeadZone.Around(head.x, head.y, NeutralHeadX, _standingY, HeadMargin);
        return WallGeometry.HitsHead(wall.lineIndex, (int)wall.lineLayer, wall.width, wall.height, _lineCount, _jumpOffsetY, zone);
    }

    /// <returns>Always true; false comes from Guard when this throws.</returns>
    private bool TickBody()
    {
        var scheduler = _scheduler!;

        // QA aid: in FPFC desktop mode, K forces a blink so the overlay can be screenshotted.
        if (_input.IsFpfc && Input.GetKeyDown(KeyCode.K))
        {
            scheduler.ForceStart();
            Plugin.Log.Info($"[{Feature}] debug blink forced at {_audio.songTime:F1}s");
        }

        var paused = _audio.state != AudioTimeSyncController.State.Playing;
        _head = _player != null ? _player.headPseudoLocalPos : new Vector3(0f, FallbackHeadY, 0f);
        _jumpOffsetY = _jumpOffset.jumpOffsetY;

        var alpha = scheduler.Tick(_audio.songTime, _audio.timeScale, _audio.songEndTime, paused, _settings, this);
        _overlay!.SetAlpha(alpha);

        if (scheduler.IsBlinking != _wasBlinking)
        {
            _wasBlinking = scheduler.IsBlinking;
            if (_wasBlinking)
            {
                Plugin.Log.Info($"[{Feature}] blink at {_audio.songTime:F1}s");
            }
        }

        return true;
    }

    private BlinkTimeline BuildTimeline()
    {
        var builder = new BlinkTimeline.Builder();
        var walls = new List<ObstacleData>();
        var forced = new List<bool>();
        var modded = 0;

        foreach (var item in _beatmapData.allBeatmapDataItems)
        {
            switch (item)
            {
                // Every note counts, bombs and chain heads included.
                case NoteData note:
                    builder.AddNote(note.time);
                    break;

                // Chains (burst sliders) count from head to tail; arcs alone do not.
                case SliderData slider when slider.sliderType == SliderData.Type.Burst:
                    builder.AddNoteSpan(slider.time, slider.tailTime);
                    break;

                case ObstacleData wall:
                    var isModded = ModdedWallCheck.HasCustomPlacement(wall);
                    modded += isModded ? 1 : 0;
                    builder.AddWall(wall.time, wall.time + wall.duration, walls.Count);
                    walls.Add(wall);
                    forced.Add(isModded);
                    break;
            }
        }

        _walls = walls.ToArray();
        _wallForcedHit = forced.ToArray();
        if (modded > 0)
        {
            Plugin.Log.Debug($"[{Feature}] {modded} walls have custom placement; treated as head walls");
        }

        return builder.Build();
    }
}
