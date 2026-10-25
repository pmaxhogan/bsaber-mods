# MaxTweaks VR playtest checklist

Final owner playtest in the headset (Quest via PC VR), after the agent's FPFC QA (docs/qa.md).
Tick each box; note anything odd next to it. Spec: docs/maxtweaks-spec.md.

## Before you start

- [ ] Game is 1.40.8 and `Plugins/MaxTweaks.dll` is the build under test (Mod Settings > MaxTweaks shows the version you expect; the log has `MaxTweaks (MaxTweaks): <version>`).
- [ ] Launched normally, WITHOUT `--debug`: no `[MaxTweaks.Debug]` lines in `Logs/_latest.log`.
- [ ] No `[MaxTweaks]` errors in the log after reaching the menu.
- [ ] Mod Settings > MaxTweaks opens; one tab per feature; status line empty (no feature disabled after an error).
- [ ] Quick tab under the song preview shows the on/off switches and they persist after a restart.

## Hide OSTs

FPFC QA covered the list logic (docs/qa-results.md); in VR check what only a headset shows.

- [ ] Solo: the white music-pack icon right of the category tabs is easy to hit with the saber pointer (no misses onto the All tab next to it), and its hover hint is readable.
- [ ] Trigger-click it: tabs collapse to Custom/Favorites/All, icon turns red, the list stays on your pack with no flicker or spinner. Click again: Music Packs is back.
- [ ] All tab with hiding on: the red icon does not cover the search field; point at the search field and type "Escape" with the VR keyboard: no results. Clear it.
- [ ] Known bug check: open a PlaylistManager playlist that contains OST songs with hiding on. Expected only its custom maps (FPFC QA saw the OST songs still listed).
- [ ] Party mode: no icon, OSTs visible.
- [ ] Quit and relaunch: the hidden state is kept.

## Auto-reload maps

- [ ] Keep the headset on, Solo > Custom, scroll down and select a song. From the desktop (or a phone/remote tool) copy a new map folder into CustomLevels: within about 2-3 s a small "1 map added" toast appears; it is readable and does not cover anything you need.
- [ ] After that reload the list is still scrolled to the same place AND the selected song's details with Play/Practice are still shown (FPFC QA saw the right panel switch to the pack card - note whether you have to re-click the song).
- [ ] Edit that map's Info.dat (e.g. song name): "1 map updated", the list shows the new name, Play starts it.
- [ ] Start that map, and while playing save a change to it on the desktop: a toast "Map updated on disk: <name> - reloads when you exit" appears in front of you, readable, not over the notes, gone after a few seconds; no stutter or missed frames.
- [ ] Exit to the menu: the change is loaded without a SongCore loading banner; play again and the new version is used.
- [ ] Delete the test folder: "1 map removed". Delete your test map afterwards.

## Blink

Use Cooldown 15 s for testing (Settings > MaxTweaks > Blink), set it back to 5 min afterwards.
Good test song: "InfernoSaber - Lilly Palmer - Hare Ram", Expert, practice from 1:20 (blink at ~1:40).

- [ ] The blink starts about 3 s after the last note before a long break, never while notes are coming.
- [ ] Comfort: the vignette fades in and out smoothly (no pop), darkens the edges in BOTH eyes equally, the centre stays clear, no stereo mismatch or double image.
- [ ] Vignette is actually visible (FPFC screenshots could not confirm it): corners noticeably darker at strength 0.6; try 1.0 and 0.3 to compare.
- [ ] Closed-eye icon: sits a little below eye level, readable, head-locked (follows your head without lag or swimming), not hiding incoming notes.
- [ ] Whole effect lasts about 3 s; pausing during it hides it at once; resuming does not bring it back mid-fade weirdly.
- [ ] A wall passing beside you during a break does not prevent the blink; a wall coming at your head does.
- [ ] Two blinks never come closer than the cooldown, also across two consecutive songs.
- [ ] Not shown in multiplayer or when watching a replay.

## Chapters

Make a chapters.json for a map you know (tools/ChaptersFromBookmarks or by hand, docs/chapters.md),
with at least two overlapping chapters and one long one.

