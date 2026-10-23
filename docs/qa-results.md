# MaxTweaks FPFC QA results - wave 1

Date: 2026-10-22. Tester: QA agent, Beat Saber 1.40.8 in FPFC desktop mode (`scripts/qa/launch.ps1`,
`--debug`), window resized to 1600x940. Evidence paths below are relative to the QA screenshot folder
`C:\Users\pmaxh\AppData\Local\Temp\claude\C--Users-pmaxh-Documents-python-projects-bsaber\3278f6f3-45dd-41af-839c-8ce255261c38\scratchpad\qa\`.
Log lines are from `UserData/MaxTweaks.debug-log.txt` (the mirror) unless marked `_latest.log`.

## Builds under test

Main moved during the session; each result names the build it ran on.

| Id | Source | Plugins/MaxTweaks.dll sha256 (prefix) |
|---|---|---|
| B1 | b525488 (start of session) | 49B9E8B8 |
| B2 | 7b5b69e + QA-only debug verbs (see "Test harness" below) | 8719670D / 16F82509 (B2 + one temporary HideOst log line) |
| B3 | 3b54053 + QA-only debug verbs + temporary AutoReload log lines | EC1A24C5, D8C22B4D |
| Final | aa4540b, clean (no QA edits) - redeployed at the end, smoke-launched to `event menu-ready` | 8BCCB96D |

## Test harness notes (read before the results)

- FPFC mouse aiming at menu UI was unreliable on this machine: the UI pointer is offset from the
  screen centre and `mouse.ps1` focus sometimes fails (input then lost or applied later). Menu
  clicks were therefore driven by throwaway debug verbs added locally to `Common/Debug`
  (`click-hint`, `click-text`, `click-name`, `find-*`, `table`, `search`, `patches`, `textinfo`).
  They fire the element's real pointer down/up/click handlers (same code path as a pointer click,
  but no raycast). Not committed; see the final report.
- In gameplay the camera-centre ray (`ChaptersController.HitFromCamera`) is exact, so chapter
  arrows were aimed with real `mouse.ps1 look` and verified with the hover colour.
- `table` reads `LevelCollectionTableView._beatmapLevels` (what the song list shows) and counts
  level IDs not starting with `custom_level_` ("official").

## Summary

| Area | Result |
|---|---|
| Hide OSTs | 11 PASS, 1 FAIL (playlists), 1 FAIL-minor (disable not live), 1 NOT RUN (soft restart) |
| Auto-reload | PASS overall; 1 FAIL (song detail lost after a menu reload), 1 intermittent (gameplay toast on B2) |
| Blink | PASS (trigger, timing, pause hide); vignette not visible in FPFC captures (needs VR check) |
| Chapters | Skip/practice/PracticePlugin PASS; FAIL: chapter names never render (bars and arrows do) |
| ChaptersFromBookmarks tool | PASS; minor: unreadable difficulty file reported as "no bookmarks" |
| General | No MaxTweaks exceptions in any `_latest.log` of the session; settings page and quick tab render |

## Hide OSTs

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | Hidden=false: 4 category tabs + white music-pack icon right of them | PASS (B1) | `20261022-214935-max.png`, `crop-tabs.png` (icon touches the right edge of the All tab - cosmetic) |
| 2 | Click icon: tabs Custom/Favorites/All, icon red, Custom shown | PASS (B2) | `[HideOst] official songs hidden`; `20261022-220323-hideost-2-hidden.png` |
| 2 | All shows only custom maps | PASS (B2) | `table rows=73 official=0` on All; with hiding off `rows=145 official=72` |
| 2 | Search "Escape" on All finds nothing | PASS (B2) | hidden: `table rows=0`; shown: `rows=2 official=2 firstOfficial=[Escape,EscapeRemix]`; `20261022-220932-hideost-2-escape-hidden.png` |
| 2 | In the All tab the red icon overlaps the left end of the search field | cosmetic | `20261022-220345-hideost-2-all.png` |
| 2b | PlaylistManager playlist containing OST songs lists only its custom maps | **FAIL** (B2, B3) | playlist `zz QA HideOst` (2 custom + Escape + SpinEternally): `table rows=4 official=2 firstOfficial=[Escape,SpinEternally]` with hiding on, also after re-toggling and re-selecting; `20261022-221106-hideost-2b-playlist-hidden.png`. Temp log in `FilterTableLevels`: `in=4 hide=True enableCustom=True` (prefix runs). `patches LevelCollectionViewController SetData`: prefix `pmaxhogan.MaxTweaks.HideOst/LevelCollectionSetDataPatch`, postfix `com.github.rithik-b.PlaylistManager/LevelCollectionViewController_SetData`. |
| 3 | Click again: 4 tabs back, Music Packs lists OST/Extras/DLC | PASS (B2) | `[HideOst] official songs shown`; Music Packs tab lists 19 DLC packs + Extras (find-hint) ; `20261022-220616-hideost-3-musicpacks.png` |
| 4 | Toggling twice keeps the selected custom pack | PASS (B2) | WIP Levels pack selected, toggled off+on, still WIP (empty table, WIP art); `20261022-220700-hideost-4-wip-twice.png` |
| 5 | Hide on while on All, then Custom: renders, no stuck spinner | PASS (B2) | `20261022-220633-hideost-5-all-then-custom.png`, `table rows=73` |
| 5b | Search term on All survives toggling | PASS (B2) | after toggle the field still reads `Escape` and the list re-filters (0 -> 2 -> 0 rows) |
| 6 | BSL LastCategory=MusicPacks + Hidden=true at boot opens Custom, no AOOR | PASS (B2) | `20261022-220508-hideost-6-boot-musicpacks.png`; no `ArgumentOutOfRange` in any session log |
| 7 | Party mode: no icon, OSTs visible | PASS (B2) | find-hint: no HideOst button, Music Packs tab present; `20261022-221218-hideost-7-party.png` |
| 8 | Hiding on + SongCore refresh (Ctrl+R) stays custom-only | PASS (B2) | `event songs-loaded custom=73`, then `table rows=73 official=0`, no Music Packs tab; `20261022-221012-hideost-8-refresh.png` |
| 9 | State persists across relaunch | PASS (B2) | relaunch with `Hidden: true` in MaxTweaks.json opened Solo hidden |
| 9 | Soft restart: icon returns | NOT RUN | no reliable way to trigger a soft restart from the harness |
| 10 | Feature disabled: no icon | PASS after reopening Solo; **FAIL-minor live** (B3) | quick tab "Hide OSTs button" off while in Solo: config `Enabled: false` but the red icon stays and OSTs stay hidden (`20261022-222857-hideost-10-disabled.png`); after leaving and re-entering Solo: no icon, Music Packs back (`20261022-222926-hideost-10-disabled-reopen.png`) |
| 11 | Log has only `[HideOst]` info lines | PASS | `_latest.log` of every run: `[HideOst]` lines all INFO, no WARN/ERROR |

## Auto-reload

Watcher backend: B1 logged `with FileSystemWatcher (backend DefaultWatcher)` (Mono's managed polling,
not native). B2 and later: `Watching CustomLevels (...\Beat Saber_Data\CustomLevels -> C:\Users\pmaxh\BSManager\SharedContent\SharedMaps\CustomLevels) (backend native ReadDirectoryChangesW)`, same for CustomWIPLevels.

Test map: copy of `4d62 (Bangarang - nitronik.exe)` as `zz-qa-test`, `_songName` "ZZ QA Test Map".
Deleted at the end (`levels zz-qa` -> 0).

| Check | Result | Evidence |
|---|---|---|
| Copy new folder in Solo > Custom (IN THE PLACE selected, scrolled) -> "1 map added" ~2 s later | PASS (B3) | copy 22:16:32.6, `Reloading: 1 new` / `Reload done: 1 map added` 22:16:35 (2.46 s); toast visible; `20261022-221635-autoreload-1-added.png` |
| Place kept (pack, scroll, selected row) | PASS | same rows visible, IN THE PLACE still highlighted |
| Selected song's detail panel kept | **FAIL** (B3) | after the reload the right panel shows the pack card (PlaylistManager pencil) instead of IN THE PLACE's details; `find-text Practice` -> no match (no Play/Practice buttons) until the song is clicked again; `20261022-221650-autoreload-1-added-b.png`. After the deferred reload on returning from gameplay the detail panel WAS kept (`20261022-222209-quicktab-mods.png`). |
| Edit Info.dat `_songName` -> "1 map updated", Play works | PASS (B3) | `Reload done: 1 map updated`; new level id; Play started `song='ZZ QA Test Map v2'` |
| Delete folder -> "1 map removed" | PASS (B3) | `Reload done: 1 map removed`; `20261022-223051-autoreload-4-removed.png` |
| Edit during gameplay: toast, no interruption, reload after exit | PASS on B3, **missed once on B2** | B2 (7b5b69e, 22:19:58): no toast, no log line, but `Reloading: 0 new, 1 changed` right after exit. B3 (3b54053): `QATEMP playing folder key '...\CustomLevels\zz-qa-test'`, batch with the same key, toast `Map updated on disk: ZZ QA Test Map v3 - reloads when you exit` shown under the clock (`20261022-222334-autoreload-3-ingame-toast-2.png`), song kept playing, next play used v4. B3 contains 3b54053 (SafeMapPaths separator fix), which may be what fixed it - not proven. |
| Toast placement in menu | cosmetic | "1 map added" toast sits on BetterSongList's random/settings buttons under the list |
| Settings toggle off/on | PASS (B3) | off: `Stopped watching map folders`, edit ignored; on: `Watching ...` lines; next edit `Reload done: 1 map updated` |
| Change made while disabled | note | the v5 edit made while off was never reloaded on re-enable (picked up only with the next change) |

## Blink

| Check | Result | Evidence |
|---|---|---|
| Settings section renders | PASS | Quiet before, Clear ahead, Blink length, Cooldown, Vignette strength present (find-text); `20261022-221317-settings-1.png` |
| Start log | PASS | `[Blink] indexed 1140 notes/chains and 29 walls; cooldown 0/300s played; head from PlayerTransforms, standing eye height 1.56 m` (head: PlayerTransforms, not fallback) |
| K mid-song: closed eye, fades, gone after ~3 s | PASS | `[Blink] debug blink forced at 50.9s`; eye icon visible at 0.9 s (`20261022-222354-blink-1-k-mid.png`), gone at 4.3 s (`20261022-222358-blink-1-k-after.png`) |
| K mid-song: vignette | INCONCLUSIVE | no darkened corners discernible in either FPFC capture (environment lighting changes too much to compare). Material: `overlay material from 'UINoGlowCustom' (shader 'Custom/CustomUI', fallback=False)`. Needs the VR check. |
| K then pause: overlay hidden while paused | PASS | `20261022-222421-blink-2-paused.png` (no eye) |
| Natural trigger, Cooldown 15 s, Hare Ram Expert practice from 1:20 (last note 97.2 s, next 112.1 s) | PASS | `cooldown 0/15s played`; `[Blink] blink at 100.2s` (= last note + 3 s); `20261022-223156-blink-3-natural.png` |
| Visual quality | note | eye icon sits low and slightly right of centre in FPFC, over the note lanes; fine but check in VR whether it hides incoming notes |

## Chapters

chapters.json in `zz-qa-test`: 6 bookmark chapters from the tool (startBeat/endBeat) plus "First half"
0:05-1:45.5, "Mid section" 30-2:10, "ExpertPlus only" (difficulties filter) and three bad entries.

| Check | Result | Evidence |
|---|---|---|
| Bad entries -> `[Chapters]` warnings only | PASS | `chapter #11 ('Bad mixed'): mixes start/end with startBeat/endBeat ... skipped`, `chapter #12 ('Bad text'): "start" must be seconds or "m:ss(.fff)", got "abc", skipped`, `chapter #10 ('Bad reversed'): end 0:50.000 is not after start 1:00.000, skipped`; `8 chapter(s) for Standard/Expert, up to 3 at once` (ExpertPlus-only chapter correctly excluded) |
| Normal play: names + bars above the notes | **FAIL** (B2, B3) | only the thin progress bars render (under the clock); chapter names never appear. `textinfo half`: `'First half' type=CurvedTextMeshPro enabled=True alpha=0.9 size=11 rect=(160.00, 14.00) font=Teko-Medium SDF ... pos=(0.00, 3.01, 7.00) verts=0` - characterCount 0, while the `<`/`>` arrows (rect 18x18, size 15) report `verts=1` and render. Screens: `20261022-221925-chapters-1-normal-2.png`, `crop-chapters.png`, `20261022-222534-chapters-2-practice-paused-hover.png` |
| Normal play: no arrows | PASS | find-text `<`/`>` -> no match in normal play |
| Longest at the bottom | PASS | Row0 = "First half" (longest), Row1 "Mid section", Row2 "Build"/"Drop 1" |
| Placement | note | the stack sits just under the clock and its bottom row overlaps HRCounter's heart widget in this setup |
| Practice: arrows | PASS | `skip on`; arrows visible, hover turns gold and scales |
| Paused skip `>` then Continue (PracticePlugin) | PASS | at 38.3 s: `[Playback] seek to 103.809s (objects from 105.500s): Chapters skip to end`, `end of #6 'First half'`; resumed at 103.81, 104.63, 109.38 s; exactly one `seek to` line, no seek back |
| During play `<` + Enter (real key) | PASS | `event input A down`, `seek to 120.491s (objects from 122.182s): Chapters skip to start`, `start of #4 'Drop 2'` |
| Pause-menu mouse click on an arrow | PASS | real `mouse.ps1 click`: `seek to 172.855s ... Chapters skip to end` |
| Second `>` leaves the chapter | PASS (B3) | `seek to 174.546s (objects from 174.546s)`; play continued at 179 s |
| Row identity shifts | note | rows are re-assigned as chapters start/end, so the arrow under the pointer can belong to a different chapter a moment later (the `<` aimed at "Mid section" jumped to "Drop 2" because Mid ended first) |

