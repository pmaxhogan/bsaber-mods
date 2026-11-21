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
- The heart rate number is HRCounter's (the owner uses that mod; it also records heart rate into
  BeatLeader replays). MaxTweaks feeds it (see "Heart rate bridge", 2026-10-25) and draws a graph
  under it (see "Heart rate graph", 2026-11-14).

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
- Every live note, bomb, chain and arc is shown mostly dissolved (the game's own `_Cutout`
  dissolve through CutoutEffect, per-object property blocks, no shared material edits); restored on
  resume. Objects spawned while active-paused are ghosted too. Walls are never dissolved (a partly
  dissolved wall renders as a solid bright slab that hides the panels); they keep the game's look.
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
  `flags/<id>.png` taken without the panels, with the notes solid and the walls hidden for that frame) to `flags.json` next to the map's Info.dat, then on
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
- The script prints one `<n> bpm` line per reading (about one a second). The feature publishes each
  to Common/HeartRate/HeartRateFeed on the frame it arrives; other features (the graph) listen there.

## Heart rate graph

- Gameplay (every mode): a graph of the heart rate over the song. X = song time / song length (left
  edge = start, right edge = end; practice mode starts part-way in), Y = MinBpm..MaxBpm (100..200),
  clamped to the edges. It starts empty and fills in as the song plays.
- Colour: by value on the plasma ramp (d3/Grafana "Plasma": dark blue at ColorMinBpm through purple,
  magenta and orange to yellow at ColorMaxBpm; 115..190, narrower than the axis so the common
  middle range shows more variation, the ends keep the end colours). Every pixel takes the colour of the heart rate at its
  height, so the fill shades through the ramp from the bottom edge up to the line and gets more
  opaque towards the top; the line has the colour of its own height.
- Between readings (the strap reports about once a second) the line follows the song at the last
  heart rate, redrawn 30 times a second. From its end a fainter level line runs to the right edge:
  where the graph ends if the heart rate stays exactly as it is.
- Holes: readings more than 5 song seconds apart are not joined (strap dropout, skip ahead). A jump
  back (practice seek, chapter jump) drops everything from the new time on; it is redrawn as the
  song replays. While the song time stands still (pause) the newest reading replaces the last point.
- Placement: directly under what HRCounter's counter draws ("HRCounter Standalone", found by name,
  no assembly reference), GapBelowHRCounter below its lowest point, following it on the rotating HUD
  of 360 levels. HRCounter draws its heart about 1.3 m above its own position setting, so the
  counter's StaticCounterPosition decides where the pair sits (the owner's is raised to y 1.9 so the
  graph takes the spot the counter had at the default 1.2). Without that counter: the Fallback
  position. Three faint guide lines mark MinBpm, the middle and MaxBpm.
- Nothing is created before the first reading of a song, and nothing at all when the HUD is hidden.
  Display only: no raycaster, no raycast targets.
- On by default (invisible without heart rate data). Settings: enabled. Config also has MinBpm,
  MaxBpm, ColorMinBpm, ColorMaxBpm, Width and Height (metres), GapBelowHRCounter and FallbackX/Y/Z.

## OBS recording

Added 2026-11-14 at the owner's request: OBS is already set up and running; the mod only presses
"start recording" and "stop recording" and names the file.

