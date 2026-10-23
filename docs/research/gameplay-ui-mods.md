# Beat Saber PC gameplay/UI mods: patterns and lessons

Scope: small-to-medium gameplay and menu-UI mods (BSIPA, C#). Core frameworks (BSIPA, SiraUtil,
BSML, SongCore), large mods and build/CI are covered by other reports; they appear here only where
a mod uses them.

All paths below are relative to `../`. Line numbers are
from the clones as of 2026-10-22.

## 1. Repos studied

| Clone | Upstream (canonical/maintained) | Last commit | Style |
|---|---|---|---|
| `CountersPlus` | NuggoDEV/CountersPlus (Caeden117/CountersPlus redirects here), 151 stars | 2026-06-28 | SiraUtil installers + Harmony transpiler; HUD counters |
| `HitScoreVisualizer` | ErisApps/HitScoreVisualizer (fork of artemiswkearney, the maintained one), 80 stars | 2026-09-02 | SiraUtil Affinity patches, memory pool, config migrations |
| `BetterSongList` | kinsi55/BeatSaber_BetterSongList, 42 stars | 2026-06-05 | Plain Harmony on song-list UI classes, async filter/sort |
| `Camera2` | kinsi55/CS_BeatSaber_Camera2, 140 stars | 2026-01-02 | Plain Harmony, per-camera render loop with middlewares |
| `Tweaks55` | kinsi55/BeatSaber_Tweaks55, 30 stars | 2026-06-14 | ~20 independent Harmony patches, each fails in isolation |
| `CustomSabersLite` | qqrz997/CustomSabersLite, 22 stars | 2025-12-31 (main), 2026-10-13 (`urp/next-SabersCore`) | Pure SiraUtil (zero Harmony), BSML custom tags |
| `JDFixer` | zeph-yr/JDFixer (original, stale since 2025-01) + remotes `kkrow` (release 7.4.1, 2026-10-01) and `mentegago` (1.44.1 compat, 2026-09-28) | see text | Single gameplay prefix + BSML GameplaySetup tab |

