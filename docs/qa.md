# In-game QA harness

How agents (and the owner) drive Beat Saber 1.40.8 in FPFC desktop mode to QA MaxTweaks without a
headset. Two halves:

- `scripts/qa/*.ps1` - PowerShell 7 scripts that build/deploy, launch, wait on the log, screenshot,
  send real keyboard/mouse input and close the game.
- A debug command channel inside the mod (`src/MaxTweaks/Common/Debug/`) that reads commands from a
  file and acks them in the game log. It exists only when the game runs with `--debug`.

Run every script with PowerShell 7 (`pwsh -File scripts/qa/<name>.ps1 ...`, or the PowerShell tool),
never from Git Bash. Each script has a usage header. The game instance defaults to
`C:\Users\pmaxh\BSManager\BSInstances\1.40.8` (override with `$env:MAXTWEAKS_BS_DIR`); screenshots
go to `$env:MAXTWEAKS_QA_OUT` or the QA session scratchpad (see `scripts/qa/_common.ps1`).

## Typical session

```powershell
pwsh -File scripts/qa/deploy.ps1                       # build Release, copy dll+pdb to Plugins (game must be closed)
pwsh -File scripts/qa/launch.ps1                       # fpfc --verbose --debug, waits for "event menu-ready"
pwsh -File scripts/qa/cmd.ps1 "play 849a expert practice 20" -WaitFor 'event gameplay-start'
pwsh -File scripts/qa/screenshot.ps1 -Name gameplay    # prints the PNG path
pwsh -File scripts/qa/cmd.ps1 "button Y press 0.5" -WaitFor 'event input Y up'
pwsh -File scripts/qa/cmd.ps1 pause -WaitFor 'event paused'
pwsh -File scripts/qa/screenshot.ps1 -Name paused
pwsh -File scripts/qa/cmd.ps1 exit -WaitFor 'event menu-ready'
pwsh -File scripts/qa/close.ps1
```

## Scripts

| Script | What it does |
|---|---|
| `deploy.ps1` | `dotnet build -c Release` with `BeatSaberDir` set, copies `MaxTweaks.dll`/`.pdb` into `Plugins`, verifies the hash. Refuses while the game runs. |
| `launch.ps1` | Starts `Beat Saber.exe fpfc --verbose --debug` with `SteamAppId=620980` (Steam must be running), waits for the log mirror of this pid to show `[MaxTweaks.Debug] event menu-ready` (about 12 s), prints `pid N`. `-NoDebug` (inertness check; waits on the game log), `-NoFpfc`, `-NoWait`, `-TimeoutSec`. |
| `cmd.ps1 "<command>"` | Appends `#<id> <command>` to the command file and waits for `ok #<id>` (exit 0) or `error #<id>` (exit 2); exit 1 on timeout. Echoes every `[MaxTweaks.Debug]` line meanwhile. `-WaitFor <regex>` then waits for a later line (searched from before the command, so synchronous events are not missed). |
| `wait-log.ps1 -Pattern <regex>` | Tails the MaxTweaks log mirror from its current end (or `-FromStart`) until a line matches. `-TimeoutSec`, `-Echo <substring>`, `-GameLog` (tail `Logs/_latest.log` instead; see below). |
| `screenshot.ps1 -Name x` | `PrintWindow(PW_RENDERFULLCONTENT)` of the game window to `<out>/<timestamp>-x.png` (works when the window is covered). Prints the path. |
| `send-keys.ps1 -Keys W,Y -Action press\|down\|up -HoldMs 300` | Real keyboard input via `SendInput` scan codes after focusing the window. |
| `mouse.ps1 -Action look\|click\|down\|up\|move` | Relative mouse motion (`look -Dx -Dy`, turns the FPFC camera and the UI pointer with it) and clicks. |
| `close.ps1` | `WM_CLOSE`, waits `-TimeoutSec` (20), then kills. |

FPFC controls (SiraUtil): moving the mouse turns the view, left click is a UI click, W/A/S/D/Q/E move
the camera. The UI pointer follows the view: aim with `mouse.ps1 -Action look`, check the hover
highlight with a screenshot, then `mouse.ps1 -Action click`. MaxTweaks keyboard fallbacks for the
controller (Common/ControllerInput): A = Enter, B = Backspace, X = X, Y = Y, right stick = arrow keys.
The game's pause menu also takes C (continue), R (restart), M (menu).

Real keys need the game window focused; `send-keys.ps1` focuses it but Windows can refuse. For
anything a feature reads through `ControllerInput`, prefer the debug channel's `button`/`stick`
commands: they need no focus and are logged.

## Debug command channel

Active only when the game is started with `--debug` (`Environment.GetCommandLineArgs()`); otherwise
`Plugin.cs` does not even install it. With the flag, the log shows
`[MaxTweaks.Debug] enabled; command file ...` at startup. If that line is missing, the deployed DLL
is not this build (another worktree may have deployed over it): run `deploy.ps1` again.

- Command file: `<instance>/UserData/MaxTweaks.debug-commands.txt`, polled every 0.1 s, read and
  truncated under an exclusive lock, one command executed per frame. Stale lines from before the
  game started are discarded.
- Line format: `[#id] <verb> <args...>`; double quotes group words (`"a b"`); `//` or `;` starts a
  comment line. `cmd.ps1` adds the id for you.
- Results: `[MaxTweaks.Debug] ok #id verb: message` or `[MaxTweaks.Debug] error #id verb: message`.
  `ok` means accepted; asynchronous results arrive as `event` lines.
- Every log line of the channel starts with `[MaxTweaks.Debug]` and is ASCII-only.

