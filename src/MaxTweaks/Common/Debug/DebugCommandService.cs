using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IPA.Utilities;
using MaxTweaks.Core.Debugging;
using UnityEngine;
using Zenject;

namespace MaxTweaks.Common.Debugging;

/// <summary>A scene-scoped set of debug commands (menu or gameplay) that registers with the service.</summary>
internal interface IDebugCommandContext
{
    IReadOnlyCollection<string> Verbs { get; }

    /// <returns>The "ok" message. Throw DebugCommandException for a user-facing error.</returns>
    string Handle(DebugCommandLine command);

    string Describe();
}

/// <summary>
/// The --debug command channel (App container). Polls UserData/MaxTweaks.debug-commands.txt, runs
/// one command per frame and logs "[MaxTweaks.Debug] ok #id verb: ..." or "error #id ...". Also
/// logs scene changes, song loading, menu readiness and every ControllerInput button edge as
/// "[MaxTweaks.Debug] event ..." lines for scripts/qa to wait on. See docs/qa.md.
/// </summary>
internal sealed class DebugCommandService : IInitializable, ITickable, IDisposable
{
    public const string CommandFileName = "MaxTweaks.debug-commands.txt";
    private const string Feature = "Debug";
    private const float PollInterval = 0.1f;

    private static readonly ControllerButton[] Buttons =
        { ControllerButton.A, ControllerButton.B, ControllerButton.X, ControllerButton.Y };

    private static readonly string[] OwnVerbs =
    {
        "help", "log", "status", "button", "stick", "release-all",
        "find-hint", "find-text", "find-name", "click-hint", "click-text", "click-name",
        "table", "search", "textinfo", "patches",
    };

    private readonly DiContainer _container;
    private readonly ControllerInput _input;
    private readonly VirtualController _virtual = new();
    private readonly Queue<string> _pending = new();
    private readonly List<(float At, string Label, Action Action)> _scheduled = new();
    private readonly string _path;
    private GameScenesManager? _scenes;
    private float _nextPoll;
    private string _lastStick = "0,0";
    private bool _menuReadyLogged;

    public DebugCommandService(DiContainer container, ControllerInput input)
    {
        _container = container;
        _input = input;
        _path = Path.Combine(UnityGame.UserDataPath, CommandFileName);
    }

    public IDebugCommandContext? Menu { get; set; }

    public IDebugCommandContext? Gameplay { get; set; }

    public bool InTransition => _scenes != null && _scenes.isInTransition;

    public bool InGameplay => Gameplay != null;

    public void Initialize()
    {
        Guard.Run(Feature, () =>
        {
            _input.DebugOverride = _virtual;
            var stale = ReadAndClear();
            DebugLog.Info($"enabled; command file {_path}" + (stale.Count > 0 ? $" (discarded {stale.Count} stale line(s))" : string.Empty));
            SongCore.Loader.SongsLoadedEvent += HandleSongsLoaded;
        });
    }

    public void Dispose()
    {
        Guard.Run(Feature, () =>
        {
            SongCore.Loader.SongsLoadedEvent -= HandleSongsLoaded;
            if (_scenes != null)
            {
                _scenes.transitionDidFinishEvent -= HandleTransitionDidFinish;
            }

            if (_input.DebugOverride == _virtual)
            {
                _input.DebugOverride = null;
            }
        });
    }

    /// <summary>Run an action on the main thread after a delay (unscaled real time).</summary>
    public void Schedule(float delaySeconds, string label, Action action) =>
        _scheduled.Add((Time.realtimeSinceStartup + delaySeconds, label, action));

    public void Tick()
    {
        Guard.Run(Feature, () =>
        {
            HookScenesManager();
            LogInputEdges();
            CheckMenuReady();
            RunScheduled();

            var now = Time.realtimeSinceStartup;
            if (now >= _nextPoll)
            {
                _nextPoll = now + PollInterval;
                foreach (var line in ReadAndClear())
                {
                    _pending.Enqueue(line);
                }
            }

            // One command per frame so e.g. "button Y down" and "button Y up" never share a frame.
            if (_pending.Count > 0)
            {
                Execute(_pending.Dequeue());
            }
        });
    }

