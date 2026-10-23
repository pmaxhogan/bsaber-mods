# deploy.ps1 - build MaxTweaks (Release) and copy MaxTweaks.dll + .pdb into the game's Plugins.
#
# Usage: pwsh -File scripts/qa/deploy.ps1 [-Configuration Release]
#
# Refuses while Beat Saber is running (the DLL is locked and a half-deployed mod is confusing).
# Prints the deployed DLL's size, time and SHA256 so a stale copy is easy to spot.
param(
  [string]$Configuration = 'Release'
)
. (Join-Path $PSScriptRoot '_common.ps1')

if (Get-BeatSaberProcess) {
  Write-Error 'Beat Saber is running; close it first (scripts/qa/close.ps1).'
  exit 1
}

$plugins = Join-Path $script:InstanceDir 'Plugins'
if (-not (Test-Path -LiteralPath $plugins)) { throw "Plugins folder not found: $plugins" }

$proj = Join-Path $script:RepoRoot 'src\MaxTweaks\MaxTweaks.csproj'
$dotnet = if (Test-Path 'C:\Program Files\dotnet\dotnet.exe') { 'C:\Program Files\dotnet\dotnet.exe' } else { 'dotnet' }

# CI= is cleared so the CopyToPlugins target runs; BeatSaberDir points the build at this instance.
$env:CI = ''
& $dotnet build $proj -c $Configuration -nologo -v q "-p:BeatSaberDir=$($script:InstanceDir)\"
if ($LASTEXITCODE -ne 0) { Write-Error "build failed ($LASTEXITCODE)"; exit $LASTEXITCODE }

# Copy explicitly as well, so this script does not depend on the build's copy target.
$bin = Join-Path $script:RepoRoot "src\MaxTweaks\bin\$Configuration"
foreach ($f in 'MaxTweaks.dll', 'MaxTweaks.pdb') {
  $src = Join-Path $bin $f
  if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination $plugins -Force }
}

$dst = Join-Path $plugins 'MaxTweaks.dll'
$srcHash = (Get-FileHash -LiteralPath (Join-Path $bin 'MaxTweaks.dll') -Algorithm SHA256).Hash
$dstItem = Get-Item -LiteralPath $dst
$dstHash = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
if ($srcHash -ne $dstHash) { Write-Error "deployed DLL differs from build output"; exit 1 }
Write-Output ("deployed {0} ({1} bytes, {2:yyyy-MM-dd HH:mm:ss}, sha256 {3})" -f $dst, $dstItem.Length, $dstItem.LastWriteTime, $dstHash.Substring(0, 16))
