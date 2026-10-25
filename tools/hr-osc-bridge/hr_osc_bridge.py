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

Only one bridge runs at a time (a second one exits with code 3). MaxTweaks starts this script
with the game (Features/HeartRateBridge) and passes --parent-pid so it also stops with the game.
"""

import argparse
import asyncio
import socket
import sys
import time

GUARD_PORT = 39721  # bound on localhost by the running bridge so a second copy notices and exits
ALREADY_RUNNING_EXIT = 3

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
            log(f"connection failed: {type(e).__name__}: {e}".rstrip(": "))
        log(f"reconnecting in {backoff:.0f}s")
        await asyncio.sleep(backoff)
        backoff = min(backoff * 1.5, 30)


def acquire_guard() -> socket.socket | None:
    guard = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        guard.bind(("127.0.0.1", GUARD_PORT))
    except OSError:
        guard.close()
        return None
    return guard


def parent_alive(pid: int) -> bool:
    if sys.platform != "win32":
        import os

        try:
            os.kill(pid, 0)
            return True
        except OSError:
            return False

    import ctypes

    SYNCHRONIZE = 0x00100000
    WAIT_TIMEOUT = 0x102
    kernel32 = ctypes.windll.kernel32
    handle = kernel32.OpenProcess(SYNCHRONIZE, False, pid)
    if not handle:
        return False
    try:
        return kernel32.WaitForSingleObject(handle, 0) == WAIT_TIMEOUT
    finally:
        kernel32.CloseHandle(handle)


async def watch_parent(pid: int) -> None:
    while parent_alive(pid):
        await asyncio.sleep(2)
    log(f"parent process {pid} is gone, exiting")


async def run_with_parent(coro, parent_pid: int | None) -> None:
    if parent_pid is None:
        await coro
        return
    main_task = asyncio.ensure_future(coro)
    watcher = asyncio.ensure_future(watch_parent(parent_pid))
    done, pending = await asyncio.wait({main_task, watcher}, return_when=asyncio.FIRST_COMPLETED)
    for task in pending:
        task.cancel()
    if main_task in done:
        main_task.result()


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
    p.add_argument("--parent-pid", type=int, metavar="PID", help="exit when this process (the game) is gone")
    args = p.parse_args()

    sender = Sender(args.host, args.port, args.address)
    if args.scan:
        asyncio.run(scan(args.scan_seconds))
        return 0

    guard = acquire_guard()
    if guard is None:
        log(f"another bridge is already running (port {GUARD_PORT} in use), exiting")
        return ALREADY_RUNNING_EXIT

    try:
        work = fake(args.fake, sender) if args.fake is not None else bridge(args, sender)
        asyncio.run(run_with_parent(work, args.parent_pid))
    except KeyboardInterrupt:
        log("stopped")
    finally:
        guard.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
