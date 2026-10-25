# Active pause flags Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** While active-paused, press buttons with the trigger, select frozen notes with the saber, and save structured feedback flags (time, labels, selected notes, screenshot) to `flags.json` next to the map.

**Architecture:** Core gets the game-free model (label catalogue, note key, flag record, file document with lenient parse/serialize, id generation, auto-reload ignore rule). The plugin gets trigger input, a note selection with highlight, a Flag/Clear row on the step panel plus a feedback panel, and a writer that resolves the map folder, appends atomically and takes a screenshot. `ActivePauseController` routes presses by what the pointing saber hits: panel first, chapter arrows next (unchanged code), notes last.

**Tech Stack:** C# (net472 plugin, netstandard2.0 Core), BSIPA/SiraUtil/Zenject, BSML, Newtonsoft.Json 13.0.2, xUnit v3, Unity XR input.

**Spec:** `docs/superpowers/specs/2026-10-25-active-pause-flags-design.md`

## Global Constraints

- Beat Saber 1.40.8 only; deps BSIPA 4.3.6, SiraUtil 3.2.1, BSML 1.12.5, SongCore 3.15.3.
- Core stays free of Unity/game types; netstandard2.0 + Newtonsoft.Json 13.0.2 only.
- Code in game callbacks under `Guard.Run`; a failure logs, toasts once, and leaves the game playable.
- ASCII only (`node scripts/check-ascii.mjs` after `git add -A`), LF, Conventional Commits, direct push to main.
- Practice mode only (everything lives inside the existing active pause, which is already practice-gated).
- Labels and their ids exactly as in the spec; `kind`, `color`, `cutDirection` vocabularies exactly as in the spec.
- Build without the game running: `$env:CI='1'; dotnet build src\MaxTweaks\MaxTweaks.csproj -c Release -p:BeatSaberDir=C:\Users\pmaxh\BSManager\BSInstances\1.40.8\` (CI=1 skips the copy into the game); tests: `dotnet test --project tests/MaxTweaks.Core.Tests -c Release`.

## Review Focus

1. A `flags.json` edited by hand with extra keys or a flag the mod does not know: re-saving must keep every unknown key and every existing flag byte-for-byte in meaning (Task 1 round-trip test with unknown keys).
2. A flag saved at 0.000 s or inside the last 0.3 s, with no labels and no notes: still a valid bookmark entry (Task 1 serialization test with empty lists).
3. Trigger held while stepping fast: one press per edge, never a repeat per frame (Task 2 edge test through the debug override is covered by existing `GetButtonDown` semantics; Task 4 routes only on `PressDown`).
4. OST/DLC level: no map folder, Flag button hidden, one toast, no exception (Task 6 writer returns a typed failure, controller test by log line in QA).
5. Auto-reload must not reload the map when `flags.json` or `flags/<id>.png` is written (Task 7 test).

---

### Task 1: Core model, catalogue and file document

**Files:**
- Create: `src/MaxTweaks.Core/ActivePause/Flags/FlagLabel.cs`, `FlagNote.cs`, `FlagRecord.cs`, `FlagDocument.cs`, `FlagIds.cs`
- Test: `tests/MaxTweaks.Core.Tests/ActivePause/Flags/FlagLabelTests.cs`, `FlagDocumentTests.cs`, `FlagIdsTests.cs`, `FlagNoteTests.cs`

**Interfaces:**
- Produces:
  - `enum FlagGroup { Good, Bad, Note }`
  - `sealed class FlagLabel { string Id; string Text; FlagGroup Group; }` with `static IReadOnlyList<FlagLabel> All` in spec order (good 5, bad 8, note 4) and `static FlagLabel? Find(string id)`.
  - `enum FlagNoteKind { Note, Bomb, ChainHead, ChainLink }`
  - `sealed class FlagNote` (immutable, value equality): `FlagNoteKind Kind; float Beat; float Time; int LineIndex; int LineLayer; string Color /* red|blue|none */; string CutDirection /* spec vocabulary */` and `static string KindId(FlagNoteKind)` -> `note|bomb|chain_head|chain_link`. Equality ignores `Beat`/`Time` rounding beyond 1e-3 (compare `Time` rounded to milliseconds) so a respawned note matches.
  - `sealed class FlagRecord { string Id; DateTime CreatedAtUtc; string Characteristic; string Difficulty; float SongTime; float Beat; float Speed; IReadOnlyList<string> Labels; IReadOnlyList<FlagNote> Notes; string? Screenshot; }`
  - `sealed class FlagDocument { int Version = 1; string LevelId; string SongName; List<FlagRecord> Flags; static FlagDocument Empty(string levelId, string songName); static FlagDocument Parse(string json); string Serialize(); void Append(FlagRecord flag); }` Parse throws `FlagDocumentException` (message says why) on malformed JSON or a non-object root; unknown keys at the document and flag level are kept (hold a `JObject` per flag and per document and write them back merged).
  - `static class FlagIds { static string New(DateTime utc, Random random) }` -> `yyyyMMdd-HHmmss-xxxx` with 4 lowercase hex chars.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void All_HasSeventeenLabels_InSpecOrder() { Assert.Equal(17, FlagLabel.All.Count); Assert.Equal("good_pattern", FlagLabel.All[0].Id); Assert.Equal(FlagGroup.Bad, FlagLabel.Find("too_dense")!.Group); Assert.Equal("Doesn't match the music", FlagLabel.Find("mismatches_music")!.Text); Assert.Null(FlagLabel.Find("nope")); }
[Fact] public void Ids_AreUnique_SnakeCase() { /* every Id matches ^[a-z_]+$ and is unique */ }
[Fact] public void New_FormatsTimestampAndHex() { var id = FlagIds.New(new DateTime(2026,10,25,19,30,12,DateTimeKind.Utc), new Random(1)); Assert.Matches(@"^20261025-193012-[0-9a-f]{4}$", id); }
[Fact] public void Serialize_WritesSpecShape() { /* document with one flag: labels ["too_dense"], one FlagNote(Note, 96f, 42.317f, 1, 0, "red", "down"), screenshot "flags/x.png"; assert JSON contains "\"version\": 1", "\"kind\": \"note\"", "\"cutDirection\": \"down\"", "\"songTime\": 42.317" and camelCase keys; invariant culture */ }
[Fact] public void Parse_KeepsUnknownKeys_AndAppends() { /* parse {"version":1,"levelId":"x","songName":"s","custom":{"a":1},"flags":[{"id":"f1","labels":[],"notes":[],"extra":true}]}; Append a new flag; Serialize; assert "custom" and "extra" survive and flags.Count == 2 */ }
[Fact] public void Parse_Malformed_Throws() { Assert.Throws<FlagDocumentException>(() => FlagDocument.Parse("{not json")); Assert.Throws<FlagDocumentException>(() => FlagDocument.Parse("[1,2]")); }
[Fact] public void EmptyFlag_RoundTrips() { /* labels and notes empty, screenshot null, SongTime 0f -> Serialize -> Parse: one flag, Screenshot null */ }
[Fact] public void FlagNote_Equality_IgnoresSubMillisecond() { Assert.Equal(new FlagNote(FlagNoteKind.Note, 96f, 42.3171f, 1, 0, "red", "down"), new FlagNote(FlagNoteKind.Note, 96.0004f, 42.3174f, 1, 0, "red", "down")); }
```

