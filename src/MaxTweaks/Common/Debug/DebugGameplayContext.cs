using System;
using System.Collections.Generic;
using MaxTweaks.Core.Debugging;
using Zenject;

namespace MaxTweaks.Common.Debugging;

/// <summary>
/// Gameplay debug commands: pause, resume, restart, exit (the same game code paths as the pause
/// menu buttons), and the Active pause flag verbs select-note, flag-open, flag-toggle, flag-save,
/// flag-cancel, flags-clear (through <see cref="IActivePauseDebug"/>, practice mode with Active pause
/// on), and njs &lt;value|default&gt; (through <see cref="IDifficultySwitchDebug"/>, practice mode with
/// Difficulty switch on; the status line then shows njs=current/default). Registered with the DebugCommandService while the player container lives, and logs
/// "event gameplay-start" / "event paused" / "event resumed" / "event gameplay-end".
/// </summary>
internal sealed class DebugGameplayContext : IDebugCommandContext, IInitializable, IDisposable
{
    private const string Feature = "Debug";
    private static readonly string[] AllVerbs =
    {
        "pause", "resume", "restart", "exit",
        "select-note", "flag-open", "flag-toggle", "flag-save", "flag-cancel", "flags-clear", "step",
        "njs",
    };

    private readonly DiContainer _container;
    private readonly DebugCommandService _service;
    private PauseController? _pause;

    public DebugGameplayContext(DiContainer container, DebugCommandService service)
    {
        _container = container;
        _service = service;
    }

    public IReadOnlyCollection<string> Verbs => AllVerbs;

    private bool IsPaused => _pause != null && _pause._paused != PauseController.PauseState.Playing;

    public void Initialize() => Guard.Run(Feature, () =>
    {
        _service.Gameplay = this;
        _pause = _container.TryResolve<PauseController>();
        if (_pause != null)
        {
            _pause.didPauseEvent += HandlePaused;
            _pause.didResumeEvent += HandleResumed;
        }

        DebugLog.Event("gameplay-start " + Describe());
    });

    public void Dispose() => Guard.Run(Feature, () =>
    {
        if (_pause != null)
        {
            _pause.didPauseEvent -= HandlePaused;
            _pause.didResumeEvent -= HandleResumed;
        }

        if (_service.Gameplay == this)
        {
            _service.Gameplay = null;
        }

        DebugLog.Event("gameplay-end");
    });

    public string Describe()
    {
        var setup = _container.TryResolve<GameplayCoreSceneSetupData>();
        var audio = _container.TryResolve<AudioTimeSyncController>();
        var key = setup?.beatmapKey ?? default;
        var practice = setup?.practiceSettings;
        return $"level={key.levelId ?? "?"} song='{setup?.beatmapLevel?.songName}' diff={key.beatmapCharacteristic?.serializedName}/{key.difficulty}"
            + $" practice={(practice != null ? $"start={practice.startSongTime:0.##},speed={practice.songSpeedMul:0.##}" : "no")}"
            + $" time={(audio != null ? audio.songTime.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "?")}"
            + $" paused={IsPaused}"
            + (ActivePause is { } activePause ? " " + activePause.Describe() : string.Empty)
            + (DifficultySwitchDebugHook.Current is { } difficultySwitch ? " " + difficultySwitch.Describe() : string.Empty);
    }

    private IActivePauseDebug? ActivePause => _container.TryResolve<IActivePauseDebug>() ?? ActivePauseDebugHook.Current;

    public string Handle(DebugCommandLine command)
    {
        if (command.Verb.StartsWith("flag", System.StringComparison.Ordinal) || command.Verb == "select-note" || command.Verb == "step")
        {
            return HandleFlags(command);
        }

        if (command.Verb == "njs")
        {
            return HandleNjs(command);
        }

        var pause = _pause ?? throw new DebugCommandException("PauseController not available in this scene");
        switch (command.Verb)
        {
            case "pause":
                if (IsPaused)
                {
                    return "already paused";
                }

                pause.Pause();
                return IsPaused ? "paused" : "pause refused by the game (intro or level end?)";
            case "resume":
                if (!IsPaused)
                {
                    return "not paused";
                }

                // Same path as the Continue button: un-hides objects, then the resume animation.
                pause.HandlePauseMenuManagerDidPressContinueButton();
                return "resume animation started";
            case "restart":
                if (IsPaused)
                {
                    pause.HandlePauseMenuManagerDidPressRestartButton();
                }
                else
                {
                    Need(_container.TryResolve<ILevelRestartController>(), "ILevelRestartController").RestartLevel();
                }

                return "restarting";
            case "exit":
                if (IsPaused)
                {
                    pause.HandlePauseMenuManagerDidPressMenuButton();
                }
                else
                {
                    Need(_container.TryResolve<IReturnToMenuController>(), "IReturnToMenuController").ReturnToMenu();
                }

                return "returning to menu";
            default:
                throw new DebugCommandException("unhandled verb");
        }
    }

    private string HandleFlags(DebugCommandLine command)
    {
        var flags = ActivePause ?? throw new DebugCommandException("Active pause is not running (practice mode with Active pause on only)");
        switch (command.Verb)
        {
            case "select-note":
                if (command.Args.Count != 1 || !int.TryParse(command.Args[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index))
                {
                    throw new DebugCommandException("usage: select-note <n> (0-based index of the live note, by time)");
                }

                return flags.SelectNote(index);
            case "flag-open":
                return flags.FlagOpen();
            case "flag-toggle":
                if (command.Args.Count == 0)
                {
                    throw new DebugCommandException("usage: flag-toggle <labelId> [labelId...]");
                }

                var results = new List<string>();
                foreach (var id in command.Args)
                {
                    results.Add(flags.FlagToggle(id));
                }

                return string.Join("; ", results);
            case "flag-save":
                return flags.FlagSave();
            case "flag-cancel":
                return flags.FlagCancel();
            case "flags-clear":
                return flags.FlagsClear();
            case "step":
                var stepCount = 1;
                if (command.Args.Count < 1 || command.Args.Count > 2
                    || (command.Args.Count == 2 && !int.TryParse(command.Args[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out stepCount)))
                {
                    throw new DebugCommandException("usage: step <label> [count] (label as on the panel, e.g. +1s or -1b)");
                }

                return flags.Step(command.Args[0], stepCount);
            default:
                throw new DebugCommandException("unhandled verb");
        }
    }

    private static string HandleNjs(DebugCommandLine command)
    {
        var difficultySwitch = DifficultySwitchDebugHook.Current ?? throw new DebugCommandException("Difficulty switch is not running (practice mode with Difficulty switch on only)");
        if (command.Args.Count != 1)
        {
            throw new DebugCommandException("usage: njs <value|default>");
        }

        if (string.Equals(command.Args[0], "default", System.StringComparison.OrdinalIgnoreCase))
        {
            return difficultySwitch.SetNoteJumpSpeed(null);
        }

        if (!DebugCommandParser.TryParseFloat(command.Args[0], out var njs))
        {
            throw new DebugCommandException($"bad NJS '{command.Args[0]}'; usage: njs <value|default>");
        }

        return difficultySwitch.SetNoteJumpSpeed(njs);
    }

    private static T Need<T>(T? value, string what)
        where T : class =>
        value ?? throw new DebugCommandException($"{what} not available");

    private void HandlePaused() => Guard.Run(Feature, () => DebugLog.Event("paused " + Describe()));

    private void HandleResumed() => Guard.Run(Feature, () => DebugLog.Event("resumed " + Describe()));
}
