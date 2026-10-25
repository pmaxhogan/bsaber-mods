# flags.json - map feedback from active pause

MaxTweaks writes `flags.json` next to a custom map's `Info.dat` when the player saves a flag in
active pause (practice mode): a moment of the map, feedback labels and, optionally, the notes the
player selected. The file is meant to be read by tools (e.g. an automapper, for training and
evaluation). The mod only ever appends; edit or delete flags by editing the file.

## Format

```json
{
  "version": 1,
  "levelId": "custom_level_<hash>",
  "songName": "...",
  "flags": [
    {
      "id": "20261025-193012-7f3a",
      "createdAt": "2026-10-25T19:30:12Z",
      "characteristic": "Standard",
      "difficulty": "Expert",
      "songTime": 42.317,
      "beat": 96.0,
      "speed": 1.0,
      "labels": ["too_dense", "awkward_hands"],
      "notes": [
        { "kind": "note", "beat": 96.0, "time": 42.317, "lineIndex": 1, "lineLayer": 0,
          "color": "red", "cutDirection": "down" }
      ],
      "screenshot": "flags/20261025-193012-7f3a.png"
    }
  ]
}
```

| Key | Meaning |
|---|---|
| `version` | Format version, `1`. |
| `levelId`, `songName` | The level as the game knows it (written when the file is created). |
| `id` | `yyyyMMdd-HHmmss-xxxx`: UTC save time and 4 random hex digits. Unique per file in practice. |
| `createdAt` | UTC, ISO 8601, seconds. |
| `characteristic`, `difficulty` | The difficulty being played (`Standard`, `Lawless`, ...; `Easy` ... `ExpertPlus`). |
| `songTime` | Song seconds of the flagged moment (3 decimals). |
| `beat` | Map beat at `songTime`, BPM changes included (the tempo map Chapters uses). Without a usable BPM: `songTime * 2`. |
| `speed` | Practice speed multiplier. |
| `labels` | Ids from the list below, in panel order. May be empty. |
| `notes` | Selected notes, in selection order. May be empty. A flag with no labels and no notes is a plain bookmark. |
| `screenshot` | Path relative to the map folder (`flags/<id>.png`; the VR mirror view, or the desktop view in FPFC), or `null`. Unity writes the image at the end of the frame, so a listed file can be missing if that failed. |

Note fields: `kind` is `note`, `bomb`, `chain_head` or `chain_link`; `beat` / `time` as above for
the note itself; `lineIndex` 0-3 (left to right) and `lineLayer` 0-2 (bottom to top) as in the map
file; `color` is `red`, `blue` or `none` (bombs); `cutDirection` is `up`, `down`, `left`,
`right`, `up_left`, `up_right`, `down_left`, `down_right`, `any` or `none`.

## Labels

| Group | Id | Button |
|---|---|---|
| Good | `good_pattern` | Good pattern |
| Good | `good_difficulty` | Good difficulty |
| Good | `good_timing` | Good timing |
| Good | `good_flow` | Good flow |
| Good | `fun` | Fun |
| Bad | `too_easy` | Too easy |
| Bad | `too_hard` | Too hard |
| Bad | `timed_poorly` | Timed poorly |
| Bad | `impossible_pattern` | Impossible pattern |
| Bad | `bad_flow` | Bad flow |
| Bad | `awkward_hands` | Awkward hands |
| Bad | `too_dense` | Too dense |
| Bad | `too_sparse` | Too sparse |
| Note | `repetitive` | Repetitive |
| Note | `surprising` | Surprising |
| Note | `mismatches_music` | Doesn't match the music |
| Note | `check_other_difficulties` | Check other difficulties |

## Rules for readers and writers

- Keep keys you do not know, at every level: the mod writes back every existing flag exactly as it
  read it and keeps unknown top-level keys, and other tools should do the same.
- Ids, label ids and vocabularies above are stable; new ones may be added.
- Writes are whole-file: the mod writes `flags.json.tmp` and swaps it in atomically (replace, or move for a new file). A file
  that does not parse is renamed to `flags.json.corrupt-<yyyyMMdd-HHmmss>` and a new one started.
- `flags.json`, `flags.json.*` and the `flags/` folder never count as a map change for Auto reload.
