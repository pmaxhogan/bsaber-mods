# hr-osc-bridge

Feeds a Bluetooth heart-rate strap (Wahoo TICKR, Polar, Garmin, anything with the standard
BLE Heart Rate service) into the HRCounter mod through the PC's own Bluetooth adapter. No phone,
no Pulsoid or HypeRate account. The strap's readings are sent as OSC `/hr` int32 messages to
HRCounter's built-in OSC server.

## HRCounter settings (UserData/HRCounter.json or Mod Settings > HRCounter)

- `DataSource`: `OSC Protocol`
- `EnableOscServer`: true, `OscPort`: 9000, `OscAddress` contains `/hr` (all defaults)

## Use

1. Put the strap on (wet the electrodes; it only advertises while worn) and make sure no phone
   app (Wahoo, Zwift, ...) is connected to it. Do not pair it in Windows Bluetooth settings.
2. Double-click `start-hr-bridge.cmd` (needs `uv`; it fetches Python 3.13 and the two
   dependencies on first run). It scans, connects to the strongest strap, prints every reading
   (about one a second) and reconnects by itself if the strap sleeps or drops.
3. Start Beat Saber. Order does not matter; OSC is fire-and-forget.

Options (`uv run hr_osc_bridge.py --help`): `--name TICKR` to prefer a strap by name,
`--port`/`--address` to match a changed HRCounter config, `--scan` to list straps,
`--fake 72` to send a constant value without Bluetooth (verifies the HRCounter side alone) and
`--fake-sweep 90 210` to ramp between two values (one up-and-down cycle per `--sweep-seconds`,
default 60; shows the whole range of the MaxTweaks heart rate graph).

MaxTweaks can start this for you: turn on "Heart rate bridge" in Mod Settings > MaxTweaks and the
mod runs the copy at `UserData/MaxTweaks/hr_osc_bridge.py` with the game, stops it with the game,
shows connect/disconnect toasts and draws its heart rate graph from the printed readings. Only one bridge runs at a time: a second copy (for example this
launcher while the game runs it) exits at once with code 3.

Right after an abrupt stop (game killed), the first reconnect can fail with "Characteristic
00002a37 was not found" because Windows still holds the old connection; the script retries by
itself a few seconds later.

Python 3.13 is pinned because bleak's Windows backend needs prebuilt WinRT wheels that lag new
Python releases.
