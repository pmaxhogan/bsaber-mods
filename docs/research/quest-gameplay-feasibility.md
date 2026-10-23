# Gameplay feasibility recon: Beat Saber PC 1.40.8 (2026-10-22)

Scope: difficulty hot-swap, seeking, lookahead, controller input, HUD/overlay placement.
Feeds design questions; not an implementation plan.

## Method and sources

- Signatures: reflection dump of the LIVE 1.40.8 install (`~\BSManager\BSInstances\1.40.8\Beat Saber_Data\Managed`,
  read-only `Assembly.LoadFrom` from PowerShell 7). Script: `scratchpad\dump.ps1`. "SIG" below = verified this way.
  (BeatSaberReferenceAssemblies HEAD is 1.45.3; the 1.40.8 snapshot is commit `b0da3b4` if a build needs it.)
- Method bodies: open-source mods only (no decompiler). Clones used: Heck (master 3bbcaf6), beatleader-mod (25c84a7d),
  SiraUtil (e165284), CountersPlus (master 2347a60), Camera2, BSML (9a74f08).
- New clones this session (all old, pre-1.30): `PracticePlugin` (Kylemc1413, 2021), `IntroSkip` (Kylemc1413/Intro-Skip, 2021),
  `Beatsaber-Clock` (rgooler, 2019, contains no useful .cs). Treat them as history, not working code for 1.40.8.

---

## 1. Difficulty hot-swap mid-song (practice mode)

### VERIFIED (SIG unless a path is given)
- `GameplayCoreSceneSetupData` [Main]: fields `beatmapKey`, `beatmapLevel`, `practiceSettings`, `gameplayModifiers`,
  `playerSpecificSettings`, `targetEnvironmentInfo`, `originalEnvironmentInfo`, private `_beatmapDataLoader`;
  props `beatmapLevelData : IBeatmapLevelData`, `transformedBeatmapData : IReadonlyBeatmapData`;
  methods `IReadonlyBeatmapData TransformBeatmapData(IReadonlyBeatmapData)`, `LoadTransformedBeatmapData()`.
- `IBeatmapLevelData` holds ALL difficulties of the level: `GetBeatmapString(ref BeatmapKey)`, `ContainsBeatmapData(ref BeatmapKey)`.
  So another difficulty's data is already available in-scene without menu loading.
- `BeatmapDataLoader.LoadBeatmapData(IBeatmapLevelData, BeatmapKey, float startBpm, bool loadingForDesignatedEnvironment,
  IEnvironmentInfo environmentInfo, IEnvironmentInfo lightshowEnvironmentInfo, BeatmapLevelDataVersion, GameplayModifiers,
  PlayerSpecificSettings)` (sync) plus `LoadBeatmapDataAsync(...)`.
- `BeatmapCallbacksController` [Main, plain class]: private `_beatmapData`, `_callbacksInTimes : Dictionary<float, CallbacksInTime>`,
  `_startFilterTime`, `_prevSongTime`, `_songTime`; `ManualUpdate(float songTime)`, `ReplayState()`, `AddBeatmapCallback(...)`.
  `InitData(IReadonlyBeatmapData beatmapData, float startFilterTime, bool shouldKeepReplayState)`.
- `CallbacksInTime.lastProcessedNode : LinkedListNode<BeatmapDataItem>` (the per-aheadTime cursor into `allBeatmapDataItems`).
- `BeatmapCallbacksUpdater` [MonoBehaviour]: drives the controller from `LateUpdate()`; has `Pause()`/`Resume()`.
- `BeatmapObjectSpawnController`: holds its own `_beatmapData`, `_beatmapObjectSpawnMovementData`, `_variableMovementDataProvider`,
  and three callback wrappers (`_noteDataCallbackWrapper`, `_obstacleDataCallbackWrapper`, `_sliderDataCallbackWrapper`).
