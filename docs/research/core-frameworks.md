# Beat Saber PC core modding frameworks: BSIPA, SiraUtil, BSML, SongCore

Researched 2026-10-22. All file:line references are relative to
`../`.

## 0. Clones and which branch is "current"

| Repo (canonical) | Local path | Default branch | What is actually current |
|---|---|---|---|
| nike4613/BeatSaber-IPA-Reloaded (bsmg/... redirects here) | `BSIPA` | master (4.3.7, last release 2026-01) | `dev` adds unreleased fixes (7d6894e4 "Replace config file watchers with a polling thread"); `v5` is a breaking rewrite (removes IPA.exe, forked Doorstop, obsolete IPlugin shims, pending-updates) |
| Auros/SiraUtil | `SiraUtil` | main (GameVersion 1.42.0, v3.4.0) | `beat-saber-1-44-2`, `beat-saber-1-44-3`, `beat-saber-urp` (1.45.3, pushed 2026-10-16) |
| monkeymanboy/BeatSaberMarkupLanguage | `BeatSaberMarkupLanguage` | master (GameVersion 1.41.1, v1.14.2) | `beat-saber-1-44-2`, `beat-saber-urp` (1.45.1) |
| Kylemc1413/SongCore | `SongCore` | master (stale, 2025-02) | `dev` (2026-09, v3.16.0, gameVersion 1.42.3). **Local clone is checked out on `dev`.** |