### ChaptersFromBookmarks tool

| Check | Result | Evidence |
|---|---|---|
| Map without bookmarks (v2 file with empty top-level `_bookmarks`) | PASS | exit 3, `no bookmarks found, nothing written` |
| v2 `_customData._bookmarks` in Expert and ExpertPlus (same list) | PASS | exit 0, `wrote 6 chapter(s)`; no `difficulties` filter; last chapter has no end |
| Unreadable difficulty file (invalid JSON) | minor | reported as `Standard/Expert: no bookmarks` / exit 3 instead of exit 2 "map unreadable" |
| Legacy top-level `_bookmarks` (MMA2 v2) | note | not read; only `_customData._bookmarks` |

## General

| Check | Result | Evidence |
|---|---|---|
| No exceptions mentioning MaxTweaks in `_latest.log` | PASS | all 13 session logs scanned (incl. .gz): only `[MaxTweaks.Debug] error` lines from my own bad debug commands. One unrelated shutdown `NullReferenceException` in `BeatSaberMarkupLanguage.GameplaySetup.GameplaySetupDidDeactivate` after I paged the mod tabs with synthetic clicks. |
| Quick tab (MaxTweaks tab in Mods) renders and toggles work | PASS | 5 toggles; `Hide OSTs button` and `Auto-reload maps` toggled and saved to MaxTweaks.json; `20261022-222835-quicktab-maxtweaks.png`. Note: the mod-tab header showed "PRACTICEPLUGIN" while the MaxTweaks toggles were displayed (after a synthetic page-button click; may be a harness artefact) |
| Mod Settings > MaxTweaks renders all sections | PASS (content), scroll not visually checked | `event settings-opened MaxTweaks`; top visible in `20261022-221317-settings-1.png`; find-text finds every section and setting (Hide OSTs, Auto-reload, Blink x6, Chapters x2 + help text, Difficulty switch) |
| Unexplained quit | note | at 22:07:38 the game quit cleanly (`OnApplicationQuit`) ~12 s after a Ctrl+R refresh with no input sent; not reproduced on a second Ctrl+R |

