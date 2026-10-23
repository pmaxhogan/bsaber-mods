# Prior-art recon: HR, auto-reload, hide OSTs, chapters (game 1.40.8)

Legend: VERIFIED = seen in file/URL; INFERRED = reasoned, not confirmed.
R = ~\Documents\csharp-projects. G = ~\BSManager\BSInstances\1.40.8.

## 1. Heart rate

- HRCounter (qe201020335) cloned to R\_recon\HRCounter (HEAD 306f38a, 2026-08-26). VERIFIED.
  - HRCounter\manifest.json: version 3.0.0-8, gameVersion "1.40.8", deps BSIPA ^4.3.6, BSML ^1.12.0, SiraUtil ^3.0.0. Targets exactly our game. It is a PRE-RELEASE (README badge). NOT installed in G\Plugins (no HRCounter.dll).
  - No direct BLE source. grep for Bluetooth/180D in the code: nothing. Sources (HRCounter\Data\DataSources): Pulsoid, Pulsoid2, PulsoidWidget, HypeRate, HypeRate2, YURApp, YURMod, WebRequest (polls a URL; JSON {bpm} or plain int), HttpServerDataSource, OscHR, HRProxy (cloud worker in hrcounter-proxy), debug sources.
  - README table: "BLE HR Monitor" is only supported via Pulsoid/HypeRate/YUR apps (a phone/PC app does the BLE).
  - Built-in local inputs (README "Data Sources"): HTTP server default http://localhost:65302, POST with integer body (HttpLocalOnly default true); OSC server UDP 9000 with int32 messages. Both enabled by default.
  - No graph. grep for graph/chart in the whole repo: no hits. UI = number counter (Counters+ counter or world-space standalone), color gradient, auto-pause, BeatLeader replay HR record/playback (Data\Replay\*).
  - Reuse as data source: HRDataManager (HRCounter\Data\HRDataManager.cs) is public with `event Action<int> OnHRUpdate` (main thread) and `CurrentBpm`. IHRDataSource / IDataSourceDescriptor are public. A new mod could depend on HRCounter and subscribe, or run its own listener. INFERRED: HRDataManager is probably bound only in gameplay installers; check HRCounter\Installers before relying on it in the menu scene (needed for a graph on the results screen).
