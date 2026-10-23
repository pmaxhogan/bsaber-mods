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

- [ ] TODO

## Regression and wrap-up

- [ ] A normal (non-practice) full song with every feature enabled plays with no stutter, missed notes or score anomalies.
- [ ] Turning every feature off restores vanilla behaviour.
- [ ] Other mods still work (BetterSongList filters, PracticePlugin, IntroSkip, BeatLeader/ScoreSaber upload of a normal play).
- [ ] Game quits cleanly; `UserData/MaxTweaks.json` keeps your settings.
