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
- Heart rate display is OUT of scope (the owner uses HRCounter). Since 2026-10-25 MaxTweaks only
  feeds HRCounter: see "Heart rate bridge".

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

- Settings also have "Test blink" (plays one blink in the menu at once, for checking the look in
  the headset) and "Screen-space overlay" (a Screen Space - Camera canvas instead of the head-locked
  world quad; added 2026-10-25 after the world quad darkened one eye only in VR). The closed-eye
  icon is 0.32 m wide at 1.5 m.

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
- NJS row (design: docs/superpowers/specs/2026-10-25-njs-switch-design.md): while holding Y, flick
  the right stick left/right (FPFC: left/right arrows) to show a horizontal row of NJS values under
  the list (integers 8 to 30, plus the difficulty's own NJS and the NJS in effect now inserted at
  their sorted place when they are not integers in range; a window of 7 values around the
  highlight). The difficulty's own NJS (with the active modifiers) is underlined, the one in effect
  now is gold, and the highlight starts on it; the first flick opens the row and moves one value.
  One flick moves one value, no repeat while held, it stops at both ends; forward/back keeps moving
  the difficulty list. Release Y with the list on another difficulty: difficulty switch (the new
  difficulty uses its own NJS); else with the row on another value: only the NJS changes, in place,
  the song keeps playing, no clear gap, toast "NJS 18 (default 16)", score submission disabled for
  the song. Release on the current value: nothing. The chosen NJS is forgotten when the song ends or
  restarts. Works while paused/active-paused (applies on release; objects spawned after the resume
  use the new speed). Mapping Extensions/360 maps change in place too; while Noodle Extensions
  objects are active the row is not offered and a flick shows "NJS switch is not available on this
  map" once (PracticePlugin also disables its NJS slider on Noodle maps). With PracticePlugin
  installed its pause panel NJS follows the change (and with its "adjust NJS with speed" option the
  spawn NJS is the chosen value divided by the song speed, as PracticePlugin itself does).

## Active pause (practice mode only)

- Practice mode only, like Chapters skip and Difficulty switch. Settings: enabled, ghost notes.
- Press X to toggle. On: the song clock, beatmap objects and callbacks stop exactly as in the
  game's pause (IGamePause.Pause plus pausing every object) but WITHOUT the pause menu. X again
  resumes at once: no countdown, objects stay where they are, they are only un-ghosted.
- While active-paused nothing can be hit: the sabers stay visible (for aiming) but the game's
  CuttingManager is disabled; score, energy and head-in-wall are off as in the game's pause.
- Every live note, bomb, chain, arc and wall is shown mostly dissolved (the game's own `_Cutout`
  dissolve through CutoutEffect, per-object property blocks, no shared material edits); restored on
  resume. Objects spawned while active-paused are ghosted too.
- Twelve buttons in two rows of six on a small panel low and to the right of the note lanes:
  `-1m -15s -5s -1s -1b -.25b` / `+.25b +1b +1s +5s +15s +1m`. Point a saber at one (it turns gold
  and grows, like the Chapters arrows) and press A or the trigger of that saber's hand; a UI pointer
  click works too. FPFC: aim with the view centre, A = Enter, right trigger = Space (left = Left
  Ctrl). The right saber wins when both point at something. While playing the trigger does nothing.
- Seconds/minutes steps are plain offsets. Beat steps use the map's tempo (start BPM + BPM change
  events) and snap to the grid of their size: +1b goes to the next beat line (a full beat when
  already on one), -1b to the previous one; +/-.25b the same on the quarter-beat grid. Without a
  usable BPM a beat counts as 0.5 s, unsnapped. Targets clamp to [0, song end - 0.3 s].
