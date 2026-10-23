# Beat Saber PC version target: research report (2026-10-22)

## TL;DR

Recommendation: STAY ON 1.40.8 (Steam) and build the new BSIPA mod against 1.40.8.

Runner-up, if you want the newest content: 1.44.1, the newest version with any mod support. On 1.44.1 you would lose
PlaylistManager, MappingExtensions, BeatSaverVoting, BetterPause, LocalLeaderboard and SiraLocalizer outright.
Heck, Chroma and Noodle only have 1.42.1 builds, which are untested on 1.44.1. Confidence: medium-high.

The latest Steam release is 1.45.3 (2026-10-15). BeatMods does not list 1.44.2, 1.44.3 or any 1.45.x as a game
version (the API returns "Invalid game version."), so "update to latest" is not a moddable option today.

---

## 1. Local install (MEASURED, read-only)

- BSInstances contains exactly one instance: ~\BSManager\BSInstances\1.40.8
  (BeatSaberVersion.txt = 1.40.8_7379; metadata.config store = steam).
- In the BSManager config, last-version-launched = 1.40.8, and BSManager flags that version "recommended": true.
- BSIPA 4.3.6. Today's log (Logs\2026.10.22.19.01.45.log) shows 49 plugins and libraries loaded, none disabled
  (UserData\Disabled Mods.json is empty). IPA\Pending is empty.
- CustomSabers, CustomNotes, CustomPlatforms and CustomAvatars are all empty, so no cosmetic model mods are installed.
- Mod versions come from the BSIPA log, which reads the embedded manifests. I cross-checked them with a byte scan of
  each DLL's manifest.

## 2. Game versions (MEASURED)

Steam releases after 1.40.8, from BSManager's bs-versions.json (updated 2026-10-15):
1.40.9, 1.40.10, 1.40.11, 1.40.12, 1.40.13, 1.41.1, 1.42.0, 1.42.1, 1.42.2, 1.42.3, 1.43.0,
1.44.0, 1.44.1, 1.44.2, 1.44.3, 1.45.0, 1.45.1, 1.45.2, 1.45.3 (2026-10-15).

BeatMods, using the rewritten API:
  GET https://beatmods.com/api/versions?gameName=BeatSaber
  GET https://beatmods.com/api/mods?gameName=BeatSaber&gameVersion=X&status=verified|all&platform=steampc
  (status=all includes pending; the platform field on the mods is "universalpc")
- The newest registered version is 1.44.1. Versions 1.40.9 to 1.40.12 are registered but have 0 mods.
  1.40.13, 1.44.2 and later, and all of 1.45.x are not registered.
- Mod counts (verified / verified+pending): 1.40.8 132/152, 1.41.1 86/92, 1.42.0 106/116, 1.42.3 106/116,
  1.43.0 108/119, 1.44.0 109/119, 1.44.1 109/126.
- The BeatMods defaultVersion flag is still set to 1.40.5. It looks stale and isn't meaningful.
- BSManager marks 1.40.8 as "recommended". No newer version carries that flag.
- The BSMG wiki (bsmg.wiki/pc-modding.html) lists 1.40.8, 1.37.1, 1.34.2 and 1.29.1 as official Steam/Oculus
  LEGACY BRANCHES. It names no newer version as recommended and tells users to check the Discord's #modding-announcements.

Activity: for each mod, I counted its latest BeatMods upload (verified+pending) if it was created on or after 2026-01-01.
- 1.40.8: 28 uploads by 19 distinct uploaders. They include Heck 1.8.1, Chroma 2.9.22, Noodle 1.7.20, ScoreSaber 3.4.1,
  Hitbloq, Vivify, and Theater (2026-10-14).
- 1.44.1: 40 uploads by 21 distinct uploaders.
- 1.41.1: 9 uploads by 5 uploaders. Most authors skipped this version.
Active development is split roughly evenly between 1.40.8 (the legacy long-term target) and the current line (1.42 to 1.44.1).
On "which version do most authors build against now", the numbers are a near-tie that slightly favors 1.44.1.