- [ ] Normal play: chapter NAMES are shown above the track (FPFC QA saw only the bars, no names - this is the first thing to check), one line each, longest at the bottom, readable at a glance without looking up much.
- [ ] Nothing overlaps the clock, HRCounter or other HUD mods badly; the text does not shimmer or swim.
- [ ] Progress bars move smoothly; Settings > Show progress bars off removes them.
- [ ] Normal play shows no < > arrows. Practice mode shows them.
- [ ] Practice, while playing: point the right saber at > and press A: jumps to the chapter end, notes fly in normally (no notes popping up in front of you). Same with the left saber pointing.
- [ ] Pointing with the saber while slashing does not trigger anything without A; the pointed arrow highlights clearly (gold, bigger).
- [ ] < jumps back to the chapter start; a second > on the same chapter leaves it.
- [ ] Pause menu: point with the menu pointer at an arrow and pull the trigger: it jumps; press Continue and play resumes near the target (PracticePlugin does not jump back).
- [ ] After any jump the song does not submit a score.

## Difficulty switch (practice mode)

- [ ] Practice a plain map with 3+ difficulties: holding Y shows the list in front of you (hardest on top, current one gold with "(now)"); releasing Y without moving does nothing.
- [ ] Right stick forward/back moves the highlight one row per flick with a slide; holding the stick does not repeat; it stops at the top/bottom.
- [ ] Release on another difficulty: the music never stops or stutters, notes in flight slide up (harder) or down (easier) and vanish, nothing appears for about 3 s, then the new difficulty continues at its own note speed/offset. Score and combo keep their values.
- [ ] Same from the pause menu: hold Y while paused, pick, release; on Continue the new difficulty plays after the gap.
- [ ] Switch back and forth a few times in one song; no double notes, no notes popping up close to you, lights keep reacting.
- [ ] Settings: Clear gap 0 / 6 s and Slide old notes off behave as described.
- [ ] A map that only suggests Chroma switches in place and keeps its Chroma light/note colours on the new difficulty.
- [ ] A map that requires Noodle/Chroma/ME, or a 360 map: the switch restarts the level at the same time on the new difficulty (short audio break, toast "Restarting on ...").
- [ ] After an in-place switch, the pause menu shows the new difficulty and its Restart restarts the new difficulty.
- [ ] After any switch, finishing the song does not submit a score (BeatLeader/ScoreSaber show no new score).
- [ ] Chapters on a map with chapters.json: after a switch the chapter names follow the new difficulty.

## Active pause (practice mode)

- [ ] Practice a map mid-pattern, press X: the song stops instantly, no pause menu, sabers stay visible, notes/walls/arcs/chains freeze and turn ghostly (mostly transparent but still readable); the 12 buttons appear low on the right and are readable and comfortable to reach without hunting.
- [ ] Swing through the frozen notes and walls: nothing is cut, no miss, combo and score unchanged, no haptic buzz.
- [ ] Point the right (then the left) saber at a button: it turns gold and grows; A steps. Try +1s, -5s, +1b, -.25b: the frozen notes jump to the new spot at once and look right for that point in the song.
- [ ] Beat steps feel locked to the music: +1b several times lands each time on a beat (the notes on the beat sit at the same depth each step); +.25b moves by quarter beats.
- [ ] -1m near the start goes to 0:00; +1m near the end stops just before the end and the level does not finish.
- [ ] X again: play resumes immediately from the frozen spot, notes un-ghost; nothing feels like it hit you in the face before you could react (if it does, note it).
- [ ] While active-paused, hold Y: the difficulty list works, switching works; on resume the new difficulty plays after its gap.
- [ ] While active-paused on a map with chapters.json: the chapter arrows highlight with the saber and A jumps.
- [ ] While active-paused press the menu button: the normal pause menu opens (buttons hidden, notes hidden); Continue resumes normally with the countdown. Then X / X still works.
- [ ] A long active pause never triggers Blink.

## Regression and wrap-up

- [ ] A normal (non-practice) full song with every feature enabled plays with no stutter, missed notes or score anomalies.
- [ ] Turning every feature off restores vanilla behaviour.
- [ ] Other mods still work (BetterSongList filters, PracticePlugin, IntroSkip, BeatLeader/ScoreSaber upload of a normal play).
- [ ] Game quits cleanly; `UserData/MaxTweaks.json` keeps your settings.
