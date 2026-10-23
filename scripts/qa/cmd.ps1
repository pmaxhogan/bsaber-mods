# cmd.ps1 - send one command to the MaxTweaks debug channel and wait for its ack in the log.
#
# Usage: pwsh -File scripts/qa/cmd.ps1 "<command>" [-TimeoutSec 15] [-WaitFor '<regex>'] [-WaitTimeoutSec 60]
#   pwsh -File scripts/qa/cmd.ps1 "play 849a expert practice 20" -WaitFor 'event gameplay-start'
#   pwsh -File scripts/qa/cmd.ps1 "button Y press 0.5"
#   pwsh -File scripts/qa/cmd.ps1 status
#
# Appends "#<id> <command>" to UserData/MaxTweaks.debug-commands.txt and waits for
# "[MaxTweaks.Debug] ok #<id>" (exit 0) or "error #<id>" (exit 2); exit 1 on timeout. Every
# [MaxTweaks.Debug] line logged meanwhile is echoed. -WaitFor then also waits for a later line.
# Commands are listed in docs/qa.md (or send "help").
param(
  [Parameter(Mandatory, Position = 0)][string]$Command,
  [double]$TimeoutSec = 15,
  [string]$WaitFor = $null,
  [double]$WaitTimeoutSec = 60
)
. (Join-Path $PSScriptRoot '_common.ps1')

if (-not (Get-BeatSaberProcess)) { Write-Error 'Beat Saber is not running'; exit 1 }
if ($Command -match '[^\x20-\x7e]') { Write-Error 'commands must be printable ASCII'; exit 1 }

$id = 'q{0:HHmmssfff}{1}' -f (Get-Date), (Get-Random -Minimum 10 -Maximum 99)
$offset = Get-LogOffset

# Exclusive append with retries: the game also opens the file exclusively while it reads + truncates.
$bytes = [System.Text.Encoding]::ASCII.GetBytes("#$id $Command`n")
$written = $false
for ($i = 0; $i -lt 100 -and -not $written; $i++) {
  try {
    $fs = [System.IO.File]::Open($script:CommandFile, 'Append', 'Write', 'None')
    try { $fs.Write($bytes, 0, $bytes.Length) } finally { $fs.Dispose() }
    $written = $true
  } catch [System.IO.IOException] {
    Start-Sleep -Milliseconds 20
  }
}
if (-not $written) { Write-Error "could not write $($script:CommandFile)"; exit 1 }

$ackPattern = '\[MaxTweaks\.Debug\] (ok|error) #' + [regex]::Escape($id) + ' '
$ack = Wait-LogPattern -Pattern $ackPattern -TimeoutSec $TimeoutSec -Offset $offset -Echo $script:DebugPrefix
if ($null -eq $ack) {
  Write-Error "no ack for #$id within ${TimeoutSec}s (game busy loading, or not started with --debug?)"
  exit 1
}
if ($ack -match '\] error #') { exit 2 }

if ($WaitFor) {
  # Search from before the command: some events (e.g. "event paused") are logged before the ack.
  $hit = Wait-LogPattern -Pattern $WaitFor -TimeoutSec $WaitTimeoutSec -Offset $offset -Echo $script:DebugPrefix -EchoFrom $script:LastMatchEnd
  if ($null -eq $hit) {
    Write-Error "ack ok, but /$WaitFor/ did not appear within ${WaitTimeoutSec}s"
    exit 1
  }
}
