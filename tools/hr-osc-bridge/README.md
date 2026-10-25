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
   dependencies on first run). It scans, connects to the strongest strap, prints the BPM every
   few seconds and reconnects by itself if the strap sleeps or drops.
3. Start Beat Saber. Order does not matter; OSC is fire-and-forget.

Options (`uv run hr_osc_bridge.py --help`): `--name TICKR` to prefer a strap by name,
`--port`/`--address` to match a changed HRCounter config, `--scan` to list straps, and
`--fake 72` to send a constant value without Bluetooth (verifies the HRCounter side alone).

Python 3.13 is pinned because bleak's Windows backend needs prebuilt WinRT wheels that lag new
Python releases.
