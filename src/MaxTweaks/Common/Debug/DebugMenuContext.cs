using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BGLib.Polyglot;
using MaxTweaks.Core.Debugging;
using Zenject;

namespace MaxTweaks.Common.Debugging;

/// <summary>
/// Menu-scene debug commands: play, select-level, open-settings, main-menu. Registered with the
/// DebugCommandService while the menu container lives. Game services are resolved lazily with
/// TryResolve so a missing binding can only fail a command, never the menu install.
/// </summary>
internal sealed class DebugMenuContext : IDebugCommandContext, IInitializable, IDisposable
{
    private const string Feature = "Debug";
    private static readonly string[] AllVerbs = { "play", "select-level", "open-settings", "main-menu", "levels" };

    private readonly DiContainer _container;
    private readonly DebugCommandService _service;

    public DebugMenuContext(DiContainer container, DebugCommandService service)
    {
        _container = container;
        _service = service;
    }

    public IReadOnlyCollection<string> Verbs => AllVerbs;

    public void Initialize() => Guard.Run(Feature, () => _service.Menu = this);

    public void Dispose() => Guard.Run(Feature, () =>
    {
        if (_service.Menu == this)
        {
            _service.Menu = null;
        }
    });

    public string Describe()
    {
        var main = _container.TryResolve<MainFlowCoordinator>();
        var child = main?.childFlowCoordinator;
        var top = child != null ? child.YoungestChildFlowCoordinatorOrSelf() : null;
        return $"flow={(top != null ? top.GetType().Name : "MainMenu")}";
    }

    public string Handle(DebugCommandLine command)
    {
        if (_service.InGameplay)
        {
            throw new DebugCommandException("not available during gameplay (use exit first)");
        }

        if (_service.InTransition)
        {
            throw new DebugCommandException("scene transition in progress, retry shortly");
        }

        return command.Verb switch
        {
            "play" => Play(command),
            "select-level" => SelectLevel(command),
            "open-settings" => OpenSettings(),
            "main-menu" => MainMenu(),
            "levels" => Levels(command),
            _ => throw new DebugCommandException("unhandled verb"),
        };
    }

    private static T Need<T>(T? value, string what)
        where T : class =>
        value ?? throw new DebugCommandException($"{what} not available");

    private string Play(DebugCommandLine command)
    {
        if (!DebugRequestParser.TryParsePlay(command.Args, out var request, out var error))
        {
            throw new DebugCommandException(error);
        }

        var (level, key) = Resolve(request);
        var transitions = Need(_container.TryResolve<MenuTransitionsHelper>(), "MenuTransitionsHelper");
        var playerData = Need(_container.TryResolve<PlayerDataModel>(), "PlayerDataModel").playerData;
        var environments = Need(_container.TryResolve<EnvironmentsListModel>(), "EnvironmentsListModel");
        var practice = request.Practice ? new PracticeSettings(request.StartSeconds, request.Speed) : null;

        // Mirrors SinglePlayerLevelSelectionFlowCoordinator.StartLevel, but sourced from the saved
        // player data so it works from any menu screen (same approach as replay launchers).
        transitions.StartStandardLevel(
            "Solo",
            in key,
            level,
            playerData.overrideEnvironmentSettings,
            playerData.colorSchemesSettings.GetOverrideColorScheme(),
            playerData.colorSchemesSettings.ShouldOverrideLightshowColors(),
            level.GetColorScheme(key.beatmapCharacteristic, key.difficulty),
            playerData.gameplayModifiers,
            playerData.playerSpecificSettings,
            practice,
            environments,
            Localization.Get("BUTTON_MENU"),
            useTestNoteCutSoundEffects: false,
            startPaused: false,
            beforeSceneSwitchToGameplayCallback: null,
            afterSceneSwitchToGameplayCallback: null,
            levelFinishedCallback: (_, results) => Guard.Run(Feature, () =>
                DebugLog.Event($"level-finished state={results.levelEndStateType} action={results.levelEndAction} score={results.modifiedScore}")),
            levelRestartedCallback: null);

        return $"starting {Describe(level, key)}" + (practice != null ? $" practice start={practice.startSongTime:0.##} speed={practice.songSpeedMul:0.##}" : string.Empty);
    }

