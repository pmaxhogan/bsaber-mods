using System;
using System.Collections.Generic;
using MaxTweaks.Core.Debugging;
using Zenject;

namespace MaxTweaks.Common.Debugging;

/// <summary>
/// Gameplay debug commands: pause, resume, restart, exit. Uses the same game code paths as the
/// pause menu buttons. Registered with the DebugCommandService while the player container lives,
/// and logs "event gameplay-start" / "event paused" / "event resumed" / "event gameplay-end".
/// </summary>
internal sealed class DebugGameplayContext : IDebugCommandContext, IInitializable, IDisposable
{
    private const string Feature = "Debug";
    private static readonly string[] AllVerbs = { "pause", "resume", "restart", "exit" };

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
            + $" paused={IsPaused}";
    }

    public string Handle(DebugCommandLine command)
    {
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

    private static T Need<T>(T? value, string what)
        where T : class =>
        value ?? throw new DebugCommandException($"{what} not available");

    private void HandlePaused() => Guard.Run(Feature, () => DebugLog.Event("paused " + Describe()));

    private void HandleResumed() => Guard.Run(Feature, () => DebugLog.Event("resumed " + Describe()));
}
