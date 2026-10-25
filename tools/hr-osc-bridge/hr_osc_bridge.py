# /// script
# requires-python = ">=3.11,<3.14"
# dependencies = ["bleak>=0.22", "python-osc>=1.9"]
# ///
"""Bluetooth heart-rate strap -> HRCounter (OSC) bridge.

Reads the standard BLE Heart Rate Measurement characteristic (any strap: Wahoo TICKR,
Polar H10, Garmin HRM...) through the PC's own Bluetooth adapter and sends each reading as
an OSC int32 to HRCounter's OSC server (default 127.0.0.1:9000, address /hr).

    uv run hr_osc_bridge.py                 # scan, connect to the first HR strap, bridge forever
    uv run hr_osc_bridge.py --name TICKR    # prefer a strap whose name contains TICKR
    uv run hr_osc_bridge.py --fake 72       # no Bluetooth: send 72 bpm every second (tests HRCounter)
    uv run hr_osc_bridge.py --scan          # list heart-rate straps in range and exit

Keeps reconnecting when the strap goes to sleep or out of range. Ctrl+C to stop.
"""

import argparse
import asyncio
import sys
import time

from pythonosc.udp_client import SimpleUDPClient

HR_SERVICE = "0000180d-0000-1000-8000-00805f9b34fb"
HR_MEASUREMENT = "00002a37-0000-1000-8000-00805f9b34fb"


def log(msg: str) -> None:
    print(time.strftime("%H:%M:%S"), msg, flush=True)


def parse_hr(data: bytes) -> int:
    flags = data[0]
    if flags & 0x01:
        return int.from_bytes(data[1:3], "little")
    return data[1]


class Sender:
    def __init__(self, host: str, port: int, address: str) -> None:
        self._client = SimpleUDPClient(host, port)
        self._address = address
        self.count = 0
        self.last_log = 0.0

    def send(self, bpm: int) -> None:
        self._client.send_message(self._address, int(bpm))
        self.count += 1
        now = time.monotonic()
        if now - self.last_log >= 5:
            self.last_log = now
            log(f"{bpm} bpm (sent {self.count})")


async def scan(timeout: float):
    from bleak import BleakScanner

    log(f"scanning {timeout:.0f}s for heart-rate straps (wear it, electrodes wet)...")
    found = await BleakScanner.discover(timeout=timeout, return_adv=True)
    straps = []
    for device, adv in found.values():
        if HR_SERVICE in [u.lower() for u in adv.service_uuids]:
            straps.append((device, adv))
    for device, adv in straps:
        log(f"  {device.address}  {device.name or '?'}  rssi {adv.rssi}")
    if not straps:
        log("  none found")
    return straps


async def bridge(args, sender: Sender) -> None:
    from bleak import BleakClient

    backoff = 2.0
    while True:
        straps = await scan(args.scan_seconds)
        if args.name:
            straps = [s for s in straps if args.name.lower() in (s[0].name or "").lower()] or straps
        if not straps:
            log(f"retrying in {backoff:.0f}s")
            await asyncio.sleep(backoff)
            backoff = min(backoff * 1.5, 30)
            continue
        straps.sort(key=lambda s: s[1].rssi, reverse=True)
        device = straps[0][0]
        disconnected = asyncio.Event()
        loop = asyncio.get_running_loop()

        def on_disconnect(_client) -> None:
            loop.call_soon_threadsafe(disconnected.set)

        def on_hr(_char, data: bytearray) -> None:
            try:
                sender.send(parse_hr(bytes(data)))
            except Exception as e:  # noqa: BLE001
                log(f"bad reading {data.hex()}: {e}")

        try:
            log(f"connecting to {device.name or device.address}")
            async with BleakClient(device, disconnected_callback=on_disconnect, timeout=20) as client:
                await client.start_notify(HR_MEASUREMENT, on_hr)
                log("connected, bridging (Ctrl+C to stop)")
                backoff = 2.0
                await disconnected.wait()
            log("strap disconnected")
        except Exception as e:  # noqa: BLE001
            log(f"connection failed: {e}")
        log(f"reconnecting in {backoff:.0f}s")
        await asyncio.sleep(backoff)
        backoff = min(backoff * 1.5, 30)


async def fake(bpm: int, sender: Sender) -> None:
    log(f"fake mode: sending {bpm} bpm every second (no Bluetooth)")
    while True:
        sender.send(bpm)
        await asyncio.sleep(1)


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--host", default="127.0.0.1", help="HRCounter OSC host (default 127.0.0.1)")
    p.add_argument("--port", type=int, default=9000, help="HRCounter OscPort (default 9000)")
    p.add_argument("--address", default="/hr", help="OSC address, must be in HRCounter's OscAddress list (default /hr)")
    p.add_argument("--name", help="prefer a strap whose Bluetooth name contains this text")
    p.add_argument("--scan-seconds", type=float, default=8)
    p.add_argument("--scan", action="store_true", help="list straps and exit")
    p.add_argument("--fake", type=int, metavar="BPM", help="send a constant BPM without Bluetooth")
    args = p.parse_args()

    sender = Sender(args.host, args.port, args.address)
    try:
        if args.scan:
            asyncio.run(scan(args.scan_seconds))
        elif args.fake is not None:
            asyncio.run(fake(args.fake, sender))
        else:
            asyncio.run(bridge(args, sender))
    except KeyboardInterrupt:
        log("stopped")
    return 0


if __name__ == "__main__":
    sys.exit(main())
