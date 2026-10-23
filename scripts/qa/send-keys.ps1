# send-keys.ps1 - send real keyboard input to the Beat Saber window (FPFC driving).
#
# Usage: pwsh -File scripts/qa/send-keys.ps1 -Keys <k1,k2,...> [-Action press|down|up] [-HoldMs 100] [-GapMs 50] [-NoFocus]
#   send-keys.ps1 -Keys W -HoldMs 800          # walk the FPFC camera forward for 0.8 s
#   send-keys.ps1 -Keys Y -Action down         # hold Y (MaxTweaks keyboard fallback for the Y button)
#   send-keys.ps1 -Keys Y -Action up
#
# Brings the window to the foreground first (Unity only reads keys while focused), then uses
# SendInput with hardware scan codes. press = down, hold HoldMs, up; several keys are sent in order.
# SiraUtil FPFC: W/A/S/D/Q/E move the camera. MaxTweaks fallbacks: A=Enter, B=Backspace, X, Y,
# right stick = arrow keys. Game pause-menu keys: C continue, R restart, M menu.
# For button presses that must not depend on window focus, prefer cmd.ps1 "button ...".
param(
  [Parameter(Mandatory)][string[]]$Keys,
  [ValidateSet('press', 'down', 'up')][string]$Action = 'press',
  [int]$HoldMs = 100,
  [int]$GapMs = 50,
  [switch]$NoFocus
)
. (Join-Path $PSScriptRoot '_common.ps1')

$hwnd = Get-BeatSaberWindow
if (-not $NoFocus -and -not (Invoke-GameFocus -Hwnd $hwnd)) {
  Write-Warning 'could not bring Beat Saber to the foreground; keys may go elsewhere'
}

foreach ($k in $Keys) {
  $vk = Get-VirtualKey $k
  switch ($Action) {
    'down' { [MaxTweaksQa.Native]::Key($vk, $false) }
    'up' { [MaxTweaksQa.Native]::Key($vk, $true) }
    default {
      [MaxTweaksQa.Native]::Key($vk, $false)
      Start-Sleep -Milliseconds $HoldMs
      [MaxTweaksQa.Native]::Key($vk, $true)
    }
  }
  Start-Sleep -Milliseconds $GapMs
}
Write-Output "$Action $($Keys -join ',')"
