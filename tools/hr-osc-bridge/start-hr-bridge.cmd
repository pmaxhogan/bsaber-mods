@echo off
rem Double-click to bridge a Bluetooth heart-rate strap into HRCounter. Keep this window open while playing.
cd /d "%~dp0"
uv run --python 3.13 hr_osc_bridge.py %*
pause