    private string SelectLevel(DebugCommandLine command)
    {
        if (!DebugRequestParser.TryParsePlay(command.Args, out var request, out var error))
        {
            throw new DebugCommandException(error.Replace("practice <start> [speed]", string.Empty));
        }

        var (level, key) = Resolve(request);
        var main = Need(_container.TryResolve<MainFlowCoordinator>(), "MainFlowCoordinator");
        var solo = Need(_container.TryResolve<SoloFreePlayFlowCoordinator>(), "SoloFreePlayFlowCoordinator");
        var playerData = Need(_container.TryResolve<PlayerDataModel>(), "PlayerDataModel").playerData;

        // The detail view picks the difficulty from the last selection.
        playerData.SetLastSelectedBeatmapCharacteristic(key.beatmapCharacteristic);
        playerData.SetLastSelectedBeatmapDifficulty(key.difficulty);

        var isCustom = SongCore.Loader.CustomLevels.Values.Any(l => l.levelID == level.levelID)
            || SongCore.Loader.CustomWIPLevels.Values.Any(l => l.levelID == level.levelID);
        if (main.childFlowCoordinator == null)
        {
            var category = isCustom ? SelectLevelCategoryViewController.LevelCategory.CustomSongs : SelectLevelCategoryViewController.LevelCategory.All;
            var pack = isCustom ? SongCore.Loader.CustomLevelsPack : null;
            solo.Setup(new LevelSelectionFlowCoordinator.State(category, pack, default, level));
            main.HandleMainMenuViewControllerDidFinish(null!, MainMenuViewController.MenuButton.SoloFreePlay);
        }
        else if (main.childFlowCoordinator == solo)
        {
            Need(_container.TryResolve<LevelCollectionNavigationController>(), "LevelCollectionNavigationController").SelectLevel(level);
        }
        else
        {
            throw new DebugCommandException($"open the main menu or solo list first (current {Describe()}); try main-menu");
        }

        // The detail view may restore another remembered difficulty: pick ours like a cell click would.
        _service.Schedule(1.2f, "select-level difficulty", () => SelectDifficulty(key));
        _service.Schedule(1.6f, "select-level check", () =>
        {
            var nav = _container.TryResolve<LevelSelectionNavigationController>();
            var selected = nav?.beatmapKey ?? default;
            DebugLog.Event(selected.levelId == key.levelId && selected.difficulty == key.difficulty && selected.beatmapCharacteristic == key.beatmapCharacteristic
                ? $"level-selected {selected.levelId} {selected.beatmapCharacteristic?.serializedName} {selected.difficulty}"
                : $"level-select-failed wanted={key.levelId} {key.beatmapCharacteristic.serializedName} {key.difficulty} selected={selected.levelId ?? "none"} {selected.beatmapCharacteristic?.serializedName} {selected.difficulty}");
        });
        return $"selecting {Describe(level, key)}";
    }

    private void SelectDifficulty(BeatmapKey key)
    {
        var detail = _container.TryResolve<StandardLevelDetailViewController>()?._standardLevelDetailView;
        if (detail == null || detail.beatmapKey.levelId != key.levelId)
        {
            return;
        }

        var characteristics = detail._beatmapCharacteristicSegmentedControlController;
        if (characteristics.selectedBeatmapCharacteristic != key.beatmapCharacteristic)
        {
            var index = characteristics._currentlyAvailableBeatmapCharacteristics.IndexOf(key.beatmapCharacteristic);
            if (index >= 0)
            {
                characteristics._segmentedControl.SelectCellWithNumber(index);
                characteristics.HandleBeatmapCharacteristicSegmentedControlDidSelectCell(characteristics._segmentedControl, index);
            }
        }

        var difficulties = detail._beatmapDifficultySegmentedControlController;
        if (difficulties.selectedDifficulty != key.difficulty)
        {
            var index = difficulties._difficulties.IndexOf(key.difficulty);
            if (index >= 0)
            {
                difficulties._difficultySegmentedControl.SelectCellWithNumber(index);
                difficulties.HandleDifficultySegmentedControlDidSelectCell(difficulties._difficultySegmentedControl, index);
            }
        }
    }

    private string MainMenu()
    {
        var main = Need(_container.TryResolve<MainFlowCoordinator>(), "MainFlowCoordinator");
        var chain = new List<HMUI.FlowCoordinator> { main };
        while (chain[chain.Count - 1].childFlowCoordinator != null)
        {
            chain.Add(chain[chain.Count - 1].childFlowCoordinator);
        }

        for (var i = chain.Count - 1; i >= 1; i--)
        {
            chain[i - 1].DismissFlowCoordinator(chain[i], HMUI.ViewController.AnimationDirection.Horizontal, null, immediately: true);
        }

        return $"dismissed {chain.Count - 1} flow coordinator(s)";
    }