## Not covered

- Difficulty switch (wave 2 code landed mid-session; not in this QA scope).
- Hide OSTs soft restart, Blink in campaign/party, multiplayer/replay exclusion, Chapters with BPM
  changes, Chapters progress-bar toggle off.

# Difficulty switch - FPFC QA results

Date: 2026-10-22. Tester: QA agent, Beat Saber 1.40.8 in FPFC desktop mode (`launch.ps1`, `--debug`,
window 1038x614 client). Evidence paths are relative to the same QA screenshot folder as above; log
lines are from the mirror unless marked `_latest.log`. Switches were driven with the channel's
`button Y` / `stick` verbs; screenshot bursts with a scratch script that appends to the command file
and captures in one process (about 50-150 ms between an input and the capture).

## Builds under test

| Id | Source | Plugins/MaxTweaks.dll sha256 (prefix) |
|---|---|---|
| D1 | a948b08 (first difficulty-switch build, 55acd7f code) | E2B6E5E0 |
| D2 | c010f06 (review fixes: restart re-runs the level start, PracticePlugin sync, real-second gap, neighbour preload) | 89FA6748 |
| D3 | c010f06 + a temporary QA-only `status` suffix printing spawn InitData NJS, VariableMovementDataProvider NJS / jump duration and timeScale (reverted, never committed) | D415E973 |
| Final | c010f06, clean - redeployed at the end and run to `event menu-ready` | 89FA6748 |