Install base (MEASURED: BeatMods downloadCount, which is cumulative, so read it with care):
| core lib | 1.40.x build (downloads, uploaded) | 1.41.1-1.44.1 build (downloads, uploaded) |
|---|---|---|
| BSIPA | 4.3.6: 617,040 (2025-04-29) | 4.3.7: 249,414 (2025-12-16) |
| SongCore | 3.15.3: 588,722 (2025-02-25) | 3.16.0: 225,221 (2025-12-16) |
| BSML | 1.12.5: 672,141 (2024-12-12) | 1.14.1: 220,488 (2025-12-22) |
| SiraUtil | 3.2.1: 602,105 (2025-06-07) | 3.3.1: 218,371 (2025-12-22) |
| BS Utils | 1.14.2: 580,514 (2024-11-13) | 1.14.3: 215,635 (2025-12-16) |
The 1.40.x builds were the only option for about 8 months before 1.41.1 mods existed, so these totals overstate the
1.40.8 line's current share. Monthly rates are about 34k/month for BSIPA 4.3.6 (over its whole life) and about
25k/month for 4.3.7 since Dec 2025. INFERRED: the current line is a large and growing share of installs but has not
clearly overtaken 1.40.8; a 2026-only split can't be measured from this data.

Release targets of authors who ship for several game versions (MEASURED via the GitHub API):
- Aeroluna/Heck (Heck, Chroma, Noodle): latest release 2026-05-09, with builds for 1.29.1, 1.34.2, 1.37.1, 1.40.8 and 1.42.1.
  The 2026-02-18 release notes say "Added Beat Saber version 1.40.8 / Updated to Beat Saber version 1.42.1".
  There are no builds for 1.43 or 1.44.
- ScoreSaber/pc-mod v3.4.2 (2026-08-10): zips for "1.40.0-to-1.40.8" and "1.42.0-to-1.44.1". On BeatMods, 3.4.1 is
  pending for 1.42 and later.
- BeatLeader v0.10.0 (2026-04-19): zips "bs1.40.2+" and "bs1.42+". BeatMods has nothing for 1.42 and later.
- kinsi55's Camera2, BetterSongList, BetterSongSearch, Tweaks55 and GottaGoFast: "1.42.0+" updates (2026-01-03 and later).
- PracticePlugin 9.1.1 for bs1.44.1 (GitHub, 2026-09-06; pending on BeatMods).
- SongPlayHistory 2.3.0 for bs1.41.1 (GitHub; pending on BeatMods for 1.41.1 through 1.44.1). 2.2.5 exists for bs1.40.8.
- CustomJSONData: the last release is 2.6.8, built for 1.40.0 (Feb 2025). BeatMods marks that same build compatible
  through 1.44.1.
- Inactive or not updated past 1.40.8:
  - PlaylistManager (rithik-b): last commit 2024-10-09; nothing past 1.40.8.
  - MappingExtensions: last commit 2025-02-28.
  - BeatSaverVoting: a source commit titled "1.41.1" (2025-12-06), but no release.
  - SiraLocalizer: last release is for 1.40.0.
  - LocalLeaderboard: last release is for 1.40.x.
  - BetterPause: nothing past 1.40.8 on BeatMods.

## 3. Availability table (MEASURED, BeatMods)

Each cell is derived from the supportedGameVersions field of every mod version seen in any BeatMods listing.
The per-gameVersion listing alone was lossy: it showed BetterSort.LastPlayed and PlayCount Sort as MISSING on 1.41.1
even though their builds declare 1.41.1. Rebuilding the table this way flipped only those two cells.

How to read it:
- A plain version number means the mod is verified on BeatMods for that game version.
- "(pend)" means only a pending upload exists.
- MISSING means BeatMods has nothing for that version.
- Columns 1.42.1, 1.42.2 and 1.44.0 are left out because they match the columns next to them, with one exception:
  Camera2 shows MISSING on 1.42.1 only, which looks like a BeatMods data quirk.
