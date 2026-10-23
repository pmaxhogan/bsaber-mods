using System;
using System.Collections.Generic;
using System.Diagnostics;
using MaxTweaks.Common;
using MaxTweaks.Common.Playback;
using MaxTweaks.Common.Toasts;
using MaxTweaks.Configuration;
using MaxTweaks.Core.DifficultySwitch;
using UnityEngine;
using Zenject;

namespace MaxTweaks.Features.DifficultySwitch;

/// <summary>
/// Practice gameplay: hold Y to show the current characteristic's difficulties, flick the right
/// stick to move the highlight, release Y to switch (see <see cref="SwitchInputMachine"/>). Works
/// while playing and in the pause menu. FPFC: Y key and the up/down arrow keys.
///
/// A switch is done in place (<see cref="InPlaceSwapper"/>: audio untouched, new difficulty's
/// objects after the clear gap, its NJS and offset, old notes slide away, score/combo carry over,
/// submission disabled). Difficulties that cannot be swapped in place (see <see cref="InPlaceGate"/>,
/// different environment, Noodle callbacks active) and any failed swap restart the level at the
/// current time on the target difficulty instead (<see cref="FallbackRestarter"/>).
/// Log lines for QA: "[DifficultySwitch] switched Expert -> ExpertPlus at 42.3s (in-place)" or
/// "(fallback restart: reason)".
/// </summary>
internal sealed class DifficultySwitchController : IInitializable, ITickable, IDisposable
{
    private const string Feature = "DifficultySwitch";
    private const string SubmissionReason = "Difficulty switch";
    private const float PreloadDelaySeconds = 2f;

    private readonly DifficultySwitchConfig _config;
    private readonly GameplayCoreSceneSetupData _setupData;
    private readonly ControllerInput _input;
    private readonly PlaybackSeeker _seeker;
    private readonly ActiveBeatmap _active;
    private readonly InPlaceSwapper _swapper;
    private readonly FallbackRestarter _restarter;
    private readonly BeatmapPreloader _preloader;
    private readonly NoteSlider _slider;
    private readonly IToastService? _toasts;
    private readonly IGamePause? _gamePause;

    private readonly SwitchInputMachine _machine = new();
    private readonly List<Entry> _entries = new();
    private readonly List<string> _labels = new();
    private DifficultyListOverlay? _overlay;
    private string? _sceneBlock;
    private float _initRealtime;
    private bool _preloadStarted;
    private bool _restarting;
    private bool _failed;

    public DifficultySwitchController(
        DifficultySwitchConfig config,
        GameplayCoreSceneSetupData setupData,
        ControllerInput input,
        PlaybackSeeker seeker,
        ActiveBeatmap active,
        InPlaceSwapper swapper,
        FallbackRestarter restarter,
        BeatmapPreloader preloader,
        NoteSlider slider,
        [InjectOptional] IToastService? toasts,
        [InjectOptional] IGamePause? gamePause)
    {
        _config = config;
        _setupData = setupData;
        _input = input;
        _seeker = seeker;
        _active = active;
        _swapper = swapper;
        _restarter = restarter;
        _preloader = preloader;
        _slider = slider;
        _toasts = toasts;
        _gamePause = gamePause;
    }

