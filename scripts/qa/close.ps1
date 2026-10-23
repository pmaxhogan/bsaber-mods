# close.ps1 - close Beat Saber gracefully, then force it after a timeout.
#
# Usage: pwsh -File scripts/qa/close.ps1 [-TimeoutSec 20]
#
# Sends WM_CLOSE to the main window (the game saves settings and mod configs on quit), waits up to
# TimeoutSec, then kills the process. Exits 0 when the game is gone (or was not running).
param(
  [int]$TimeoutSec = 20
)
. (Join-Path $PSScriptRoot '_common.ps1')

$p = Get-BeatSaberProcess
if (-not $p) { Write-Output 'not running'; exit 0 }

$null = $p.CloseMainWindow()
if ($p.WaitForExit($TimeoutSec * 1000)) {
  Write-Output "closed gracefully (pid $($p.Id))"
  exit 0
}

Write-Warning "still running after ${TimeoutSec}s; killing pid $($p.Id)"
Stop-Process -Id $p.Id -Force
if (-not $p.WaitForExit(10000)) { Write-Error 'could not kill Beat Saber'; exit 1 }
Write-Output "killed (pid $($p.Id))"