    public string SceneSummary()
    {
        if (_scenes == null)
        {
            return "scene=unknown";
        }

        var names = string.Join(",", _scenes.GetCurrentlyLoadedSceneNames());
        var state = InTransition ? "transition" : InGameplay ? "gameplay" : Menu != null ? "menu" : "other";
        return $"scene={state} scenes=[{names}]";
    }

    private void Execute(string line)
    {
        DebugCommandLine? command;
        try
        {
            command = DebugCommandParser.ParseLine(line);
        }
        catch (Exception e)
        {
            DebugLog.Error(null, "parse", e.Message);
            return;
        }

        if (command == null)
        {
            return;
        }

        try
        {
            var message = Dispatch(command);
            DebugLog.Ok(command.Id, command.Verb, message);
        }
        catch (DebugCommandException e)
        {
            DebugLog.Error(command.Id, command.Verb, e.Message);
        }
        catch (Exception e)
        {
            DebugLog.Error(command.Id, command.Verb, e.GetType().Name + ": " + e.Message);
            Plugin.Log.Debug(e);
        }
    }

    private string Dispatch(DebugCommandLine command)
    {
        switch (command.Verb)
        {
            case "":
                throw new DebugCommandException("empty command");
            case "help":
                return "commands: " + string.Join(" ", OwnVerbs
                    .Concat(Menu?.Verbs ?? Array.Empty<string>())
                    .Concat(Gameplay?.Verbs ?? Array.Empty<string>())
                    .Distinct());
            case "log":
                return string.Join(" ", command.Args);
            case "status":
                return Status();
            case "button":
                if (!DebugRequestParser.TryParseButton(command.Args, out var button, out var buttonError))
                {
                    throw new DebugCommandException(buttonError);
                }

                _virtual.Apply(button);
                return $"{button.Button} {button.Action}" + (button.Seconds > 0f ? $" {button.Seconds:0.###}s" : string.Empty);
            case "stick":
                if (!DebugRequestParser.TryParseStick(command.Args, out var stick, out var stickError))
                {
                    throw new DebugCommandException(stickError);
                }

                _virtual.Apply(stick);
                return $"right stick {stick.X:0.##},{stick.Y:0.##}" + (stick.Seconds > 0f ? $" for {stick.Seconds:0.###}s" : " until changed");
            case "release-all":
                _virtual.ReleaseAll();
                return "virtual input released";
            case "find-hint":
            case "find-text":
            case "find-name":
            case "click-hint":
            case "click-text":
            case "click-name":
                if (!UiRequestParser.TryParseQuery(command.Verb, command.Args, out var query, out var queryError))
                {
                    throw new DebugCommandException(queryError);
                }

                return query.Click ? DebugUiClicker.Click(query) : DebugUiClicker.Find(query);
            case "patches":
                if (!UiRequestParser.TryParsePatches(command.Args, out var typeName, out var methodName, out var patchesError))
                {
                    throw new DebugCommandException(patchesError);
                }

                return DebugUiClicker.Patches(typeName, methodName);
            case "textinfo":
                if (!UiRequestParser.TryParseSingle(command.Verb, command.Args, out var needle, out var textError))
                {
                    throw new DebugCommandException(textError);
                }

                return DebugUiClicker.TextInfo(needle);
            case "table":
                return DebugUiClicker.Table();
            case "search":
                // Everything after the verb, so unquoted multi-word text works; empty clears the search.
                return DebugUiClicker.Search(string.Join(" ", command.Args));
        }

        // Gameplay first: the menu scene stays loaded (inactive) during gameplay.
        if (Gameplay != null && Gameplay.Verbs.Contains(command.Verb))
        {
            return Gameplay.Handle(command);
        }

        if (Menu != null && Menu.Verbs.Contains(command.Verb))
        {
            return Menu.Handle(command);
        }

        throw new DebugCommandException($"unknown command or not available now ({SceneSummary()}); try 'help'");
    }