- When a mod built before 1.41 shows as available on a newer version (for example CustomJSONData 2.6.8+1.40.0 or
  BeatSaverDownloader 6.0.7), an approver marked it compatible. It was not rebuilt.

| mod | installed | 1.40.8 | 1.41.1 | 1.42.0 | 1.42.3 | 1.43.0 | 1.44.1 |
|---|---|---|---|---|---|---|---|
| BSIPA | 4.3.6 | 4.3.6 | 4.3.7 | 4.3.7 | 4.3.7 | 4.3.7 | 4.3.7 |
| SiraUtil | 3.2.1+bs.1.40.0 | 3.2.1 | 3.3.1+1.41.1 | 3.3.1 | 3.3.1 | 3.3.1 | 3.3.1 |
| System.IO.Compression | 4.6.57 | 4.6.57 | 4.6.57 | 4.6.57 | 4.6.57 | 4.6.57 | 4.6.57 |
| System.IO.Compression.FileSystem | 4.7.3056 | 4.7.3056 | 4.7.3056 | 4.7.3056 | 4.7.3056 | 4.7.3056 | 4.7.3056 |
| SiraLocalizer | 2.0.3+bs.1.40.0 | 2.0.3 | MISSING | MISSING | MISSING | MISSING | MISSING |
| BeatSaberMarkupLanguage | 1.12.5+bs.1.40.0 | 1.12.5 | 1.14.1 | 1.14.1 | 1.14.1 | 1.14.1 | 1.14.1 |
| SongCore | 3.15.3 | 3.15.3 | 3.16.0 | 3.16.0 | 3.16.0 | 3.16.0 | 3.16.0 |
| PracticePlugin | 9.1.0 | 9.1.0 | MISSING | MISSING | MISSING | MISSING | 9.1.1(pend) |
| Ini Parser | 2.5.9 | 2.5.9 | 2.5.9 | 2.5.9 | 2.5.9 | 2.5.9 | 2.5.9 |
| BS Utils | 1.14.2 | 1.14.2 | 1.14.3 | 1.14.3 | 1.14.3 | 1.14.3 | 1.14.3 |
| BeatSaverSharp | 3.4.5 | 3.4.5 | 3.4.5 | 3.4.5 | 3.4.5 | 3.4.5 | 3.4.5 |
| ScoreSaberSharp | 0.1.0 | 0.1.0 | 0.1.0 | 0.1.0 | 0.1.0 | 0.1.0 | 0.1.0 |
| BeatSaverDownloader | 6.0.7 | 6.0.7 | 6.0.7 | 6.0.7 | 6.0.7 | 6.0.7 | 6.0.7 |
| IntroSkip | 4.0.6 | 4.0.6 | 4.0.8 | 4.0.8 | 4.0.8 | 4.0.8 | 4.0.8 |
| ScoreSaber | 3.3.17 | 3.3.17 | MISSING | 3.4.1(pend) | 3.4.1(pend) | 3.4.1(pend) | 3.4.1(pend) |
| BetterSongList | 0.4.2 | 0.4.2 | MISSING | 0.4.3 | 0.4.3 | 0.4.3 | 0.4.3 |
| BetterSort.LastPlayed | 3.2.3 | 3.2.3 | 3.2.3 | 3.2.3 | 3.2.3 | 3.2.3 | 3.2.3 |
| protobuf-net | 3.0.102 | 3.0.102 | 3.0.102 | 3.0.102 | 3.0.102 | 3.0.102 | 3.0.102 |
| Enhancements | 3.0.18 | 3.0.18 | 3.0.20 | 3.0.20 | 3.0.20 | 3.0.20 | 3.0.20 |
| CustomJSONData | 2.6.8+1.40.0 | 2.6.8+1.40.0 | 2.6.8+1.40.0 | 2.6.8+1.40.0 | 2.6.8+1.40.0 | 2.6.8+1.40.0 | 2.6.8+1.40.0 |
| Heck | 1.7.14+1.40.3 | 1.7.14+1.40.3 | MISSING | MISSING | MISSING | MISSING | MISSING |
| LookupID | 1.0.1 | 1.0.1 | 1.0.1 | 1.0.1 | 1.0.1 | 1.0.1 | 1.0.1 |
| Chroma | 2.9.20+1.40.3 | 2.9.20+1.40.3 | MISSING | MISSING | MISSING | MISSING | MISSING |
| BeatSaverVoting | 2.4.6 | 2.4.6 | MISSING | MISSING | MISSING | MISSING | MISSING |
| SongPlayHistory | 2.2.3 | 2.2.3 | 2.3.0(pend) | 2.3.0(pend) | 2.3.0(pend) | 2.3.0(pend) | 2.3.0(pend) |
| SixLabors.ImageSharp | 2.0.0 | 2.0.0 | 2.0.0 | 2.0.0 | 2.0.0 | 2.0.0 | 2.0.0 |
| NoodleExtensions | 1.7.18+1.40.3 | 1.7.18+1.40.3 | MISSING | MISSING | MISSING | MISSING | MISSING |
| MappingExtensions | 1.7.6 | 1.7.6 | MISSING | MISSING | MISSING | MISSING | MISSING |
| BeatSaberPlaylistsLib | 1.7.2 | 1.7.2 | 1.7.2 | 1.7.2 | 1.7.2 | 1.7.2 | 1.7.2 |
| PlaylistManager | 1.7.3 | 1.7.3 | MISSING | MISSING | MISSING | MISSING | MISSING |
| LeaderboardCore | 1.7.0 | 1.7.0 | 1.7.0 | 1.7.0 | 1.7.0 | 1.7.0 | 1.7.0 |
| I Forgor | 1.4.1 | 1.4.1 | 1.4.1 | 1.4.1 | 1.4.1 | 1.4.1 | 1.4.1 |
| SongDetailsCache | 1.4.0 | 1.4.0 | 1.4.0 | 1.4.0 | 1.4.0 | 1.4.0 | 1.4.0 |
| SliceDetails | 1.3.6 | 1.3.6 | 1.3.6 | 1.3.7 | 1.3.7 | 1.3.7 | 1.3.7 |
| BeatSaverUpdater | 1.2.11 | 1.2.11 | 1.2.11 | 1.2.11 | 1.2.11 | 1.2.11 | 1.2.11 |
| SongChartVisualizer | 1.1.11 | 1.1.11 | 1.1.11 | 1.1.11 | 1.1.11 | 1.1.11 | 1.1.11 |
| BetterPause | 1.1.2 | 1.1.2 | MISSING | MISSING | MISSING | MISSING | MISSING |
| AdBlocker | 1.0.5 | 1.0.5 | 1.0.5 | 1.0.5 | 1.0.5 | 1.0.5 | 1.0.5 |
| HighlightBombs | 1.0.3 | 1.0.3 | 1.0.3 | 1.0.3 | 1.0.3 | 1.0.3 | 1.0.3 |
| BeatSaviorUI | 1.0.3 | 1.0.3 | 1.0.3 | 1.0.3 | 1.0.3 | 1.0.3 | 1.0.3 |
| WhyIsThereNoLeaderboard | 1.0.3 | 1.0.3 | 1.0.3 | 1.0.3 | 1.0.3 | 1.0.3 | 1.0.3 |
| BetterSongList-PlayCount | 1.0.0 | 1.0.0 | 1.0.0 | 1.0.0 | 1.0.0 | 1.0.0 | 1.0.0 |
| Camera2 | 0.6.118 | 0.6.118 | MISSING | 0.6.119 | 0.6.119 | 0.6.119 | 0.6.119 |
| BeatLeader | 0.9.33 | 0.9.33-1.40.2 | MISSING | MISSING | MISSING | MISSING | MISSING |
| BetterSongSearch | 0.8.1 | 0.8.1 | MISSING | 0.8.2 | 0.8.2 | 0.8.2 | 0.8.2 |
| Tweaks55 | 0.3.17 | 0.3.17 | MISSING | 0.3.18 | 0.3.18 | 0.3.18 | 0.3.18 |
| GottaGoFast | 0.2.4 | 0.2.4 | MISSING | 0.2.5 | 0.2.5 | 0.2.5 | 0.2.5 |
| LocalLeaderboard | 0.0.12+1.40.0 | 0.0.12+1.40.0 | MISSING | MISSING | MISSING | MISSING | MISSING |
| FailButton | 0.0.4 | 0.0.4 | 0.0.4 | 0.0.4 | 0.0.4 | 0.0.4 | 0.0.4 |