Forks checked with `gh api repos/X/forks`: recently-pushed forks (SlopSaber/*, DaVarga, GalaxyMaster2, BrandonLogandi)
are personal forks / PR sources; the originals are the maintained ones (recent pushes, releases, PRs merged from those forks,
e.g. SongCore dev merge of GalaxyMaster2 PR #182). The `beat-saber-urp` branches cover game 1.45.x; the URP runtime
assembly reference (`Unity.RenderPipelines.Universal.Runtime`) first appears in SiraUtil's 1.44.2 port.

Key takeaway: **main/master is not "latest game".** Pick the branch matching your installed game version.

---

## 1. BSIPA plugin lifecycle

### 1.1 Attributes
- `[Plugin(RuntimeOptions)]` on the class: `BSIPA/IPA.Loader/PluginInterfaces/Attributes/PluginAttribute.cs:14-31`.
- `RuntimeOptions` (`PluginAttribute.cs:43-75`):
  - `SingleStartInit` - enabled once with the game, disabled at exit. Simplest; BSML and SongCore use it.
  - `DynamicInit` - can be enabled/disabled at runtime; the instance is NOT destroyed on disable and is reused, Init runs only on first enable (`:65-71`). SiraUtil uses it.
- `[Init]` on constructor and/or methods (`PluginAttribute.cs:77-91`). If several constructors are marked, the one with the most params wins. Multiple `[Init]` methods are all called, in undefined order (`BSIPA/docs/articles/start-dev.md`).
- `[OnStart]`/`[OnEnable]` are interchangeable, as are `[OnExit]`/`[OnDisable]` (`LifecycleAttributes.cs:17-108`). Always invoked on the Unity main thread.
- Lifecycle methods are discovered with `BindingFlags.Public | BindingFlags.Instance` only, and may return `Task` (`BSIPA/IPA.Loader/Loader/PluginExecutor.cs:109-125`). A private `OnEnable` is silently ignored.
- Missing enable/disable methods log a Notice "Is this intentional?" (`PluginExecutor.cs:117-121, 173-177`). Suppress with `[NoEnableDisable]` (`LifecycleAttributes.cs:113-124`; used by `SiraUtil/SiraUtil.Suite/Plugin.cs:16`) or an empty `[OnExit]` (SongCore does exactly this: `SongCore/source/SongCore/Plugin.cs:65-69`).

### 1.2 What can be injected into `[Init]`
`BSIPA/IPA.Loader/Loader/PluginInitInjector.cs:137-140`: `IPA.Logging.Logger`, `PluginMetadata`, `IPA.Config.Config`, `IAntiMalware`.
Other mods can register more with `PluginInitInjector.AddInjector(Type, ...)` (`:97-105`). SiraUtil does this for `Zenjector`
(`SiraUtil/SiraUtil/Plugin.cs:40-41, 81-91`) - that is why `Zenjector` "magically" appears in your ctor once you depend on SiraUtil.

### 1.3 manifest.json
Fields from `BSIPA/IPA.Loader/Loader/PluginManifest.cs:16-79`:
- Required: `name`, `description` (string or array of lines; `"#![Ns.file.md]"` first line loads an embedded markdown file), `version` (SemVer), `author`.
- `id` (nullable but should be set; package managers key on it), `gameVersion`, `dependsOn` / `conflictsWith` (id -> SemVer range, e.g. `"^1.12.0"`), `loadBefore` / `loadAfter`, `features`, `icon` (embedded resource name), `files`, `links` (`project-home`, `project-source`, `donate`), `misc.plugin-hint` (full type name of the plugin class).
- `gameVersion` mismatch is only a **warning**: `PluginLoader.cs:884-885` "developed for game version X, so it may not work properly". Not enforced.
- `features`: object keyed by feature name, value = config object(s). Examples: `"IPA.DefineFeature"` in BSIPA's own manifest (`BSIPA/IPA.Loader/Loader/manifest.json`), `"SiraLocalizer.LocalizedPlugin"` in BSML (`BeatSaberMarkupLanguage/BeatSaberMarkupLanguage/BeatSaberMarkupLanguage.csproj:21-28`). Feature base class with hooks BeforeInit/AfterInit/AfterDisable: `BSIPA/IPA.Loader/Loader/Features/Feature.cs:14-59`. You will rarely write one.
- Manifest must be an **EmbeddedResource** (`BSIPA/docs/articles/dev-resources/Demo.csproj:12-15`).

Two styles exist in the wild:
1. Hand-written `manifest.json` embedded (BSIPA tutorial, SongCore `source/SongCore/manifest.json`, SiraUtil.Suite).
2. Generated from csproj properties by `nicoco007.BeatSaberModdingTools.Tasks`: `<GameVersion>`, `<Version>`, `<DependsOn Include="BSIPA" Version="^4.3.6"/>`, `<PluginFeatures>` (`SiraUtil/SiraUtil/SiraUtil.csproj:17-29, 89-92`; `BeatSaberMarkupLanguage.csproj:18-32`). SiraUtil has no manifest.json at all. (Build details: other agent's lane.)

### 1.4 Game-version boundary behaviour ("yeet")
`BSIPA/IPA.Loader/Loader/PluginLoader.cs:53-70` + `Utilities/UnityGame.cs:104-131`: BSIPA stores the last game version; when the game
updates it moves `Plugins/` to `Old <ver> Plugins/` (and restores a matching old folder if one exists). Controlled by
`YeetMods` in `UserData/Beat Saber IPA.json` (`Config/SelfConfig.cs:152-155`). Practical effect: after every game update your
dev mod disappears from Plugins until you rebuild/copy it.

### 1.5 BSIPA config stores
Canonical sample: `BSIPA/docs/articles/dev-resources/PluginConfig.cs` and `Plugin.cs:17-26`.
```csharp
[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)] // needed if class is internal
internal class PluginConfig {
    public static PluginConfig Instance { get; set; }
    public virtual bool Enabled { get; set; } = true;          // virtual => auto-save on set
    [UseConverter(typeof(ListConverter<string>))]
    public virtual List<string> Items { get; set; } = new();   // collections need converters
    public virtual void OnReload() { }   // called OFF the main thread after file reload
    public virtual void Changed() { }    // called after any virtual prop set; call manually after mutating collections
    public virtual void CopyFrom(PluginConfig other) { }
}
// in [Init]: PluginConfig.Instance = conf.Generated<PluginConfig>();
```
Rules (`BSIPA/IPA.Loader/Config/Stores/GeneratedStorePublicInterface.cs:29-78`): class public (or internal + InternalsVisibleTo) and NOT sealed;
only public/protected fields and props with public/protected get+set; `virtual` enables change tracking + locking;
`ChangeTransaction()` for batching; `[NotifyPropertyChanges]` or implementing `INotifyPropertyChanged` makes the generated type raise
PropertyChanged (useful for BSML binding). Attributes: `[Ignore]`, `[NonNullable]`, `[UseConverter]`, `[SerializedName]`
(`Config/Stores/Attributes.cs:15-132`). File lands at `UserData/<manifest name>.json`; `[Config.Name]` / `[Config.Prefer]` on the ctor param change that.
For a subfolder SongCore uses `Config.GetConfigFor("SongCore/SongCore")` and must create the dir first (`SongCore/source/SongCore/Plugin.cs:24-31`).

---

## 2. Zenject + SiraUtil DI

### 2.1 Mechanism
The game is built on Zenject. SiraUtil Harmony-prefixes `Zenject.Context.InstallInstallers` and records every installer type the
context is about to run, then fires `ContextInstalling` (`SiraUtil/SiraUtil/Zenject/Harmony/ContextDecorator.cs:10-71`). The
`ZenjectManager` matches your registered install instructions against those types and installs your installer into that same
container. So "Location.Menu" really means "whenever a context that contains `MainSettingsMenuViewControllersInstaller` installs".

### 2.2 Locations -> backing game installer
`SiraUtil/SiraUtil/Zenject/Location.cs:8-99` and the mapping `Zenjector.cs:107-177`:
- `App` -> `BeatSaberInit` (project-wide; visible in every child container)
- `Menu` -> `MainSettingsMenuViewControllersInstaller`
- `StandardPlayer` -> `StandardGameplayInstaller`, `CampaignPlayer` -> `MissionGameplayInstaller`, `MultiPlayer` -> `MultiplayerLocalActivePlayerInstaller`; `Player` = all three; `Singleplayer` = Standard|Campaign|Tutorial
- `Tutorial` -> `TutorialInstaller`, `GameCore` -> `GameCoreSceneSetup` (current map/difficulty, not necessarily player objects)
- `MultiplayerCore`, `ConnectedPlayer`, `AlwaysMultiPlayer`, `InactiveMultiPlayer`
- `HealthWarning`, `Credits`, `StartupError` (by scene name)
For any game installer not in the enum: `zenjector.Install<MyInstaller, GameplayCoreInstaller>()` (`SiraUtil.Suite/Plugin.cs:28`, `Zenjector.cs:78`).

### 2.3 Zenjector API (`SiraUtil/SiraUtil/Zenject/Zenjector.cs`)
- `Install<T>(Location, params object[] ctorArgs)` `:59` (args override injection; then T cannot be a MonoInstaller)
- `Install(Location, Action<DiContainer>)` `:89` - inline lambda installer
- `Expose<TMonoBehaviour>(...)` `:192` - bind a scene MonoBehaviour the game didn't bind (like a ZenjectBinding)
- `Mutate<TMonoBehaviour>(Action<Context,T>, order)` `:227`, `Mutate<T, TNewComponent>` `:242` - tweak/add components to scene objects at install time
- `UseLogger(logger)` `:274` -> inject `SiraLog`; `UseMetadataBinder<TKey>()` `:293`; `UseHttpService()` `:304`; `UseSiraSync(...)` `:318`
- `UseAutoBinder()` `:343` + `[Bind(Location...)]` on classes (`SiraUtil/SiraUtil/Attributes/BindAttribute.cs`) - binds IInitializable/ITickable/IDisposable/IAffinity/ViewController/FlowCoordinator automatically. Scans all assembly types, so optional (soft) deps cause warnings.
- `IAsyncInitializable` (`SiraUtil/SiraUtil/Zenject/IAsyncInitializable.cs:9`).
- View controller binding helper: `Container.Bind<MyVC>().FromNewComponentAsViewController().AsSingle()` (`SiraUtil/SiraUtil/Extras/ZenjectExtensions.cs:21`).

### 2.4 Binding patterns seen in the libs
- `Container.BindInterfacesTo<X>().AsSingle()` for pure services/hooks (SongCore `Installers/MenuInstaller.cs`, `GameInstaller.cs`, `AppInstaller.cs`).
- `BindInterfacesAndSelfTo<X>()` when others need the concrete type (`SongCore/source/SongCore/Installers/MenuInstaller.cs`, `Loader`).
- `Container.BindInstance(config).AsSingle()` to pass the BSIPA config into DI (`SongCore AppInstaller.cs:23`).
- `BindExecutionOrder<T>(-900)` to initialize early/dispose late (`BeatSaberMarkupLanguage/BeatSaberMarkupLanguage/Harmony Patches/MainMenuInstaller.cs:40-43`).
- **Gotcha (SiraUtil issue #49):** `Container.Bind<X>()` on a type that implements `IInitializable`/`ITickable`/`IDisposable` does NOT register those interfaces; Zenject never calls them and there is no error. Use `BindInterfacesTo`/`BindInterfacesAndSelfTo`.

### 2.5 BSML does NOT need SiraUtil
BSML hooks the same installers with plain Harmony prefixes (`BeatSaberMarkupLanguage/BeatSaberMarkupLanguage/Harmony Patches/MainMenuInstaller.cs:14-69`):
prefix on `BeatSaberInit.InstallBindings` and `MainSettingsMenuViewControllersInstaller.InstallBindings`, grabbing
`__instance.Container`. That is the "raw" version of what SiraUtil abstracts. Useful pattern if you want zero dependencies.

---

## 3. Patching: Harmony vs Affinity vs MonoMod Hook

### 3.1 Harmony (HarmonyX)
BSIPA ships **HarmonyX 2.16.0** (`BSIPA/IPA.Loader/IPA.Loader.csproj:31`, manifest `files` -> `Libs/0Harmony.dll`). Mods reference
`$(BeatSaberDir)\Libs\0Harmony.dll` with `Private=False` (`BeatSaberMarkupLanguage.csproj` 0Harmony reference) and never ship their own.

Conventions observed:
- One Harmony instance per mod, unique id, `PatchAll(assembly)`:
  - BSML patches in `[Init]` (`BeatSaberMarkupLanguage/BeatSaberMarkupLanguage/Plugin.cs:25-33`, wrapped in try/catch).
  - SiraUtil patches in `[OnEnable]` and unpatches in `[OnDisable]` - correct for DynamicInit (`SiraUtil/SiraUtil/Plugin.cs:61-75`). Unpatch is deferred to the next main-thread tick so Zenject's DisposableManager runs first (`:73-74`).
- Attribute style: `[HarmonyPatch(typeof(T), nameof(T.Method))]` on a static class with `Prefix`/`Postfix`/`Transpiler` named methods (`BSML Harmony Patches/MainMenuInstaller.cs:14-21`), or `[HarmonyPatch(typeof(T))]` on class + `[HarmonyPatch(nameof(...))] [HarmonyPrefix]` on methods (`SiraUtil/SiraUtil/Zenject/Harmony/ContextDecorator.cs:10-21`).
- Overload disambiguation by argument type array (`ContextDecorator.cs:19`; `BSML Harmony Patches/SegmentedControlCellSelectionStateDidChange.cs:10`). Getter via `MethodType.Getter` (`BSML Harmony Patches/ScrollViewGetPositionPatch.cs:8`). Ctor via `MethodType.Constructor` (`BSML Harmony Patches/TableView_ctor.cs:9`).
- Multiple targets: `[HarmonyPatch]` + `static IEnumerable<MethodInfo> TargetMethods()` (`SiraUtil/SiraUtil/Tweaks/ZenjectLifecycleUnasserter.cs:12-33`).
- Transpiler canonical shape with `CodeMatcher` + `ThrowIfInvalid` so a game update fails loudly instead of silently mis-patching (`SiraUtil/SiraUtil/Tweaks/DontCallSetTrackingSpaceType.cs:9-21`):
```csharp
[HarmonyPatch(typeof(SettingsApplicatorSO), nameof(SettingsApplicatorSO.ApplyGraphicSettings))]
internal class DontCallSetTrackingSpaceType {
    static readonly MethodInfo M = AccessTools.DeclaredMethod(typeof(XRDevice), nameof(XRDevice.SetTrackingSpaceType));
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> ins) =>
        new CodeMatcher(ins)
            .MatchForward(false, new CodeMatch(OpCodes.Ldc_I4_1), new CodeMatch(i => i.Calls(M)), new CodeMatch(OpCodes.Pop))
            .ThrowIfInvalid("Call to SetTrackingSpaceType not found")
            .RemoveInstructions(3).InstructionEnumeration();
}
```

### 3.2 Affinity (SiraUtil)
Harmony patches as **instance methods on DI-constructed objects**. Mark a class `IAffinity`, bind it in an installer, decorate methods.
Example: `SiraUtil/SiraUtil.Suite/Tests/Affinity/MainMenuViewControllerPatchTest.cs:6-27`:
```csharp
internal class LeaderboardPatch : IAffinity {
    private readonly SiraLog _log;
    public LeaderboardPatch(SiraLog log) => _log = log;   // constructor DI works
    [AffinityPrefix, AffinityPatch(typeof(PlatformLeaderboardViewController), nameof(PlatformLeaderboardViewController.SetData))]
    protected void Prefix() => _log.Info("SetData");
}
// installer: Container.BindInterfacesTo<LeaderboardPatch>().AsSingle();
```
- Attributes: `AffinityPrefix/Postfix/Transpiler/Finalizer/Priority/Before/After/EmitIL` (`SiraUtil/SiraUtil/Affinity/Attributes/`). `AffinityPatch(Type, methodName, AffinityMethodType {Normal,Getter,Setter,Constructor}, argumentTypes, argumentVariations {Ref,Out,Pointer})` (`AffinityPatchAttribute.cs:31,61`; `AffinityMethodType.cs`).
- Lifetime: patches are applied when the container builds the `AffinityKernel` (`SiraUtil/SiraUtil/Affinity/AffinityKernel.cs:14-29`, injected per container by `Affinity/Harmony/Patches/SceneAffinityKernelInjector.cs:47-55`) and **removed in `LateDispose`** when that container dies (`AffinityKernel.cs:31-34`). So an affinity bound in `Location.Player` only exists during a song. Harmony `PatchAll` is static and global for the whole session.
- When to use which: Affinity when the patch needs services/state from DI or should be scoped to a scene; plain Harmony for global fixes, patches that must exist before Zenject (installers, init), or static utility patches.
- Limits: no TargetMethod support yet (issue #54 open). Failures are caught and logged per class (`AffinityKernel.cs:20-27`) but issue #73 shows a bad parameter name can still crash the game.

### 3.3 MonoMod `Hook` (SongCore's choice)
SongCore migrated all Harmony patches to MonoMod RuntimeDetour hooks (commit d993ae1 "Migrate Harmony patches to MonoMod hooks").
Hook lifetime is tied to Zenject `IInitializable`/`IDisposable`: `SongCore/source/SongCore/Hooks/AllowNegativeNoteJumpSpeedHook.cs:14-31`:
```csharp
internal class AllowNegativeNoteJumpSpeedHook : IInitializable, IDisposable {
    private Hook _hook = null!;
    public void Initialize() => _hook = new Hook(
        typeof(BeatmapDifficultyMethods).GetMethod(nameof(BeatmapDifficultyMethods.NoteJumpMovementSpeed))!, Detour, true);
    public void Dispose() => _hook.Dispose();
    private float Detour(Func<BeatmapDifficulty,float,bool,float> orig, BeatmapDifficulty d, float njs, bool fast)
        => njs <= -VariableMovementDataProvider.kMinNoteJumpMovementSpeed ? njs : orig(d, njs, fast);
}
```
Pros: strongly typed, no magic parameter names (`__instance`, `___field`), can call/skip `orig`. Good alternative to Affinity for DI-scoped patches.

### 3.4 How to find what to patch
- The csproj `<Reference>` lists show which game assemblies matter: `Main`, `HMUI`, `HMLib`, `BeatSaber.ViewSystem`, `GameplayCore`, `BGLib.*`, `DataModels`, `Zenject` (`SiraUtil/SiraUtil/SiraUtil.csproj:32-78`). Decompile those from `Beat Saber_Data/Managed` with ILSpy/dnSpy (general practice, not shown in these repos).
- `Publicize="True"` on a reference (BepInEx.AssemblyPublicizer, `SiraUtil.csproj:94-97`) makes private game members compile-time accessible: e.g. `asyncSceneContext._registry.installers` (SiraUtil 1.44.2 branch ContextDecorator), `nameof(StandardLevelDetailView.RefreshContent)`. Without publicizing you use string names (`"DidActivate"`, `"TextForValue"` in BSML Harmony Patches) and `AccessTools`/reflection (`SiraUtil/SiraUtil/Sabers/Effects/ObstacleSaberSparkleEffectManagerPatch.cs:11`). Prefer `nameof` + publicizer: renames then become compile errors.
- Start from installers: a game Installer's `InstallBindings` lists every service in a scene; that is the map of what you can inject or patch.

---

## 4. BSML (UI)

### 4.1 Setup and access
BSML binds its services into the **menu** container (`Harmony Patches/MainMenuInstaller.cs:31-46`): `BSMLSettings`, `BSMLParser`,
`MenuButtons`, `GameplaySetup`. Static access goes through `ZenjectSingleton<T>.Instance`, which **throws "Tried getting X too early!"**
if used before that installer ran (`BeatSaberMarkupLanguage/BeatSaberMarkupLanguage/Util/ZenjectSingleton.cs:22`). The old lowercase
`.instance` singletons are superseded by `ZenjectSingleton<T>.Instance` (stale comments in `Util/TestPresenter.cs:14-18`);
`Util/NotifiableSingleton.cs:7` is `[Obsolete("Avoid using singletons.")]`. Preferred: inject `BSMLSettings`/`GameplaySetup`/
`MenuButtons` into a class bound at `Location.Menu`. Without Zenject: `MainMenuAwaiter.MainMenuInitializing` event or
`WaitForMainMenuAsync()` (`Util/MainMenuAwaiter.cs:12-53`); it fires again after every internal (soft) restart.

### 4.2 Mod settings tab (Main menu -> Mod Settings)
`BSMLSettings.AddSettingsMenu(name, resourcePath, host)` / `RemoveSettingsMenu(host)` (`Settings/BSMLSettings.cs:43-70`).
The resource is looked up in `Assembly.GetCallingAssembly()` (`:49`), so call it from your own assembly, not via a helper in another DLL.
Host class example: `Settings/SettingsTest.cs:8-60`; view: `Views/settings-test.bsml:1-16`:
```xml
<settings-container>
  <bool-setting text='Enabled' value='enabled' apply-on-change='true' />
  <slider-setting text='Offset' value='offset' min='-10' max='10' increment='0.5' />
  <dropdown-list-setting text='Mode' value='mode' choices='mode-options' />
</settings-container>
```
```csharp
internal class SettingsHost /* : NotifiableBase if you need NotifyPropertyChanged */ {
    [UIValue("enabled")] public bool Enabled { get => PluginConfig.Instance.Enabled; set => PluginConfig.Instance.Enabled = value; }
    [UIValue("mode-options")] private List<object> modes = new() { "A", "B" };
    [UIAction("#apply")] private void OnApply() { }
}
```
Attributes: `[UIValue]`, `[UIAction]` (`#apply`, `#post-parse` are special), `[UIComponent]`, `[UIObject]`, `[UIParams]` (`Attributes/`).
Properties that change from code need `NotifyPropertyChanged()` (NotifiableBase) to refresh the UI (`SettingsTest.cs:30-39`).

### 4.3 Gameplay setup tab (left panel in Solo/Online/Campaign)
`GameplaySetup.AddTab(name, resource, host, MenuType)` / `RemoveTab(name)` (`GameplaySetup/GameplaySetup.cs:66-80`); `MenuType` flags Solo/Online/Campaign/Custom/All (`GameplaySetup/MenuType.cs:6-13`).

### 4.4 Menu buttons (left "Mods" panel)
`MenuButtons.RegisterButton(new MenuButton(text, hoverHint, onClick))` (`MenuButtons/MenuButtons.cs:23`, `MenuButton.cs:14-22`).

### 4.5 View controllers and flow coordinators
- `BSMLAutomaticViewController` loads its `.bsml` from `[ViewDefinition("Ns.Views.file.bsml")]` or, by default, the embedded resource named `<Namespace>.<TypeName>[.bsml]` (`ViewControllers/BSMLAutomaticViewController.cs:31-78, 172-178`).
- `[HotReload(RelativePathToLayout=...)]` reads the .bsml from disk and reloads on change in debug - fast UI iteration (`Attributes/HotReloadAttribute.cs`, `BSMLAutomaticViewController.cs:17-29`).
- Create: `BeatSaberUI.CreateViewController<T>()`, `BeatSaberUI.CreateFlowCoordinator<T>()` (`BeatSaberUI.cs:117,158`); present: `parentFlow.PresentFlowCoordinator(child, ...)` (`BeatSaberUI.cs:628`). With SiraUtil: `Container.Bind<MyVC>().FromNewComponentAsViewController().AsSingle()`.
- Raw parse into any GameObject: `BSMLParser.Instance.Parse(content, parentGO, host)` (`BSMLParser.cs:116`).

### 4.6 Floating screens (world-space panels, also usable in-game)
`FloatingScreen.CreateFloatingScreen(Vector2 size, bool createHandle, Vector3 pos, Quaternion rot, float curvatureRadius = 0, bool hasBackground = false)`
(`FloatingScreen/FloatingScreen.cs:139-200`); then `screen.SetRootViewController(vc, AnimationType.None)` (inherited HMUI `Screen` game API, verify against your version). Handle events `HandleGrabbed`/`HandleReleased`
(`:61-63`) to persist position. It injects via `BeatSaberUI.DiContainer` (`:146`), so the menu must have loaded at least once.
Note the extra overload kept "for binary compatibility" (`:134-137`): adding optional parameters breaks already-compiled dependents, so libs keep old overloads.

---

## 5. SongCore

- Public API is static `SongCore.Collections` (`SongCore/source/SongCore/Collections.cs`) and `SongCore.Loader`:
  - `Collections.GetCustomLevelSongData(levelID)` -> `SongData` (`:46`); `GetCustomLevelSongDifficultyData(BeatmapKey)` -> per-difficulty data (`:60`); `GetCustomLevelHash(levelID)` (`:41`); `songWithHashPresent(hash)` (`:30`); `levelIDsForHash(hash)` (`:36`).
  - `RegisterCapability(string)` / `DeregisterCapability` / `capabilities` (`:163-174, 27`); `RegisterCustomCharacteristic(...)` (`:115`); `AddSeparateSongFolder(...)` (`:140`).
  - `Loader.SongsLoadedEvent`, `LoadingStartedEvent`, `OnLevelPacksRefreshed` (`Loader.cs:71-73`); `Loader.AreSongsLoaded/AreSongsLoading` (`:88-89`); `Loader.CustomLevels` dictionary (`:77`); `Loader.Instance.RefreshSongs(fullRefresh)` (`:258`, refuses during GameCore scene `:260`).
- `SongData` = parsed SongCore-specific `_customData` from Info.dat: per-difficulty `_difficultyLabel`, colors, `_requirements`, `_suggestions`, `_warnings`, `_information`, `_oneSaber`, `_styleTags`, plus song-level `contributors`, `_colorSchemes`, `_environmentNames`, characteristic details (`Data/SongData.cs:13-110`). Custom level IDs start with `CustomLevelLoader.kCustomLevelPrefixId` (`Collections.cs:64`).
- **Capabilities contract**: map authors list mod names in `_requirements`; SongCore disables Play and Practice if any requirement is not in `Collections.capabilities` (`Hooks/SongDataMenuHooks.cs:331-340`). A mod that implements a map feature (Noodle, Chroma, etc.) calls `Collections.RegisterCapability("Name")` at startup. `_warnings` containing `"WIP"` disables Play (`:307-310`).
- SongCore itself uses SiraUtil installers (`Plugin.cs:30-33`): App (config + global hooks), Menu (Loader, UI, hooks), StandardPlayer (in-song hooks), all hooks being MonoMod `Hook`s bound as `IInitializable, IDisposable` (`Installers/MenuInstaller.cs`, `GameInstaller.cs`, `AppInstaller.cs`).

Reading song data from your own mod (sketch):
```csharp
// Location.GameCore or Player: inject the current map key
public MyThing(GameplayCoreSceneSetupData setup) {
    var diff = SongCore.Collections.GetCustomLevelSongDifficultyData(setup.beatmapKey);
    var label = diff?._difficultyLabel;
}
```
(`GameplayCoreSceneSetupData` / `BeatmapKey` are game types; verify member names against your game version.)

---

## 6. How the libs handle game-version breakage

- **Branch per game version, no `#if`.** Across all four repos the only conditional symbols are `DEBUG`, `HRVC_DEBUG`, `BeatSaber` (BSIPA's multi-game build) - none per game version (grep of `#if`). SiraUtil and BSML keep `main`/`master` on an older game and cut `beat-saber-1-44-2`, `beat-saber-1-44-3`, `beat-saber-urp` branches; each is a small commit titled just "Beat Saber 1.44.2" etc.
- Typical port diffs (SiraUtil `git show 80c7a29`, "Beat Saber 1.44.2"):
  - Bump `<GameVersion>` and `<Version>` in csproj.
  - Game type renames: `StandardLevelScenesTransitionSetupDataSO` -> `StandardLevelScenesTransitionSetupData` (all `*SetupDataSO`) in `SiraUtil/SiraUtil/Services/ILevelFinisher.cs`.
  - Private field moves: `asyncSceneContext._asyncInstallers` -> `asyncSceneContext._registry.installers` in `ContextDecorator.cs:59`.
  - New assembly reference needed: `Unity.RenderPipelines.Universal.Runtime` (already in the 1.44.2 port).
  - BSML URP port: one-line GameVersion bump and deletion of a now-unneeded patch (`git diff master...origin/beat-saber-urp`: removed `Harmony Patches/VerticalScrollIndicator.cs`).
- Commit-log examples of version fixes: SongCore "Fix for game version 1.41.1", "Update for game v1.42.3", "Fix broken note spawn line hook", "Fix lighting not working"; BSML "Update for Beat Saber 1.40.0/1.40.9/1.41.1"; BSIPA "Downgrade Newtonsoft due to binary breaking change", "Fix patches breaking on Proton".
- Defensive patterns: `CodeMatcher.ThrowIfInvalid(...)` in transpilers; try/catch around `PatchAll` (BSML Plugin.cs:25-33); Affinity per-class try/catch; `nameof` + publicizer so renames fail at compile time.
- For personal use: target exactly one game version (the one installed), keep one branch per game version if you ever upgrade, and pin dependency mod versions in `dependsOn` with `^`.

---

## 7. Canonical minimal mod (BSIPA + SiraUtil + BSML)

manifest.json (EmbeddedResource; or generate via BeatSaberModdingTools.Tasks csproj props):
```json
{
  "$schema": "https://raw.githubusercontent.com/bsmg/BSIPA-MetadataFileSchema/master/Schema.json",
  "id": "MyMod", "name": "MyMod", "author": "me", "version": "0.1.0",
  "description": "Personal tweaks.", "gameVersion": "1.42.3",
  "dependsOn": { "BSIPA": "^4.3.6", "SiraUtil": "^3.1.0", "BeatSaberMarkupLanguage": "^1.12.0" }
}
```
Plugin.cs (shape from `SongCore/source/SongCore/Plugin.cs:14-34` and `SiraUtil.Suite/Plugin.cs:16-41`):
```csharp
[Plugin(RuntimeOptions.SingleStartInit), NoEnableDisable]
public class Plugin {
    [Init]
    public Plugin(IPA.Logging.Logger logger, IPA.Config.Config conf, Zenjector zenjector) {
        zenjector.UseLogger(logger);                                  // inject SiraLog anywhere
        var cfg = conf.Generated<PluginConfig>();
        zenjector.Install(Location.App,  c => c.BindInstance(cfg).AsSingle());
        zenjector.Install(Location.Menu, c => c.BindInterfacesTo<SettingsRegistrar>().AsSingle());
        zenjector.Install(Location.Player, c => c.BindInterfacesTo<MyGameplayPatch>().AsSingle()); // IAffinity
    }
}
internal class SettingsRegistrar : IInitializable, IDisposable {
    private readonly BSMLSettings _settings; private readonly SettingsHost _host;
    public SettingsRegistrar(BSMLSettings s, PluginConfig cfg) { _settings = s; _host = new SettingsHost(cfg); }
    public void Initialize() => _settings.AddSettingsMenu("MyMod", "MyMod.Views.settings.bsml", _host);
    public void Dispose()    => _settings.RemoveSettingsMenu(_host);
}
```
(Lifecycle/DI shapes verified against the repos; the composite above is illustrative, compile against your game version.)

Zero-dependency alternative: BSIPA only + `new Harmony("me.mymod").PatchAll()` in `[OnStart]`, `UnpatchSelf()` in `[OnExit]`
(BSML `Plugin.cs:15-47` style).

---

## 8. Gotchas and lessons

1. `gameVersion` mismatch only warns; BSIPA moves `Plugins/` aside on every game update (`PluginLoader.cs:53-70`). Re-deploy after updates.
2. Lifecycle methods must be **public instance** (`PluginExecutor.cs:112`), else silently skipped.
3. Config class must be public non-sealed, or internal + `InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)`; props `virtual` for auto-save; collections need `[UseConverter]` and a manual `Changed()` after mutation (`GeneratedStorePublicInterface.cs:29-78`, `start-dev.md`).
4. `OnReload()` runs on a background thread; do not touch Unity there (`docs/articles/dev-resources/PluginConfig.cs:61-68`). Config file watching was unreliable (BSIPA issue #115 open) and is replaced by polling on `dev` (7d6894e4, unreleased).
5. `Bind<X>()` vs `BindInterfacesTo<X>()`: IInitializable/ITickable silently not called with plain Bind (SiraUtil #49).
6. Harmony parameter/field injection (`__instance`, `___field`, original param names) is by **name** and fails at patch time when names don't match, e.g. after a game rename (SiraUtil #73: `Parameter "_tableView" not found` in a method whose param is `tableView`). MonoMod `Hook` delegates are positional and avoid this.
7. BSML `Instance` throws before the menu container installs (`ZenjectSingleton.cs:22`); register from a Menu-bound IInitializable. The menu container is rebuilt on internal restart, so register in Initialize and remove in Dispose, never once in `[OnStart]`. (BSML commit dfc6ed5 "Fix monochrome font having improper fallbacks after internal restart" shows the same class of bug.)
8. `AddSettingsMenu` resolves the resource in the *calling* assembly (`BSMLSettings.cs:49`).
9. DynamicInit: unpatch on disable, and defer unpatch until disposables ran (`SiraUtil/SiraUtil/Plugin.cs:73-74`).
10. Do not ship `0Harmony.dll`; BSIPA provides HarmonyX. Reference game/lib DLLs with `Private=False`.
11. Public lib API: adding optional parameters is a binary break; keep the old overload (`FloatingScreen.cs:134`). Relevant if anyone else ever depends on your mod.
12. `UseAutoBinder` scans every type; optional/soft dependencies cause type-load warnings (`Zenjector.cs:336-341`).
13. SongCore requirements: if you write a mod that adds map features, `RegisterCapability` or maps listing you in `_requirements` stay unplayable (`SongDataMenuHooks.cs:331-340`).
14. Verify before building against SongCore `dev`: its manifest requires `BSIPA ^4.3.8` (commit 32570f1 "Update for BSIPA v4.3.8") but the latest BSIPA release tag is 4.3.7.
15. BSIPA anti-piracy has false positives (ReShade, OpenComposite, Intel GPA: issues #112, #118-120) - not your mod's fault if users hit "Invalid installation".
16. `Directory.CreateDirectory` before `Config.GetConfigFor("Sub/Name")` if you want a UserData subfolder (`SongCore Plugin.cs:24-25`).