Test maps: `4d62` Bangarang (5 Standard difficulties, custom labels Skrillex/Expert+/Expert/Hard/Normal),
`4355b` Supermassive Black Hole (all difficulties require Noodle), `3fa87` 360 (360Degree),
`1fdb5` Glue Gun (all suggest Chroma; Chroma light colours on 28862 events, environment removal),
and `zz-qa-ds-test`, a copy of 4d62 with a chapters.json (Whole / Expert only / XPlus only); on D2/D3
also Info.dat 2.1.0 with `_environmentNames` (Hard on TriangleEnvironment) and ExpertPlus requiring
Noodle Extensions. All test folders were deleted at the end (`levels zz-qa` -> 0).

## Summary

| Area | Result |
|---|---|
| Input, panel, in-place switch, pause, restart, chapters, speed, disable, settings | PASS |
| Fallback restarts (Noodle required, 360, vanilla <-> Noodle, different environment) | PASS (D2) |
| PracticePlugin after a swap | spawn NJS correct (PASS), but its NJS label shows the old value (minor bug 1) |
| Chroma-suggested in place | PASS (log + visuals); one Heck ERROR line per switch (D1, bug 2) |
| Score/combo carry-over, submission disabled, cut sounds | NOT VERIFIABLE in FPFC (no cuts, practice never submits, no log line) |
| No MaxTweaks/DifficultySwitch/Playback exceptions in `_latest.log` | PASS (all 5 sessions incl. .gz) |

