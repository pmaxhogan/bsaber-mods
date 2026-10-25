using System;
using System.Collections.Generic;
using MaxTweaks.Common;
using MaxTweaks.Common.Playback;
using MaxTweaks.Common.Pointing;
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

    private BpmTimeline BuildTempo() => BeatmapTempo.Build(_beatmapData, _setupData, _bpmInitData);

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
        // Sit above the gameplay clock as seen from a slightly low eye (player height minus a little
        // for ducking), so the clock never overlaps the bottom row.
        var eye = Mathf.Clamp(_setupData.playerSpecificSettings.playerHeight - 0.2f, 1.2f, 2.0f);
        var clock = ClockLocator.Find();
        var bottomY = ChaptersPlacement.BottomY(clock, eye);
        Plugin.Log.Info($"[{Feature}] stack bottom at y {bottomY:0.00} m (clock {(clock == null ? "none" : $"y {clock.Y:0.00} z {clock.Z:0.00}")}, eye {eye:0.00} m)");
        _display = new ChaptersDisplay(rows, bottomY, _config.ShowProgressBars, CanSkip, HandleArrowClicked);
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
            else
            {
                if (paused)
                {
                    hit = HitFrom(display, _menuControllers?.RightController) ??
                          HitFrom(display, _menuControllers?.LeftController);
                }

                // Sabers while playing, and while paused with the sabers still shown (MaxTweaks
                // active pause; the pause menu hides them, so they never hit there). A is on the
                // right controller, so the right saber wins when both point at arrows.
                if (hit == null && _saberManager != null)
                {
                    hit = HitFrom(display, _saberManager.rightSaber) ??
                          HitFrom(display, _saberManager.leftSaber);
                }
            }
        }

        display.SetRayHover(hit);
        if (hit != null && _input.GetButtonDown(ControllerButton.A))
        {
            Skip(hit.Row, hit.ToEnd);
        }
    }

    private ChapterArrowButton? HitFromCamera(ChaptersDisplay display) =>
        PointerRays.TryFromCamera(ref _camera, out var ray) ? display.HitTest(ray) : null;

    private static ChapterArrowButton? HitFrom(ChaptersDisplay display, VRController? controller) =>
        PointerRays.TryFromController(controller, out var ray) ? display.HitTest(ray) : null;

    private static ChapterArrowButton? HitFrom(ChaptersDisplay display, Saber? saber) =>
        PointerRays.TryFromSaber(saber, out var ray) ? display.HitTest(ray) : null;

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
