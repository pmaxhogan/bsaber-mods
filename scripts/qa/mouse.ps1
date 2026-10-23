# mouse.ps1 - mouse input for the Beat Saber window (FPFC driving).
#
# Usage:
#   pwsh -File scripts/qa/mouse.ps1 -Action look -Dx 200 -Dy 0      # relative motion = FPFC camera turn
#   pwsh -File scripts/qa/mouse.ps1 -Action click [-Button right]   # click where the camera points (UI click)
#   pwsh -File scripts/qa/mouse.ps1 -Action down|up [-Button left]
#   pwsh -File scripts/qa/mouse.ps1 -Action move -X 640 -Y 360      # absolute, window client coordinates
#
# SiraUtil FPFC: moving the mouse turns the camera; the UI pointer is the centre of view, so aim
# with "look" and press with "click". Large -Dx/-Dy are split into -Steps chunks (default 10) so
# Unity sees smooth motion. Brings the window to the foreground first unless -NoFocus.
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