## Checklist results

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | `play 4d62 expert practice 20`: ready line, preload ~2 s later | PASS (D1, D2) | `[DifficultySwitch] ready on Standard/Expert: ExpertPlus, Expert, Hard, Normal, Easy` 22:39:09, `preloaded ExpertPlus/Hard/Normal/Easy` 22:39:11. D2 preloads only the neighbours plus the highlighted row (`preloaded Normal` when the highlight reached it) |
| 2 | Y down: panel, gold "(now)" row, hardest on top | PASS (D1) | `20261022-223946-645-ds2-panel.png`: title "Difficulty (stick: move, release Y: switch)", rows Skrillex / Expert+ (now) / Expert / Hard / Normal (the map's custom labels), current row gold. The panel sits in front of the note lanes and hides incoming notes while open (by design) |
| 3 | `stick 0 1`: highlight slides up one row; a second push without centring does not move | PASS (D1) | mid-slide `20261022-223946-815-ds3-slide.png`; from ExpertPlus two `stick 0 -1` without centring -> one row (`20261022-224041-676-ds3b-second.png`), then centre + push -> committed Hard (two rows), not Normal |
| 4 | Y up: switched line, audio continuous, old notes slide up, gap, new NJS, toast | PASS (D1, D2) | `switched Expert -> ExpertPlus at 57.6s (in-place); objects from 60.6s, preloaded, Heck data refreshed (0 sets, 0 tracks), 15 ms`. Status 57.55 @48.198 -> 59.91 @50.543 -> 62.49 @53.113 (song/wall 1.006, 1.004). Slide `20261022-223948-354-ds4-a.png`, empty lane `-571-ds4-b`, `-492-ds4-gap`, notes back `-054-ds4-after`; toast "Switched to Skrillex". D2: 54.61 -> 57.18 over 2.549 s wall. D3: spawn NJS 16 -> 10 (`initNjs=10 vmpNjs=10 jumpDist=21.82`) |
| 5 | Y press/release without moving: no switch | PASS (D1) | `button Y press 0.4` -> only `event input Y down/up` |
| 6 | Easier difficulty: notes slide down | PASS (D1) | ExpertPlus -> Hard: `20261022-224043-048-ds6-d.png` notes below the lane, then gone |
| 7 | Paused: switch, objects gone at once, resume after the gap | PASS (D1, D2) | `switched Hard -> Expert at 136.4s ... objects from 139.4s` while paused; `20261022-224110-004-ds7-after.png`, resume `20261022-224114-292-ds7-res-a.png` empty, `20261022-224116-447-ds7-res-c.png` wall + bombs. Note 4 below |
| 7b | Pause during a slide (D2 fix) | PASS (D2) | switch then `pause` 120 ms later: `20261022-225414-010-b2-slidepause-b.png` no notes behind the menu; after resume no stuck notes (`20261022-225416-544-b2-slidepause-res-a.png`, `20261022-225420-371-b2-slidepause-res-c.png`) |
| 8 | Noodle-required map: fallback restart at the switch time | PASS (D1, D2) | D1 `switched Expert -> ExpertPlus at 47.2s (fallback restart: Noodle Extensions objects)` -> `gameplay-start ... diff=Standard/ExpertPlus practice=start=47.16`. D2 adds `; level start re-run for the target` |
| 8 | 360 map: fallback restart | PASS (D1) | `switched Expert -> Hard at 35.7s (fallback restart: rotation (360/90) map)` -> `diff=360Degree/Hard practice=start=35.74` |
| 8 | Vanilla -> Noodle-required (zz map): Noodle active after the restart | PASS (D2) | `switched Normal -> ExpertPlus at 80.9s (fallback restart: requires Noodle Extensions; level start re-run for the target)`; the new scene logs `ready on Standard/ExpertPlus: ... (all restart: Noodle Extensions objects)` (Noodle callbacks present) |
| 8 | Noodle -> vanilla: Noodle gone after the restart | PASS (D2) | `switched ExpertPlus -> Expert at 96.6s (fallback restart: Noodle Extensions objects; level start re-run ...)`, next ready line without "all restart" |
| 8 | Different environment | PASS (D2) | `switched Expert -> Hard at 104.8s (fallback restart: different environment; level start re-run ...)`, `event scenes Replace: [...,TriangleEnvironment,...]` |
| 8 | Submission disabled after a switch | NOT VERIFIABLE | practice mode never submits; no SiraUtil submission line in `_latest.log` |
| 9 | Chroma-suggested map in place, colours persist | PASS (D1) | `ExpertPlus suggests Chroma: swapping in place ...`, `switched Expert -> ExpertPlus at 47.1s (in-place) ... Heck data refreshed (2 sets, 1 tracks), 10 ms`; before `20261022-224702-128-ds9-before.png`, after the gap `20261022-224709-227-ds9-after.png`, `20261022-224734-296-ds9-later3.png`: grey/blue note colours, Chroma-coloured lasers and the stripped environment persist. Bug 2. Not re-run on D2 |
| 10 | After an in-place switch: pause level bar shows the new difficulty; Restart restarts it | PASS (D1, D2) | level bar Hard -> Expert (`crop-ds7-before.png`, `crop-ds7-after.png`); Expert -> Easy -> Normal while paused, `restart` -> `gameplay-start diff=Standard/Normal` (song started on Expert). D2: pause-menu Restart after the Noodle restart -> `diff=Standard/ExpertPlus` with Noodle; after the environment restart -> Hard on TriangleEnvironment |
| 11 | Chapters follow the difficulty | PASS (D1, D2) | `[Chapters] 2 chapter(s) for Standard/Expert` -> after switches `2 ... Standard/ExpertPlus`, `1 ... Standard/Hard`, `1 ... Standard/Normal` |
| 12 | Normal (non-practice) play: Y does nothing | PASS (D1) | no ready/switch line, no panel playing or paused (`20261022-224505-253-ds12-normal-paused-Yheld.png`) |
| 13 | Several switches, a switch inside the gap, two switches while paused | PASS (D1) | Expert->E+ (42.9 s), E+->Normal (82.1), Normal->Hard (88.9), Hard->Expert at 91.3 inside the previous gap (objects from 94.3; `20261022-224321-023-ds13-4-gap2.png`, `20261022-224322-575-ds13-4-after.png`), paused Expert->Easy->Normal at 120.9, resume fine (`20261022-224358-336-ds13-6-res-b.png`). No errors, no stuck objects, no burst of notes; a switch costs 1-15 ms |
| 13 | Exit during a slide; switch 5 s before the song end | PASS (D1) | exit 0.1 s after a switch: clean `level-finished state=Incomplete`; switch at 214.4 s (objects from 217.4, song 219 s) -> `level-finished state=Cleared` |
| 14 | Speed 1.2 practice | PASS (D1) | song/wall rate 1.17-1.20 before and after `switched Expert -> ExpertPlus at 40.1s ... objects from 43.1s` (D1 gap = 3 song-s = 2.5 real s). D2 gap is in real seconds: at PracticePlugin 105% `switched Normal -> Easy at 67.2s; objects from 70.4s` |
| 14b | PracticePlugin speed change after a swap keeps the new NJS | PASS (D3), label minor FAIL | Expert -> Normal, pause, Speed + (105%): `initNjs=10 vmpNjs=10 timeScale=1.05` before and after resume. The panel's NJS value still reads `<u>16</u>` (find-text) - bug 1 |
| 15 | Feature disabled -> Y does nothing | PASS (D2) | Mod Settings > MaxTweaks > Difficulty switch Enabled off (config `Enabled: false`): no ready/switch line, no panel (`20261022-225527-566-b2-disabled-Yheld.png`); re-enabled afterwards |
| - | Clear gap 0 + Slide old notes off | PASS (D3) | switch at 44.6 s -> `objects from 46.3s` (spawn-ahead floor); old notes gone in the first frame (`20261022-225655-643-b3-gap0-a.png`), new ones ~1 s later (`20261022-225656-775-b3-gap0-c.png`) |
| - | Clear gap 6 | PASS (D3) | `switched Expert -> Normal at 45.0s; objects from 51.0s`; empty at 4 s, notes from ~5 s (`20261022-225757-615-b3-gap6-4s.png`, `20261022-225801-740-b3-gap6-8s.png`) |
| - | Logs | PASS | `_latest.log` of all sessions (22:38, 22:50, 22:56, 22:57, 23:01; .gz included): no exception or ERROR from MaxTweaks; only other mods (Heck track errors from map 4355b on load, Chroma light-ID errors on 3fa87, Counters+ missing) and bug 2 |

## Bugs and notes

1. Minor (D2/D3): after an in-place switch PracticePlugin's pause panel keeps showing the old NJS
   (16 for Expert after switching to Normal, NJS 10), also after changing its speed. The effective NJS
   is right (spawn InitData and VariableMovementDataProvider both 10). `PracticePluginSync.AfterDifficultySwap`
   (src/MaxTweaks/Common/Playback/PracticePluginSync.cs:59-68) writes the `_njs`/`_offset` backing
   fields without notifying, so the BSML value text never refreshes.
2. Minor (D1, not re-run on D2): an in-place switch on Glue Gun (Chroma environment enhancements)
   logs `[ERROR @ 22:47:04 | Heck/TransformControllerFactory] Overwriting existing [TransformController] on [FrontLights]...`
   in `_latest.log`. No visible effect. Suspected: `HeckDataRefresher.Refresh`
   (src/MaxTweaks/Features/DifficultySwitch/HeckDataRefresher.cs:52) re-running Heck environment/track
   setup for the new data.
3. Note: after a fallback restart the practice start becomes the switch time (`practice=start=47.16`),
   so a later pause-menu Restart starts there, not at the original practice start
   (FallbackRestarter.cs:88-89).
4. Note: a switch while paused replays the light events, so the environment visibly changes behind
   the pause menu (dim -> bright).

## Wave-1 re-checks on c010f06

| Check | Result | Evidence |
|---|---|---|
| Chapter names render | PASS | "Whole" visible in gameplay; `[Chapters] label metrics: preferred 22.7x15.8, box 160x30, font 11` |
| Chapter stack above the clock, clear of HRCounter | FAIL | the name and its `<` `>` arrows are drawn on top of the clock text ("10:58 PM") in FPFC, and small; clear of the heart: `crop-chapters-clock-b3.png`, `20261022-225048-680-b2-gap2.png` |
| Hide OSTs: All tab only customs | PASS | hidden: `table rows=74 official=0`; search Escape -> 0 rows. Playlist case not re-run (the old test playlist no longer exists) |
| Hide OSTs: live disable via quick tab | PASS | off while in Solo: icon gone, `rows=146 official=72`, Music Packs tab back; on again: icon back, `official=0` |
| Auto-reload keeps Play/Practice detail | PASS | IN THE PLACE selected, folder added -> `Reload done: 1 map added`, `find-text Practice` still matches, detail unchanged (`20261022-230121-w1-ar-added.png`) |
| Auto-reload menu toast placement | note | toast `1 map added` at world (0, 2.55, 2.5), the clock mod's spot; in captures only a dark box behind the clock is visible (`20261022-230139-137-w1-ar-removed-a.png`) |
| Auto-reload re-enable catch-up | PASS | Mod Settings toggle off, Info.dat edited, on: `Reloading: 0 new, 1 changed, 0 gone, rescan` / `Reload done: 1 map updated` |

## Not covered

- Score/combo carry-over with real hits, cut sounds and score submission (need VR or a non-practice
  flow; FPFC cannot cut and practice never submits).
- Chroma map on D2; OneSaber/NoArrows/Lawless characteristics.

# Final re-check

Date: 2026-10-22 23:10-23:19. Build: main 93d23ba (HEAD later 5e74bd7, README only), deployed with
`deploy.ps1`, `Plugins/MaxTweaks.dll` sha256 1C327A2A46E0EED6. FPFC, `--debug`. Screenshots in the same
QA folder as above. Test map `zz-qa-final`: copy of `4d62` Bangarang, `_songName` "ZZ QA Final", with
chapters "Whole Song Long Chapter" 0:05-3:00, "Verse Overlap" 0:20-1:20, "Drop" 1:40-2:30; deleted
at the end (`Reload done: 1 map removed`, `levels zz-qa` -> 0).

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | Chapters, normal play: placement log | PASS | `[Chapters] stack bottom at y 4.73 m (clock y 3.00 z 3.90, eye 1.46 m)`; `3 chapter(s) for Standard/Expert, up to 2 at once, skip off` |
| 1 | Names visible, stack above the Enhancements clock, clear of HRCounter's heart | PASS | `20261022-231143-final-chapters-normal.png`, `crop-final-chapters-normal.png`: "Verse Overlap" over "Whole Song Long Chapter" (longest at the bottom), both with progress bars, clearly above "11:11 PM", heart well below the clock |
| 1 | Practice: arrows visible | PASS | paused at 0:33: `< Verse Overlap >` and `< Whole Song Long Chapter >` (`20261022-231213-final-chapters-practice-paused.png`); `skip on` |
| 1 | Practice: skip | PASS | `click-text ">" 0` (Row1) while paused: `[Playback] seek to 78.309s (objects from 80.000s): Chapters skip to end`, `end of #1 'Verse Overlap' [0:20.000, 1:20.000)`; resumed at 78.31, playing at 82.02 (`20261022-231230-final-chapters-after-skip.png`) |
| 2 | Menu toast placement (Solo > Custom, ZZ QA Final selected, camera fwd (0.00, 0.41, 0.91) = 24 deg up, yaw 0) | PASS (note) | `Reload done: 1 map updated` 2.6 s after editing Info.dat; toast "1 map updated" centred directly above the "11:14 PM" clock with a clear gap, clear of the Solo header and every menu panel (`20261022-231413-final-toast-24up-a.png`, `crop-final-toast-24up.png`). Note: it sits on the bright background BEAT SABER logo, whose glow washes the small text out in the capture. Note: the toast follows the camera yaw at show time; with the camera turned 38 deg right it appeared to the right of the clock near the leaderboard (`20261022-231310-final-toast-a.png`) - by design |
| 2 | Play/Practice detail kept after the reload | PASS | `find-text Practice` -> `PracticeButton<ActionButtons 'Practice'`; detail shows `ZZ QA Final v3` |
| 3 | Bangarang Expert -> Normal in place | PASS | `levels Bangarang` resolves to the base-game id `Bangarang`, which does not load here (`_latest.log`: `Could not load StandardLevel. Audio valid: False`, level quits at once; DLC not owned), so custom `4d62` Bangarang was used: `switched Expert -> Normal at 32.1s (in-place); objects from 35.1s ... 7 ms` (`20261022-231543-final-ds-panel.png`) |
| 3 | PracticePlugin NJS label shows the new NJS (10, underlined) | **FAIL** | after the switch, paused: `find-text "<u>"` -> `'<u>16</u>'` (Expert's NJS), `20261022-231548-final-ds-paused-njs.png`. Also after a second switch while paused (Normal -> Hard, NJS 13): still `<u>16</u>`. Clicking PracticePlugin's NJS + gave `17`, so its `_njs` backing value was never updated (not only the label). No `[Playback] PracticePlugin ...` warning in the log |
| 3 | Glue Gun (1fdb5) Expert -> ExpertPlus in place: no Heck `Overwriting existing [TransformController]` | PASS | `ExpertPlus suggests Chroma: swapping in place ...`, `switched Expert -> ExpertPlus at 49.2s (in-place) ... Heck data refreshed (2 sets, 1 tracks), 10 ms` (23:17:33); `_latest.log` after closing has no `TransformController` line at all (`20261022-231737-final-gluegun-after.png`) |
| 4 | Hide OSTs: on, All tab | PASS | `[HideOst] official songs hidden`; `table rows=74 official=0` (`20261022-231803-final-hideost-all-hidden.png`) |
| 4 | Hide OSTs: off | PASS | `[HideOst] official songs shown`; `table rows=146 official=72`; MaxTweaks.json `Hidden: false` |
| 5 | `_latest.log`: no exception/ERROR mentioning MaxTweaks | PASS | only other mods: Cam2 Harmony patch, HRCounter/Counters+ load, LocalLeaderboard TaskCanceled, base-game Bangarang load failure |

Clean-up: zz-qa-final deleted, Hide OSTs back off, PracticePlugin speed set back to 100% (the NJS +1
probe was in-level only; PracticePlugin.json unchanged), game closed gracefully, the clean main build
(1C327A2A) left deployed.

Bug (open): PracticePlugin NJS after an in-place switch. Repro: `play 4d62 expert practice 20`, at
~32 s `button Y down`, `stick 0 -1`, `stick 0 0`, `stick 0 -1`, `stick 0 0`, `button Y up` (log
`switched Expert -> Normal ... (in-place)`), `pause`, `find-text "<u>"` -> NJS `<u>16</u>`, expected
`<u>10</u>`. `PracticePluginSync.AfterDifficultySwap` (src/MaxTweaks/Common/Playback/PracticePluginSync.cs)
returns silently when `_container.TryResolve(PracticeUI)` is null, which would explain both the stale
label and the unchanged `_njs` (suspected, not verified).