GitHub builds that are not on BeatMods change the picture for 1.42 and later:
- BeatLeader: bs1.42+ build.
- ScoreSaber 3.4.2: covers 1.42.0 to 1.44.1.
- Heck, Chroma and Noodle: built for 1.42.1 exactly.
- PracticePlugin 9.1.1: built for 1.44.1.
- SongPlayHistory 2.3.0: built for 1.41.1 (pending on BeatMods for 1.42 to 1.44.1).

## 4. Critical vs nice-to-have (INFERRED: my classification; guesses are flagged)

Critical: core, libraries, scoring, and mods that enable content:
- Core and libraries: BSIPA, SiraUtil, BSML, SongCore, BS Utils, BeatSaberPlaylistsLib, LeaderboardCore,
  CustomJSONData, Heck, and all the pure libraries (protobuf-net, SongDetailsCache, ImageSharp,
  System.IO.Compression*, INI Parser, BeatSaverSharp, ScoreSaberSharp, LookupID).
- Scoring, camera and song management: ScoreSaber, BeatLeader, Camera2, PlaylistManager, BeatSaverDownloader,
  BetterSongList, BetterSongSearch.
- Map extensions: Chroma, NoodleExtensions and MappingExtensions (guess: critical if you play modded maps that use them;
  many precision maps need MappingExtensions).