- BLE bridge tools (VERIFIED via search results only, repos not cloned):
  - RealWhyKnot/hr-bridge-ble: reads 0x180D, POSTs a bare number to 127.0.0.1:8080 (port configurability unknown; HRCounter listens on 65302).
  - nullstalgia/iron-heart (Rust; BLE HR to OSC / WebSocket / logging), YzenWu/VRC-HRMonitor, AkaiMage/BluetoothHeartRateOSC (fork of jlennox/HeartRate), DangerKiddy HeartRateMonitorVRC (C#, MIT, OSC :9000). All Windows BLE to OSC/HTTP, i.e. pluggable into HRCounter's OSC/HTTP input with zero game-side BLE code.
- BLE inside Unity Mono net472: INFERRED, not tested. Windows.Devices.Bluetooth is a WinRT API; it needs the Windows SDK contracts projection (works in a normal .NET Framework/.NET process, not trivially in Unity Mono loaded by BSIPA). Practical design: a separate helper exe doing BLE and pushing to localhost HTTP/OSC (or WebSocket), with the mod just listening. HRCounter has no named-pipe source, so HTTP/OSC is the lowest-friction contract.
- Wahoo TICKR: standard BLE HR profile (0x180D / 0x2A37). Many straps allow only one BLE central at a time, so a connected phone app blocks the PC; newer models may allow multiple connections. From general knowledge, not verified; check the exact model.
- Graph prior art in-game: SongChartVisualizer is installed (note-density chart, not HR). Nothing for HR graphs.

## 2. Auto-reload maps

- SongCore versions: installed G\Plugins\SongCore.dll is 3.15.3 (gameVersion 1.40.1; installed.json:431-434). R\SongCore branch dev = 3.16.0 for game 1.42.3 (source\SongCore\manifest.json:5,8), so it is NOT the code running on 1.40.8. grep -a on the installed DLL finds RefreshSongs, RefreshLevelPacks, LoadCustomLevel, DeleteSong, SongsLoadedEvent, OnLevelPacksRefreshed, _ostBeatmapLevelPacks, but not SetPacksAndRefreshUI (newer: dev commit d993ae1, 2025-12-16, "Migrate Harmony patches to MonoMod hooks"). Signatures below are from dev, assumed the same in 3.15.3 (INFERRED).
- Public API (R\SongCore\source\SongCore\Loader.cs):
  - `Loader.Instance.RefreshSongs(bool fullRefresh = true)` :258. No-op if already loading or if active scene is GameCore. Fires LoadingStartedEvent, then RetrieveAllSongs.
  - fullRefresh=true clears CustomLevels/WIP/hash dictionaries and rescans everything. fullRefresh=false (the R key; Ctrl+R = full; Tick :119-129) keeps loaded levels and uses the hash cache (cachedSongHashData keys) so only unknown folders are loaded. Folders are loaded in parallel (cores/2-1). Cost not benchmarked; with 73 maps it is small.
  - Per-level add is not exposed. `Loader.LoadCustomLevel(path, entry)` :1037 returns (hash, BeatmapLevel) but does NOT register it in CustomLevels/packs. RefreshSongs(false) is the intended call. Removal: `Loader.Instance.DeleteSong(path)` :722 (deletes files, then RefreshLevelPacks).
  - `RefreshLevelPacks()` :207 rebuilds packs, calls BeatmapLevelsModel.ClearLoadedBeatmapLevelsCaches + LoadAllBeatmapLevelPacks and, if the level filtering controller is active, UpdateCustomSongs(), then OnLevelPacksRefreshed.
  - Events: SongsLoadedEvent, LoadingStartedEvent, OnLevelPacksRefreshed (Loader.cs:71-73).
- UI effect: UpdateCustomSongs ends in UpdateSecondChildControllerContent (dev LoadingHooks.cs), which re-sets packs and the collection table. BetterSongList persists category/pack/last song and restores selection (R\BetterSongList\HarmonyPatches\RestoreLevelSelection.cs, RestoreTableScroll.cs). Whether the selected song survives a mid-menu refresh is INFERRED/unknown; test in game before promising no flicker.
- Existing auto-refresh mod: none found. grep -a FileSystemWatcher over G\Plugins\*.dll: only BSML.dll (bsml hot reload). SongCore, PlaylistManager, BeatSaverDownloader do not watch. I did not search BeatMods/Mod Assistant listings for a dedicated watcher mod.
- FileSystemWatcher under this Unity Mono on Windows: VERIFIED G\Beat Saber_Data\Managed\System.dll contains the string "ReadDirectoryChanges" (Win32 watcher) and "FAMWatcher" (Linux fallback class); no "inotify" in the mono runtime. So Windows should use ReadDirectoryChangesW (INFERRED but well supported). Production proof: BSIPA uses FileSystemWatcher for config hot reload (R\BSIPA\IPA.Loader\Config\ConfigRuntime.cs:26,117); BSML for .bsml hot reload (R\BeatSaberMarkupLanguage\BeatSaberMarkupLanguage\ViewControllers\WatcherGroup.cs:182, NotifyFilter LastWrite). General FSW caveats (INFERRED): events come on thread-pool threads, so marshal to the Unity main thread (IPA UnityGame.SwitchToMainThreadAsync, as SongCore does); duplicate and partial events during zip extract, so debounce (about 1-2 s quiet) and require Info.dat + audio present; buffer overflow on mass copies, so on Error event fall back to RefreshSongs(false).
- Junction: VERIFIED. G\Beat Saber_Data\CustomLevels is a Junction -> ~\BSManager\SharedContent\SharedMaps\CustomLevels (73 map folders). G\Playlists is also a junction to SharedContent\Playlists. Recommend (INFERRED) watching the resolved target path (or test both); ReadDirectoryChangesW on a junction path opens the target directory and should work, but the real path removes doubt. Other BSManager instances share this folder, so maps added by them or by BSManager also trigger it.
- External writers should write to a temp folder and rename into CustomLevels so the watcher sees a complete folder.

## 3. Hide OSTs

- Representation (VERIFIED in SongCore dev LoadingHooks.cs SetPacksAndRefreshUI): LevelFilteringNavigationController has separate arrays `_ostBeatmapLevelPacks`, `_musicPacksBeatmapLevelPacks` (DLC/music packs) and `_customLevelPacks` (= Loader.CustomLevelsRepository.beatmapLevelPacks). `_allBeatmapLevelPacks` = OST + music packs + custom and feeds `_levelSearchViewController.Setup(_allBeatmapLevelPacks)`. SelectLevelCategoryViewController.LevelCategory has None, All, Favorites, CustomSongs and a music-packs category (strings CustomSongs/Favorites/MusicPacks in G\Beat Saber_Data\Managed\Main.dll; BSL uses All/Favorites/CustomSongs, RestoreLevelSelection.cs:82-83). So "All" mixes OST, DLC and custom. Confirm the 3.15.3 equivalent of SetPacksAndRefreshUI by decompiling the installed SongCore.dll before patching.
- Custom vs official by id: custom ids are `custom_level_<40 hex>` (R\BetterSongList\Util\BeatmapsUtil.cs:16). Official = no such prefix.
- BetterSongList is installed (G\Plugins\BetterSongList.dll; source 0.4.3, manifest gameVersion 1.42.0). Its filters are hard-coded: SS Ranked, SS Qualified, Unplayed, Played, Requirements, Unranked, All (Filters\FilterMethods.cs). No hide-OST/official filter. Extension: public `FilterMethods.Register<T>(T) where T : ITransformerPlugin, IFilter` (name max 20 chars, register before FilterUI init, needs Config.AllowPluginSortsAndFilters). IFilter = { bool isReady; Task Prepare(CancellationToken); bool GetValueFor(BeatmapLevel) }. Applied in a prefix on LevelCollectionTableView.SetData (HookLevelCollectionTableSet.cs); BSL also clears filters when a non-custom playlist is selected (HookSelectedCollection.cs:22-33).
- Recommendation: standalone patch. A BSL filter only filters the rows of the open pack; it cannot remove OST packs from the pack list, the category tab or the search pack list (`_allBeatmapLevelPacks`). Implement as a hook/postfix on LevelFilteringNavigationController that removes the OST/music-pack entries from `_allBeatmapLevelPacks` and the search Setup (and hides the OST tab). Keep BSL optional; make sure BSL's restored LastPack/LastCategory is not a hidden OST pack. Conflict risk: SongCore hooks the same controller (UpdateCustomSongs), so load after SongCore and BetterSongList.

## 4. Chapters from map data

- Formats (VERIFIED from ChroMapper source cloned to R\_recon\ChroMapper):
  - v2 difficulty: `_customData._bookmarks: [{_time (beats), _name, _color [r,g,b,a]}]` plus `_bookmarksUseOfficialBpmEvents` (Assets\__Scripts\Beatmap\V2\V2Difficulty.cs:88; V2\Customs\V2Bookmark.cs).
  - v3 difficulty: `customData.bookmarks` + `bookmarksUseOfficialBpmEvents` (Converters\V2ToV3.cs:248-252, V3ToV2.cs:233-237).
  - v4: bookmarks are not in the difficulty file. Official editor stores `<mapdir>/Bookmarks/<BookmarkFileName>` (name from the Info.dat difficulty entry), JSON { name, characteristic, difficulty, color "RRGGBB", bookmarks:[{beat, label, text}] } (V4\V4Difficulty.cs:599-625; BaseDifficulty.cs:447-466).
  - Unit is BEATS, not seconds. Constant BPM: seconds = beat*60/bpm; otherwise BPM events are needed.
- Real data in the user install: VERIFIED 28 of 73 map folders contain a bookmarks key; 67 .dat files have a non-empty `_bookmarks`. Example: "1fdb5 (Glue Gun - Artjoms)" has "Intro" (beat 5), "Vocal" (69), "Vocal 2" (101) in every difficulty file (SharedContent\SharedMaps\CustomLevels\1fdb5 (Glue Gun - Artjoms)\ExpertPlusStandard.dat). Names are mapper-defined and may differ per difficulty; many maps have none, so a fallback is needed.
- SongCore exposure: no. grep for bookmark in SongCore, BetterSongList, beatleader-mod, Heck, Camera2, CountersPlus: zero hits. SongCore reads Info.dat _customData only (source\SongCore\Data\SongData.cs). A mod must read the difficulty .dat itself (folder via Loader.CustomLevelLoader._loadedBeatmapSaveData[levelID].customLevelFolderInfo). CustomJSONData is installed and may keep customData on parsed beatmap data (INFERRED; verify).
- In-game displays: none found. PracticePlugin (installed) has start-time/seek; not source-checked for sections. BeatLeader replay UI not checked beyond the bookmark grep.

## Questions for the user
1. HR: depend on HRCounter (pre-release 3.0.0-8, matches 1.40.8) or standalone? Own BLE helper exe, or document hr-bridge-ble / iron-heart feeding HRCounter HTTP/OSC?
2. HR graph: live in-song, results screen, or both (HRCounter only exposes an int event, no history)?
3. Which TICKR model, and is phone + PC simultaneous connection required?
4. Auto-reload: incremental RefreshSongs(false) after debounce is the only API; menu-only trigger OK (no-op in GameCore)? Tolerate possible list reset?
5. Hide OSTs: also DLC music packs? Only "All" view, or tabs and search too?
6. Chapters: map bookmarks (28/73 coverage) with manual/auto fallback? Per difficulty or per song?
