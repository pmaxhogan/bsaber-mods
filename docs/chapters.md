# Chapters

Named sections of a song, shown above the notes while you play. In practice mode you can jump to the
start or end of a chapter.

## chapters.json

Put a `chapters.json` next to the map's `Info.dat`:

```json
{
  "chapters": [
    { "start": "0:12.5", "end": "0:45", "label": "Intro" },
    { "start": 45, "end": 90.25, "label": "Verse 1" },
    { "startBeat": 64, "endBeat": 128, "label": "Drop", "difficulties": ["Expert", "ExpertPlus"] },
    { "start": "2:30", "label": "Outro" }
  ]
}
```

Each chapter:

| Field | Meaning |
|---|---|
| `start`, `end` | Song time: a number of seconds (`45`, `90.25`) or text `"ss(.fff)"`, `"m:ss(.fff)"` or `"h:mm:ss(.fff)"`. |
| `startBeat`, `endBeat` | Position in beats instead of time. Converted with the map's tempo, including BPM changes of the played difficulty (the same conversion the game uses for the notes), else the Info.dat BPM. |
| `label` | Name shown on screen. Optional; defaults to `Chapter N` (N = position in the file). |
| `difficulties` | Optional. Limits the chapter to these difficulties; otherwise it applies to all. Entries: a difficulty (`"Hard"`, `"ExpertPlus"`, `"Expert+"`), a characteristic (`"Lawless"`), or both (`"Standard/ExpertPlus"`, `"Lawless:Hard"`). Case, spaces, `-` and `_` are ignored. A single string is accepted too. |

Rules:

- Use either `start`/`end` or `startBeat`/`endBeat` in one chapter, not both.
- The end is optional: a chapter without an end runs to the end of the song.
- The end must be after the start and the chapter must be at least 1 second long. Ends past the song
  end are clamped to it; chapters starting after the song ends are dropped.
- Chapters may overlap. Overlapping chapters stack on screen, the longest at the bottom.
- A bare array (`[ {...}, {...} ]`) instead of `{ "chapters": [...] }` is accepted. Property names are
  case-insensitive.
- Bad entries are skipped and logged (`[Chapters]` lines in `Logs/_latest.log`), never an error in game.

## In game

- All modes: one line per active chapter, `<  Name  >`, above the track, starting 4 m up and 7 m
  ahead (above the default spots of clock mods and HRCounter; the height is the `BottomCenter`
  constant in `Features/Chapters/ChaptersDisplay.cs`). A chapter keeps its line while it is active;
  lines only move down when a chapter below ends, and never while you are aiming at an arrow.
  Each line has a thin progress bar under the name (Settings > MaxTweaks > Chapters > Show progress bars).
  Outside practice mode the arrows are hidden.
- Practice mode only: point at `<` (jump to the chapter start) or `>` (jump to its end) and press A.
  While playing, point with either saber; in the pause menu, point with the menu pointer and press A
  or simply click with the trigger. The pointed arrow lights up.
- A jump starts the audio a moment (the note spawn lead time, about 1-2 s) before the target so the
  first notes fly in normally; notes before the target are skipped. So right after a `>` the chapter
  is still shown for that moment; pressing `>` again during it jumps straight to the chapter end
  (no lead-in), so repeated `>` always moves on. Any jump disables score submission for the rest of
  that song.
- Desktop (FPFC) testing: look at the arrow with the mouse and press Enter.

## Generating a starter file from editor bookmarks

`tools/ChaptersFromBookmarks` turns the bookmarks mappers leave in the editor into chapters: each
bookmark starts a chapter and the next bookmark ends it; the last chapter has no end (runs to the song
end). Supported: v2 `_customData._bookmarks` (and the legacy top-level `_bookmarks`), v3 `customData.bookmarks`, v4 official-editor files in
`Bookmarks/` (matched by their own `characteristic`/`difficulty` fields).

```
dotnet run --project tools/ChaptersFromBookmarks -c Release -- "<map folder>"            # writes <map folder>/chapters.json
dotnet run --project tools/ChaptersFromBookmarks -c Release -- "<map folder>" --stdout   # print only
dotnet run --project tools/ChaptersFromBookmarks -c Release -- "<map folder>" --out x.json --force
```

- An existing output file is never overwritten without `--force`.
- If every difficulty with bookmarks has the same list, the chapters get no `difficulties` filter (so
  they also show on difficulties without bookmarks). Otherwise each distinct list becomes its own set of
  chapters limited to its difficulties, and difficulties without bookmarks get none.
- Exit codes: 0 written, 1 usage, 2 map unreadable (missing/invalid Info.dat or a difficulty file
  that is not valid JSON), 3 no bookmarks, 4 output exists (nothing
  written; the check and the write are one atomic create), 5 write failed (folder missing, no
  permission, file locked).
- Then edit the file by hand: rename, merge, overlap (e.g. add one long "Second half" chapter).