    private int CurrentIndex
    {
        get
        {
            var current = (int)_active.Key.difficulty;
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Rank == current)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    public void Initialize()
    {
        _initRealtime = Time.realtimeSinceStartup;
        Guard.Run(Feature, BuildCatalog);
        if (FallbackRestarter.PendingSubmissionDisable != null)
        {
            if (FallbackRestarter.PendingSubmissionDisable == _setupData.beatmapLevel.levelID)
            {
                _seeker.DisableSubmission(SubmissionReason);
            }

            FallbackRestarter.PendingSubmissionDisable = null;
        }
    }

    public void Tick()
    {
        if (_failed || _restarting)
        {
            return;
        }

        try
        {
            var deltaTime = Time.deltaTime;
            _slider.Tick(deltaTime);
            StartPreloadWhenSettled();

            var allowed = _config.Enabled && _seeker.CanSeek && _entries.Count > 0;
            var yHeld = _input.GetButton(ControllerButton.Y);
            var stickY = yHeld || _machine.IsOpen ? _input.GetRightStick().y : 0f;
            var currentIndex = CurrentIndex;
            switch (_machine.Update(yHeld, stickY, allowed, currentIndex, _entries.Count))
            {
                case SwitchInputEvent.Opened:
                    _overlay ??= new DifficultyListOverlay(_labels);
                    _overlay.Show(currentIndex, _labels, _machine.Highlight);
                    break;
                case SwitchInputEvent.Moved:
                    _overlay?.SetHighlight(_machine.Highlight);
                    break;
                case SwitchInputEvent.Committed:
                    _overlay?.Hide();
                    Switch(_entries[_machine.CommittedIndex]);
                    break;
                case SwitchInputEvent.Cancelled:
                    _overlay?.Hide();
                    break;
            }

            _overlay?.Tick(deltaTime);
        }
        catch (Exception e)
        {
            // Never spam the log every frame or keep touching gameplay: switch off for this song.
            _failed = true;
            Plugin.Log.Error($"[{Feature}] disabled for this song: {e}");
            _overlay?.Hide();
        }
    }

    public void Dispose()
    {
        _overlay?.Dispose();
        _overlay = null;
        _slider.Dispose();
    }

    private void BuildCatalog()
    {
        var key = _setupData.beatmapKey;
        var level = _setupData.beatmapLevel;
        var characteristic = key.beatmapCharacteristic;
        var currentEnvironment = level.GetEnvironmentName(characteristic, key.difficulty);
        if (_seeker.HasNoodleCallbacks)
        {
            _sceneBlock = "Noodle Extensions objects";
        }

        var ranks = new List<int>();
        foreach (var difficulty in level.GetDifficulties(characteristic))
        {
            ranks.Add((int)difficulty);
        }

        foreach (var rank in DifficultyOrder.HardestFirst(ranks))
        {
            var difficulty = (BeatmapDifficulty)rank;
            var entryKey = new BeatmapKey(key.levelId, characteristic, difficulty);
            var songCore = Guard.Run(Feature, () => SongCore.Collections.GetCustomLevelSongDifficultyData(entryKey), null);
            var extra = songCore?.additionalDifficultyData;
            var block = InPlaceGate.Check(extra?._requirements, extra?._suggestions, characteristic.containsRotationEvents);
            if (block == null && level.GetEnvironmentName(characteristic, difficulty) != currentEnvironment)
            {
                block = "different environment";
            }

            _entries.Add(new Entry(rank, entryKey, block));
            _labels.Add(DifficultyOrder.Label(rank, songCore?._difficultyLabel));
        }

        var summary = new System.Text.StringBuilder();
        foreach (var entry in _entries)
        {
            summary.Append(summary.Length == 0 ? string.Empty : ", ")
                .Append(entry.Key.difficulty)
                .Append(entry.Block == null ? string.Empty : " [restart: " + entry.Block + "]");
        }

        Plugin.Log.Info($"[{Feature}] ready on {characteristic.serializedName}/{key.difficulty}: {summary}{(_sceneBlock == null ? string.Empty : " (all restart: " + _sceneBlock + ")")}");
    }

    private void StartPreloadWhenSettled()
    {
        if (_preloadStarted || Time.realtimeSinceStartup - _initRealtime < PreloadDelaySeconds || !_config.Enabled)
        {
            return;
        }

        _preloadStarted = true;
        if (_sceneBlock != null)
        {
            return;
        }

        foreach (var entry in _entries)
        {
            if (entry.Block == null && !entry.Key.Equals(_active.Key))
            {
                _preloader.Queue(entry.Key);
            }
        }
    }

    private void Switch(Entry target)
    {
        var from = _active.Key;
        if (target.Key.Equals(from))
        {
            return;
        }

        var currentIndex = CurrentIndex;
        var block = _sceneBlock ?? target.Block ?? (currentIndex >= 0 ? _entries[currentIndex].Block : null);
        if (block != null)
        {
            Fallback(from, target.Key, block, failed: false);
            return;
        }

        var now = _seeker.SongTime;
        try
        {
            var watch = Stopwatch.StartNew();
            var preloaded = _preloader.TryTake(target.Key, out var data);
            if (!preloaded)
            {
                data = _preloader.LoadNow(target.Key) ?? throw new InvalidOperationException($"no beatmap data for {target.Key.difficulty}");
            }

            var objectsFrom = _swapper.Swap(target.Key, data, _config.ClearGapSeconds);
            var paused = _gamePause != null && _gamePause.isPaused;
            _slider.ClearLiveObjects(_config.SlideOldNotes && !paused, SlideOut.For((int)from.difficulty, (int)target.Key.difficulty));
            _seeker.DisableSubmission(SubmissionReason);
            _active.Set(target.Key);
            _preloader.Queue(from);
            watch.Stop();

            Plugin.Log.Info($"[{Feature}] switched {from.difficulty} -> {target.Key.difficulty} at {now:0.0}s (in-place); objects from {objectsFrom:0.0}s, {(preloaded ? "preloaded" : "loaded now")}, {watch.ElapsedMilliseconds} ms");
            _toasts?.Show($"Switched to {LabelOf(target.Key)}", 2.5f);
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"[{Feature}] in-place switch {from.difficulty} -> {target.Key.difficulty} failed: {e}");
            Fallback(from, target.Key, "in-place swap failed: " + e.Message, failed: true);
        }
    }

    private void Fallback(BeatmapKey from, BeatmapKey to, string reason, bool failed)
    {
        var now = _seeker.SongTime;
        if (!_restarter.IsAvailable)
        {
            Plugin.Log.Error($"[{Feature}] cannot switch {from.difficulty} -> {to.difficulty}: {reason}; restart unavailable");
            _toasts?.Show($"Switch to {LabelOf(to)} failed");
            return;
        }

        Plugin.Log.Info($"[{Feature}] switched {from.difficulty} -> {to.difficulty} at {now:0.0}s (fallback restart: {reason})");
        _toasts?.Show(failed ? $"Switch failed, restarting on {LabelOf(to)}" : $"Restarting on {LabelOf(to)}", 3f);
        try
        {
            _restarting = true;
            _slider.FinishAll();
            _restarter.Restart(to, now);
        }
        catch (Exception e)
        {
            _restarting = false;
            Plugin.Log.Error($"[{Feature}] fallback restart failed: {e}");
            _toasts?.Show($"Switch to {LabelOf(to)} failed");
        }
    }

    private string LabelOf(BeatmapKey key)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Key.Equals(key))
            {
                return _labels[i];
            }
        }

        return DifficultyOrder.DefaultLabel((int)key.difficulty);
    }

    private readonly struct Entry
    {
        public Entry(int rank, BeatmapKey key, string? block)
        {
            Rank = rank;
            Key = key;
            Block = block;
        }

        public int Rank { get; }

        public BeatmapKey Key { get; }

        /// <summary>Why this difficulty needs the fallback restart, or null for in-place.</summary>
        public string? Block { get; }
    }
}