    private string OpenSettings()
    {
        var main = Need(_container.TryResolve<MainFlowCoordinator>(), "MainFlowCoordinator");
        if (main.childFlowCoordinator != null)
        {
            throw new DebugCommandException($"open the main menu first (current {Describe()}); try main-menu");
        }

        // BSML keeps this internal (not publicized here): reach it by reflection, best effort.
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var settings = Need(BeatSaberMarkupLanguage.Settings.BSMLSettings.Instance, "BSMLSettings");
        var type = settings.GetType();
        var present = Need(type.GetMethod("PresentSettings", Any), "BSMLSettings.PresentSettings");
        present.Invoke(settings, null);

        _service.Schedule(1.2f, "open-settings", () =>
        {
            var flow = Need(type.GetField("modSettingsFlowCoordinator", Any)?.GetValue(settings), "modSettingsFlowCoordinator");
            var menus = Need(type.GetProperty("SettingsMenus", Any)?.GetValue(settings) as IEnumerable, "SettingsMenus").Cast<object>().ToList();
            var index = menus.FindIndex(m => (m.GetType().GetProperty("Text", Any)?.GetValue(m) ?? m.GetType().GetField("Text", Any)?.GetValue(m)) as string == "MaxTweaks");
            if (index < 0)
            {
                DebugLog.Event("settings-open-failed MaxTweaks menu not registered");
                return;
            }

            // Click the list cell (same path as a pointer click), so the list highlight matches too.
            var listController = Need(flow.GetType().GetProperty("SettingsMenuListViewController", Any)?.GetValue(flow), "SettingsMenuListViewController");
            var list = Need(listController.GetType().GetProperty("List", Any)?.GetValue(listController) as BeatSaberMarkupLanguage.Components.CustomListTableData, "settings list");
            list.TableView.ScrollToCellWithIdx(index, HMUI.TableView.ScrollPositionType.Center, false);
            list.TableView.SelectCellWithIdx(index, callbackTable: true);
            _service.Schedule(0.8f, "open-settings check", () =>
            {
                var active = flow.GetType().GetProperty("ActiveController", Any)?.GetValue(flow);
                var wanted = menus[index].GetType().GetProperty("ViewController", Any)?.GetValue(menus[index]);
                DebugLog.Event(active != null && active == wanted ? "settings-opened MaxTweaks" : "settings-open-failed MaxTweaks page not active");
            });
        });
        return "opening Mod Settings > MaxTweaks";
    }

    private string Levels(DebugCommandLine command)
    {
        var query = string.Join(" ", command.Args);
        var hits = query.Length == 0 ? Candidates().Select(c => c.Candidate).ToList() : LevelMatcher.Match(Candidates().Select(c => c.Candidate), query).ToList();
        foreach (var hit in hits.Take(50))
        {
            DebugLog.Info("level " + hit);
        }

        return $"{hits.Count} level(s)" + (hits.Count > 50 ? " (first 50 listed)" : string.Empty);
    }

    private (BeatmapLevel Level, BeatmapKey Key) Resolve(PlayRequest request)
    {
        if (!SongCore.Loader.AreSongsLoaded)
        {
            throw new DebugCommandException("songs are still loading, retry shortly");
        }

        var candidates = Candidates();
        var hits = LevelMatcher.Match(candidates.Select(c => c.Candidate), request.Level);
        if (hits.Count == 0)
        {
            throw new DebugCommandException($"no level matches '{request.Level}' (try 'levels <text>')");
        }

        if (hits.Count > 1)
        {
            throw new DebugCommandException($"'{request.Level}' is ambiguous: " + string.Join("; ", hits.Take(8)));
        }

        var level = candidates.First(c => c.Candidate.LevelId == hits[0].LevelId).Level;
        var characteristics = SongCore.Loader.beatmapCharacteristicCollection
            ?? Need(_container.TryResolve<BeatmapCharacteristicCollection>(), "BeatmapCharacteristicCollection");
        var characteristic = characteristics.GetBeatmapCharacteristicBySerializedName(request.Characteristic)
            ?? throw new DebugCommandException($"unknown characteristic '{request.Characteristic}'");
        var difficulty = (BeatmapDifficulty)(int)request.Difficulty;
        var key = new BeatmapKey(level.levelID, characteristic, difficulty);
        if (!level.beatmapBasicData.ContainsKey((characteristic, difficulty)))
        {
            var available = string.Join(", ", level.GetBeatmapKeys().Select(k => $"{k.beatmapCharacteristic.serializedName}/{k.difficulty}"));
            throw new DebugCommandException($"{level.levelID} has no {request.Characteristic}/{difficulty}; available: {available}");
        }

        return (level, key);
    }

    private List<(LevelCandidate Candidate, BeatmapLevel Level)> Candidates()
    {
        var result = new List<(LevelCandidate, BeatmapLevel)>();
        foreach (var pair in SongCore.Loader.CustomLevels.Concat(SongCore.Loader.CustomWIPLevels))
        {
            var folder = Path.GetFileName(pair.Key.TrimEnd('/', '\\'));
            result.Add((new LevelCandidate(pair.Value.levelID, folder, pair.Value.songName ?? string.Empty), pair.Value));
        }

        var model = _container.TryResolve<BeatmapLevelsModel>();
        if (model != null)
        {
            foreach (var pack in model.GetAllPacks())
            {
                foreach (var level in pack.AllBeatmapLevels())
                {
                    result.Add((new LevelCandidate(level.levelID, null, level.songName ?? string.Empty), level));
                }
            }
        }

        return result;
    }

    private static string Describe(BeatmapLevel level, BeatmapKey key) =>
        $"{level.levelID} '{level.songName}' {key.beatmapCharacteristic.serializedName}/{key.difficulty}";
}