- Each step seeks in place (objects resume exactly at the target, score submission off for the
  song) and then spawns the objects of the new position immediately, frozen and ghosted, so the
  pattern there can be studied before resuming (also after a Chapters skip while active-paused).
  On Noodle Extensions maps the objects appear only on resume. Objects spawned while paused are
  respawned in place right before resuming, so their cut sounds are timed correctly, and the cut
  sounds scheduled by a paused spawn are stopped at once (a burst of steps otherwise exhausts
  Unity's audio voices, which stops the paused music source for good). After any resume, and every
  frame while not active-paused, a stopped music source is restarted at the song clock
  (`[Playback] music source was stopped; restarted`). A forward step never moves backward (inside
  the last 0.3 s it stays put).
- Flags (feedback for the automapper, [docs/flags.md](flags.md)): while active-paused the saber ray
  also picks frozen notes, bombs, chain heads and chain links (sphere of 0.28 m around each, nearest
  along the ray; Chapters arrows win). The hovered note gets a dim white frame. Each hand aims on its
  own: a panel button aimed at by either hand beats any note; a trigger presses what its own hand
  aims at; A presses the button, else the note (right saber first), and does nothing while a
  Chapters arrow is aimed at (A skips the chapter then). One press never does two things. Selected
  notes are drawn solid (not ghosted) with a bright white frame, also while playing; the selection
  is kept by map data across steps, seeks and resume until Clear, a difficulty switch or the end of
  the song. A third panel row has `Flag (n)` and `Clear` (faded when nothing is selected). Flag
  swaps in the feedback panel: three columns of toggles (Good / Bad / Note, lit gold), Save and
  Cancel. Save appends a flag (time, beat, speed, difficulty, labels, selected notes, screenshot
  `flags/<id>.png` taken without the panels) to `flags.json` next to the map's Info.dat, then on
  the next frame toasts `Flag saved: 2 labels, 3 notes` and shows the steps again; the labels are
  cleared, the selection kept. Cancel, X, the pause menu and a difficulty switch close it without
  saving. Non-custom levels hide Flag quietly (`flag-open` on the debug channel toasts `Flags need a
  custom level`). A file that does not parse is kept as `flags.json.corrupt-<time>`; a failed write
  toasts `Flag not saved: ...` and never affects the pause. Auto reload ignores these files.
- While active-paused: hold Y difficulty switch works, Chapters arrows work with the same saber
  ray (A or the trigger of the aiming hand; also in the pause menu), Blink does not trigger. Opening the real pause menu (menu button, focus loss) drops active
  pause; the game then owns the pause and Continue resumes as usual.

## Heart rate bridge

- Keeps `tools/hr-osc-bridge/hr_osc_bridge.py` (shipped in the zip as
  `UserData/MaxTweaks/hr_osc_bridge.py`) running as a hidden child process while the game runs:
  `uv run --python 3.13 <script> --parent-pid <game pid> <ExtraArguments>`. The script reads the
  strap over the PC's Bluetooth (standard Heart Rate service) and sends `/hr` int32 OSC messages to
  HRCounter's OSC server (127.0.0.1:9000); HRCounter must be on its "OSC Protocol" data source.
- Off by default (needs a strap, HRCounter and uv). Settings: enabled, only with HRCounter (skip
  when the HRCounter plugin is not loaded), show toasts; config also has UvPath, ScriptPath and
  ExtraArguments (e.g. `--name TICKR`).
- The process dies with the game: it is put in a Windows job object with kill-on-close, and the
  script itself exits when the parent pid is gone. Only one bridge runs at a time (the script holds
  a localhost UDP guard port and exits with code 3 if another holds it; the feature then retries
  every 30 s, so a bridge started by hand is left alone and taken over when it stops).
- A crashed bridge restarts after 5 s, doubling to 60 s; a run longer than 60 s resets the backoff.
  Missing script or uv is a permanent failure for the session (logged, toasted) until the feature is
  toggled. Script output goes to the game log as `[HeartRateBridge] ...` (readings at most once a
  minute); "connected"/"disconnected" also show as toasts.

## Testing and QA

- Unit tests for all Core logic (xUnit v3, Microsoft.Testing.Platform, coverage ratchet).
- In-game QA by the agent in FPFC desktop mode, including practice-mode runs driven by debug
  commands/hotkeys (Common/ControllerInput has keyboard fallbacks: A=Enter, B=Backspace, X=X, Y=Y,
  right stick = arrow keys). The owner does the final VR playtest from docs/qa-checklist.md.