- [ ] **Step 2: Run tests, expect compile failures (types missing)**

Run: `dotnet test --project tests/MaxTweaks.Core.Tests -c Release`

- [ ] **Step 3: Implement the five Core files** (JSON through `JObject`/`JsonConvert` with `Formatting.Indented`, `CultureInfo.InvariantCulture` for floats, `DateTime` as ISO 8601 UTC `yyyy-MM-ddTHH:mm:ssZ`).

- [ ] **Step 4: Run tests, expect all green (633 + new)**

- [ ] **Step 5: Commit** `feat(core): flag model, label catalogue and flags.json document`

### Task 2: Trigger input and debug buttons

**Files:**
- Modify: `src/MaxTweaks/Common/ControllerInput.cs` (enum + ReadButton), `src/MaxTweaks.Core/Debugging/DebugRequests.cs:17-23` (`DebugButton`), `src/MaxTweaks.Core/Debugging/DebugCommandParser.cs` (button names), `src/MaxTweaks/Common/Debug/VirtualController.cs:89-92`, `src/MaxTweaks/Common/Debug/DebugCommandService.cs:36-37` (`Buttons`), `docs/qa.md` (`button` row: `A|B|X|Y|LT|RT`)
- Test: `tests/MaxTweaks.Core.Tests/DebugCommandParserTests.cs` (add cases)

**Interfaces:**
- Produces: `ControllerButton.LeftTrigger`, `ControllerButton.RightTrigger` (XR `CommonUsages.triggerButton` on LeftHand/RightHand; FPFC: `KeyCode.LeftControl` / `KeyCode.Space`); `enum ControllerHand { Left, Right }`; `bool ControllerInput.PressDown(ControllerHand hand)` = `GetButtonDown(A) || GetButtonDown(hand == Left ? LeftTrigger : RightTrigger)`; debug `button LT|RT ...`.

