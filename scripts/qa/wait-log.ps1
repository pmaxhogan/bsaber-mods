# wait-log.ps1 - wait for a regex to appear in Beat Saber's Logs/_latest.log.
#
# Usage: pwsh -File scripts/qa/wait-log.ps1 -Pattern '<regex>' [-TimeoutSec 60] [-FromStart] [-Echo '<substring>']
#
# Tails from the CURRENT end of the log (only new lines count) unless -FromStart. Prints the matching
# line and exits 0; exits 1 on timeout. -Echo prints every new line containing that substring while
# waiting, e.g. -Echo '[MaxTweaks' to watch the mod.
param(
  [Parameter(Mandatory)][string]$Pattern,
  [double]$TimeoutSec = 60,
  [switch]$FromStart,
  [string]$Echo = $null
)
. (Join-Path $PSScriptRoot '_common.ps1')

$offset = if ($FromStart) { 0 } else { Get-LogOffset }
$line = Wait-LogPattern -Pattern $Pattern -TimeoutSec $TimeoutSec -Offset $offset -Echo $Echo
if ($null -eq $line) { Write-Error "timed out after ${TimeoutSec}s waiting for /$Pattern/"; exit 1 }
Write-Output $line
