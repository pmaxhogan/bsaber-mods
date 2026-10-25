# setup-beat-saber.ps1 - fallback for the nicoco007/setup-beat-saber action.
#
# Usage: pwsh -File scripts/ci/setup-beat-saber.ps1 -ProjectPath <csproj> -Path <dir>
#
# Does what the action does without the BeatMods "/api/versions" endpoint (which disappeared on
# 2026-10-25 and made every CI build fail at that first request):
#   1. downloads nicoco007/BeatSaberReferenceAssemblies at tag v<GameVersion> (stripped game DLLs),
#   2. downloads the latest BeatMods build of every <DependsOn> mod for that game version,
#   3. writes BeatSaberDir / GameDirectory to $GITHUB_ENV (when set) so the build finds them.
# GameVersion and DependsOn are read straight from the csproj (they are literals in this repo).
# Runs locally too: point -Path at a scratch folder and build with -p:BeatSaberDir=<Path>\.
param(
  [Parameter(Mandatory)][string]$ProjectPath,
  [Parameter(Mandatory)][string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$headers = @{ 'User-Agent' = 'MaxTweaks-ci-setup-beat-saber' }
$githubHeaders = $headers.Clone()
$githubHeaders['Accept'] = 'application/vnd.github+json'
$githubHeaders['X-GitHub-Api-Version'] = '2022-11-28'
if ($env:GITHUB_TOKEN) { $githubHeaders['Authorization'] = "Bearer $env:GITHUB_TOKEN" }

$csproj = Get-Content -LiteralPath $ProjectPath -Raw
if ($csproj -notmatch '<GameVersion>([^<]+)</GameVersion>') { throw "no <GameVersion> in $ProjectPath" }
$gameVersion = $Matches[1].Trim()
$deps = @{}
foreach ($m in [regex]::Matches($csproj, '<DependsOn\s+Include="([^"]+)"\s+Version="([^"]+)"')) {
  $deps[$m.Groups[1].Value] = $m.Groups[2].Value
}
if ($deps.Count -eq 0) { throw "no <DependsOn> items in $ProjectPath" }
Write-Host "game $gameVersion; dependencies: $(($deps.Keys | Sort-Object) -join ', ')"

New-Item -ItemType Directory -Force -Path $Path | Out-Null
$Path = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\')
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("setup-beat-saber-" + [guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Force -Path $work | Out-Null

# Downloads a zip and extracts it into $Path. $Strip removes that many leading path segments from
# every entry: GitHub zipballs wrap everything in "<owner>-<repo>-<sha>/", and the reference
# assemblies repo keeps its files under "Beat Saber/" on top of that (the action strips both).
function Expand-ZipInto {
  param([string]$Url, [hashtable]$Headers, [int]$Strip)
  $zip = Join-Path $work ([guid]::NewGuid().ToString('n') + '.zip')
  Invoke-WebRequest -Uri $Url -Headers $Headers -OutFile $zip
  $stage = Join-Path $work ([guid]::NewGuid().ToString('n'))
  Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force
  $root = $stage
  for ($i = 0; $i -lt $Strip; $i++) {
    $top = @(Get-ChildItem -LiteralPath $root)
    if ($top.Count -ne 1 -or -not $top[0].PSIsContainer) { throw "unexpected zip layout from $Url" }
    $root = $top[0].FullName
  }
  Copy-Item -Path (Join-Path $root '*') -Destination $Path -Recurse -Force
}

Write-Host "downloading reference assemblies v$gameVersion"
Expand-ZipInto -Url "https://api.github.com/repos/nicoco007/BeatSaberReferenceAssemblies/zipball/refs/tags/v$gameVersion" -Headers $githubHeaders -Strip 2

Write-Host "fetching BeatMods mods for $gameVersion"
$mods = (Invoke-RestMethod -Uri "https://beatmods.com/api/mods?gameName=BeatSaber&gameVersion=$gameVersion" -Headers $headers).mods
foreach ($name in ($deps.Keys | Sort-Object)) {
  $range = $deps[$name]
  $entry = $mods | Where-Object { $_.mod.name -eq $name } | Select-Object -First 1
  if (-not $entry) { throw "mod '$name' has no build for Beat Saber $gameVersion on BeatMods" }
  $version = $entry.latest.modVersion
  # The ranges in this repo are caret ranges; the latest build for the game version must share the major.
  if ($range -match '^\^(\d+)\.' -and -not $version.StartsWith("$($Matches[1]).")) {
    throw "$name $version on BeatMods does not satisfy '$range'"
  }
  Write-Host "downloading $name $version"
  Expand-ZipInto -Url "https://beatmods.com/cdn/mod/$($entry.latest.zipHash).zip" -Headers $headers -Strip 0
  if ($name -eq 'BSIPA') {
    # IPA.exe moves these when it installs; the build references them from their installed places.
    foreach ($pair in @(@('IPA\Libs', 'Libs'), @('IPA\Data', 'Beat Saber_Data'))) {
      $dest = Join-Path $Path $pair[1]
      New-Item -ItemType Directory -Force -Path $dest | Out-Null
      Copy-Item -Path (Join-Path $Path ($pair[0] + '\*')) -Destination $dest -Recurse -Force
    }
  }
}

Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
if ($env:GITHUB_ENV) {
  Add-Content -LiteralPath $env:GITHUB_ENV -Value "BeatSaberDir=$Path`nGameDirectory=$Path"
}
Write-Host "ready: $Path"