- Talks to OBS over obs-websocket 5 (built into OBS 28+, rpcVersion 1). The one prerequisite is
  OBS: Tools > WebSocket Server Settings > Enable WebSocket server. Port and password are read at
  connect time from OBS's own settings file on this PC
  (`%APPDATA%/obs-studio/plugin_config/obs-websocket/config.json`), so nothing needs configuring;
  `ObsRecording.Host` (default `127.0.0.1`), `Port` (0 = OBS's own, else 4455) and `Password`
  (empty = OBS's own) in `UserData/MaxTweaks.json` override them. The password is never logged,
  shown or put in a toast, and no real address or password belongs in this repository.
- OBS's settings file describes the OBS on this PC, so it is only used when the host is this PC
  (`127.0.0.1`, another plain loopback address, `::1` or `localhost`). For any other host the port
  defaults to 4455, the password must be set in `ObsRecording.Password` (this PC's OBS password is
  never sent to another machine), and finished files are not renamed: they are on that machine.
- Off by default. Settings: enabled, show toasts, and a live status line ("Connected to OBS
  31.0.0", "Not connected: ...").
- Every gameplay scene (solo, campaign, multiplayer; practice and replays included): `StartRecord`
  when the level scene starts, `StopRecord` when it goes away (finish, fail, quit to menu,
  restart). The game is never paused or blocked for it: all socket work runs on the thread pool, so
  the first and last moments of a level may be missing from the file.
- The saved file is renamed once OBS reports the output as stopped (`RecordStateChanged` with
  `OBS_WEBSOCKET_OUTPUT_STOPPED`), using the path OBS reports, in the same folder:
  `<OBS's own name> - <song name> - <difficulty><ext>`. Difficulty as the game shows it (`Easy`,
  `Normal`, `Hard`, `Expert`, `Expert+`) with the characteristic in front unless it is Standard
  (`OneSaber Expert+`); after a practice-mode difficulty switch it is the difficulty at the end. Practice-mode runs get ` - Practice` after it (`Expert - Practice`).
  Characters Windows forbids become spaces, the song name is cut to 80 characters, other Unicode is
  kept, and an existing file is never overwritten (` (2)`, ` (3)`...). A file another program still
  has open is retried every 0.5 s for 10 s. If OBS never reports the stop, the path from the
  `StopRecord` response is used after 30 s.
- The path OBS reports is not trusted (whatever answers on the port decides it). It is renamed
  only if it is a full path on a drive of this PC (`C:\...` or `C:/...`) with a recording's
  extension (`.mp4 .mkv .mov .flv .ts .m3u8 .avi .webm .m4v .mpg .mxf`). Network and device paths
  (`\\server\share`, `\\?\`, `\\.\`), relative paths, `.`/`..` parts, alternate data streams and
  device names are refused: the file keeps its name and the log says why
  (`[ObsRecording] not renaming the recording of ...`).
- Restart (end immediately followed by start): the new `StartRecord` is held back until OBS has
  finished the previous file (OBS refuses to start while the output is active), then sent; both
  files are renamed.
- Only a recording the mod started is ever stopped or renamed. If OBS is already recording when a
  level starts (`StartRecord` answers 500), that recording is left alone and the level gets no file
  of its own. A recording of ours that ends before the level does (stopped by hand in OBS, or an
  OBS error) is reported and keeps OBS's name.
- One connection is opened when the game starts (quietly: a failure there is only logged) and kept
  between levels; a dropped connection is reopened at the next level start or end, and in the menu
  within 5 s of OBS being there again (see "OBS status line"). A problem (OBS
  not reachable, WebSocket server off, password rejected, already recording, a refused request) is
  logged and toasted once until something works again, not once per level.
- Quitting the game in the middle of a level: `StopRecord`, then at most 2.5 s for OBS to finish
  the file and for the rename.
- Code: Core `ObsRecording/` (protocol, sign-in, file names, `RecordingCoordinator`; unit tested
  with fakes and against a local fake OBS server), plugin `Features/ObsRecording/` (app-lifetime
  service, player-scene hook, settings host).

## OBS clips

Added 2026-11-18 at the owner's request: short clips of the interesting moments of play, taken from
OBS's replay buffer, at about one clip per two minutes of play.

- OBS's replay buffer holds the last N seconds of video (60 s is what this is built for; the
  owner's choice over judging each moment as it ends with a 10 s buffer). So nothing has to be
  decided on the spot: every 10 s stretch still in the buffer stays a candidate, the best one is
  claimed just before it would scroll out of the buffer (or when the song ends), OBS is asked to
  save the buffer, and the stretch is cut out of the saved file. A better stretch that comes later
  therefore replaces an earlier one.
- Off by default. Needs OBS with its WebSocket server on (as for OBS recording; the same
  `ObsRecording.Host` / `Port` / `Password` apply, OBS's own settings when unset), **Settings >
  Output > Replay Buffer > Enable Replay Buffer** with **Maximum Replay Time** of 20 s or more
  (60 s is best), and `ffmpeg` on PATH (or `ObsClips.FfmpegPath`). Own connection to OBS, so it works
  with OBS recording on or off.
- Replay buffer: started when a level starts and it is not running. A buffer that was already
  running is used and left running; only one the mod started is stopped again (feature switched off,
  or the game quits). Its length is read from OBS's current profile (`Output/Mode`, then
  `RecRBTime` of `AdvOut` or `SimpleOutput`); the mod never changes OBS's settings. Replay buffer
  switched off in OBS, shorter than 20 s, or length unreadable: one toast and log line saying what
  to change, and no clips.
- What makes a stretch interesting (Core `ClipPicker`, scored twice a second):
  - notes: the notes cut correctly in the stretch, times the share of its notes that were hit (bad
    cuts, misses and cut bombs count against it), divided by the song's average stretch so far. 1.0
    is an ordinary stretch on any map at any skill; a burst of fast notes all hit is 2 or more. Under
    5 hits is never a clip.
  - heart rate (optional, `ObsClips.UseHeartRate`, needs the heart rate bridge): up to +1.5 for a
    climb from the start of the stretch to 5 s after its end (the heart lags the effort), 1.0 per
    10 bpm, counting only what is above the average of the last couple of minutes of play. So the
    ramp from rest at the start of a song is not a climb, and a stretch that started in the first
    two minutes of readings of a session gets nothing for its heart rate (the warm-up; 90 s and
    judged-when-evaluated at first, which let a warm-up stretch through in the first VR session).
    Without a strap it is simply 0. One climb gives one clip: after a clip taken for the heart rate,
    stretches within 60 s of it get nothing for theirs (a 45 s climb credited two neighbouring
    stretches in the second VR session).
- Rate: a budget of one clip per `ObsClips.SecondsPerClip` (120) of unpaused play, starting at 1 and
  capped at 2, carried from song to song. A clip costs 1. The bar a stretch must clear falls as the
  budget builds: 1.9 at a budget of 0.25 (the minimum), 1.36 at 1, 1.0 at 1.5, 0.64 at 2. So right
  after a clip only something exceptional is taken, and after a dull spell the best of the last
  minute is. A candidate with better ones still in the buffer must clear the bar of the budget left
  after those. Simulated half hours of flat, bursty, wavy and often-paused play give 11 to 16 clips.
- Not every second in the buffer is play. A stretch is only a candidate when it lies entirely
  inside uninterrupted play of the current song: not before the song started, not across a pause
  (the pause menu and active pause alike), not within 3 s after the start or a resume (the cut keeps
  a little video in front), and not ending within 1 s of a pause. No clip in the first 20 s of a
  song, none overlapping or within 10 s of an earlier clip, none from before the buffer was running.
  A candidate that is about to scroll out while the game is paused is still claimed in time.
- The cut (Core `ClipCut`, plugin `FfmpegCutter`): `ffmpeg -sseof -<seconds from the end> -i <replay>
  -t <length> -map 0:v? -map 0:a? -c copy ...`, counted back from the end of the file because that
  is the moment of the save. A stream copy: no re-encode, a fraction of a second, below-normal
  priority, so the game never notices. A copy can only start on a keyframe, so a clip is the 10 s
  stretch plus 0.5 s either side plus up to one keyframe interval in front (11 to 13 s with OBS's
  2 s keyframes).
- Files: the clip goes next to the replay as `<OBS's replay name> - <song> - <difficulty> - <song
  time> <notes|heart rate>.<ext>` (e.g. `Replay 2026-11-18 20-15-03 - Song - Expert+ - 1m23s
  notes.mp4`; the song time is where the stretch starts). The whole replay is then deleted, unless
  `ObsClips.KeepFullReplay`. If ffmpeg is missing or the cut fails three times, the whole replay is
  kept instead, renamed the same way with ` uncut` at the end (missing ffmpeg: one toast).
- Only a replay the mod asked for is ever cut, renamed or deleted: a replay saved with the player's
  own OBS hotkey is ignored. The file name OBS reports is trusted no more than for OBS recording
  (full local path with a recording's extension, OBS on this PC; otherwise nothing is touched). OBS
  on another PC: the replays stay there uncut.
- One save at a time; a clip OBS did not save costs no budget. `ClipSeconds` (3 to 30, default 10)
  sets the stretch length.
- Feedback: nothing during play. After a song, one toast with the number of clips it gave. Problems
  are toasted once until something works again. Settings: enabled, heart rate, show toasts, and a
  live status line ("Connected to OBS 33.0.1; replay buffer running (60 s)").
- Quitting the game: at most 2.5 s for a save in flight and its cut, then the buffer is stopped if
  the mod started it.
- Code: Core `ObsClips/` (`ClipPicker`, `ClipCoordinator`, `ClipCut`, `SongTimeTrail`; unit tested,
  the picker also by simulation), plugin `Features/ObsClips/` (app-lifetime service, player-scene
  hook, ffmpeg runner, settings host), `Common/Obs/ObsEndpointSource` (where OBS is, shared with OBS
  recording).

## OBS status line

Added 2026-11-20 at the owner's request: see on the song selection screen whether OBS is connected,
and how many recordings and clips were saved since the game started.

- One line of text under the song detail panel (below Practice / Play), e.g.
  `OBS connected   |   3 recordings   |   12 clips`. On by default, but only drawn while OBS
  recording or OBS clips is on; a count is shown per feature that is on (a feature switched off
  mid-session keeps its count on the line if it saved something).
- The word after "OBS": green `connected` when every OBS feature that is on has its connection, red
  `not connected` when none has, amber `connecting` in between (each feature has its own connection;
  a mix lasts a moment). All connected but clips cannot work: amber `replay buffer off`, `replay
  buffer too short`, `replay buffer unknown` or `replay buffer not started` instead (the whole
  sentence is on the settings page and in the toast at the next level).
- Counts are per game session. They live in Core's coordinators, so a feature switched off and on
  keeps its count; the game's internal restart (base game settings, a mod asking for one) rebuilds
  the app container and the coordinators with it (seen with the debug channel's `soft-restart`),
  so each service hands its count to `Common/Obs/ObsSessionTotals` (process lifetime) when it
  goes, and the line shows that plus the current count:
  - recordings: recordings this mod started and stopped, renamed or not. A recording the player
    started is not counted.
  - clips: replays OBS accepted to save for this mod, the same number the "N clips from this song"
    toasts add up to (a clip kept whole because ffmpeg failed still counts).
- For the line to be true in the menu, both OBS features now look for OBS again every 5 s while
  they are on, not connected and no level is running (before this they only connected at game
  start and at a level start, so an OBS started after the game showed as not connected until a
  song was played). OBS clips also looks again while the replay buffer cannot be used. Quiet: no
  toast, one attempt at a time, and one log line per reason instead of one per attempt.
- Placement, measured in the solo screen with BetterSongList installed (the song selection view is
  160 x 80 units of 3 cm, its bottom edge at floor level): the detail panel ends at y -35 and
  BetterSongList's sort/filter row and icons take the bottom of the left half up to x 15, so the
  line takes x 17 to 78 at the height of that row. It shrinks its font (3.4 down to 2.4) rather than
  spill. It is a child of the song selection view (solo, party and multiplayer song lists), so it
  shows, hides and fades with it. Display only: nothing on it takes pointer hits.
- Code: Core `ObsStatus/ObsStatusLine` (the text; unit tested) and `Connected` / `SavedCount` /
  `Trouble` on the two coordinators, `Common/Obs/ObsStatusBoard` (app lifetime; each OBS feature
  publishes its state there once a second, so the line depends on neither feature), plugin
  `Features/ObsStatus/` (the text object, settings host).

## Testing and QA

- Unit tests for all Core logic (xUnit v3, Microsoft.Testing.Platform, coverage ratchet).
- In-game QA by the agent in FPFC desktop mode, including practice-mode runs driven by debug
  commands/hotkeys (Common/ControllerInput has keyboard fallbacks: A=Enter, B=Backspace, X=X, Y=Y,
  right stick = arrow keys). The owner does the final VR playtest from docs/qa-checklist.md.