    private string Status()
    {
        var sb = new StringBuilder(SceneSummary());
        sb.Append($" songsLoaded={SongCore.Loader.AreSongsLoaded} fpfc={_input.IsFpfc} {_virtual.Describe()}");
        if (Gameplay != null)
        {
            sb.Append(' ').Append(Gameplay.Describe());
        }
        else if (Menu != null)
        {
            sb.Append(' ').Append(Menu.Describe());
        }

        return sb.ToString();
    }

    private List<string> ReadAndClear()
    {
        var lines = new List<string>();
        if (!File.Exists(_path))
        {
            return lines;
        }

        try
        {
            // Exclusive open: the writer (scripts/qa/cmd.ps1) also opens exclusively and retries,
            // so a line is never half-read or lost between read and truncate.
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (stream.Length == 0)
            {
                return lines;
            }

            using (var reader = new StreamReader(stream, new UTF8Encoding(false), true, 4096, leaveOpen: true))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Trim().Length > 0)
                    {
                        lines.Add(line);
                    }
                }
            }

            stream.SetLength(0);
        }
        catch (IOException)
        {
            // Writer holds the file; try again next poll.
        }

        return lines;
    }

    private void HookScenesManager()
    {
        if (_scenes != null)
        {
            return;
        }

        _scenes = _container.TryResolve<GameScenesManager>();
        if (_scenes != null)
        {
            _scenes.transitionDidFinishEvent += HandleTransitionDidFinish;
        }
    }

    private void HandleTransitionDidFinish(GameScenesManager.SceneTransitionType type, ScenesTransitionSetupDataSO setupData, DiContainer container)
    {
        Guard.Run(Feature, () =>
        {
            DebugLog.Event($"scenes {type}: [{string.Join(",", _scenes!.GetCurrentlyLoadedSceneNames())}]");
            if (InGameplay)
            {
                _menuReadyLogged = false;
            }
        });
    }

    private void HandleSongsLoaded(SongCore.Loader loader, System.Collections.Concurrent.ConcurrentDictionary<string, BeatmapLevel> levels)
    {
        Guard.Run(Feature, () => DebugLog.Event($"songs-loaded custom={levels.Count}"));
    }

    /// <summary>"event menu-ready" each time the menu becomes usable: menu scene up, no transition, songs loaded.</summary>
    private void CheckMenuReady()
    {
        var ready = Menu != null && !InGameplay && _scenes != null && !InTransition
            && SongCore.Loader.AreSongsLoaded && !SongCore.Loader.AreSongsLoading;
        if (ready && !_menuReadyLogged)
        {
            _menuReadyLogged = true;
            DebugLog.Event("menu-ready");
        }
        else if (!ready && (InGameplay || Menu == null))
        {
            _menuReadyLogged = false;
        }
    }

    private void LogInputEdges()
    {
        foreach (var button in Buttons)
        {
            if (_input.GetButtonDown(button))
            {
                DebugLog.Event($"input {button} down");
            }
            else if (_input.GetButtonUp(button))
            {
                DebugLog.Event($"input {button} up");
            }
        }

        var stick = _input.GetRightStick();
        var quantized = $"{Mathf.Round(stick.x * 10f) / 10f:0.#},{Mathf.Round(stick.y * 10f) / 10f:0.#}";
        if (quantized != _lastStick)
        {
            _lastStick = quantized;
            DebugLog.Event($"input stick {quantized}");
        }
    }

    private void RunScheduled()
    {
        if (_scheduled.Count == 0)
        {
            return;
        }

        var now = Time.realtimeSinceStartup;
        var due = _scheduled.Where(s => s.At <= now).ToList();
        _scheduled.RemoveAll(s => s.At <= now);
        foreach (var item in due)
        {
            try
            {
                item.Action();
            }
            catch (Exception e)
            {
                DebugLog.Info($"scheduled '{item.Label}' failed: {e.GetType().Name}: {e.Message}");
                Plugin.Log.Debug(e);
            }
        }
    }
}