Notes on lineage:
- Skipped BeatSaberPlus (large, other agent's lane), PlaylistManager (last push 2024-10), ImageFactory
  (repo name not found under that owner).
- JDFixer upstream has not had a game-compat commit since Jan 2025; several forks carry it. The
  clone's default branch is `BS_1.26_Offset`; maintained forks are fetched as remotes `kkrow/BS_1.26_Offset`
  and `mentegago/BS_1.26_Offset`.
- `SlopSaber/*` forks (CountersPlus, HSV, JDFixer, Tweaks55, Camera2, CSL, BSL, ...) target 1.45.x and are
  self-described "AI-assisted Beat Saber experiments". Not canonical. Fetched as remote `slopsaber` in the
  CountersPlus clone only because its commits are a clean before/after for hot-path fixes (section 5).
- Every manifest still says `gameVersion: 1.42.0` even for mods pushed mid/late 2026, so that field is not a
  reliable "supports version X" signal; check commit history.

## 2. Project layout: how a small/medium mod is organized

Two distinct house styles exist and both are current.

### 2a. "Static Harmony" style (kinsi55: Tweaks55, BetterSongList, Camera2; JDFixer)
- `Plugin.cs` with `[Plugin(RuntimeOptions.SingleStartInit)]` or `DynamicInit`, static `Log`, static `harmony`,
  `harmony.PatchAll(Assembly.GetExecutingAssembly())` in `[OnStart]`/`[OnEnable]`, `UnpatchSelf()` in
  `[OnExit]`/`[OnDisable]` (`Tweaks55/Plugin.cs:60,79`, `BetterSongList/Plugin.cs:11,27`, `Camera2/Plugin.cs:14,58`).
- `Configuration/Config.cs`: BSIPA generated config with a static `Config.Instance`
  (`BetterSongList/Configuration/Config.cs:6-7`, `Tweaks55/Configuration/Config.cs`).
- `HarmonyPatches/<Feature>.cs`: one static class per feature; state lives in static fields.
- `UI/` with `.bsml` + a FlowCoordinator, `Util/` with helpers, `manifest.json` at root.
- Pros: simplest; works without SiraUtil (Tweaks55 depends only on BSIPA, `Tweaks55/manifest.json`).
  Cons: global static state, manual lifetime management, harder to test.

### 2b. "SiraUtil/Zenject" style (HSV, CustomSabersLite, Counters+, JDFixer menu side)
- `Plugin.cs` is tiny and only wires installers per scene context:
  ```csharp
  // HitScoreVisualizer/HitScoreVisualizer/Plugin.cs:11-30
  [Plugin(RuntimeOptions.DynamicInit), NoEnableDisable]
  public class Plugin {
      [Init] public Plugin(Logger logger, Config config, PluginMetadata pluginMetadata, Zenjector zenject) {
          ...
          zenject.UseLogger(logger);
          zenject.UseMetadataBinder<Plugin>();
          zenject.Install<HsvAppInstaller>(Location.App, Config);   // services, config, patches
          zenject.Install<HsvMenuInstaller>(Location.Menu);         // flow coordinator, view controllers
          zenject.Install<HsvPlayerInstaller>(Location.Player);     // gameplay-only objects
      }
  }
  ```
  Same shape: `CustomSabersLite/CustomSabers/Plugin.cs:13-27` (adds `Location.MultiPlayer`),
  `CountersPlus/Counters+/Plugin.cs:38-42` (`Location.StandardPlayer | Location.CampaignPlayer`, plus a
  multiplayer installer bound to `MultiplayerLocalActivePlayerInstaller`).
- Folders: `Installers/`, `HarmonyPatches/` (Affinity classes), `Components/` (MonoBehaviours),
  `Models/`, `Services/`, `UI/` (+ `UI/Views/*.bsml` as EmbeddedResource), `Utilities/`.
  See `HitScoreVisualizer/HitScoreVisualizer/` and `CustomSabersLite/CustomSabers/` trees.
- Config is injected rather than static. CSL separates the BSIPA on-disk model from a runtime model:
  `Container.Bind<PluginConfig>().FromInstance(PluginConfig.FromJson(configModel))`
  (`CustomSabersLite/CustomSabers/Installers/AppInstaller.cs:21`). HSV issue #13 ("HSV optimization pass")
  explicitly asks for this split so the in-memory config can be pre-baked for gameplay.
- Gameplay installer early-outs when disabled, so nothing is bound and there is zero runtime cost:
  `HitScoreVisualizer/HitScoreVisualizer/Installers/HsvPlayerInstaller.cs:20`,
  `CustomSabersLite/CustomSabers/Installers/PlayerInstaller.cs:25`,
  `CountersPlus/Counters+/Installers/CountersInstaller.cs:26`.

Recommendation for a personal mod: style 2b. It gives scene-scoped lifetimes (objects die with the
GameCore scene, so no stale static references), constructor injection of game objects
(`ScoreController`, `BeatmapObjectManager`, `AudioTimeSyncController`, `SaberManager`...), and Affinity patches
that are only active while their container lives.

## 3. Game hooks

### 3a. Prefer game events and SiraUtil extension points over patches
- Counters+ reads gameplay purely via injected game objects and their events; the only Harmony patch is for
  hiding base HUD. Broadcasters isolate the event surface so a game update breaks only one file:
  ```csharp
  // CountersPlus/Counters+/Counters/Event Broadcasters/NoteEventBroadcaster.cs:11-38
  [Inject] private BeatmapObjectManager beatmapObjectManager;
  public override void Initialize() {
      beatmapObjectManager.noteWasCutEvent += NoteWasCutEvent;
      beatmapObjectManager.noteWasMissedEvent += NoteWasMissedEvent;
  }
  private void NoteWasCutEvent(NoteController data, in NoteCutInfo noteCutInfo) {
      foreach (INoteEventHandler h in EventHandlers) h?.OnNoteCut(data.noteData, noteCutInfo);
  }
  public override void Dispose() { beatmapObjectManager.noteWasCutEvent -= NoteWasCutEvent; ... }
  ```
  `EventHandlers` is `[Inject] protected List<T>` (`.../EventBroadcaster.cs:15`): Zenject injects every bound
  implementation of the interface, a cheap plugin system.
- Score events used: `ScoreController.scoreDidChangeEvent` (`.../ScoreEventBroadcaster.cs`),
  `ScoreController.scoringForNoteFinishedEvent` with `GoodCutScoringElement.cutScoreBuffer`
  (`beforeCutScore`, `afterCutScore`, `centerDistanceCutScore`, `noteScoreDefinition`) and
  `noteData.scoringType` (Normal/ArcHead/ArcTail/ChainHead/ChainLink) at
  `CountersPlus/Counters+/Counters/CutCounter.cs:49-110`.
- Menu events used by JDFixer: `StandardLevelDetailViewController.didChangeDifficultyBeatmapEvent` and
  `didChangeContentEvent` (gives `beatmapKey` and `beatmapLevel`), `MissionSelectionMapViewController.didSelectMissionLevelEvent`
  (`JDFixer/Managers/JDFixerUIManager.cs:38-50`).
- CustomSabersLite replaces sabers with zero Harmony: `Container.BindInstance(SaberModelRegistration.Create<LiteSaberModelController>(5))`
  (`CustomSabersLite/CustomSabers/Installers/PlayerInstaller.cs:34`), with `IPreSaberModelInit.PreInit`
  returning false to suppress the default model (`.../Components/LiteSaberModelController.cs:35-43`).

### 3b. SiraUtil Affinity (instance patches with DI)
HSV turned all its gameplay patches into `IAffinity` classes bound in the player installer, so the patch is
live only during gameplay and receives the current config through its constructor:
```csharp
// HitScoreVisualizer/HitScoreVisualizer/HarmonyPatches/FlyingScoreEffectPatch.cs:8-58
internal class FlyingScoreEffectPatch : IAffinity {
    private readonly HsvConfigModel config;
    private FlyingScoreEffectPatch(HsvConfigModel config) { this.config = config; }

    [AffinityPrefix]
    [AffinityPriority(1000)]
    [AffinityPatch(typeof(FlyingScoreEffect), nameof(FlyingScoreEffect.InitAndPresent))]
    internal bool InitAndPresent(ref FlyingScoreEffect __instance, IReadonlyCutScoreBuffer cutScoreBuffer,
                                 float duration, Vector3 targetPos) {
        ... // set text/color, register cutScoreBuffer callbacks, call the inner InitAndPresent overload
        return false; // skip original
    }
}
```
Bound with `Container.BindInterfacesTo<FlyingScoreEffectPatch>().AsSingle()`
(`HitScoreVisualizer/HitScoreVisualizer/Installers/HsvPlayerInstaller.cs:36`).

Installer-time hooks: patching `GameplayCoreInstaller.InstallBindings` with an Affinity prefix lets you read
prefab data and bind your own instances into the game's container before it resolves
(`HitScoreVisualizer/HitScoreVisualizer/HarmonyPatches/GameCoreInstallerHook.cs:12-30`). Same idea for prefab
mutation: `EffectPoolsManualInstaller.ManualInstallBindings` prefix edits the FlyingScoreEffect prefab's TMP
settings once (`.../EffectPoolsManualInstallerPatch.cs`).

### 3c. Plain Harmony: commonly patched classes in these repos
Gameplay/HUD:
- `CoreGameHUDController.Initialize` (transpiler; Counters+ `Harmony/CoreGameHUDControllerPatch.cs:27`)
- `GameplayCoreInstaller.InstallBindings` (HSV transpiler `PersistentScoreEffectPatch.cs`, HSV affinity)
- `FlyingScoreEffect.InitAndPresent / HandleCutScoreBufferDidChange / HandleCutScoreBufferDidFinish` (HSV)
- `MissedNoteEffectSpawner.HandleNoteWasMissed`, `BadNoteCutEffectSpawner.HandleNoteWasCut` (HSV)
- `FlyingScoreSpawner.SpawnFlyingScore`, `NoteDebrisSpawner.SpawnDebris`, `NoteDebrisPoolInstaller.InstallBindings`,
  `NoteCutParticlesEffect.SpawnParticles/Awake`, `BombExplosionEffect.SpawnExplosion`, `SaberClashEffect.Start`,
  `SaberBurnMarkArea.OnEnable`, `ObstacleSaberSparkleEffectManager.Start`, `BeatmapObjectsInstaller.InstallBindings`,
  `ComboUIController.Start`, `ScoreUIController.UpdateScore(int)`, `NoteCutHapticEffect.HitNote`,
  `BeatLineManager.Start`, `BlueNoiseDitheringUpdater.HandleCameraPreRender` (all Tweaks55 `HarmonyPatches/*.cs`)
- `VariableMovementDataProvider.Init` prefix rewriting `noteJumpValueType`/`noteJumpValue` (JDFixer `HarmonyPatches.cs:7-10`)
- `StandardLevelScenesTransitionSetupDataSO.Init`, `MissionLevelScenesTransitionSetupDataSO.Init`,
  `MultiplayerLevelScenesTransitionSetupDataSO.Init` postfix to capture level/modifiers before load
  (`Camera2/HarmonyPatches/HookLeveldata.cs:19-45`; JDFixer for `songSpeedMul`)
- `SmoothCameraController.ActivateSmoothCameraIfNeeded` as a "main camera is ready" signal
  (`Camera2/HarmonyPatches/InitOnMainAvailable.cs:8-25`), `MainSystemInit.Init` for FPS cap.

Menu / song list (BetterSongList, `BetterSongList/HarmonyPatches/`):
- `LevelCollectionTableView.SetData` (core: swap the list for filtered/sorted list), `.Init`, `.HandleDidSelectCellWithIndex`
- `LevelFilteringNavigationController.ShowPacksInSecondChildController`, `.UpdateSecondChildControllerContent`,
  `.HandleSelectLevelCategoryViewControllerDidSelectLevelCategory`
- `AnnotatedBeatmapLevelCollectionsViewController.HandleDidSelectAnnotatedBeatmapLevelCollection`,
  `AnnotatedBeatmapLevelCollectionsGridView.CloseLevelCollection`, `SelectLevelCategoryViewController...DidSelectCell`
- `LevelSelectionFlowCoordinator.DidActivate`, `LevelCollectionNavigationController.DidActivate`,
  `StandardLevelDetailView.RefreshContent` (extra params, delete button), `LevelSearchViewController.ResetAllFilterSettings`,
  `LevelFilter.FilterLevelByText`, `PlayerLevelStatsData.UpdateScoreData`
- Tweaks55: `HealthWarningViewController.DidActivate`, `PlayerSettingsPanelController.SetLayout`,
  `MenuLightsManager.Start`, `HMUI.ScrollView.HandleJoystickWasNotCenteredThisFrame`.

### 3d. How hooks are discovered
No repo documents this. The evidence is indirect: all of them compile against publicized game assemblies
(`BepInEx.AssemblyPublicizer.MSBuild` + `Publicize="true"`, e.g. `CustomSabersLite/CustomSabers/CustomSabersLite.csproj`
lines 22, 62, 94, 102, 114; Counters+ commit 3e5ce32 "Use publicizer instead of reflection"; Camera2 76162b9 "publicizer")
and access private fields directly (`__instance._missedNoteEffectSpawnerPrefab._missedNoteFlyingSpriteSpawner` at
`GameCoreInstallerHook.cs:16`). Inference: authors browse decompiled `Main.dll`/`HMUI.dll` (dnSpy/ILSpy) and pick
the narrowest method that (a) fires once per event, not per frame, and (b) is where the game builds or binds the thing
they want to change. Harmony's `___field` parameter injection (`____bombNotePrefab`, `____animator`) is the
pre-publicizer way to reach privates and still appears everywhere in Tweaks55.

## 4. Settings / UI

Patterns seen, from simplest to richest:
1. BSIPA generated config (`conf.Generated<T>()`), properties `public virtual`, `[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]`,
   `[Ignore]` for runtime-only fields, `[UseConverter]` for enums/dictionaries
   (`HitScoreVisualizer/HitScoreVisualizer/PluginConfig.cs:6-17`, `CountersPlus/Counters+/ConfigModels/ConfigModel.cs:16-32`).
   `Changed()` override reacts to edits (`Tweaks55/Configuration/Config.cs:60` calls `ApplyValues()`).
2. Main-menu mod button opening a FlowCoordinator, registered/unregistered by an `IInitializable, IDisposable`:
   ```csharp
   // HitScoreVisualizer/HitScoreVisualizer/UI/MenuButtonManager.cs:17-41
   public MenuButtonManager(HsvMainFlowCoordinator fc, MainFlowCoordinator main, MenuButtons menuButtons) { ... }
   public void Initialize() => menuButtons.RegisterButton(hsvMenuButton);
   public void Dispose()    => menuButtons.UnregisterButton(hsvMenuButton);
   private void OnClick()   => mainFlowCoordinator.PresentFlowCoordinator(hsvMainFlowCoordinator);
   ```
   View controllers are bound with `FromNewComponentAsViewController()` and the coordinator with
   `FromNewComponentOnNewGameObject()` (`.../Installers/HsvMenuInstaller.cs`, `JDFixer/Installers/JDFixerMenuInstaller.cs:27-31`).
3. `BSMLAutomaticViewController` with `[ViewDefinition("Ns.UI.Views.X.bsml")]` and `[HotReload(RelativePathToLayout=...)]`
   for live layout edits during development; properties bound by name with `apply-on-change="true"`
   (`HitScoreVisualizer/HitScoreVisualizer/UI/PluginSettingsViewController.cs:11-35`, `.../UI/Views/PluginSettings.bsml:10-20`).
4. Gameplay Setup tab (left panel in solo menu): injected `GameplaySetup.AddTab(name, resource, host)` / `RemoveTab`
   (`CustomSabersLite/CustomSabers/Menu/GameplaySetupTabController.cs:23,28`). Older code uses the static
   `GameplaySetup.Instance.AddTab(..., MenuType.Solo | MenuType.Campaign)` (`JDFixer/UI/ModifierUI.cs:25`).
   Best spot for per-play toggles (JDFixer's JD/RT slider lives here).
5. Custom BSML tags and type handlers bound via DI (`Container.Bind<BSMLTag>().To<ToggleableSliderTag>()`,
   `Container.Bind<TypeHandler>().To<...Handler>()`, `CustomSabersLite/CustomSabers/Installers/MenuInstaller.cs:20-29`).
6. Counters+ goes furthest: pushes the tutorial scene to render live mock counters in settings
   (`CountersPlus/Counters+/UI/FlowCoordinators/CountersPlusSettingsFlowCoordinator.cs`). It is also the part that
   breaks on most game updates (commit 3a6bc4b changed `tutorialSceneSetup.Init(...)` signature for 1.42.0; open
   issue #146 "menu options remain after exiting counter+ menu fast"). Avoid scene tricks in a personal mod.

Optional BSML: Tweaks55 lists BSML in `loadAfter` (not `dependsOn`) and guards UI setup:
```csharp
// Tweaks55/UI/MAN.cs:128-141
static readonly bool hasBsml = IPA.Loader.PluginManager.GetPluginFromId("BeatSaberMarkupLanguage") != null;
public static void EnableUI() { void wrap() => TweaksFlowCoordinator.Initialize(); if (hasBsml) wrap(); }
```
The local function keeps BSML types out of the caller's IL so the JIT does not need BSML when it is absent.

Config versioning: HSV keeps user JSON configs with a `version`, classifies them
(NewerVersion / Incompatible / NeedsMigration / ValidationFailed / Compatible), backs up before migrating,
and runs an ordered list of migration objects (`HitScoreVisualizer/HitScoreVisualizer/Utilities/Services/ConfigMigrator.cs:15-70`).
Tweaks55 does a cruder inline migration in `ApplyValues()` (`Tweaks55/Configuration/Config.cs:67`).

## 5. Performance in the gameplay hot path

### Good patterns
- Do the work once at install/prefab time instead of per object or per frame. Tweaks55 recolors bombs and
  wall glow by mutating prefab materials in `BeatmapObjectsInstaller.InstallBindings`
  (`Tweaks55/HarmonyPatches/BombColor.cs:25-41`, `WallStuff.cs:26-55`), and when debris is disabled it rewrites
  the pool's `WithInitialSize(n)` to 0 so the game never instantiates 40 debris objects:
  ```csharp
  // Tweaks55/HarmonyPatches/Debris.cs:21-40
  x.MatchForward(true,
      new CodeMatch(OpCodes.Ldc_I4_S),
      new CodeMatch(c => (c.operand as MethodInfo)?.Name == "WithInitialSize")
  ).Repeat(x => x.InsertAndAdvance(m));          // m = call NoInitialSizeIfDebrisDisabled(int)
  static int NoInitialSizeIfDebrisDisabled(int inSize) => Config.Instance.disableDebris ? 0 : inSize;
  ```
- Pool spawned effects. HSV uses a Zenject `MonoMemoryPool` pre-warmed to 20 and despawns on the
  `didFinishEvent` (`HitScoreVisualizer/HitScoreVisualizer/Components/HsvFlyingEffect.cs:10`,
  `.../Installers/HsvPlayerInstaller.cs:29-31`, `.../Components/HsvFlyingEffectSpawner.cs:34,43-47`).
- Cache shader property IDs and field accessors in `static readonly` fields
  (`Tweaks55/HarmonyPatches/CameraNoise.cs:14`, `BombColor.cs:13-19`, `WallStuff.cs:28-32`).
- In per-frame prefixes, early-out on a cached bool and avoid repeated work with a "last state" latch
  (`Tweaks55/HarmonyPatches/CameraNoise.cs:16-29`: sets the global texture once, then returns false each frame).
- Event-driven updates instead of polling: Counters+ cut/score counters update only on
  `scoringForNoteFinishedEvent` / `scoreDidChangeEvent`.
- Camera2's per-frame loop iterates a fixed array of middleware interfaces (no LINQ, no allocation), lets any
  middleware veto the render, and renders manually by toggling `UCamera.enabled` only on frames it wants:
  ```csharp
  // Camera2/Behaviours/Cam.cs:207-226
  internal void PrepareMiddlewaredRender(bool forceRender = false) {
      if (!UCamera || !renderTexture || middlewares == null) return;
      foreach (var t in middlewares) if (!t.Pre() && !forceRender) return;  // e.g. FPSLimiter
      transformchain.Calculate();
      UCamera.enabled = true;
      if (forceRender) UCamera.Render();
  }
  ```
  FPS limiter uses a rolling remainder so the average rate is exact (`Camera2/Middlewares/FPSLimiter.cs:29-35`).
  Expensive lookups (`GameObject.Find`, `Camera.main`) only run when the cached parent is lost
  (`Camera2/Middlewares/Smoothfollow.cs:112-121`). Reflection setup is "marinated" off-thread at startup with
  `Task.Run` (`Camera2/Plugin.cs:65-71`).
- Menu path: BSL's `SetData` prefix short-circuits when called with the same list instance
  (`BetterSongList/HarmonyPatches/HookLevelCollectionTableSet.cs:163-169`), runs filter/sort on a worker thread
  and applies the result on the next `SetData` (`:52-60`, `:198-205`), and cancels superseded preparation with a
  `CancellationTokenSource` (`:131-148`). Commit eae8cd5 "Fix possible stutters resulting from played filter"
  replaced a linear scan of `levelsStatsData` with a `HashSet` plus a postfix on
  `PlayerLevelStatsData.UpdateScoreData` to keep it current (`BetterSongList/Util/LocalScoresUtil.cs:8,24-33`).

### Anti-patterns (Counters+ is a counterexample here, not a model)
- `CountersPlus/Counters+/Counters/SpeedCounter.cs:65-75`: every frame appends to a `List<float>` that is never
  trimmed, then calls LINQ `Average()` over the whole list (O(n) per frame, grows for the whole song) and formats a
  new string. `:83-88` same with `Max()`.
- `CountersPlus/Counters+/Counters/Spinometer.cs:42-47`: four unbounded lists appended per frame, LINQ `Last()`.
- `CountersPlus/Counters+/Counters/ProgressCounter.cs:75-91`: assigns a freshly formatted string to TMP every frame
  even when the displayed value has not changed (TMP re-meshes on every `text` set with a new string).
- `CountersPlus/Counters+/Counters/CutCounter.cs:126-143`: `new string[3]` and several interpolations per cut.
- HSV: `HandleCutScoreBufferDidChange` runs per frame per in-flight note while post-swing accumulates, and each
  call builds a new judgment string (`HitScoreVisualizer/HitScoreVisualizer/HarmonyPatches/FlyingScoreEffectPatch.cs:62-75`,
  `StringBuilder` in `Utilities/Extensions/JudgmentExtensions.cs:50`). The config flag `DoIntermediateUpdates` lets
  users turn it off; issue #13 notes "A LOT of memory allocations regarding color conversion".

Before/after reference (non-canonical fork, remote `slopsaber` in the CountersPlus clone):
- a8da820 "Reduce counter updates and release unused resources": SpeedCounter lists replaced by running
  `double` totals + sample counts and a running max. Still formats a string per frame, so the next step would be
  "only set `text` when the rounded value changes".
- fccd8bd "Avoid foreground UI rebuilds each frame": a `LateUpdate` that called `GetComponentsInChildren` every frame
  is gated by a dirty flag set from `OnTransformChildrenChanged`, plus a 0.5 s fallback rescan; materials are cached
  in a dictionary and destroyed in `OnDestroy`.

Hot-path checklist distilled from the above:
1. Subscribe to events; use `ITickable`/`Update` only for continuous values (saber speed, song time).
2. No LINQ, no closures, no `string.Format`/interpolation, no boxing in per-frame code. Keep running aggregates.
3. Only assign `TMP_Text.text` when the displayed value changes (compare ints/rounded values first).
4. Pool anything spawned per note; pre-warm in the installer.
5. Mutate prefabs once at install time rather than every instance on spawn.
6. Cache `Shader.PropertyToID`, component references and reflection results.
7. Scope gameplay objects to the Player container so they are disposed with the scene (unsubscribe in `Dispose`).

## 6. Robustness of patches and multi-version support

### Branch per game version, never `#if`
None of the seven repos uses conditional compilation for game versions (only `DEBUG`/`TRACE`/`DEV`/`FPSCOUNT`).
Legacy support is a frozen branch: `Camera2` `origin/pre-unity-upgrade` (2023-10) and `origin/1.39.1` (2025-04),
`BetterSongList` `origin/pre-unity-upgrade`, `Tweaks55` `origin/pre1.30.2`, JDFixer `BS_1.16.4`, `BS_1.19`, `BS_1.20`,
`BS_1.26`. Master always targets the newest game. For a personal mod pinned to one game version this means:
target exactly one version, tag a branch when you move.

### Patch robustness spectrum (best to worst)
1. Do not patch: use events / SiraUtil registrations (CSL, Counters+ broadcasters).
2. Fail per feature, not per mod. Tweaks55 gives every patch class its own `TargetMethod()` and a `Cleanup` that
   swallows the exception so one broken tweak does not abort `PatchAll`:
   ```csharp
   // Tweaks55/HarmonyPatches/BurnMarks.cs:9-20 and Tweaks55/Plugin.cs:47-53
   [HarmonyPatch] static class BurnMark {
       [HarmonyPriority(int.MaxValue)]
       static bool Prefix(MonoBehaviour __instance) { if (!Config.Instance.disableBurnMarks) return true; __instance.enabled = false; return false; }
       static MethodBase TargetMethod() => Resolver.GetMethod(nameof(SaberBurnMarkArea), nameof(SaberBurnMarkArea.OnEnable));
       static Exception Cleanup(Exception ex) => Plugin.PatchFailed(ex);   // logs "X Tweak failed to setup! Ignoring", returns null
   }
   ```
   It worked as designed on 1.44 (issue #31: only "DisableAnalytics" failed), but the failure was an NRE inside
   `Resolver.GetFirstContructor` because `assembly.GetType(className)` returned null and was dereferenced
   (`Tweaks55/Util/Resolver.cs:35-37`). Lesson: null-check the type and return null so Harmony reports a clean
   "target not found".
   HSV wraps `PatchAll` in try/catch inside a Zenject-bound manager (`HitScoreVisualizer/HitScoreVisualizer/HarmonyPatches/HarmonyPatchManager.cs:15-23`),
   which protects the game, but the first failing patch class aborts every class after it in the PatchAll pass.
3. Overload-agnostic targeting by name when signatures churn:
   `AccessTools.GetDeclaredMethods(typeof(StandardLevelScenesTransitionSetupDataSO)).Where(m => m.Name == "Init")`
   (`Camera2/HarmonyPatches/HookLeveldata.cs:19-30`, commit 7a183c5 "Look up Methods dynamically so its less prone to break";
   `BetterSongList/HarmonyPatches/RestoreTableScroll.cs:10`). The opposite move also happens when only one overload is
   correct: Tweaks55 32be24b replaced "last method named UpdateScore" with `AccessTools.Method(typeof(ScoreUIController), "UpdateScore", new[] { typeof(int) })`.
   Pick by intent: patch all overloads only if your postfix parameters exist on all of them.
4. Transpilers anchored by pattern, failing loudly: HSV uses `CodeMatcher.MatchStartForward(...)` on the
   `noTextsAndHuds` / `zenMode` getters, `.ThrowIfInvalid("...")`, and `Transpilers.EmitDelegate`
   (`HitScoreVisualizer/HitScoreVisualizer/HarmonyPatches/PersistentScoreEffectPatch.cs:27-52`).
5. Transpilers anchored by index or by counting opcodes (brittle): Tweaks55 checks `instructions.ElementAt(41).opcode`
   and skips the patch if it does not match (`Tweaks55/HarmonyPatches/CampaignFireworks.cs:14-17`, at least it degrades);
   Counters+ counts `callvirt`s and inserts at `i - 4` with no validation
   (`CountersPlus/Counters+/Harmony/CoreGameHUDControllerPatch.cs:27-64`). Avoid.

Priorities: everyone sets explicit priority so their skip-original prefix wins or runs last as intended:
`[HarmonyPriority(int.MaxValue)]` for "disable" prefixes (Tweaks55), `int.MinValue` for postfixes that must see the
final state, `[AffinityPriority(1000)]` in HSV (commit 8c1d847). If you return false from a prefix, other mods' prefixes
with lower priority and the original are skipped, which is the usual source of cross-mod conflicts; HSV declares
`ConflictsWith NalulunaFlyingScore` in metadata (`HitScoreVisualizer/HitScoreVisualizer/Directory.Build.props:20`).

### Soft (optional) dependencies, three flavors
- Reflection only: `PluginManager.GetPluginFromId("ScoreSaber")?.Assembly.GetType("...")?.GetMethod(...)` cached once,
  invoked only on scene change (`Camera2/Utils/Scoresaber.cs:12-28`). Safest; no compile-time reference.
- Version-gated reference with a wrapper class so field types do not force assembly load
  (`AntiBox`, `GetPluginFromId("SongDetailsCache")` + `HVersion >= 1.1.5`, `BetterSongList/Util/SongDetailsCache.cs:6-24`),
  with `loadBefore`/`conflictsWith` ranges in `BetterSongList/manifest.json`.
- Hard reference with method-level isolation: JDFixer referenced `Custom Campaigns.dll`, picked a CC-typed handler only
  when CC was installed (`JDFixer/Managers/JDFixerUIManager.cs:41-48,93-129`). Works only while the CC-typed method is
  never JIT-compiled. It broke the build for 1.44.1 when CC was not updated; the mentegago fork's compat commit
  36ff25c simply deleted the CC reference, the CC handler, and the `MissionSelectionPatch`. Prefer reflection.

## 7. Timeline of game updates and what broke

| Game version (approx date) | What changed for mods | Evidence |
|---|---|---|
| 1.29.1 -> 1.30/1.31 Unity upgrade (May-Aug 2023) | Big break; legacy branches cut; publicizer adopted | Camera2 e48a55e "newunity" merge, `pre-unity-upgrade` branches, Tweaks55 `pre1.30.2`, Camera2 76162b9 |
| 1.34 (Dec 2023) | Minor recompiles | Counters+ c423fa1, BSL 317148e, Tweaks55 3b872fb |
| 1.35 (Apr 2024) | `*ScenesTransitionSetupDataSO.Init` signatures (BeatmapKey/BeatmapLevel model) | Camera2 7a183c5 |
| 1.36-1.37.x + BSML 1.12 (May-Sep 2024) | BSML API changes, method retargets | Tweaks55 a279be5 "BSML 1.12 Changes", 4a6d0b7, Counters+ b062862, Camera2 b91a269 |
| 1.40.0 (Dec 2024) | Recompile + small API moves | Tweaks55 ff87dbf, Camera2 8b66f82, BSL 263b9ee |
| 1.40.9 (Jul 2025) | TextMeshPro update: `enableWordWrapping` -> `textWrappingMode = TextWrappingModes.NoWrap` | HSV ab027a7, CSL 0c8e63a |
| 1.42.0 (Dec 2025-Jan 2026) | "Recompile required"; `TutorialScenesTransitionSetupDataSO.Init` gained params; reflection on `_screenSystem` removed | BSL 9cad21c, Counters+ 3a6bc4b, Tweaks55 97f7e9d, Camera2 ee82cfd |
| 1.44.x / 1.45.x (mid/late 2026) | Analytics types moved (Tweaks55 #31); JDFixer `StandardLevelScenesTransitionSetupDataSO.Init` overload filter no longer needed. Separately, a URP render-pipeline move is coming or under way (CSL `urp/next-SabersCore` branch rewrites color/trail code; issue #47 says "with the new URP update around the corner" and that URP screen displacement needs objects on the `ScreenDisplacement` layer). The exact game version for URP is not confirmed from these repos | Tweaks55 issue #31, JDFixer mentegago 36ff25c, CSL urp branch |

Practical takeaway: most updates are "recompile + fix 1-3 renamed members". The expensive breaks are rendering
(Unity upgrade, URP), TMP, and anything that drives scenes or flow coordinators directly.

## 8. Common bugs and lessons from issues/commits

1. Exceptions inside a gameplay prefix break the game, not just the mod. JDFixer issue #2: an NRE in the
   spawn-movement prefix meant notes did not spawn in Tournament Assistant. Guard every gameplay patch
   (null checks, try/catch returning `true` to fall back to the original).
2. Unity APIs off the main thread / during static init. BSL 15301c5 "Fix Unity exceptions": `ContinueWith` callbacks
   touching sprites needed `TaskScheduler.FromCurrentSynchronizationContext()`
   (`BetterSongList/HarmonyPatches/UI/ScrollEnhancements.cs:98`). Tweaks55 736bf8f: `ScriptableObject.CreateInstance` in
   a `static readonly` initializer threw under Unity debug builds; moved to an explicit `Init()` called from `[Init]`.
   Counters+ d5a9c74: config-changed coroutine replaced with `UnityMainThreadTaskScheduler.Factory.StartNew`
   (`CountersPlus/Counters+/ConfigModels/MainConfigModel.cs:57-61`; note the `Task.Yield()` there is not awaited and does nothing).
3. Type checks with `BaseType ==` instead of `IsAssignableFrom` silently skipped subclassed MonoBehaviours
   (Counters+ df00bea, now `CountersInstaller.cs:98,120`).
4. Overriding "No Texts and HUDs": users expect your HUD to respect it, and some want an override. Both Counters+
   (`IgnoreNoTextAndHUDOption`, `CountersInstaller.cs:93-94`) and HSV (`OverrideNoTextsAndHuds`, transpiler above,
   e063d03 extended it to miss/bad-cut text) grew a setting for it.
5. Zen mode, multiplayer, campaigns, replays and practice mode are separate code paths. Counters+ #123 (Zen mode lag),
   #126 ("Disabled Counters+ breaks Multi"), Camera2 #115 (multiplayer scene name), #90/#132 (SS vs BL replays, FPFC),
   JDFixer #13 (song speed and replays). Decide early which contexts your mod installs into (`Location.StandardPlayer`
   vs `Location.Player`, multiplayer installers).
6. Chains/arcs scoring semantics: Counters+ a36d9ea "MissCounter ignore chains to follow base game combo",
   cbef4e6 toggles for arcs/chains; HSV #27 "Arcs leading into a chain head make HSV think the chain head has
   post-swing score". Always branch on `NoteData.ScoringType`.
7. Base-game object names are not stable API: Tweaks55 1bcd57c had to add `"DustBritney"` next to `"DustPS"`;
   Counters+ 1.42 removed a hard-coded `GameObject.Find("RootContainer/Wrapper/MenuEnvironmentManager/...")`.
8. Stale selection/state across menu refreshes: BSL #22 "Same song is not reselected after async filter",
   BSL nicoco007 PR #31 "`_hideDetailViewController` not being reset properly". Re-entrancy around `SetData` is the
   recurring menu bug class.
9. Disabled-mod side effects: Counters+ #126; Tweaks55 `OnDisable` restores everything it changed
   (`Tweaks55/Plugin.cs:76-86`). If you support `DynamicInit` enable/disable, undo every global mutation.
10. Config migration: JDFixer #4 "Mod failed after upgrade with old settings file"; HSV solved it with versioned
    migrations and backups (section 4).

## 9. Recommended skeleton for a personal gameplay/UI mod

```
MyMod/
  Plugin.cs                      // [Plugin(RuntimeOptions.SingleStartInit), NoEnableDisable]; zenjector.Install<...> only
  Configuration/PluginConfig.cs  // BSIPA generated, public virtual props, [Ignore] runtime fields
  Installers/AppInstaller.cs     // config instance, services, HarmonyPatchManager (only for transpilers)
  Installers/MenuInstaller.cs    // MenuButtonManager or GameplaySetup tab, view controllers
  Installers/PlayerInstaller.cs  // early-return if disabled; gameplay services, pools, IAffinity patches
  Patches/*.cs                   // IAffinity classes, one per game method, guarded and prioritized
  Gameplay/*.cs                  // IInitializable/ITickable/IDisposable consumers of game events
  UI/*.cs + UI/Views/*.bsml      // BSMLAutomaticViewController + [HotReload] during dev
```

Minimal gameplay consumer (event-driven, allocation-free in the hot path):
```csharp
internal sealed class CutTracker : IInitializable, IDisposable {
    private readonly ScoreController scoreController;
    private readonly TMP_Text label;           // created once
    private int sum, count, lastShown = -1;
    public CutTracker(ScoreController sc /*, injected UI factory */) { scoreController = sc; }
    public void Initialize() => scoreController.scoringForNoteFinishedEvent += OnScored;
    public void Dispose()    => scoreController.scoringForNoteFinishedEvent -= OnScored;
    private void OnScored(ScoringElement e) {
        if (e is not GoodCutScoringElement g || g.noteData.scoringType != NoteData.ScoringType.Normal) return;
        sum += g.cutScoreBuffer.cutScore; count++;
        var avg = sum / count;
        if (avg != lastShown) { lastShown = avg; label.text = avg.ToString(); } // set text only on change
    }
}
```
(Pattern assembled from `CountersPlus/Counters+/Counters/CutCounter.cs:49-110` and the checklist in section 5;
not copied from any one repo.)
