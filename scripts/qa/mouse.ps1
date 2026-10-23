# mouse.ps1 - mouse input for the Beat Saber window (FPFC driving).
#
# Usage:
#   pwsh -File scripts/qa/mouse.ps1 -Action look -Dx 200 -Dy 0      # relative motion = FPFC camera turn
#   pwsh -File scripts/qa/mouse.ps1 -Action click [-Button right]   # click where the camera points (UI click)
#   pwsh -File scripts/qa/mouse.ps1 -Action down|up [-Button left]
#   pwsh -File scripts/qa/mouse.ps1 -Action move -X 640 -Y 360      # absolute, window client coordinates
#
# SiraUtil FPFC: moving the mouse turns the camera and the UI pointer with it, but in menus the
# pointer is offset from the screen centre: aim with "look", check the hover highlight with
# screenshot.ps1, then "click". For menu buttons prefer cmd.ps1 "click-text <label>" (no aiming, no
# focus needed). Large -Dx/-Dy are split into -Steps chunks (default 10) so Unity sees smooth
# motion. Brings the window to the foreground first (with retries) unless -NoFocus; button actions
# refuse to run (exit 1) when focus fails, so a click never lands in another window.
param(
  [Parameter(Mandatory)][ValidateSet('look', 'move', 'click', 'down', 'up')][string]$Action,
  [int]$Dx = 0,
  [int]$Dy = 0,
  [int]$X = 0,
  [int]$Y = 0,
  [ValidateSet('left', 'right')][string]$Button = 'left',
  [int]$Steps = 10,
  [int]$HoldMs = 80,
  [switch]$NoFocus
)
. (Join-Path $PSScriptRoot '_common.ps1')

$hwnd = Get-BeatSaberWindow
if (-not $NoFocus -and -not (Invoke-GameFocus -Hwnd $hwnd)) {
  if ($Action -in 'click', 'down', 'up') {
    Write-Error 'could not bring Beat Saber to the foreground; not clicking (use cmd.ps1 click-text, or -NoFocus to force)'
    exit 1
  }
  Write-Warning 'could not bring Beat Saber to the foreground; input may go elsewhere'
}
$right = $Button -eq 'right'

switch ($Action) {
  'look' {
    $n = [Math]::Max(1, $Steps)
    $sentX = 0; $sentY = 0
    for ($i = 1; $i -le $n; $i++) {
      $tx = [int][Math]::Round($Dx * $i / $n); $ty = [int][Math]::Round($Dy * $i / $n)
      [MaxTweaksQa.Native]::MouseMoveRelative($tx - $sentX, $ty - $sentY)
      $sentX = $tx; $sentY = $ty
      Start-Sleep -Milliseconds 16
    }
  }
  'move' { [MaxTweaksQa.Native]::MouseMoveClient($hwnd, $X, $Y) }
  'down' { [MaxTweaksQa.Native]::MouseButton($right, $false) }
  'up' { [MaxTweaksQa.Native]::MouseButton($right, $true) }
  'click' {
    [MaxTweaksQa.Native]::MouseButton($right, $false)
    Start-Sleep -Milliseconds $HoldMs
    [MaxTweaksQa.Native]::MouseButton($right, $true)
  }
}
Write-Output "mouse $Action"
