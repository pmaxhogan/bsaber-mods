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

- [ ] TODO

## Auto-reload maps

- [ ] TODO

## Blink

- [ ] TODO

## Chapters

- [ ] TODO

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

## Regression and wrap-up

- [ ] A normal (non-practice) full song with every feature enabled plays with no stutter, missed notes or score anomalies.
- [ ] Turning every feature off restores vanilla behaviour.
- [ ] Other mods still work (BetterSongList filters, PracticePlugin, IntroSkip, BeatLeader/ScoreSaber upload of a normal play).
- [ ] Game quits cleanly; `UserData/MaxTweaks.json` keeps your settings.
