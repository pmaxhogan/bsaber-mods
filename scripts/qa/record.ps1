# record.ps1 - record the Beat Saber window to an mp4 with ffmpeg (start / stop / status).
#
# Usage: pwsh -File scripts/qa/record.ps1 -Start -Name <name> [-OutDir <dir>] [-MaxSec 120] [-Mode desktop|window] [-Fps 30]
#        pwsh -File scripts/qa/record.ps1 -Stop
#        pwsh -File scripts/qa/record.ps1 -Status
#
# -Start launches ffmpeg (gdigrab, no audio, <=1280 px wide, libx264 veryfast crf 26) through a hidden
# helper process and prints <OutDir>/<yyyyMMdd-HHmmss>-<name>.mp4. State lives in <OutDir>/record-state.json.
# -Stop asks the helper to send 'q' to ffmpeg (kills it if that fails), waits for the mp4 to be finalized
# and prints path, duration and size. -Status prints whether a recording is running (exit 0 = yes, 3 = no).
# -MaxSec is a safety limit (ffmpeg -t) so a forgotten recording cannot fill the disk.
# -Mode desktop (verified) captures the screen region of the window client area; the window must stay
# unoccluded (it is focused first). -Mode window (gdigrab by title) records a FROZEN frame for Beat Saber; avoid.
# Default OutDir: $env:MAXTWEAKS_QA_OUT, else the QA session scratchpad (see _common.ps1).
param(
  [switch]$Start,
  [switch]$Stop,
  [switch]$Status,
  [string]$Name = 'clip',
  [string]$OutDir = $null,
  [int]$MaxSec = 120,
  [ValidateSet('window', 'desktop')][string]$Mode = 'desktop',
  [string]$Title = 'Beat Saber',
  [int]$Fps = 30,
  # Internal: the hidden helper that owns ffmpeg's stdin. Not for direct use.
  [switch]$Worker,
  [string]$WorkerArgsFile = $null
)
. (Join-Path $PSScriptRoot '_common.ps1')

if (-not $OutDir) { $OutDir = $script:DefaultShotDir }
$StateFile = Join-Path $OutDir 'record-state.json'
$StopFile = Join-Path $OutDir 'record-stop.flag'

function Get-RecordState {
  if (-not (Test-Path -LiteralPath $StateFile)) { return $null }
  return Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json
}

function Test-PidAlive {
  param([int]$ProcessId)
  return [bool](Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)
}

