# launch.ps1 - start Beat Saber in FPFC desktop mode with the MaxTweaks debug channel and wait for the menu.
#
# Usage: pwsh -File scripts/qa/launch.ps1 [-TimeoutSec 180] [-NoFpfc] [-NoDebug] [-NoWait] [-ExtraArgs '...']
#
# Runs "Beat Saber.exe fpfc --verbose --debug" from the instance dir with SteamAppId=620980 (Steam must
# be running). Waits until UserData/MaxTweaks.debug-log.txt (the mod's log mirror, recreated each run
# and headed "session pid=<pid>") shows "[MaxTweaks.Debug] event menu-ready" (menu scene up, no
# transition, SongCore songs loaded). Prints "pid <n>" and exits 0, or exits 1 on timeout/crash.
# -NoDebug omits --debug (the debug channel must then stay silent): it waits for SongCore's
# "Loaded N new songs" line in Logs/_latest.log instead.
param(
  [int]$TimeoutSec = 180,
  [switch]$NoFpfc,
  [switch]$NoDebug,
  [switch]$NoWait,
  [string[]]$ExtraArgs = @()
)
. (Join-Path $PSScriptRoot '_common.ps1')

if (Get-BeatSaberProcess) {
  Write-Error 'Beat Saber is already running (scripts/qa/close.ps1 first).'
  exit 1
}

$exe = Join-Path $script:InstanceDir 'Beat Saber.exe'
$gameArgs = @()
if (-not $NoFpfc) { $gameArgs += 'fpfc' }
$gameArgs += @('--verbose')
if (-not $NoDebug) { $gameArgs += '--debug' }
$gameArgs += $ExtraArgs

$started = Get-Date
$deadline = $started.AddSeconds($TimeoutSec)
$env:SteamAppId = '620980'
$proc = Start-Process -FilePath $exe -ArgumentList $gameArgs -WorkingDirectory $script:InstanceDir -PassThru
Write-Output "pid $($proc.Id) ($exe $($gameArgs -join ' '))"
if ($NoWait) { exit 0 }

function Get-Remaining { [Math]::Max(1, ($deadline - (Get-Date)).TotalSeconds) }

if ($NoDebug) {
  # Previous runs' _latest.log may still be there: wait for a log file created by this run first.
  while (-not (Get-ChildItem -LiteralPath $script:LogDir -Filter '*.log' | Where-Object {
        $t = [datetime]::MinValue
        [datetime]::TryParseExact($_.BaseName, 'yyyy.MM.dd.HH.mm.ss', $null, 'None', [ref]$t) -and $t -ge $started.AddSeconds(-2)
      })) {
    if ($proc.HasExited -or (Get-Date) -gt $deadline) { Write-Error 'no log for this run'; exit 1 }
    Start-Sleep -Milliseconds 250
  }
  $line = Wait-LogPattern -Path $script:LatestLog -Offset 0 -TimeoutSec (Get-Remaining) -Pattern '\| SongCore\] Loaded \d+ new songs'
  if (-not $line) { Write-Error "timed out after ${TimeoutSec}s waiting for SongCore to load songs"; exit 1 }
  Write-Output ("songs loaded after {0:0.0}s" -f ((Get-Date) - $started).TotalSeconds)
  Write-Output "pid $($proc.Id)"
  exit 0
}

# The mirror is recreated by the mod at plugin init; its first line names our pid.
$header = "session pid=$($proc.Id) "
$fresh = $false
while (-not $fresh -and (Get-Date) -lt $deadline) {
  if ($proc.HasExited) { Write-Error "Beat Saber exited early (code $($proc.ExitCode))"; exit 1 }
  if (Test-Path -LiteralPath $script:DebugLogFile) {
    try {
      $fs = [System.IO.File]::Open($script:DebugLogFile, 'Open', 'Read', 'ReadWrite, Delete')
      try { $first = (New-Object System.IO.StreamReader($fs)).ReadLine() } finally { $fs.Dispose() }
      $fresh = $first -and $first.Contains($header)
    } catch [System.IO.IOException] {
    }
  }
  if (-not $fresh) { Start-Sleep -Milliseconds 250 }
}
if (-not $fresh) {
  Write-Error "timed out: $($script:DebugLogFile) never started for pid $($proc.Id) - is the deployed MaxTweaks.dll a build with Common/Debug? (scripts/qa/deploy.ps1)"
  exit 1
}

$line = Wait-LogPattern -Offset 0 -TimeoutSec (Get-Remaining) -Pattern '\[MaxTweaks\.Debug\] event menu-ready'
if (-not $line) { Write-Error "timed out after ${TimeoutSec}s waiting for menu-ready"; exit 1 }
if ($proc.HasExited) { Write-Error 'Beat Saber exited'; exit 1 }
Write-Output ("menu ready after {0:0.0}s" -f ((Get-Date) - $started).TotalSeconds)
Write-Output "pid $($proc.Id)"
