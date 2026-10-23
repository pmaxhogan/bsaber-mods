# wait-log.ps1 - wait for a regex to appear in the MaxTweaks log (or, with -GameLog, the game log).
#
# Usage: pwsh -File scripts/qa/wait-log.ps1 -Pattern '<regex>' [-TimeoutSec 60] [-FromStart] [-Echo '<substring>'] [-GameLog]
#
# Default source: UserData/MaxTweaks.debug-log.txt, the mod's --debug mirror of every MaxTweaks log
# line (features included), safe to tail. -GameLog tails Logs/_latest.log instead (needed for other
# mods' lines): BSIPA opens that file exclusively per write batch, so reading it can make BSIPA drop
# lines; this script only opens it after its size changed, but use it sparingly.
# Tails from the CURRENT end (only new lines count) unless -FromStart. Prints the matching line and
# exits 0; exits 1 on timeout. -Echo prints every new line containing that substring while waiting.
param(
  [Parameter(Mandatory)][string]$Pattern,
  [double]$TimeoutSec = 60,
  [switch]$FromStart,
  [string]$Echo = $null,
  [switch]$GameLog
)
. (Join-Path $PSScriptRoot '_common.ps1')

$path = if ($GameLog) { $script:LatestLog } else { $script:DebugLogFile }
$offset = if ($FromStart) { 0 } else { Get-LogOffset -Path $path }
$line = Wait-LogPattern -Path $path -Pattern $Pattern -TimeoutSec $TimeoutSec -Offset $offset -Echo $Echo
if ($null -eq $line) { Write-Error "timed out after ${TimeoutSec}s waiting for /$Pattern/ in $path"; exit 1 }
Write-Output $line
