using System;
using System.Collections.Generic;
using MaxTweaks.Common;
using MaxTweaks.Common.Playback;
using MaxTweaks.Configuration;
using MaxTweaks.Core.Chapters;
using SiraUtil.Services;
using UnityEngine;
using Zenject;

namespace MaxTweaks.Features.Chapters;

/// <summary>
/// Gameplay side of Chapters: resolves the file for the played difficulty once the song length is
/// known, drives <see cref="ChaptersDisplay"/> every frame and, in practice mode, handles skipping:
/// point at an arrow (sabers while playing, the pause-menu controllers while paused, the camera in
/// FPFC) and press A, or click it with the trigger in the pause menu.
/// </summary>
internal sealed class ChaptersController : IInitializable, ITickable, IDisposable
{
    private const string Feature = "Chapters";
    private const int MaxRows = 6;
    private const float SkipCooldownSeconds = 0.35f;
    private const float LeadInEpsilon = 0.25f;

    private readonly ChaptersConfig _config;
    private readonly ChaptersDocument _document;
    private readonly GameplayCoreSceneSetupData _setupData;
    private readonly AudioTimeSyncController _audioTimeSyncController;
    private readonly IReadonlyBeatmapData _beatmapData;
    private readonly ControllerInput _input;
    private readonly BpmController.InitData? _bpmInitData;
    private readonly PlaybackSeeker? _seeker;
    private readonly IGamePause? _gamePause;
    private readonly SaberManager? _saberManager;
    private readonly IMenuControllerAccessor? _menuControllers;
    private readonly ActiveBeatmap? _activeBeatmap;

    private readonly int[] _active = new int[MaxRows];
    private ChapterStack? _stack;
    private ChaptersDisplay? _display;
    private ChapterSlots? _slots;
    private BpmTimeline? _tempo;
    private Camera? _camera;
    private bool _failed;
    private float _lastSkipRealtime = float.MinValue;

    public ChaptersController(
        ChaptersConfig config,
        ChaptersDocument document,
        GameplayCoreSceneSetupData setupData,
        AudioTimeSyncController audioTimeSyncController,
        IReadonlyBeatmapData beatmapData,
        ControllerInput input,
        [InjectOptional] BpmController.InitData? bpmInitData,
        [InjectOptional] PlaybackSeeker? seeker,
        [InjectOptional] IGamePause? gamePause,
        [InjectOptional] SaberManager? saberManager,
        [InjectOptional] IMenuControllerAccessor? menuControllers,
        [InjectOptional] ActiveBeatmap? activeBeatmap)
    {
        _config = config;
        _document = document;
        _setupData = setupData;
        _audioTimeSyncController = audioTimeSyncController;
        _beatmapData = beatmapData;
        _input = input;
        _bpmInitData = bpmInitData;
        _seeker = seeker;
        _gamePause = gamePause;
        _saberManager = saberManager;
        _menuControllers = menuControllers;
        _activeBeatmap = activeBeatmap;
    }

    private bool CanSkip => _seeker != null && _setupData.practiceSettings != null;

    public void Initialize()
    {
        Guard.Run(Feature, () => _tempo = BuildTempo());
        if (_activeBeatmap != null)
        {
            _activeBeatmap.Changed += HandleDifficultyChanged;
        }
    }

    // Difficulty switch replaced the beatmap data in place: re-read tempo and re-resolve chapters
    // for the new difficulty on the next tick.
    private void HandleDifficultyChanged(BeatmapKey oldKey, BeatmapKey newKey)
    {
        _display?.Dispose();
        _display = null;
        _slots = null;
        _stack = null;
        _failed = false;
        _tempo = BuildTempo();
    }

    public void Tick()
    {
        if (_failed)
        {
            return;
        }

        try
        {
            if (_stack == null && !TryResolve())
            {
                return;
            }

            UpdateFrame();
        }
        catch (Exception e)
        {
            // Never let a display bug spam the log every frame or touch gameplay: switch off for this song.
            _failed = true;
            Plugin.Log.Error($"[{Feature}] disabled for this song: {e}");
            _display?.Dispose();
            _display = null;
        }
    }

    public void Dispose()
    {
        if (_activeBeatmap != null)
        {
            _activeBeatmap.Changed -= HandleDifficultyChanged;
        }

        _display?.Dispose();
        _display = null;
    }

    private BpmTimeline BuildTempo()
    {
        var changes = new List<BpmChange>();
        for (var node = _beatmapData.allBeatmapDataItems.First; node != null; node = node.Next)
        {
            if (node.Value is BPMChangeBeatmapEventData bpmChange)
            {
                changes.Add(new BpmChange(bpmChange.time, bpmChange.bpm));
            }
        }

        var startBpm = _bpmInitData?.startBpm ?? 0f;
        if (!(startBpm > 0f))
        {
            startBpm = _setupData.beatmapLevel.beatsPerMinute;
        }

        return BpmTimeline.FromSecondChanges(startBpm, changes);
    }