Nice-to-have (guess):
- SiraLocalizer (only matters for a non-English UI).
- PracticePlugin (guess: semi-critical if you train).
- SongPlayHistory, BeatSaverVoting, BeatSaverUpdater, BetterPause, LocalLeaderboard.
- IntroSkip, Enhancements, Tweaks55, GottaGoFast, FailButton, AdBlocker, HighlightBombs.
- BeatSaviorUI, SliceDetails, SongChartVisualizer, I Forgor, WhyIsThereNoLeaderboard.
- BetterSort.LastPlayed, BetterSongList PlayCount Sort.

## 5. What you lose on each candidate version

1.40.8 (stay): nothing. Upgrades available on this version:
- ScoreSaber 3.4.2 and BeatLeader 0.10.0 (the bs1.40.2+ zip).
- Heck 1.8.x, Chroma 2.9.22 and Noodle 1.7.21 (GitHub; pending on BeatMods).
- SongPlayHistory 2.2.5 and BeatSaverUpdater 1.2.14.

1.44.1 (newest moddable version):
- Hard losses, with no build found anywhere: PlaylistManager (critical), MappingExtensions (critical for some maps),
  BeatSaverVoting, BetterPause, LocalLeaderboard, SiraLocalizer.
- At risk: Heck, Chroma and Noodle. They only have 1.42.1 builds, which need a manual GitHub install and may not load on 1.44.1.
- Manual installs (GitHub or pending, so not in the default verified-only view): BeatLeader, ScoreSaber,
  PracticePlugin, SongPlayHistory.
- Everything else is fine. The core libraries are updated: BSIPA 4.3.7, SiraUtil 3.3.1, BSML 1.14.1, SongCore 3.16.0.

1.42.1 (best fit for the Heck family):
- Same hard losses as 1.44.1, but without the Heck risk.
- PracticePlugin is missing as well.
- It is 9 Steam releases behind current.
- Camera2 shows MISSING on BeatMods for 1.42.1 specifically. The 1.42.0+ DLL should work (inferred).

1.41.1: the worst option. Many authors skipped it; ScoreSaber, BetterSongList, BetterSongSearch, Camera2, Tweaks55,
GottaGoFast and BeatLeader are MISSING (no GitHub build either for most of them).

