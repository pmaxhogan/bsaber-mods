# MaxTweaks - locked feature spec

Decisions from the owner interview on 2026-10-22. These are LOCKED: implement them as written and do
not re-ask. Anything not covered is the implementer's call (pick the option most consistent with
this file, prefer reversible choices, and list the call in your report).

## Global

- One combined BSIPA plugin, MaxTweaks, for Beat Saber 1.40.8 only (BSIPA 4.3.6, SiraUtil 3.2.1,
  BSML 1.12.5, SongCore 3.15.3 as installed). Every feature has its own Enabled flag.
- Polish bar: solid personal tools. Settings for every feature, sensible defaults, patches guarded so
  a broken hook can never hurt gameplay. No BeatMods submission, no localization.
- Settings: a full Mod Settings page "MaxTweaks" with one section per feature (UI/Settings.bsml) plus a
  quick-toggle tab under the song preview with the on/off switches (UI/QuickTab.bsml, done).
- A game patch that fails switches off only its own feature and logs why (Common/SafePatcher,
  Common/FeatureStatus). Code running inside game callbacks is wrapped in Common/Guard.
- Pure logic lives in src/MaxTweaks.Core (no Unity/game types) with xUnit tests; coverage ratchet in CI.
- Heart rate is OUT of scope (the owner uses HRCounter). Active pause is OUT of scope (later idea).

## Hide OSTs

- One-click icon toggle button in the solo song list header, next to the search/sort controls.
  State persists across restarts (HideOstConfig.Hidden).
- When on, hide every non-custom song: base OST, Extras and all DLC music packs. Remove them from
  the "All" list, from the pack tabs/pack list and from search. Only custom levels remain.
- Button only appears in the solo song list.
- Standalone patch (not a BetterSongList filter: a BSL filter cannot remove packs, tabs or search).

## Auto-reload maps

- Watch the custom level folders with the OS-native watcher (FileSystemWatcher, Win32
  ReadDirectoryChangesW under Unity Mono). Poll only if the native watcher is unavailable or errors.
  The 1.40.8 instance's CustomLevels is a junction into BSManager SharedContent: watch the resolved
  target. Also cover CustomWIPLevels and any extra SongCore folders.
- Batch changes until DebounceSeconds (default 2s) of quiet on disk; wait for Info.dat to exist.
- In the menu: reload quietly and keep the place (selected song, pack and scroll position), then show
  a small toast summarising, e.g. "3 maps added, 1 updated".
- During gameplay: never reload. If the map being played changed on disk, show a small toast
  "Map updated on disk: <name> - reloads when you exit", then reload once back in the menu so the
  next play uses the new version.
- Settings: enabled, debounce seconds, show toasts.

## Blink

- Runs in solo modes: normal, practice and party (standard player + campaign). Not multiplayer, not
  replays.
- Shows a blink overlay when ALL of these hold: no notes in the last QuietBeforeSeconds (3s); no notes
  in the next ClearAheadSeconds (8s); no walls that would hit the head in the next 8s. Walls that
  pass beside the player are fine. Bombs count as notes.
- Length DurationSeconds (3s) including a 0.3s fade in and 0.3s fade out. At most once every
  CooldownSeconds (300s) of played time; the cooldown counts only play time and carries across songs.
- Look: soft vignette (about VignetteStrength 0.6 at the corners, centre clear) plus a small
  closed-eye icon a little below eye level, head-locked.
- Settings: every threshold above plus vignette strength, defaults as given.

## Chapters

- Source: a sidecar file chapters.json in the map folder (next to Info.dat).
- Format: {"chapters": [ {start, end, label, difficulties?} ]}. Each chapter uses either time or beats:
  start/end as "m:ss(.fff)" strings or seconds, OR startBeat/endBeat (converted with the map's BPM).
  Optional "difficulties" limits a chapter to those difficulties; otherwise it applies to all.
  Chapters may overlap; each is at least 1s and up to several minutes.
- Display (all modes): the active chapters' names above the notes (where clock mods display), each
  on its own line as "<  Name  >" with a thin progress bar showing progress through that chapter.
  Overlapping chapters stack, longest underneath. Progress bars can be switched off.
- Skip: in practice mode, during play AND from the pause menu. Point at < or > with either saber and
  press A to jump to that chapter's start (<) or end (>). Skipping is practice-only; normal play
  only shows the names.
- Helper tool: a CLI in tools/ that generates a starter chapters.json from a map's editor bookmarks
  (v2 _customData._bookmarks, v3 customData.bookmarks, v4 Bookmarks/ files): each bookmark starts a
  chapter and the next one ends it. The owner then edits/overlaps by hand.
- Settings: enabled, show progress bars.

## Difficulty switch (practice mode only)

- While playing or paused in practice mode: hold Y to show a vertical list of the current
  characteristic's difficulties (hardest at the top), overlaid prominently in front of the player,
  with the current difficulty highlighted.
- While holding Y, push the right stick forward/back to move the highlight up/down one difficulty
  (with a slide animation of the highlight). Releasing Y switches to the highlighted difficulty;
  releasing on the current one cancels.
- The switch happens IN PLACE: the song audio never pauses, slows, skips or stops. All beatmap objects
  (notes, bombs, walls, arcs, chains) are removed for ClearGapSeconds (3s) after the switch, then
  the new difficulty's objects continue. Notes already in flight slide up/down out of the way
  (SlideOldNotes) instead of vanishing.
- If the in-place swap fails, fall back to restarting the level at the current time on the new
  difficulty (the owner accepted the audio break for the failure case only).
- After a switch: use the new difficulty's note jump speed and offset; disable score submission for
  the rest of that song (SiraUtil Submission). Score/combo carry over (not reset).
- Settings: enabled, clear gap seconds, slide old notes.

## Testing and QA

- Unit tests for all Core logic (xUnit v3, Microsoft.Testing.Platform, coverage ratchet).
- In-game QA by the agent in FPFC desktop mode, including practice-mode runs driven by debug
  commands/hotkeys (Common/ControllerInput has keyboard fallbacks: A=Enter, B=Backspace, X=X, Y=Y,
  right stick = arrow keys). The owner does the final VR playtest from docs/qa-checklist.md.