    private bool TryResolve()
    {
        // songEndTime needs the clip and the audio latency, both set in AudioTimeSyncController.Start;
        // isReady turns true on its first Update.
        if (!_audioTimeSyncController.isReady || _audioTimeSyncController.songLength <= 0f)
        {
            return false;
        }

        var key = _activeBeatmap?.Key ?? _setupData.beatmapKey;
        var characteristic = key.beatmapCharacteristic != null ? key.beatmapCharacteristic.serializedName : "Standard";
        var difficulty = key.difficulty.ToString();
        var songEnd = _audioTimeSyncController.songEndTime;
        var warnings = new List<string>();
        var chapters = ChapterResolver.Resolve(_document, _tempo, characteristic, difficulty, songEnd, warnings);
        foreach (var warning in warnings)
        {
            Plugin.Log.Warn($"[{Feature}] {warning}");
        }

        _stack = new ChapterStack(chapters);
        Plugin.Log.Info($"[{Feature}] {_stack.Count} chapter(s) for {characteristic}/{difficulty}, up to {_stack.MaxConcurrent} at once, skip {(CanSkip ? "on" : "off")}");
        if (_stack.Count == 0)
        {
            _failed = true; // nothing to show; stop ticking work
            return false;
        }

        var rows = Mathf.Clamp(_stack.MaxConcurrent, 1, MaxRows);
        _display = new ChaptersDisplay(rows, _config.ShowProgressBars, CanSkip, HandleArrowClicked);
        _slots = new ChapterSlots(rows);
        return true;
    }

    private void UpdateFrame()
    {
        var display = _display!;
        var stack = _stack!;
        var slots = _slots!;
        var time = _audioTimeSyncController.songTime;
        var count = stack.GetActive(time, _active);
        if (count > display.RowCount)
        {
            count = display.RowCount;
        }

        // Rows stay put while an arrow is aimed at, so the target cannot change under the pointer.
        slots.Update(_active, count, display.AnyArrowHovered);
        var visible = 0;
        for (var row = 0; row < slots.RowCount; row++)
        {
            var index = slots.ChapterAt(row);
            if (index < 0)
            {
                display.HideRow(row);
                continue;
            }

            var chapter = stack.Chapters[index];
            display.SetRow(row, index, chapter.Label, chapter.Progress(time));
            visible++;
        }

        if (CanSkip)
        {
            UpdatePointing(display, visible);
        }
    }

    private void UpdatePointing(ChaptersDisplay display, int activeRows)
    {
        ChapterArrowButton? hit = null;
        if (activeRows > 0)
        {
            var paused = _gamePause != null && _gamePause.isPaused;
            if (_input.IsFpfc)
            {
                hit = HitFromCamera(display);
            }
            else if (paused)
            {
                hit = HitFrom(display, _menuControllers?.RightController) ??
                      HitFrom(display, _menuControllers?.LeftController);
            }
            else if (_saberManager != null)
            {
                // A is on the right controller, so the right saber wins when both point at arrows.
                hit = HitFrom(display, _saberManager.rightSaber) ??
                      HitFrom(display, _saberManager.leftSaber);
            }
        }

        display.SetRayHover(hit);
        if (hit != null && _input.GetButtonDown(ControllerButton.A))
        {
            Skip(hit.Row, hit.ToEnd);
        }
    }

    private ChapterArrowButton? HitFromCamera(ChaptersDisplay display)
    {
        if (_camera == null)
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                return null;
            }
        }

        var t = _camera.transform;
        return display.HitTest(new Ray(t.position, t.forward));
    }

    private static ChapterArrowButton? HitFrom(ChaptersDisplay display, VRController? controller)
    {
        if (controller == null || !controller.isActiveAndEnabled)
        {
            return null;
        }

        var t = controller.transform;
        return display.HitTest(new Ray(t.position, t.forward));
    }

    private static ChapterArrowButton? HitFrom(ChaptersDisplay display, Saber? saber)
    {
        if (saber == null || !saber.isActiveAndEnabled)
        {
            return null;
        }

        var bottom = saber.saberBladeBottomPos;
        var direction = saber.saberBladeTopPos - bottom;
        if (direction.sqrMagnitude < 1e-6f)
        {
            return null;
        }

        return display.HitTest(new Ray(bottom, direction.normalized));
    }

    private void HandleArrowClicked(int row, bool toEnd)
    {
        if (CanSkip)
        {
            Skip(row, toEnd);
        }
    }

    private void Skip(int row, bool toEnd)
    {
        var now = Time.realtimeSinceStartup;
        if (now - _lastSkipRealtime < SkipCooldownSeconds || _display == null || _stack == null || _seeker == null)
        {
            return;
        }

        var index = _display.ChapterAt(row);
        if (index < 0 || index >= _stack.Count)
        {
            return;
        }

        _lastSkipRealtime = now;
        var chapter = _stack.Chapters[index];
        var target = toEnd ? chapter.End : chapter.Start;

        // Audio starts a little early so the first notes at the target fly in from the far end
        // instead of appearing right in front of the player; notes before the target are dropped.
        // A ">" press while already inside that lead-in window (typically a second ">" right after
        // the first) jumps straight to the end without lead-in, so repeated ">" always leaves the
        // chapter instead of landing on the same spot again.
        var leadIn = Mathf.Clamp(_seeker.SpawnAheadTime + 0.1f, 0.5f, 3f);
        if (toEnd && chapter.End - _audioTimeSyncController.songTime <= leadIn + LeadInEpsilon)
        {
            leadIn = 0f;
        }

        var reason = toEnd ? "Chapters skip to end" : "Chapters skip to start";
        if (_seeker.SeekTo(target - leadIn, target, reason))
        {
            Plugin.Log.Info($"[{Feature}] {(toEnd ? "end" : "start")} of {chapter}");
        }
    }
}