### Log mirror (read this, not `_latest.log`)

BSIPA opens `Logs/_latest.log` with `FileShare.None` for every print batch. Any reader holding the
file open at that moment makes BSIPA drop the batch (`printer errored: Sharing violation` on its
console) - this lost acks in early testing. So with `--debug` the mod mirrors every MaxTweaks log line
(the plugin logger and its children: features, Guard, the channel) to
`<instance>/UserData/MaxTweaks.debug-log.txt`, opened with read/write/delete sharing. The file is
recreated each run and starts with `[MaxTweaks.Debug] session pid=<pid> started=...`. All scripts
read the mirror. `wait-log.ps1 -GameLog` exists for other mods' lines; it only opens the game log
after its size changed, but avoid it during anything you are measuring, and do not tail `_latest.log`
with other tools (`Select-String`, `Get-Content -Wait`) while the game runs.

### Commands

| Command | Where | Notes |
|---|---|---|
| `help` | any | Lists the commands available right now. |
| `log <text>` | any | Echo, for synchronising with the log. |
| `status` | any | `scene=menu\|gameplay\|transition`, loaded scenes, songs loaded, fpfc, virtual input; in the menu the active flow coordinator; in gameplay level, difficulty, practice settings, song time, paused. |
| `button <A\|B\|X\|Y> <down\|up\|press> [seconds]` | any | Virtual controller button through `ControllerInput`, so features see a real press (edges included). `press` holds 0.15 s by default; `down` holds until `up` (or for `seconds`). |
| `stick <x> <y> [seconds]` | any | Virtual right thumbstick in [-1, 1] (y > 0 = forward). Held until changed (`stick 0 0`) unless `seconds` is given. |
| `release-all` | any | Releases every virtual button and the stick. |
| `play <level> <difficulty> [characteristic] [practice <start> [speed]]` | menu | Starts a level via `MenuTransitionsHelper.StartStandardLevel` with the saved player settings (works from any menu screen). Practice start is seconds or `m:ss`, speed 0.5-2. On exit you return to the screen you started from. |
| `select-level <level> <difficulty> [characteristic]` | menu | Opens Solo (from the main menu) or uses the open solo list, selects the level and difficulty without starting it. Then `event level-selected ...` or `event level-select-failed ...`. |
| `levels [text]` | menu | Lists matching levels (`level <id> (<folder or song>)` lines, max 50). |
| `open-settings` | main menu | Opens Mod Settings and clicks the MaxTweaks entry (same path as a pointer click). Then `event settings-opened` (the MaxTweaks view controller is active; says nothing about whether its content rendered - check a screenshot) or `settings-open-failed`. |
| `main-menu` | menu | Dismisses child flow coordinators back to the main menu. |
| `pause` / `resume` | gameplay | Same code paths as the pause trigger / Continue button. `resume` plays the countdown; wait for `event resumed`. |
| `restart` | gameplay | Restarts the level (practice settings kept). |
| `exit` | gameplay | Returns to the menu (via the pause menu path if paused). |

Level queries match, first tier wins: exact level id, exact folder name, exact song name, folder
substring, song-name substring (case-insensitive). Ambiguous queries fail and list candidates. Base
game ids such as `Sandstorm` win over a custom map whose song is also called Sandstorm, so prefer the
custom folder's short code (e.g. `849a`). Difficulties: `easy normal hard expert expertplus` (also
`e+`, `ex+`, `x+`). Characteristic: serialized name, default `Standard` (`OneSaber`, `NoArrows`,
`360Degree`, `Lawless`, ...).

### Events

| Line | When |
|---|---|
| `event scenes <Push\|Pop\|Replace...>: [names]` | Every finished scene transition. |
| `event songs-loaded custom=N` | SongCore finished (re)loading songs. |
| `event menu-ready` | Menu scene up, no transition, songs loaded. Logged again each time you return to the menu. |
| `event gameplay-start level=... diff=... practice=... time=... paused=...` | Player container initialised (each start and restart). |
| `event paused ...` / `event resumed ...` | Game pause state changes, with song time. |
| `event gameplay-end` | Player container disposed (exit, restart, finish). |
| `event level-finished state=... action=...` | A level started by `play` ended. |
| `event input <A\|B\|X\|Y> down\|up`, `event input stick x,y` | Any `ControllerInput` edge: virtual, FPFC keyboard fallback or a real controller. |

### Adding a test hook for a feature

Feature agents must not depend on `Common/Debug`. To observe a feature, log a distinctive line from
the feature itself through `Plugin.Log` (or a SiraLog/child logger of it), e.g. `[Blink] overlay
shown`; it lands in the mirror, so wait for it with `cmd.ps1 -WaitFor` or `wait-log.ps1`. Input for features goes through `ControllerInput`, which the channel already drives.

## Known limits

- One game instance and one QA driver at a time: the command file and screenshots are shared.
- `play` does not go through the solo screen, so it shows no results screen after finishing; use
  `select-level` + a real click on Play/Practice when the solo flow itself is under test.
- `open-settings` needs the main menu (`main-menu` first). `select-level` from another flow
  (campaign, party, multiplayer) is refused.
- `send-keys.ps1` and `mouse.ps1` need the window in the foreground; screenshots do not.
- Multiplayer is not supported by the gameplay commands.
- A virtual `button X down` stays held across scene changes until `button X up` or `release-all`.
- Each script compiles its Win32 helper with Add-Type on start (about 1-2 s per call).
- `main-menu` dismisses flow coordinators only, not a view controller presented on the main menu
  itself (e.g. Player Options).
