# MaxTweaks

Personal quality-of-life mod for Beat Saber PC (BSIPA), built for game version **1.40.8**.
Open source, but designed for one player's setup.

## Features

Every feature has its own on/off switch in **Mod Settings > MaxTweaks** and in the MaxTweaks tab
under the song preview.

- **Hide OSTs** - one-click icon next to the solo song list's category tabs hides every official
  song (OST, Extras, DLC) from the packs, the All list, search and playlists.
- **Auto-reload maps** - watches the custom map folders (native Windows file notifications) and
  reloads added, edited or deleted maps in place without Ctrl+R, keeping your place in the list.
  Never reloads mid-song; shows a toast if the map you are playing changed on disk.
- **Blink** - in long note-free, wall-free stretches, a soft vignette and closed-eye icon remind you
  to rest your eyes (at most once every 5 minutes of play; all thresholds configurable).
- **Chapters** - a `chapters.json` next to a map's `Info.dat` defines labelled (possibly
  overlapping) sections, shown above the notes with progress bars. In practice mode, point at
  `<` / `>` and press A to jump to a chapter's start or end. See [docs/chapters.md](docs/chapters.md);
  `tools/ChaptersFromBookmarks` generates a starter file from a map's editor bookmarks.
- **Difficulty switch (practice mode)** - hold Y to show the difficulty list, flick the right
  stick to pick, release Y to switch. The notes are swapped in place without stopping the music;
  maps that need Noodle/Mapping Extensions/360 fall back to a restart at the same time. Flick the
  stick left/right instead to pick a note jump speed (your difficulty's own is underlined) and
  release Y to change only the NJS, in place, until the song ends or restarts.
- **Active pause (practice mode)** - press X to freeze the song in place without the pause menu;
  notes turn ghostly and twelve buttons (-1m ... -.25b / +.25b ... +1m, b = beats, snapped to the
  beat grid) let you step through the map with a saber and A or the trigger. Press X again to play
  on from there. Point at frozen notes to select them and press Flag to save feedback (labels like
  "Too dense", the selected notes, a screenshot) to `flags.json` next to the map - see
  [docs/flags.md](docs/flags.md).
- **Heart rate bridge** (off by default) - starts [tools/hr-osc-bridge](tools/hr-osc-bridge) with
  the game and keeps it running: your Bluetooth chest strap (Wahoo TICKR, Polar, Garmin...) is read
  through the PC's own Bluetooth and fed to the [HRCounter](https://github.com/qe201020335/HRCounter)
  mod over OSC. No phone app. Needs HRCounter on its "OSC Protocol" source and
  [uv](https://docs.astral.sh/uv/) installed.

## Install

Requires BSIPA, SiraUtil, BeatSaberMarkupLanguage and SongCore (all standard). Download the zip
from the latest [GitHub Release](https://github.com/pmaxhogan/bsaber-mods/releases) and extract it
into the game folder (it adds `Plugins/MaxTweaks.dll` and `UserData/MaxTweaks/hr_osc_bridge.py`).

## Development

See [CLAUDE.md](CLAUDE.md) for commands and layout, [docs/maxtweaks-spec.md](docs/maxtweaks-spec.md)
for the feature spec, [docs/qa.md](docs/qa.md) for the desktop-mode (FPFC) QA harness and
[docs/qa-checklist.md](docs/qa-checklist.md) for the VR playtest checklist.

## License

MIT
