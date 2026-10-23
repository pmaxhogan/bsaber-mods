# screenshot.ps1 - capture the Beat Saber window to a PNG and print its path.
#
# Usage: pwsh -File scripts/qa/screenshot.ps1 -Name <name> [-OutDir <dir>]
#
# Uses PrintWindow(PW_RENDERFULLCONTENT), which works for the Unity window even when it is covered.
# Default OutDir: $env:MAXTWEAKS_QA_OUT, else the QA session scratchpad (see _common.ps1).
# The file is <OutDir>/<yyyyMMdd-HHmmss>-<name>.png.
param(
  [Parameter(Mandatory, Position = 0)][string]$Name,
  [string]$OutDir = $null
)
. (Join-Path $PSScriptRoot '_common.ps1')

if (-not $OutDir) { $OutDir = $script:DefaultShotDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$safe = ($Name -replace '[^A-Za-z0-9._-]', '_')
$path = Join-Path $OutDir ('{0:yyyyMMdd-HHmmss}-{1}.png' -f (Get-Date), $safe)
$hwnd = Get-BeatSaberWindow
$size = [MaxTweaksQa.Native]::Capture($hwnd, $path)
Write-Output $path
Write-Verbose "captured $size"
