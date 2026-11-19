# OBS clips: automatic clips from the replay buffer

Requested by the owner in chat on 2026-11-18. The owner decided two things ("owner"); everything
marked "(call)" is the designer's and was not put to the owner first, who said to be flexible on
implementation. The built behaviour is in docs/maxtweaks-spec.md ("OBS clips").

## Intent

Come away from a session with a handful of short clips of the good moments without thinking about
it: start OBS's replay buffer if it is not running, and save a clip of about 10 s whenever something
interesting happened, at roughly one clip per two minutes of play (owner). Interesting, in the
owner's words: hitting a bunch of notes in rapid succession, a heart rate that is rising quickly, or
the hardest/fastest part of the last 60 s, "or something like that".

## Goals, non-goals, anti-goals

- Goal: clips that show a stretch the player would call the best of that minute, with no menu or
  pause footage in them, at about the asked rate.
- Goal: no attention needed during play (no prompts, no mid-song toasts) and no cost to the game.
- Non-goal: editing, uploading, montage, or choosing clips after the session.
- Non-goal: changing OBS's settings for the player (the mod reads them and says what to change).
- Anti-goal: touching video files the mod did not ask OBS for, or a replay buffer it did not start.

## Approaches

1. Judge each moment as it ends, with the owner's 10 s buffer: save when the 10 s that just ended
   beat the previous 60 s. Simple, no cutting. But the decision is final the moment it is made: a
   better stretch a few seconds later finds the budget spent, and "the hardest part of the last
   60 s" cannot be honoured, only guessed.
2. **Chosen (owner, 2026-11-18: "build the 60s buf cut idea"):** a 60 s buffer, pick in hindsight,
   cut the best 10 s out of the saved replay. A stretch is claimed only just before it would leave
   the buffer, so a better later one replaces it. Costs a cutting step (ffmpeg) and a 60 s buffer in
   OBS (the owner set it).
3. Plan from the map: precompute the densest sections of the beatmap and schedule saves. Rejected
   (call): it rewards what the map contains, not what the player hit, and practice seeks, speed
   changes and the mid-song difficulty switch all break the schedule.

## Decisions

- A separate feature "OBS clips" with its own Enabled flag (off by default) and its own connection
  to OBS, sharing only the address settings and the Core protocol code with OBS recording (call):
  the two stay independent, as the repo's conventions ask.
- One score for "rapid succession" and "hardest part": notes hit in the stretch times the share
  hit, relative to the song's own average (call). Relative, so an Easy map and an Expert+ map both
  produce clips, and misses count against a stretch.
- Heart rate adds to the same score instead of being a second trigger, and only a climb above the
  recent average counts (call): otherwise every song's warm-up is a clip.
- Rate by a budget that builds with unpaused play and a bar that falls as the budget builds (call):
  the long-run rate cannot exceed the target, and a dull spell lowers the bar instead of forcing a
  clip on a timer.
- Cut by stream copy, never re-encode (call): a re-encode in the middle of a VR session would cost
  frames. The price is a clip that starts up to one keyframe interval early.
- Delete the whole replay after a successful cut (call; `KeepFullReplay` turns it off): 60 s
  replays at recording quality add up to gigabytes per hour.
- The replay buffer is started at level start, not at game start, and a buffer the mod started is
  stopped when the game quits (call): nothing runs in OBS for someone only browsing songs, and
  nothing is left running afterwards.
- Replays being watched (BeatLeader/ScoreSaber) are treated like play (call), as OBS recording does.
- No toast per clip; one after the song (call).
- A debug-channel verb `clip` drives the save path for FPFC QA, where no notes are hit.

## Left to verify in VR

The picker's constants (bar, guard times, heart-rate weights) were tuned on simulated play only. The
checklist in docs/qa-checklist.md ("OBS clips") is the owner's pass over real clips.
