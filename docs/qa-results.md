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