- `BeatmapData` [DataModels] private storage: `_allBeatmapData`, `_allBeatmapDataItemToNodeMap` (new vs Heck's copy, see below),
  `_beatmapDataItemsPerTypeAndId`, `_beatmapObjectsInTimeRowProcessor`, and backing fields for
  `cuttableNotesCount`/`obstaclesCount`/`bombsCount`.
- `VariableMovementDataProvider.Init(startHalfJumpDurationInBeats, maxHalfJumpDistance, noteJumpMovementSpeed,
  minRelativeNoteJumpSpeed, bpm, NoteJumpValueType, noteJumpValue, centerPosition, forwardVector)`: NJS/offset are
  baked in at scene start (JDFixer patches this Init: `JDFixer/HarmonyPatches.cs:7-10` per earlier notes).
- `BeatmapObjectManager`: private `_allBeatmapObjects : List<IBeatmapObjectController>`, `Despawn(NoteController|ObstacleController|SliderController)`,
  `DissolveAllObjects()`, `HideAllBeatmapObjects(bool)`.

Precedent A, Heck ReLoader (in-place reload of the SAME difficulty, no scene restart, audio untouched):
- `Heck/Heck/ReLoad/ReLoader.cs:197-275` `Reload()`: pauses, calls `_beatmapDataLoader.LoadBeatmapData(beatmapLevelData, beatmapKey, ...)`
  with the 1.40.8 signature (`#if !PRE_V1_40_8` branch), runs `TransformBeatmapData`, then `FillBeatmapData` (`:179-195`)
  overwrites the private lists of the ALREADY-INJECTED `IReadonlyBeatmapData` instance so every consumer that cached the
  reference sees new data.
- `:283-303` `Rewind()`: dissolves all live objects, stops cut sounds, sets every `CallbacksInTime.lastProcessedNode = null`,
  `_prevSongTime = 0`. `:305-311` seeks via `_startSongTime = t; SeekTo(0)`.
- Gated to debug mode AND practice: `Heck/Heck/HarmonyPatches/PatchedPlayerInstaller.cs:77-80`
  (`HeckController.DebugMode && sceneSetupData.practiceSettings != null`). Only works for CustomBeatmapData (CustomJSONData).
- Note: Heck's `FillBeatmapData` does not copy `_allBeatmapDataItemToNodeMap` or the count backing fields; a swap to a
  different difficulty should copy those too (counts feed notes-left / progress counters).

Precedent B, BeatLeader replayer seek (best reference for repositioning callbacks):
- `beatleader-mod/Source/2_Core/Replayer/BeatmapTimeController.cs:86-117` `Rewind(time)`: pause ATSC, `_beatmapCallbacksUpdater.Pause()`,
  silence `NoteCutSoundEffectManager.HandleNoteWasSpawned`, despawn all live objects (`:173+`, invokes private `Despawn` overloads),
  `_prevAudioSamplePos = -1`, `SeekTo((time - SongStartTime)/timeScale)`, `_prevSongTime = float.MinValue`, set each
  `lastProcessedNode = FindBeatmapItem(time)` (`:119-128`, last node before `time`, respecting `startFilterTime`),
  resume, force one `LateUpdate()`.

### INFERRED
- Realistic approaches, with risk:
  1. In-place data swap (Heck A + BeatLeader B combined, new BeatmapKey): load other difficulty via `LoadBeatmapData` +
     `TransformBeatmapData` (main thread hitch: tens to hundreds of ms for dense maps; or preload ALL difficulties async at
     scene start and swap instantly), despawn live objects, overwrite the injected `BeatmapData` internals, reposition
     every `lastProcessedNode` to "just before now + aheadTime", leave audio running. RISK: MEDIUM-HIGH.
     Gotchas: (a) NJS/JD differs per difficulty, so `VariableMovementDataProvider` (and `BeatmapObjectSpawnMovementData`)
     must be re-Init'ed or notes fly at the old NJS; re-Init re-registers its NJS callback (double registration risk).
     (b) Score/combo/max-score/energy keep running against old totals (`ScoreController` max-possible, notes-left counters).
     Practice mode does not submit, so mostly cosmetic. (c) Objects whose spawn time already passed but hit time is in the
     future (inside the jump window) will pop in mid-flight unless you re-spawn them deliberately; or accept a "gap"
     of one jump duration (about 0.5-1 s) after the swap. (d) Lighting events are also in the list: resetting cursors to
     null replays every past event (Heck does this on purpose); positioning cursors (BeatLeader) avoids it.
     (e) Swap must avoid noodle/chroma maps (CustomJSONData types) or handle CustomBeatmapData fields like Heck.
  2. Replace the whole `IReadonlyBeatmapData` reference instead of its internals: NOT practical; it is cached in many
     places (callbacks controller, spawn controller, score/counters, Heck/Counters+ via DI). Overwrite internals instead.
  3. Fast restart at same time: restart the level with new `BeatmapKey` and `PracticeSettings(startSongTime = now,
     songSpeedMul)` (ctor SIG `PracticeSettings(float startSongTime, float songSpeedMul)`; `startInAdvanceAndClearNotes`
     flag exists). Scene reload costs ~2-4 s plus `PracticeSettings.kDelayBeforeStart` lead-in; audio restarts
     (a crossfade is cosmetic only). RISK: LOW. Mechanism: patch the setup data before `ILevelRestartController`
     restart, or go through `MenuTransitionsHelper` (Heck has a `ReloadOnRestart` concept, `Heck/Heck/Settings/Config.cs`).
  Recommendation for questions: offer (3) as baseline, (1) as stretch; ask whether a ~3 s restart is acceptable.
- Practice detection: `GameplayCoreSceneSetupData.practiceSettings != null` (SIG field; used exactly this way by Heck
  `PatchedPlayerInstaller.cs:77`, Counters+ `Counters+/Counters/NotesLeftCounter.cs:20`, IntroSkip
  `Intro Skip/Installers/IntroSkipGameInstaller.cs:18`, PracticePlugin `Plugin.cs:217`).

### Score / leaderboard (VERIFIED by proxy, not by game method body)
- SIG: `SoloFreePlayFlowCoordinator.ProcessLevelCompletionResultsAfterLevelDidFinish(..., bool practice)`.
- SiraUtil disables submission by forcing that `practice` arg to true: `SiraUtil/SiraUtil/Submissions/SubmissionPatches.cs:7-14`.
  That only makes sense if the game skips platform leaderboard upload when `practice` is true (INFERRED from intent).
- BeatLeader still UPLOADS practice plays, tagged `LevelEndType.Practice` (`beatleader-mod/Source/7_Utils/Replays/ScoreUtil.cs:30-48`,
  `LevelEndType.cs:8`); they are not ranked scores. ScoreSaber behaviour not checked (closed source).
- Safe design: also call SiraUtil `Submission.DisableScoreSubmission("ModName")` (`Submission.cs:46`) whenever the mod
  alters data or time. This blocks base game, SiraUtil-aware leaderboards, and BeatLeader (it checks `SiraSubmission`).

---

## 2. Seeking within a song

### VERIFIED
- SIG `AudioTimeSyncController.SeekTo(float startTimeOffset)`, plus private `_startSongTime`, `_songTime`, `_prevAudioSamplePos`,
  `_audioSource`, `timeScale`, `songEndTime`, `state`. Seek alone does NOT move beatmap callbacks; mods also fix
  `BeatmapCallbacksController` cursors (Section 1 precedents A and B).
- Seek users and their gating:
  - Heck ReLoader: debug + practice only (above).
  - BeatLeader: replays only (`BeatmapTimeController.cs:86-117`).
  - PracticePlugin (2021, pre-callbacks era): practice only (`PracticePlugin/Plugin.cs:217`), sets `_songTime`/`_startSongTime`
    by reflection (`SongSeekBeatmapHandler.cs:171`, `Plugin.cs:123`).
  - IntroSkip (2021): NORMAL PLAY ONLY (`IntroSkipGameInstaller.cs:18` requires `practiceSettings == null`), seeks by setting
    `AudioSource.time` directly (`SkipBehavior.cs:154`), only across empty intro/outro gaps (first object > 5 s), so it never
    needed callback fixes. It did not disable submission. Read on triggers via the old `VRControllersInputManager`.

### INFERRED
- "Skip to start/end of chapter": BeatLeader recipe works for forward and backward seeks at any time. Skipping only over
  empty gaps (IntroSkip style) is the simplest and least risky; skipping over notes needs despawn + cursor reposition.
- In normal play, skipping past notes changes the score profile; disable submission in that case. Gap-only skips have
  community precedent in normal play (IntroSkip) but leaderboards may treat them differently today (unverified).

---

## 3. Lookahead queries (upcoming notes and walls)

### VERIFIED
- Data: inject `IReadonlyBeatmapData` (SIG: `allBeatmapDataItems : LinkedList<BeatmapDataItem>`, `GetBeatmapDataItems(int)`),
  or `GameplayCoreSceneSetupData.transformedBeatmapData`. Items are time-sorted (seconds, `BeatmapDataItem.time`).
- `NoteData`: `lineIndex`, `noteLineLayer`, `colorType` (bombs = `ColorType.None` / `gameplayType`), `cutDirection`, `time`.
- `ObstacleData`: `time`, `duration`, `endBeat`, `lineIndex`, `lineLayer : NoteLineLayer` (Base/Upper/Top), `width`, `height` (ints).
- `SliderData` arcs/chains have `tailTime` (as used in `NoodleObjectsCallbacksManager.cs:93-98`).
- Geometry helper: `BeatmapObjectSpawnMovementData.GetObstacleSpawnData(ObstacleData)` returns `ObstacleSpawnData(moveOffset,
  obstacleWidth, obstacleHeight)` in metres; also `GetObstacleOffset(lineIndex, layer)`, `noteLinesDistance`,
  `verticalLayersDistance`, `centerPos`. Reachable via `BeatmapObjectSpawnController.beatmapObjectSpawnMovementData` (public prop).
- Head: `PlayerTransforms.headPseudoLocalPos` / `headWorldPos`. Live hit test: `PlayerHeadAndObstacleInteraction`
  (`playerHeadIsInObstacle`, `headDidEnterObstacleEvent`, `RefreshIntersectingObstacles(Vector3 worldPos)`) checks
  only ACTIVE obstacles, so it cannot predict; use the spawn data for prediction.

### INFERRED
- "No notes in next 8 s": walk `allBeatmapDataItems` from a cached cursor (keep your own node pointer, advance each frame),
  check `NoteData` (excluding bombs if desired) and `SliderData` with `time` in (now, now+8]. O(items in window).
- "No walls hitting the head in next 8 s": for each `ObstacleData` overlapping [now, now+8] (time to time+duration),
  compute x span = moveOffset.x +- obstacleWidth/2 and y span = moveOffset.y .. +obstacleHeight from
  `GetObstacleSpawnData`, test against head x (with margin, e.g. +-0.2 m) and head y. Simple lane rule: walls covering
  lineIndex 1 or 2 with lineLayer Base/Upper and height >= 3 usually hit a standing head (crouch walls start at Top).
- Mapping Extensions / Noodle maps use out-of-range lineIndex (>= 1000) or custom positions; skip or treat as unknown.

---

## 4. Controller input (Y button, right thumbstick)

### VERIFIED
- 1.40.8 uses Unity Input System + OpenXR: `UnityXRHelper` [HMLib] has `InputActionReference _pauseGameActionReference`,
  `_headPositionActionReference`, etc. `VRControllersInputManager` (used by IntroSkip 2021) is NOT present in Main/HMLib.
- SIG `UnityXRController` [HMLib] binds only `positionAction`, `rotationAction`, `thumbstickAction` per hand (plus pause action on
  `UnityXRHelper`): the game binds no face-button (A/B/X/Y) action, so Y is unclaimed by the game.
- `IVRPlatformHelper` [HMLib] exposes `GetThumbstickValue(XRNode)`, `GetTriggerValue(XRNode)`, `GetAnyJoystickMaxAxis()`,
  `GetMenuButton()/Down()`, `TriggerHapticPulse(...)`. No face-button (A/B/X/Y) API.
- Y-button precedent: BeatLeader replayer reads left-hand `CommonUsages.secondaryButton` via legacy XR
  `InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).TryGetFeatureValue(...)` with a 2 s hold
  (`beatleader-mod/Source/8_UI/Replayer/VR/ReplayerFloatingPanelResetController.cs:35-66`). On Quest/Index under OpenXR, left
  secondaryButton = Y / B-left. Same API for `CommonUsages.primary2DAxis` on `XRNode.RightHand`.
- SiraUtil FPFC patches `UnityXRHelper.GetThumbstickValue` (`SiraUtil/SiraUtil/Tools/FPFC/FPFCFixDaemon.cs:25-31`), confirming
  that is the game's thumbstick read path.

### INFERRED
- Game does not use thumbstick or Y during gameplay (thumbstick drives menu ScrollView; Tweaks55 patches
  `HMUI.ScrollView.HandleJoystickWasNotCenteredThisFrame`). Pause is the menu button action. So Y and right stick are free
  in gameplay, but the pause menu UI still scrolls with the stick.
- Prefer `IVRPlatformHelper.GetThumbstickValue(XRNode.RightHand)` for the stick (works with FPFC and the game's runtime
  mapping); use legacy `InputDevices` + `CommonUsages.secondaryButton` for Y. Re-acquire the `InputDevice` if `!isValid`
  (BeatLeader caches it in a field initializer, which breaks if controllers reconnect). Index "Y" mapping differs (B on left);
  WMR has no Y. Use press edge + hold timing to avoid accidental triggers mid-song.

---

## 5. HUD placement and overlays

### VERIFIED
- Base HUD: `CoreGameHUDController` with `songProgressPanelGO`, `relativeScoreGO`, `immediateRankGO`, `energyPanelGO`.
  Combo panel = `ComboUIController` (child of the HUD), multiplier = `ScoreMultiplierUIController`.
- Counters+ reads combo position at runtime: `comboPos = coreGameHUD.GetComponentInChildren<ComboUIController>().transform.position`,
  `hudWidth = |comboPos.x|`, `hudHeight = comboPos.y`, `hudDepth = comboPos.z` (field defaults 3.2 / 0 / 7)
  (`CountersPlus/Counters+/Utils/CanvasUtility.cs:20-40`).
- Counters+ slot offsets (`CanvasUtility.cs:224-275`): BelowCombo `(-X, 1.15 - comboOffset, 0)`, AboveCombo `(-X, 2 + off)`,
  BelowMultiplier `(X, 1.05 - off)`, AboveMultiplier `(X, 2 + off)`, BelowEnergy `(0, -1.5)`, AboveHighway `(0, 2.5)`,
  canvas soft-parented to the HUD (`:62-80`). NOTE: combo is on the LEFT (negative X); the RIGHT side is the
  multiplier/score/rank. "Above the notes" = Counters+ AboveHighway.
- BSML `FloatingScreen.CreateFloatingScreen(Vector2 size, bool createHandle, Vector3 pos, Quaternion rot, float curvatureRadius = 0,
  bool hasBackground = false)` (research notes `core-frameworks.md` 4.6); world-fixed, usable in-game.
- Camera2 layers (`Camera2/Utils/VisibilityMasks.cs:4-34`): UI = 5, FirstPerson = 6, Notes = 8, Walls = 11, etc.; Camera2 cameras
  choose which layers they render.

### INFERRED
- Clock/HR text: simplest is a world-space Canvas + TMP (BSML `BeatSaberUI.CreateText` or Counters+ custom counter API),
  placed via the Counters+ offsets (AboveHighway for the clock; BelowMultiplier on the right or BelowCombo on the left for HR).
  Writing it as a Counters+ custom counter gives placement UI for free but adds a dependency.
- "In front of the player" overlay: world-space Canvas parented to the HMD camera transform (`Camera.main.transform` or
  `PlayerTransforms._headTransform`) at ~1-2 m, with smoothing (lerp) to avoid nausea. FloatingScreen is world-fixed, so
  only good for non-head-locked panels.
- Fullscreen vignette: a head-locked quad/sphere just past the near clip with an unlit transparent radial-gradient shader
  (ship a material in an AssetBundle or use a built-in UI shader with a generated texture). Post-processing on the main
  camera is not practical (custom game render pipeline, single-pass instanced; no mod precedent found). Put the quad on a
  layer the HMD camera renders (default/UI) and let Camera2 users hide it via layer settings; it will otherwise show in
  Camera2 first-person views (often desirable, ask the user).

---

## Verdict table

| Item | Feasible | Risk | Main precedent |
|---|---|---|---|
| 1 Hot-swap in place | Yes, with effort | Med-High (NJS re-init, score state, pop-in) | Heck ReLoader + BeatLeader Rewind |
| 1 Restart-at-time swap | Yes | Low (3-4 s interruption) | PracticeSettings startSongTime |
| 2 Seek / chapter skip | Yes | Low (gap skip) / Medium (over notes) | BeatLeader Rewind, IntroSkip |
| 3 Lookahead | Yes | Low | IReadonlyBeatmapData + GetObstacleSpawnData |
| 4 Y + right stick | Yes | Low (Index/WMR mapping) | BeatLeader Y hold, IVRPlatformHelper |
| 5 HUD / overlay / vignette | Yes | Low (text) / Medium (vignette shader) | Counters+ CanvasUtility |