1.45.x (current Steam): no mod support on BeatMods at all. Not viable.

## 6. Recommendation (INFERRED from the facts above)

Target and play 1.40.8. This rests on MOD COVERAGE, not on developer momentum (which is a near-tie, see section 2).
- The deal-breakers on any newer version are PlaylistManager (no build past 1.40.8 anywhere), MappingExtensions
  (same), and Heck/Chroma/Noodle (no build past 1.42.1). If you don't rely on PlaylistManager's in-game playlist UI
  or on Chroma/Noodle/MappingExtensions maps, 1.44.1 becomes the better answer: the other losses (BeatSaverVoting,
  BetterPause, LocalLeaderboard, SiraLocalizer) are nice-to-haves, and BeatLeader, ScoreSaber, PracticePlugin and
  SongPlayHistory all have 1.42+/1.44.1 builds on GitHub or pending on BeatMods.
- All 49 installed mods are available and verified on 1.40.8. Every newer candidate loses at least 6 mods that have no
  replacement, PlaylistManager among them.
- 1.40.8 is an official Steam legacy branch and a long-term target:
  - Heck, ScoreSaber, BeatLeader, SongPlayHistory and about 19 BeatMods uploaders still ship 1.40.8 builds in 2026.
  - Heck explicitly added 1.40.8 as a maintained target in Feb 2026.
  - BSManager flags it "recommended".
  Together, that gives a new mod the largest PC modded audience that is not about to move again.
- The non-legacy line (1.42 to 1.44.1) is already behind Steam (1.45.3), and Steam patches arrive every few weeks.
  Targeting it means chasing a moving target.
- For the new mod:
  - Build against the 1.40.8 game assemblies with BSIPA 4.3.x, SiraUtil 3.2.x and BSML 1.12.x.
  - If you also want reach on the current line, keep the code that touches game APIs thin (isolate the Harmony patches).
    You can then publish a second build for the current line later, as Heck, ScoreSaber and BeatLeader do with
    separate zips per game-version range.

Cost of staying on 1.40.8 (partly INFERRED):
- No new official content or features from 1.40.9 through 1.45.3, which is 15 months of patches. For example,
  BeatLeader's notes say 1.42 brought OST 8. I did not check what the other releases added.
- Official online multiplayer generally requires players to be on matching game versions (inferred, not verified).
- Some of the newest mod releases target 1.42 and later only.
- The community's long-term version will eventually move (historically 1.29.1, then 1.34.2, 1.37.1 and 1.40.8).
  A future move would require porting the mod.

Confidence: medium-high. Main uncertainties:
- Whether the BSMG Discord's #modding-announcements (not accessible here) currently declares 1.44.x moddable or recommended.
- Whether the Heck 1.42.1 builds load on 1.44.1.

## Sources
- Local: ~\BSManager\BSInstances\1.40.8 (Plugins, Libs, IPA, UserData, Logs\2026.10.22.19.01.45.log);
  %APPDATA%\bs-manager\config.json
- https://beatmods.com/api/versions?gameName=BeatSaber
- https://beatmods.com/api/mods?gameName=BeatSaber&gameVersion=<v>&status=verified|all&platform=steampc
- https://raw.githubusercontent.com/Zagrios/bs-manager/master/assets/jsons/bs-versions.json
- https://bsmg.wiki/pc-modding.html
- GitHub releases and commits: Aeroluna/Heck, Aeroluna/CustomJSONData, ScoreSaber/pc-mod, BeatLeader/beatleader-mod,
  kinsi55/*, denpadokei/PracticePlugin, qe201020335/SongPlayHistory, rithik-b/PlaylistManager,
  Kylemc1413/MappingExtensions, Top-Cat/BeatSaverVoting, Auros/SiraLocalizer, speecil/LocalLeaderboard-PC
- Raw data in the same scratchpad research dir: dl/*.json, table.json, installed_ids.txt, scripts/*.py