# The ffmpeg first on PATH here is a stripped audio-only build without gdigrab, so pick the first
# ffmpeg on PATH that has it (override: $env:MAXTWEAKS_FFMPEG). ffprobe is taken from the same folder.
function Find-Ffmpeg {
  $cands = @()
  if ($env:MAXTWEAKS_FFMPEG) { $cands += $env:MAXTWEAKS_FFMPEG }
  $cands += @(Get-Command ffmpeg -All -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
  foreach ($c in $cands) {
    if ((& $c -hide_banner -devices 2>&1 | Out-String) -match 'gdigrab') { return (Resolve-Path -LiteralPath $c).Path }
  }
  throw 'no ffmpeg with gdigrab found on PATH (set $env:MAXTWEAKS_FFMPEG)'
}

# Reads a media file's duration in seconds with ffprobe ($null when unreadable).
function Get-ClipDuration {
  param([string]$Path)
  $probe = Join-Path (Split-Path (Find-Ffmpeg)) 'ffprobe.exe'
  $d = & $probe -v error -show_entries format=duration -of default=nw=1:nk=1 $Path 2>$null
  if ($LASTEXITCODE -ne 0 -or -not $d) { return $null }
  return [double]::Parse(($d | Select-Object -First 1), [Globalization.CultureInfo]::InvariantCulture)
}

# --- worker: runs ffmpeg with a redirected stdin and sends 'q' when the stop flag appears ------------
if ($Worker) {
  $cfg = Get-Content -LiteralPath $WorkerArgsFile -Raw | ConvertFrom-Json
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $cfg.ffmpeg
  foreach ($a in $cfg.args) { $psi.ArgumentList.Add([string]$a) }
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true
  $psi.RedirectStandardInput = $true
  $psi.RedirectStandardError = $true
  $ff = [System.Diagnostics.Process]::Start($psi)
  # Drain stderr to a log (an unread pipe would eventually block ffmpeg).
  $logStream = [System.IO.File]::Create($cfg.log)
  $copy = $ff.StandardError.BaseStream.CopyToAsync($logStream)
  # Publish the ffmpeg pid for -Start.
  Set-Content -LiteralPath $cfg.pidFile -Value $ff.Id -Encoding ascii
  while (-not $ff.HasExited -and -not (Test-Path -LiteralPath $cfg.stopFile)) { Start-Sleep -Milliseconds 100 }
  if (-not $ff.HasExited) {
    try { $ff.StandardInput.Write('q'); $ff.StandardInput.Flush(); $ff.StandardInput.Close() } catch { }
    if (-not $ff.WaitForExit(30000)) { $ff.Kill() }
  }
  $null = $copy.Wait(5000)
  $logStream.Dispose()
  exit 0
}

# --- status ----------------------------------------------------------------------------------------
if ($Status) {
  $st = Get-RecordState
  if ($st -and (Test-PidAlive $st.ffmpegPid)) {
    $secs = [int]((Get-Date) - [DateTime]::Parse($st.started)).TotalSeconds
    Write-Output "recording: $($st.path) (ffmpeg pid $($st.ffmpegPid), ${secs}s, mode $($st.mode))"
    exit 0
  }
  Write-Output 'not recording'
  exit 3
}

# --- start -----------------------------------------------------------------------------------------
if ($Start) {
  $ffmpeg = Find-Ffmpeg
  New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
  $st = Get-RecordState
  if ($st -and (Test-PidAlive $st.ffmpegPid)) { throw "already recording to $($st.path); run -Stop first" }
  $hwnd = Get-BeatSaberWindow

  $safe = ($Name -replace '[^A-Za-z0-9._-]', '_')
  $path = Join-Path $OutDir ('{0:yyyyMMdd-HHmmss}-{1}.mp4' -f (Get-Date), $safe)

  $in = @('-f', 'gdigrab', '-framerate', "$Fps", '-draw_mouse', '0')
  if ($Mode -eq 'window') {
    $in += @('-i', "title=$Title")
  } else {
    # Physical pixels are needed (gdigrab uses them), so opt this process out of DPI virtualization first.
    if (-not ('MaxTweaksQa.Dpi' -as [type])) {
      Add-Type -TypeDefinition 'using System.Runtime.InteropServices; namespace MaxTweaksQa { public static class Dpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); } }'
    }
    $null = [MaxTweaksQa.Dpi]::SetProcessDPIAware()
    if (-not (Invoke-GameFocus -Hwnd $hwnd)) { Write-Warning 'could not bring Beat Saber to the foreground; the region may be covered' }
    # Capture the client area (no title bar or borders).
    $cr = New-Object MaxTweaksQa.Native+RECT
    $null = [MaxTweaksQa.Native]::GetClientRect($hwnd, [ref]$cr)
    $pt = New-Object MaxTweaksQa.Native+POINT
    $null = [MaxTweaksQa.Native]::ClientToScreen($hwnd, [ref]$pt)
    $w = ($cr.Right - $cr.Left) -band -2
    $h = ($cr.Bottom - $cr.Top) -band -2
    $in += @('-offset_x', "$($pt.X)", '-offset_y', "$($pt.Y)", '-video_size', "${w}x${h}", '-i', 'desktop')
  }
  # Width capped at 1280 (never upscaled), both sides even for yuv420p.
  $vf = 'scale=w=trunc(min(1280\,iw)/2)*2:h=-2'
  $ffArgs = @('-hide_banner', '-y') + $in + @('-t', "$MaxSec", '-an', '-vf', $vf,
    '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '26', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', $path)

  Remove-Item -LiteralPath $StopFile -ErrorAction SilentlyContinue
  $log = [System.IO.Path]::ChangeExtension($path, '.ffmpeg.log')
  $pidFile = Join-Path $OutDir 'record-ffmpeg.pid'
  Remove-Item -LiteralPath $pidFile -ErrorAction SilentlyContinue
  $argsFile = Join-Path $OutDir 'record-worker.json'
  @{ ffmpeg = $ffmpeg; args = $ffArgs; log = $log; stopFile = $StopFile; pidFile = $pidFile } | ConvertTo-Json | Set-Content -LiteralPath $argsFile -Encoding ascii

  $helper = Start-Process -FilePath 'pwsh' -WindowStyle Hidden -PassThru -ArgumentList @(
    '-NoProfile', '-File', $PSCommandPath, '-Worker', '-WorkerArgsFile', $argsFile, '-OutDir', $OutDir)

  $deadline = [DateTime]::UtcNow.AddSeconds(15)
  while (-not (Test-Path -LiteralPath $pidFile) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
  if (-not (Test-Path -LiteralPath $pidFile)) { throw 'ffmpeg did not start' }
  $ffPid = [int](Get-Content -LiteralPath $pidFile -Raw)
  Start-Sleep -Seconds 1
  if (-not (Test-PidAlive $ffPid)) { throw "ffmpeg exited right away; see $log" }

  @{ path = $path; log = $log; ffmpegPid = $ffPid; workerPid = $helper.Id; mode = $Mode; maxSec = $MaxSec
     started = (Get-Date).ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $StateFile -Encoding ascii
  Write-Output $path
  exit 0
}

# --- stop ------------------------------------------------------------------------------------------
if ($Stop) {
  $st = Get-RecordState
  if (-not $st) { Write-Error 'no recording state file'; exit 1 }
  New-Item -ItemType File -Force -Path $StopFile | Out-Null
  # The worker sends 'q' and waits up to 30 s for ffmpeg to finalize the mp4.
  $deadline = [DateTime]::UtcNow.AddSeconds(40)
  while ((Test-PidAlive $st.ffmpegPid) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
  if (Test-PidAlive $st.ffmpegPid) {
    Write-Warning 'ffmpeg did not exit; killing it (the mp4 may be unplayable)'
    Stop-Process -Id $st.ffmpegPid -Force
  }
  Remove-Item -LiteralPath $StopFile, $StateFile -ErrorAction SilentlyContinue
  if (-not (Test-Path -LiteralPath $st.path)) { Write-Error "no output file; see $($st.log)"; exit 1 }
  $dur = Get-ClipDuration $st.path
  if ($null -eq $dur) { Write-Error "output is not playable; see $($st.log)"; exit 1 }
  $mib = (Get-Item -LiteralPath $st.path).Length / 1MB
  Write-Output $st.path
  Write-Output ('duration {0:N1}s, size {1:N2} MiB' -f $dur, $mib)
  exit 0
}

Write-Error 'specify one of -Start, -Stop, -Status'
exit 1
