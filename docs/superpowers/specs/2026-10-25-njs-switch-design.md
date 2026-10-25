# Difficulty switch: NJS row (hold Y, stick left/right)

Approved by the owner in chat on 2026-10-25 ("Always from the default is fine, go ahead").
Decisions marked "(call)" are the designer's.

## Intent

While practising, change the Note Jump Speed (NJS) of the running song with the same gesture as
the difficulty switch, to feel a pattern at a different speed without leaving the song.

## Behaviour

- Hold Y (the difficulty list appears as today). Flick the right stick LEFT or RIGHT: a horizontal
  row of NJS values appears under the difficulty list; the vertical list keeps working with
  forward/back. One flick moves the highlight one value; holding the stick does not repeat
  (reuse the vertical list's edge/smoothing logic). The row stops at both ends.
- Values: integers 8 to 30 (call). The row is centred on the current difficulty's OWN NJS (the
  value the game computed for this difficulty with the active modifiers, i.e. what
  `InPlaceSwapper.GetNoteJumpValues` returns), which is underlined; the highlight starts on the
  value in effect now (the default, or the last chosen NJS of this song). If the default is not an
  integer or lies outside 8..30 it is inserted in the row at its sorted place (call).
- Release Y with the row open on a value: the NJS changes in place, the song keeps playing, notes
  already in flight finish at the old speed, newly spawned notes use the new one; no clear gap,
  nothing disappears (reuse `InPlaceSwapper.ReinitSpawning` and `PlaybackSeeker.SyncPracticePluginNoteJump`
  with the current difficulty's `NoteJumpValueType`/offset so PracticePlugin never fights it).
  Toast "NJS 18 (default 16)" (call). Release with the row open on the current value: nothing.
- Release Y with the vertical list on another difficulty: difficulty switch as today; the NJS of the
  new difficulty is its own default (the chosen NJS is NOT carried over).
- The chosen NJS is forgotten when the song ends or restarts (always from the default; owner).
- Works while active-paused exactly like the difficulty list (the change applies on release; the
  frozen objects keep their positions; objects spawned after resume use the new speed).
- Noodle/ME/360 maps: NJS change is still in place (it does not need a restart) (call). If
  `ReinitSpawning` is not possible for the song (whatever makes the difficulty switch fall back),
  the row is not shown and a toast says "NJS switch is not available on this map" once.
- Settings: none new; the feature is part of Difficulty switch and follows its Enabled flag.

## As built (deviations, 2026-10-25)

- Notes already in flight do NOT keep the old speed: every note reads the shared jump timing each
  frame, so on a change they move at once to where the new speed puts them and still land on their
  beat (PracticePlugin's own slider and the difficulty swap behave the same). Frozen notes in active
  pause may therefore shift on the frame of the change.
- PracticePlugin's update path is not called (it re-runs the spawn-controller setup, which
  duplicated Chroma environment objects in earlier QA); its mechanism is copied, then its NJS value
  and pause panel are written so the slider shows the new value. Its pause-time record is updated
  so Continue does not jump back.
- Only Noodle Extensions maps block the row (toast once per song); Mapping Extensions and 360 maps
  change in place. An NJS change disables score submission for the song. The first sideways flick
  opens the row and moves one value.

## Constraints

- Core (no Unity): the row model (values, default insertion, highlight movement, underline index)
  in `src/MaxTweaks.Core/DifficultySwitch/NjsRow.cs` with xUnit tests. Input edge handling reuses
  `SwitchInputMachine` (extend it with a horizontal axis, keeping its vertical behaviour and tests
  intact).
- Everything inside game callbacks under `Guard`; a failure logs and never stops the song.
- ASCII only, LF, Conventional Commits with the session trailers, 0 warnings.
- Log lines: `[DifficultySwitch] njs 16 -> 18 (default 16)`; status in the debug channel shows
  `njs=<current>/<default>`.
- Docs: spec section in docs/maxtweaks-spec.md (Difficulty switch), README bullet, qa-checklist
  items, docs/qa.md (debug: the `stick` verb with x values already exists; add the log line).

## Left to the builder

- Exact row placement, font and the underline (a thin bar under the default value is fine).
- How many values are visible at once (a window of 7 to 9 around the highlight is fine).