- [ ] **Step 1: Add parser tests** (`button LT press` -> `DebugButton.LeftTrigger`, `button rt down 0.5` -> `RightTrigger`, unknown `button Z` still errors)
- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement** (enum members `LeftTrigger`, `RightTrigger` in both enums; parser accepts `lt`/`rt` case-insensitively; `Refresh` iterates the enum so nothing else changes)
- [ ] **Step 4: Build plugin + run tests, expect green**
- [ ] **Step 5: Commit** `feat(input): controller triggers with FPFC and debug fallbacks`

### Task 3: Auto-reload ignores flag files

**Files:**
- Modify: `src/MaxTweaks.Core/AutoReload/MapPaths.cs` (where a changed path inside a map folder is accepted)
- Test: `tests/MaxTweaks.Core.Tests/AutoReload/MapPathsTests.cs` (add)

**Interfaces:**
- Produces: `static bool MapPaths.IsSidecarOnly(string relativePathInsideMapFolder)` true for `flags.json`, anything under `flags\` or `flags/` (case-insensitive); `TryGetMapFolderName` returns false for such paths.

- [ ] **Step 1: Tests**: `root\Map\flags.json` -> no folder; `root\Map\flags\20261025-1.png` -> no folder; `root\Map\Info.dat` and `root\Map\chapters.json` -> `Map` (unchanged behaviour).
- [ ] **Step 2: Run, expect FAIL**  - [ ] **Step 3: Implement**  - [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Commit** `fix(auto-reload): ignore flags.json and flags/ writes`

### Task 4: Note selection and highlight

**Files:**
- Create: `src/MaxTweaks/Features/ActivePause/NoteSelection.cs`, `src/MaxTweaks/Features/ActivePause/NoteHighlighter.cs`
- Modify: `src/MaxTweaks/Features/ActivePause/ObjectGhoster.cs:28` (`Apply(live, Func<IBeatmapObjectController,bool>? keepSolid = null)`: a kept-solid object is restored if tracked and not ghosted)
- Create (Core helper, pure): `src/MaxTweaks.Core/ActivePause/Flags/RayMath.cs` with `static bool RaySphere(float ox, float oy, float oz, float dx, float dy, float dz, float cx, float cy, float cz, float radius, out float distance)`; test in `tests/MaxTweaks.Core.Tests/ActivePause/Flags/RayMathTests.cs` (hit at known distance; miss; origin inside sphere -> hit at 0).

**Interfaces:**
- Produces:
  - `sealed class NoteSelection` : `const float HitRadius = 0.28f`; `FlagNote? Hovered`; `IReadOnlyCollection<FlagNote> Selected`; `NoteController? HitTest(Ray ray, IReadOnlyList<IBeatmapObjectController> live)` (nearest of `GameNoteController`, `BombNoteController`, `BurstSliderGameNoteController`, centre = `noteTransform.position`); `bool IsSelected(NoteController)`; `void Toggle(NoteController)`; `void Clear()`; `static FlagNote ToFlagNote(NoteController note, BpmTimeline? tempo)` (kind from `noteData.gameplayType`: Normal->Note, Bomb->Bomb, BurstSliderHead->ChainHead, BurstSliderElement->ChainLink; colour `ColorType.ColorA`->`red`, `ColorB`->`blue`, else `none`; cut direction lower snake of `NoteCutDirection`; `Time = noteData.time`; `Beat = tempo?.SecondsToBeat(time) ?? time * FallbackBpm / 60f` using `StepMath` fallback).
  - `sealed class NoteHighlighter : IDisposable` : `void Update(IReadOnlyList<IBeatmapObjectController> live, NoteSelection selection)` places one halo per selected live note at `noteTransform.position` (and a dimmer one on the hovered note), hides the rest; `void Clear()`. Halo: a quad or ring, additive white, about 0.35 m, facing the camera, no collider, layer 0, implementer's exact look.
- Consumes: `PointerRays.TryFromSaber(Saber?, out Ray)`, `BpmTimeline.SecondsToBeat(float)`.

- [ ] **Step 1: RayMath tests, run (FAIL), implement, run (PASS)**
- [ ] **Step 2: Implement NoteSelection, NoteHighlighter, Ghoster `keepSolid`** (build with CI=1 must be clean)
- [ ] **Step 3: Commit** `feat(active-pause): note selection with saber ray and highlight`

### Task 5: Panel: Flag/Clear row and the feedback panel

**Files:**
- Modify: `src/MaxTweaks/Features/ActivePause/StepButtonPanel.cs` (third row; `Action<int>` click index: 0-11 steps, `FlagIndex = 12`, `ClearIndex = 13`; `void SetSelectedCount(int n)` sets the Flag label to `Flag` or `Flag (n)` and dims Clear when 0)
- Create: `src/MaxTweaks/Features/ActivePause/FeedbackPanel.cs`

**Interfaces:**
- Produces: `sealed class FeedbackPanel : IDisposable` with ctor `(float eyeHeight, Action<IReadOnlyList<string>> onSave, Action onCancel)`; `Show()`, `Hide()`, `bool Visible`, `int HitTest(Ray)`, `void SetRayHover(int)`, `void Press(int index)` (toggles a label, or Save/Cancel), `IReadOnlyList<string> LitLabels`, `void ClearLabels()`. Same canvas position as the step panel (`Center`), three columns (Good, Bad, Note) with column headers, lit buttons gold (`RayHoverButton` already has hover colour; add `SetLit(bool)` to `RayHoverButton` that keeps a gold base colour), bottom row `Save` `Cancel`. Both panels are never visible at the same time.

- [ ] **Step 1: Implement; build clean (CI=1)**
- [ ] **Step 2: Commit** `feat(active-pause): flag and clear buttons, feedback panel`

### Task 6: Flag writer, screenshot, controller wiring

**Files:**
- Create: `src/MaxTweaks/Common/CustomLevelFolders.cs` (`static string? TryGetFolder(BeatmapLevel? level)` moved from `ChaptersLoader.FindFolder`, which now calls it), `src/MaxTweaks/Features/ActivePause/FlagWriter.cs`
- Modify: `src/MaxTweaks/Features/ActivePause/ActivePauseController.cs` (`UpdatePointing` routing, flag flow, selection + highlighter lifecycle, Exit/Dispose cleanup), `src/MaxTweaks/Features/Chapters/ChaptersController.cs:243-247` (press = `PressDown(hand of the saber that hit)` while `_gamePause.isPaused`, A otherwise: track the hand in `UpdatePointing`), `src/MaxTweaks/Common/Debug/DebugGameplayContext.cs` (verbs `select-note <n>`, `flag-open`, `flag-toggle <id>`, `flag-save`, `flag-cancel`, `flags-clear` through an `IActivePauseDebug` interface the controller implements and the player debug installer resolves optionally)

**Interfaces:**
- Produces: `sealed class FlagWriter` : `FlagWriteResult Save(FlagRecord flag, string folder, bool screenshot)` where `FlagWriteResult { bool Ok; string? Error; string Path; string? ScreenshotPath }`; reads `folder\flags.json` if present (`FlagDocument.Parse`; on `FlagDocumentException` rename to `flags.json.corrupt-yyyyMMdd-HHmmss` and start `FlagDocument.Empty`), appends, writes `flags.json.tmp` then `File.Copy(tmp, path, overwrite)` + delete tmp (File.Replace is not atomic across all setups; copy+delete is acceptable), screenshot via `ScreenCapture.CaptureScreenshot(Path.Combine(folder, "flags", id + ".png"))` after `Directory.CreateDirectory`, recorded as `flags/<id>.png`.
- Controller behaviour (spec): routing order panel > feedback panel > chapter arrows (unchanged, they test their own ray) > notes; `PressDown(hand)` of the hitting saber, right saber tried first; Flag hidden (`SetFlagAvailable(false)`) when `CustomLevelFolders.TryGetFolder` is null, toast "Flags need a custom level" once; Save builds `FlagRecord` (`Id = FlagIds.New(DateTime.UtcNow, random)`, characteristic/difficulty from `ActiveBeatmap?.Key ?? setupData.beatmapKey`, `SongTime`, `Beat` via `_tempo`, `Speed = _seeker.TimeScale`, labels, `Selected` notes), writes, toasts `Flag saved: {labels} labels, {notes} notes` or `Flag not saved: {error}`, logs `[ActivePause] flag saved {id} at {time}s: {n} labels, {m} notes{, screenshot}`; X or pause menu while the feedback panel is open = Cancel; `Exit` hides panels; `Dispose` disposes highlighter and panels; selection cleared on Dispose (song end).

- [ ] **Step 1: Implement writer + folder helper; build clean**
- [ ] **Step 2: Wire the controller and chapters; build clean; unit tests green**
- [ ] **Step 3: Commit** `feat(active-pause): trigger presses, note flags saved to flags.json`

### Task 7: Docs

**Files:**
- Modify: `docs/maxtweaks-spec.md` (Active pause section: trigger, selection, flags, file), `README.md` (Active pause bullet + link to `docs/flags.md`), `docs/qa-checklist.md` (Active pause: trigger on buttons and arrows, select/deselect, flag save with toast, file content check, screenshot present), `docs/qa.md` (new verbs, log lines)
- Create: `docs/flags.md` (format for the automapper: the JSON from the spec, vocabularies, ids, "readers must keep unknown keys")

- [ ] **Step 1: Write; `git add -A && node scripts/check-ascii.mjs`**
- [ ] **Step 2: Commit** `docs: active pause flags`
