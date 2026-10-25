using System;
using System.Collections.Generic;
using System.Linq;
using MaxTweaks.Common;
using MaxTweaks.Common.Debugging;
using MaxTweaks.Common.Playback;
using MaxTweaks.Common.Pointing;
using MaxTweaks.Common.Toasts;
using MaxTweaks.Configuration;
using MaxTweaks.Core.ActivePause;
using MaxTweaks.Core.ActivePause.Flags;
using MaxTweaks.Core.Chapters;
using UnityEngine;
using Zenject;
using Object = UnityEngine.Object;

namespace MaxTweaks.Features.ActivePause;

/// <summary>
/// Practice gameplay: X pauses the song in place without the pause menu ("active pause"), shows the
/// twelve seek buttons (<see cref="StepButtonPanel"/>) and ghosts every beatmap object
/// (<see cref="ObjectGhoster"/>); X again resumes immediately (no countdown, objects stay where they
/// are). Point a saber at a button and press A or that hand's trigger (FPFC: camera ray and Enter or
/// Space) to step; each step seeks
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
/// Flags (see docs/flags.md): while active-paused the saber ray also picks frozen notes (nearest
/// hit; panel buttons and Chapters arrows win), and the press toggles their selection
/// (<see cref="NoteSelection"/>, drawn solid with a halo by <see cref="NoteHighlighter"/>, kept
/// across steps and resume until Clear or the song ends). Flag opens the <see cref="FeedbackPanel"/>;
/// Save appends the flag to flags.json next to the map (<see cref="FlagWriter"/>). Custom levels only.
///
/// Log lines for QA: "[ActivePause] on at 42.317s", "[ActivePause] off at 42.317s",
/// "[ActivePause] step +1b: 42.317s -> 42.857s", "[ActivePause] dropped: pause menu opened",
/// "[ActivePause] selected note at 42.317s (1 selected)", "[ActivePause] deselected ...",
/// "[ActivePause] flag saved &lt;id&gt; at 42.317s: 2 labels, 3 notes, screenshot",
/// "[ActivePause] flag file error: ...".
/// </summary>
internal sealed class ActivePauseController : IInitializable, ITickable, IDisposable, IActivePauseDebug
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
    private readonly IToastService? _toasts;

    private readonly ActivePauseToggle _toggle = new();
    private readonly ObjectGhoster _ghoster = new();
    private readonly NoteSelection _selection = new();
    private readonly NoteHighlighter _highlighter = new();
    private readonly System.Random _random = new();
    private readonly Func<IBeatmapObjectController, bool> _keepSolid;
    private StepButtonPanel? _panel;
    private FeedbackPanel? _feedback;
    private string? _flagFolder;
    private bool _flagFolderResolved;
    private bool _highlightFailed;
    private string _lastFlagResult = "no flag saved yet";
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
        [InjectOptional] ActiveBeatmap? activeBeatmap,
        [InjectOptional] IToastService? toasts)
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
        _toasts = toasts;
        _keepSolid = item => item is NoteController note && note != null && _selection.IsSelected(note);
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
        ActivePauseDebugHook.Current = this;
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
                Ghost();
                UpdatePointing();
            }
            else
            {
                _selection.SetHovered(null);

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

            return;
        }

        UpdateHighlights();
    }

    // Halos follow the selected notes while playing too. A failure here only turns the halos off.
    private void UpdateHighlights()
    {
        if (_highlightFailed)
        {
            return;
        }

        try
        {
            _highlighter.Update(_seeker.LiveObjects, _selection);
        }
        catch (Exception e)
        {
            _highlightFailed = true;
            Plugin.Log.Error($"[{Feature}] note highlights off for this song: {e}");
            Guard.Run(Feature, _highlighter.Clear);
        }
    }

    private void Ghost()
    {
        if (_config.GhostNotes)
        {
            _ghoster.Apply(_seeker.LiveObjects, _selection.Count > 0 ? _keepSolid : null);
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
        if (ActivePauseDebugHook.Current == this)
        {
            ActivePauseDebugHook.Current = null;
        }

        _panel?.Dispose();
        _panel = null;
        _feedback?.Dispose();
        _feedback = null;
        _highlighter.Dispose();
        _selection.Clear();
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
        Ghost();

        if (_panel == null)
        {
            _panel = new StepButtonPanel(EyeHeight(), HandleButtonClicked);
            _panel.SetFlagAvailable(FlagFolder() != null);
        }

        _panel.SetSelectedCount(_selection.Count);
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
        Guard.Run(Feature, () => CloseFeedback("resume"));
        Guard.Run(Feature, () => _panel?.Hide());
        _selection.SetHovered(null);
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

        CloseFeedback("pause menu");
        _panel?.Hide();
        _selection.SetHovered(null);
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

        var target = default(PointerTarget);
        var hand = ControllerHand.Right;
        Ray ray;
        if (_input.IsFpfc)
        {
            if (PointerRays.TryFromCamera(ref _camera, out ray))
            {
                target = FindTarget(ray);
            }
        }
        else if (_saberManager != null)
        {
            // The right saber wins when both point at something.
            if (PointerRays.TryFromSaber(_saberManager.rightSaber, out ray))
            {
                target = FindTarget(ray);
            }

            if (target.Kind == TargetKind.None && PointerRays.TryFromSaber(_saberManager.leftSaber, out ray))
            {
                target = FindTarget(ray);
                hand = ControllerHand.Left;
            }
        }

        panel.SetRayHover(target.Kind == TargetKind.Step ? target.Index : -1);
        _feedback?.SetRayHover(target.Kind == TargetKind.Feedback ? target.Index : -1);
        _selection.SetHovered(target.Note);

        // A, or the trigger of the hand whose saber aims (one press per edge).
        if (target.Kind == TargetKind.None || !_input.PressDown(hand))
        {
            return;
        }

        switch (target.Kind)
        {
            case TargetKind.Step:
                PressPanelButton(target.Index);
                break;
            case TargetKind.Feedback:
                _feedback?.Press(target.Index);
                break;
            case TargetKind.Note:
                ToggleNote(target.Note!);
                break;
        }
    }

    // Panel buttons first (the feedback panel replaces the step panel), then notes, unless a
    // Chapters arrow is aimed at (Chapters handles that press itself).
    private PointerTarget FindTarget(Ray ray)
    {
        if (_feedback != null && _feedback.Visible)
        {
            var index = _feedback.HitTest(ray);
            if (index >= 0)
            {
                return new PointerTarget(TargetKind.Feedback, index, null);
            }
        }
        else if (_panel != null)
        {
            var index = _panel.HitTest(ray);
            if (index >= 0)
            {
                return new PointerTarget(TargetKind.Step, index, null);
            }
        }

        if (PointerClaims.ClaimedRecently)
        {
            return default;
        }

        var note = NoteSelection.HitTest(ray, _seeker.LiveObjects);
        return note != null ? new PointerTarget(TargetKind.Note, -1, note) : default;
    }

    private void HandleButtonClicked(int index)
    {
        if (_toggle.IsActive && !_failed)
        {
            PressPanelButton(index);
        }
    }

    private void PressPanelButton(int index)
    {
        switch (index)
        {
            case StepButtonPanel.FlagIndex:
                OpenFeedback();
                break;
            case StepButtonPanel.ClearIndex:
                ClearSelection();
                break;
            default:
                Step(index);
                break;
        }
    }

    private FlagNote ToggleNote(NoteController note)
    {
        var (key, selected) = _selection.Toggle(note, _tempo);
        Plugin.Log.Info($"[{Feature}] {(selected ? "selected" : "deselected")} {FlagNote.KindId(key.Kind)} at {StepMath.FormatTime(key.Time)} ({_selection.Count} selected)");
        SelectionChanged();
        return key;
    }

    private int ClearSelection()
    {
        var count = _selection.Count;
        _selection.Clear();
        Plugin.Log.Info($"[{Feature}] selection cleared ({count} notes)");
        SelectionChanged();
        return count;
    }

    private void SelectionChanged()
    {
        _panel?.SetSelectedCount(_selection.Count);
        if (_feedback != null && _feedback.Visible)
        {
            _feedback.Show(FeedbackTitle());
        }

        // Selected notes are drawn solid, deselected ones ghosted again, from this frame on.
        if (_toggle.IsActive)
        {
            Ghost();
        }
    }

    private string? FlagFolder()
    {
        if (!_flagFolderResolved)
        {
            _flagFolderResolved = true;
            _flagFolder = Guard.Run(Feature, () => CustomLevelFolders.TryGetFolder(_setupData.beatmapLevel), null);
            if (_flagFolder == null)
            {
                Plugin.Log.Info($"[{Feature}] flags off: not a custom level with a map folder");
                _toasts?.Show("Flags need a custom level");
            }
        }

        return _flagFolder;
    }

    private void OpenFeedback()
    {
        if (FlagFolder() == null || _panel == null)
        {
            return;
        }

        _feedback ??= new FeedbackPanel(EyeHeight(), SaveFlag, () => CloseFeedback("cancel"));
        _panel.Hide();
        _feedback.Show(FeedbackTitle());
        Plugin.Log.Info($"[{Feature}] flag panel open at {StepMath.FormatTime(SongTime)} ({_selection.Count} notes selected)");
    }

    private string FeedbackTitle()
    {
        var notes = _selection.Count;
        return $"Flag at {StepMath.FormatTime(SongTime)}, {notes} {(notes == 1 ? "note" : "notes")}";
    }

    // Cancel (also on resume and when the pause menu opens): hides the feedback panel without
    // saving, clears its labels and shows the step panel again while still active.
    private void CloseFeedback(string reason)
    {
        if (_feedback == null || !_feedback.Visible)
        {
            return;
        }

        _feedback.ClearLabels();
        _feedback.Hide();
        if (_toggle.IsActive)
        {
            _panel?.Show();
        }

        Plugin.Log.Info($"[{Feature}] flag panel closed ({reason})");
    }

    private void SaveFlag(IReadOnlyList<string> labels)
    {
        var folder = FlagFolder();
        if (folder == null || _feedback == null)
        {
            return;
        }

        var time = SongTime;
        var key = _activeBeatmap?.Key ?? _setupData.beatmapKey;
        var now = DateTime.UtcNow;
        var flag = new FlagRecord
        {
            Id = FlagIds.New(now, _random),
            CreatedAtUtc = now,
            Characteristic = key.beatmapCharacteristic != null ? key.beatmapCharacteristic.serializedName : "Standard",
            Difficulty = key.difficulty.ToString(),
            SongTime = time,
            Beat = _tempo != null ? _tempo.SecondsToBeat(time) : time * StepMath.FallbackBpm / 60f,
            Speed = _seeker.TimeScale,
            Labels = labels.ToArray(),
            Notes = _selection.Selected.ToArray(),
        };

        // The panel hides in this frame, before the end-of-frame screenshot, so it is not in it.
        _feedback.ClearLabels();
        _feedback.Hide();
        _panel?.Show();

        var result = FlagWriter.Save(flag, folder, key.levelId ?? string.Empty, _setupData.beatmapLevel?.songName ?? string.Empty, screenshot: true);
        var counts = $"{flag.Labels.Count} {(flag.Labels.Count == 1 ? "label" : "labels")}, {flag.Notes.Count} {(flag.Notes.Count == 1 ? "note" : "notes")}";
        if (result.Ok)
        {
            Plugin.Log.Info($"[{Feature}] flag saved {flag.Id} at {StepMath.FormatTime(time)}: {counts}{(flag.Screenshot != null ? ", screenshot" : string.Empty)} ({result.Path})");
            _lastFlagResult = $"saved {flag.Id} at {StepMath.FormatTime(time)}: {counts}, screenshot={flag.Screenshot ?? "none"}";
            _toasts?.Show($"Flag saved: {counts}" + (result.CorruptBackup != null ? $" (unreadable old file kept as {result.CorruptBackup})" : string.Empty));
        }
        else
        {
            Plugin.Log.Error($"[{Feature}] flag file error: {result.Error} ({(result.Path.Length > 0 ? result.Path : folder)})");
            _lastFlagResult = "not saved: " + result.Error;
            _toasts?.Show($"Flag not saved: {result.Error}");
        }
    }

    private void Step(int index)
    {
        if (index < 0 || index >= ActivePauseStep.All.Count)
        {
            return;
        }

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
        _selection.SetHovered(null);
        SpawnAtCurrentTime();
        Ghost();
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

    // --- Debug channel (IActivePauseDebug): the same flows as the buttons, without aiming. ---

    public string SelectNote(int index)
    {
        RequireActive();
        var notes = new List<NoteController>();
        foreach (var item in _seeker.LiveObjects)
        {
            if (NoteSelection.IsSelectable(item, out var note))
            {
                notes.Add(note);
            }
        }

        notes.Sort((a, b) =>
        {
            var byTime = a.noteData.time.CompareTo(b.noteData.time);
            if (byTime != 0)
            {
                return byTime;
            }

            var byLine = a.noteData.lineIndex.CompareTo(b.noteData.lineIndex);
            return byLine != 0 ? byLine : ((int)a.noteData.noteLineLayer).CompareTo((int)b.noteData.noteLineLayer);
        });

        if (index < 0 || index >= notes.Count)
        {
            throw new DebugCommandException($"no live note {index} ({notes.Count} selectable live notes)");
        }

        var key = ToggleNote(notes[index]);
        return $"{(_selection.IsSelected(key) ? "selected" : "deselected")} {key} ({_selection.Count} selected)";
    }

    public string FlagOpen()
    {
        RequireActive();
        if (FlagFolder() == null)
        {
            throw new DebugCommandException("flags need a custom level with a map folder");
        }

        if (_feedback != null && _feedback.Visible)
        {
            return "flag panel already open";
        }

        OpenFeedback();
        return $"flag panel open ({_selection.Count} notes selected)";
    }

    public string FlagToggle(string labelId)
    {
        var feedback = RequireFeedback();
        if (!feedback.Toggle(labelId))
        {
            throw new DebugCommandException($"unknown label '{labelId}' (ids: {string.Join(" ", FlagLabel.All.Select(l => l.Id))})");
        }

        return $"{labelId} {(feedback.LitLabels.Contains(labelId) ? "on" : "off")}";
    }

    public string FlagSave()
    {
        RequireFeedback().Press(FeedbackPanel.SaveIndex);
        return _lastFlagResult;
    }

    public string FlagCancel()
    {
        RequireFeedback().Press(FeedbackPanel.CancelIndex);
        return "flag panel closed without saving";
    }

    public string FlagsClear() => $"cleared {ClearSelection()} selected notes";

    public string Describe() =>
        $"activePause={(_toggle.IsActive ? "on" : "off")} selectedNotes={_selection.Count}"
        + $" flagPanel={(_feedback != null && _feedback.Visible ? "open" : "closed")}"
        + $" flagLabels=[{(_feedback != null ? string.Join(",", _feedback.LitLabels) : string.Empty)}]"
        + $" flagFolder={(_flagFolderResolved ? (_flagFolder != null ? "yes" : "none") : "?")}";

    private void RequireActive()
    {
        if (!_toggle.IsActive || _failed)
        {
            throw new DebugCommandException("not active-paused (press X in practice mode first: button X press)");
        }
    }

    private FeedbackPanel RequireFeedback()
    {
        RequireActive();
        return _feedback != null && _feedback.Visible
            ? _feedback
            : throw new DebugCommandException("flag panel not open (flag-open first)");
    }

    private enum TargetKind
    {
        None,
        Step,
        Feedback,
        Note,
    }

    private readonly struct PointerTarget
    {
        public PointerTarget(TargetKind kind, int index, NoteController? note)
        {
            Kind = kind;
            Index = index;
            Note = note;
        }

        public TargetKind Kind { get; }

        public int Index { get; }

        public NoteController? Note { get; }
    }
}
