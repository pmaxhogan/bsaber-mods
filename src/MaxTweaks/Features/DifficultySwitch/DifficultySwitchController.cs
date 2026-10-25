using System;
using System.Collections.Generic;
using System.Diagnostics;
using MaxTweaks.Common;
using MaxTweaks.Common.Debugging;
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
///
/// NJS row: with the list open, flick the right stick left/right (FPFC: left/right arrows) to show
/// the NJS row (<see cref="NjsRow"/>, centred on the difficulty's own NJS, highlight on the NJS in
/// effect now) and move along it; release Y on another value to change only the NJS, in place
/// (<see cref="InPlaceSwapper.SetNoteJumpSpeed"/>; PracticePlugin's panel follows). Not offered while
/// Noodle Extensions objects are active (Noodle keeps its own per-object jump data; PracticePlugin
/// also disables its NJS slider there). A difficulty switch resets the NJS to the new difficulty's
/// own; a restart forgets it. Log line: "[DifficultySwitch] njs 16 -> 18 (default 16); ...".
/// </summary>
internal sealed class DifficultySwitchController : IInitializable, ITickable, IDisposable, IDifficultySwitchDebug
{
    private const string Feature = "DifficultySwitch";
    private const string SubmissionReason = "Difficulty switch";
    private const string NjsSubmissionReason = "NJS switch";
    private const string NjsUnavailableToast = "NJS switch is not available on this map";
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
    private NjsRow? _njsRow;
    private bool _njsUnavailableShown;
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
        DifficultySwitchDebugHook.Current = this;
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
            var stick = yHeld || _machine.IsOpen ? _input.GetRightStick() : Vector2.zero;
            var currentIndex = CurrentIndex;
            var row = _machine.IsOpen ? _njsRow : null;
            switch (_machine.Update(yHeld, stick.x, stick.y, allowed, currentIndex, _entries.Count, row?.Count ?? 0, row?.CurrentIndex ?? -1))
            {
                case SwitchInputEvent.Opened:
                    _njsRow = BuildNjsRow();
                    _overlay ??= new DifficultyListOverlay(_labels);
                    _overlay.Show(currentIndex, _labels, _machine.Highlight, _njsRow != null);
                    break;
                case SwitchInputEvent.Moved:
                    _overlay?.SetHighlight(_machine.Highlight);
                    QueuePreload(_machine.Highlight);
                    break;
                case SwitchInputEvent.Committed:
                    _overlay?.Hide();
                    Switch(_entries[_machine.CommittedIndex]);
                    break;
                case SwitchInputEvent.Cancelled:
                    _overlay?.Hide();
                    break;
                case SwitchInputEvent.RowOpened:
                    if (row != null)
                    {
                        _overlay?.ShowNjsRow(row, _machine.RowHighlight);
                    }

                    break;
                case SwitchInputEvent.RowMoved:
                    _overlay?.SetNjsHighlight(_machine.RowHighlight);
                    break;
                case SwitchInputEvent.RowCommitted:
                    _overlay?.Hide();
                    if (row != null)
                    {
                        ApplyNjs(row.Values[_machine.CommittedRowIndex]);
                    }

                    break;
                case SwitchInputEvent.RowUnavailable:
                    if (!_njsUnavailableShown)
                    {
                        _njsUnavailableShown = true;
                        Plugin.Log.Info($"[{Feature}] NJS row not available: {NjsBlock ?? "no NJS values"}");
                        _toasts?.Show(NjsUnavailableToast, 2.5f);
                    }

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

            // Sliding notes are paused and uncuttable; without Tick they would hang and fly in as misses.
            Guard.Run(Feature, _slider.FinishAll);
        }
    }

    public void Dispose()
    {
        if (DifficultySwitchDebugHook.Current == this)
        {
            DifficultySwitchDebugHook.Current = null;
        }

        _overlay?.Dispose();
        _overlay = null;
        _slider.Dispose();
    }

    /// <summary>Why the NJS row is not offered on this song, or null.</summary>
    private string? NjsBlock => _sceneBlock;

    /// <summary>The NJS the player has now: PracticePlugin's panel value when it is loaded (before its optional speed scaling), else the spawn controller's.</summary>
    private float CurrentNjs => Guard.Run(Feature, () => _seeker.PracticePlugin.ReadNoteJumpSpeed(), null) ?? _swapper.CurrentNoteJumpSpeed;

    private NjsRow? BuildNjsRow()
    {
        if (NjsBlock != null)
        {
            return null;
        }

        var row = Guard.Run(Feature, () => NjsRow.Build(_swapper.DefaultNoteJumpSpeed(_active.Key), CurrentNjs), null);
        return row != null && row.Count > 0 ? row : null;
    }

    /// <summary>
    /// Changes the NJS in place (row release or the debug verb). Failures log, toast and leave the
    /// song running on the old NJS (the swapper rolls back).
    /// </summary>
    /// <returns>What happened, for the debug channel.</returns>
    private string ApplyNjs(float njs)
    {
        var defaultNjs = _swapper.DefaultNoteJumpSpeed(_active.Key);
        var from = CurrentNjs;
        if (NjsRow.Same(from, njs))
        {
            return $"NJS already {NjsRow.Label(njs)}";
        }

        var now = _seeker.SongTime;
        try
        {
            var practicePlugin = _seeker.PracticePlugin;
            var adjust = Guard.Run(Feature, () => practicePlugin.AdjustsNoteJumpSpeedWithSpeed(), false);
            var timeScale = _seeker.TimeScale;
            var applied = adjust && timeScale > 0f ? njs / timeScale : njs;
            var aheadBefore = _seeker.SpawnAheadTime;

            _seeker.DisableSubmission(NjsSubmissionReason);
            var ahead = _swapper.SetNoteJumpSpeed(applied);
            Guard.Run(Feature, () => practicePlugin.AfterNoteJumpSpeedChange(njs));

            var detail = $"at {now:0.0}s, spawn ahead {aheadBefore:0.00}s -> {ahead:0.00}s" + (adjust ? $", spawn NJS {applied:0.##} (PracticePlugin adjusts NJS with speed x{timeScale:0.##})" : string.Empty);
            Plugin.Log.Info($"[{Feature}] {NjsRow.LogChange(from, njs, defaultNjs)}; {detail}");
            var toast = NjsRow.Toast(njs, defaultNjs);
            _toasts?.Show(toast, 2.5f);
            return toast + "; " + detail;
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"[{Feature}] NJS change {NjsRow.Label(from)} -> {NjsRow.Label(njs)} failed: {e}");
            _toasts?.Show("NJS change failed");
            return "NJS change failed: " + e.Message;
        }
    }

    string IDifficultySwitchDebug.SetNoteJumpSpeed(float? njs)
    {
        if (_failed || _restarting)
        {
            throw new DebugCommandException("difficulty switch is off for this song (failed or restarting)");
        }

        if (!_config.Enabled || !_seeker.CanSeek)
        {
            throw new DebugCommandException("not allowed now (Difficulty switch disabled or the song is not running)");
        }

        if (NjsBlock != null)
        {
            throw new DebugCommandException($"NJS switch not available: {NjsBlock}");
        }

        var target = njs ?? _swapper.DefaultNoteJumpSpeed(_active.Key);
        if (!(target > 0f) || float.IsInfinity(target))
        {
            throw new DebugCommandException("NJS must be a positive number");
        }

        return ApplyNjs(target);
    }

    string IDifficultySwitchDebug.Describe()
    {
        if (NjsBlock != null)
        {
            return "njs=unavailable";
        }

        return Guard.Run(Feature, () => NjsRow.Status(CurrentNjs, _swapper.DefaultNoteJumpSpeed(_active.Key)), null) ?? "njs=?";
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

            _entries.Add(new Entry(rank, entryKey, block, InPlaceGate.SuggestsChroma(extra?._suggestions)));
            _labels.Add(DifficultyOrder.Label(rank, songCore?._difficultyLabel));
        }

        var summary = new System.Text.StringBuilder();
        foreach (var entry in _entries)
        {
            summary.Append(summary.Length == 0 ? string.Empty : ", ")
                .Append(entry.Key.difficulty)
                .Append(entry.Block == null ? (entry.SuggestsChroma ? " [in-place, Chroma data refresh]" : string.Empty) : " [restart: " + entry.Block + "]");
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
        QueueNeighbours();
    }

    // Only the difficulties one row up and down are preloaded (each is a full beatmap; loading all of
    // them at once caused GC spikes); others load when the highlight reaches them.
    private void QueueNeighbours()
    {
        var current = CurrentIndex;
        QueuePreload(current - 1);
        QueuePreload(current + 1);
    }

    private void QueuePreload(int index)
    {
        if (_sceneBlock != null || index < 0 || index >= _entries.Count)
        {
            return;
        }

        var entry = _entries[index];
        if (entry.Block == null && !entry.Key.Equals(_active.Key))
        {
            _preloader.Queue(entry.Key);
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
            Fallback(from, target.Key, block, failed: false, needsInit: true);
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

            if (target.SuggestsChroma)
            {
                Plugin.Log.Info($"[{Feature}] {target.Key.difficulty} suggests Chroma: swapping in place and refreshing Heck/Chroma custom data");
            }

            // Before touching the data: even a swap that fails half-way must not submit.
            _seeker.DisableSubmission(SubmissionReason);
            var objectsFrom = _swapper.Swap(target.Key, data, _config.ClearGapSeconds, (float)watch.Elapsed.TotalSeconds);
            var paused = _gamePause != null && _gamePause.isPaused;
            _slider.ClearLiveObjects(_config.SlideOldNotes && !paused, SlideOut.For((int)from.difficulty, (int)target.Key.difficulty));
            Guard.Run(Feature, () => _restarter.Retarget(target.Key, null));
            _active.Set(target.Key);
            QueueNeighbours();
            watch.Stop();

            Plugin.Log.Info($"[{Feature}] switched {from.difficulty} -> {target.Key.difficulty} at {now:0.0}s (in-place); objects from {objectsFrom:0.0}s, {(preloaded ? "preloaded" : "loaded now")}, {_swapper.LastCustomDataNote}, {watch.ElapsedMilliseconds} ms");
            _toasts?.Show($"Switched to {LabelOf(target.Key)}", 2.5f);
        }
        catch (Exception e)
        {
            Plugin.Log.Error($"[{Feature}] in-place switch {from.difficulty} -> {target.Key.difficulty} failed: {e}");
            Fallback(from, target.Key, "in-place swap failed: " + e.Message, failed: true, needsInit: false);
        }
    }

    private void Fallback(BeatmapKey from, BeatmapKey to, string reason, bool failed, bool needsInit)
    {
        var now = _seeker.SongTime;
        if (!_restarter.IsAvailable)
        {
            Plugin.Log.Error($"[{Feature}] cannot switch {from.difficulty} -> {to.difficulty}: {reason}; restart unavailable");
            _toasts?.Show($"Switch to {LabelOf(to)} failed");
            return;
        }

        try
        {
            _restarting = true;
            _overlay?.Hide();
            _slider.FinishAll();
            var how = _restarter.Restart(to, now, needsInit);
            Plugin.Log.Info($"[{Feature}] switched {from.difficulty} -> {to.difficulty} at {now:0.0}s (fallback restart: {reason}; {how})");
            _toasts?.Show(failed ? $"Switch failed, restarting on {LabelOf(to)}" : $"Restarting on {LabelOf(to)}", 3f);
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
        public Entry(int rank, BeatmapKey key, string? block, bool suggestsChroma)
        {
            Rank = rank;
            Key = key;
            Block = block;
            SuggestsChroma = suggestsChroma;
        }

        public bool SuggestsChroma { get; }

        public int Rank { get; }

        public BeatmapKey Key { get; }

        /// <summary>Why this difficulty needs the fallback restart, or null for in-place.</summary>
        public string? Block { get; }
    }
}
