# launch.ps1 - start Beat Saber in FPFC desktop mode with the MaxTweaks debug channel and wait for the menu.
#
# Usage: pwsh -File scripts/qa/launch.ps1 [-TimeoutSec 180] [-NoFpfc] [-NoDebug] [-NoWait] [-ExtraArgs '...']
#
# Runs "Beat Saber.exe fpfc --verbose --debug" from the instance dir with SteamAppId=620980 (Steam must
# be running). Waits until this run's log shows "[MaxTweaks.Debug] event menu-ready" (menu scene up,
# no transition, SongCore songs loaded). Prints "pid <n>" and exits 0, or exits 1 on timeout/crash.
# -NoDebug omits --debug (the debug channel must then stay silent); it waits for SongCore's
# "Loaded N new songs" line instead.
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
$env:SteamAppId = '620980'
$proc = Start-Process -FilePath $exe -ArgumentList $gameArgs -WorkingDirectory $script:InstanceDir -PassThru
Write-Output "pid $($proc.Id) ($exe $($gameArgs -join ' '))"
if ($NoWait) { exit 0 }

# _latest.log from the previous run still exists, and NTFS tunnelling can keep its creation time,
# so find this run's dated log (Logs/yyyy.MM.dd.HH.mm.ss.log) by the timestamp in its name.
$log = $null
$deadline = $started.AddSeconds($TimeoutSec)
while (-not $log -and (Get-Date) -lt $deadline) {
  if ($proc.HasExited) { Write-Error "Beat Saber exited early (code $($proc.ExitCode))"; exit 1 }
  $log = Get-ChildItem -LiteralPath $script:LogDir -Filter '*.log' |
    Where-Object { $_.Name -ne '_latest.log' } |
    Where-Object {
      $t = [datetime]::MinValue
      [datetime]::TryParseExact($_.BaseName, 'yyyy.MM.dd.HH.mm.ss', $null, 'None', [ref]$t) -and $t -ge $started.AddSeconds(-2)
    } | Sort-Object Name | Select-Object -Last 1
  if (-not $log) { Start-Sleep -Milliseconds 250 }
}
if (-not $log) { Write-Error 'no log file for this run appeared'; exit 1 }
Write-Output "log $($log.FullName)"

$remaining = [Math]::Max(1, ($deadline - (Get-Date)).TotalSeconds)
if ($NoDebug) {
  $line = Wait-LogPattern -Path $log.FullName -Offset 0 -TimeoutSec $remaining -Pattern '\| SongCore\] Loaded \d+ new songs'
  if (-not $line) { Write-Error "timed out after ${TimeoutSec}s waiting for SongCore to load songs"; exit 1 }
  Write-Output ("songs loaded after {0:0.0}s" -f ((Get-Date) - $started).TotalSeconds)
  Write-Output "pid $($proc.Id)"
  exit 0
}
$line = Wait-LogPattern -Path $log.FullName -Offset 0 -TimeoutSec $remaining -Pattern '\[MaxTweaks\.Debug\] (event menu-ready|enabled)'
if ($line -and $line -match 'enabled') {
  $remaining = [Math]::Max(1, ($deadline - (Get-Date)).TotalSeconds)
  $line = Wait-LogPattern -Path $log.FullName -Offset 0 -TimeoutSec $remaining -Pattern '\[MaxTweaks\.Debug\] event menu-ready'
} elseif (-not $line) {
  Write-Error "timed out: no '[MaxTweaks.Debug] enabled' line - is the deployed MaxTweaks.dll a build with Common/Debug? (scripts/qa/deploy.ps1)"
  exit 1
}
if (-not $line) { Write-Error "timed out after ${TimeoutSec}s waiting for menu-ready"; exit 1 }
if ($proc.HasExited) { Write-Error 'Beat Saber exited'; exit 1 }
Write-Output ("menu ready after {0:0.0}s" -f ((Get-Date) - $started).TotalSeconds)
Write-Output "pid $($proc.Id)"
