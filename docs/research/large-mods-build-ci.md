# Large Beat Saber PC mods: build tooling, CI, release, testing

Research date: 2026-10-22. All paths below are relative to `../`.
Line numbers are from the clones at the commits noted.

## 0. What was studied

| Clone dir | Upstream | Kind | Last commit / release |
|---|---|---|---|
| `Heck` | Aeroluna/Heck (Heck + Chroma + NoodleExtensions) | mod, multi-project, multi-game-version | 3bbcaf6 2026-05-09 (pushed 2026-07-26 on dev); release v2026-05-09 |
| `beatleader-mod` | BeatLeader/beatleader-mod | mod, branch per game version | 25c84a7d 2026-10-21; release v0.10.0 2026-04-19 |
| `BeatSaberCustomAvatars` | nicoco007/BeatSaberCustomAvatars | mod + Unity editor package | 2a4f4ed 2026-09-21; release v5.4.13 2026-03-15 |
| `MultiplayerCore` | Goobwabber/MultiplayerCore | mod | 0f20a09 2025-06-08; v1.6.2 |
| `Aeroluna-workflows` | Aeroluna/workflows | reusable GH Actions workflows used by Heck | 2026-02-05 |
| `Aeroluna-download-beatmods-deps` | Aeroluna/download-beatmods-deps | GH Action (TS) | 2026-04-30 |
| `Aeroluna-beatmods-upload` | Aeroluna/beatmods-upload | GH Action (TS), uploads to BeatMods | 2026-02-17 |
| `Aeroluna-LunaBSMod.Tasks` | Aeroluna/LunaBSMod.Tasks | MSBuild task: per-game-version constants | 2024-09-30 |
| `Aeroluna-BeatSaberModdingTools.Tasks.Luna` | Aeroluna fork of BSMT.Tasks | MSBuild tasks | 2024-10-06 |
| `Aeroluna-GenericStripper` | Aeroluna/GenericStripper | tool that strips game DLLs for CI | 2026-02-05 |
| `nicoco007-BeatSaberModdingTools.Tasks` | nicoco007 fork of BSMT.Tasks | MSBuild tasks (cleanest targets seen) | 2025-05-17 |
| `nicoco007-BeatSaberModTemplate` | nicoco007/BeatSaberModTemplate | `dotnet new` template | 2024-08-13 |
| `nicoco007-setup-beat-saber` | nicoco007/setup-beat-saber | GH Action: game refs + BeatMods deps | 2026-10-09 (branch `next`) |
| `BeatSaberReferenceAssemblies` | nicoco007/BeatSaberReferenceAssemblies | stripped game DLLs, one tag per game version | v1.45.3 2026-10-16 |
| `download-beatmods-deps` | Goobwabber/download-beatmods-deps | GH Action (JS), original deps downloader | 2025-02-27 |
| `BeatSaberModdingTools` | Zingabopp/BeatSaberModdingTools | VS extension (VSIX), the "BSMT" origin | 2023-12-12 (stale) |
| `Zingabopp-UnityModdingTools.Templates.BeatSaber` | Zingabopp templates (VSIX) | legacy non-SDK csproj templates | 2023-07-20 (stale) |

Not cloned but inspected via API: `beat-forge/init-beatsaber` (action, last push 2024-10-10), `beat-forge/beatsaber-stripped`
(stripped DLLs, branches up to `version/1.42.0`, pushed 2025-12), `Aeroluna/beatsaber-stripped` (branches up to
`version/1.44.2`, 112 branches, 76 MB). Core frameworks (BSIPA, SiraUtil, BSML, SongCore) were left to other agents.

## 1. csproj conventions

### 1.1 Three generations of project files

1. **Legacy non-SDK csproj (2019-2023), VSIX templates.** `Zingabopp-UnityModdingTools.Templates.BeatSaber/BSIPA Plugin (Core)/CoreProjectTemplate.csproj:2`
   (`ToolsVersion="15.0"`), `TargetFrameworkVersion` at :13, `LocalRefsDir`/`BeatSaberDir` fallback to `..\Refs` at :17-18,
   every reference with `HintPath=$(BeatSaberDir)\...` and `<Private>False</Private>` at :49-60. Separate
   `manifest.json` checked in (`manifest.json` in that folder, `gameVersion` 1.18.3), `csproj.user.template` with an empty
   `BeatSaberDir` (lines 1-8), `Directory.Build.props.template` sets `ImportBSMTTargets`/`BSMTProjectType=BSIPA` (lines 4-5 of that file).
   This is what most older tutorials describe. Do not start here.
2. **SDK-style csproj + checked-in manifest.json + BSMT.Tasks 2.0.0-beta7 (BeatLeader, MultiplayerCore).**
   `beatleader-mod/Source/BeatLeader.csproj:1` `Sdk="Microsoft.NET.Sdk"`, `:3` `net48`, `:5-6` `LocalRefsDir` = `..\Refs` and
   `BeatSaberDir = $(LocalRefsDir)` (overridden by the gitignored `BeatLeader.csproj.user`, referenced at :~352
   `None Include="BeatLeader.csproj.user"`). References are verbose 4-line `<Reference>` blocks with HintPath, `Private=False`,
   `SpecificVersion=False`; 76 references total. Packages at :357-358: `BeatSaberModdingTools.Tasks 2.0.0-beta7` and
   `BepInEx.AssemblyPublicizer.MSBuild 0.4.2`. `Source/manifest.json` is checked in and embedded as a resource; version and
   gameVersion live there (`manifest.json:6,8`), and `Source/Directory.Build.targets` (BSMT "BuildTargetsVersion 2.0", line 6)
   reads it via `GetManifestInfo` (line 23) and fails a Release build if assembly and manifest versions mismatch
   (`ErrorOnMismatchedVersions`, line 13).
   MultiplayerCore is the same shape (`MultiplayerCore/MultiplayerCore/MultiplayerCore.csproj:8` `net472`, :10-11 `..\Refs`).
3. **SDK-style csproj with the manifest GENERATED from MSBuild items (Heck, CustomAvatars, nicoco007 template).** No
   manifest.json in the repo. Identity and dependencies are MSBuild properties/items:
   - `Heck/Heck/Heck.csproj:12-16` `PluginId`, `PluginName`, `Author`, `Version`, `Description`; `:18-23` `<DependsOn Include="SiraUtil" Version="^3.0.5"/>` etc.
   - `BeatSaberCustomAvatars/Source/Common.props:17-18` `<GameVersion>1.41.1</GameVersion>` `<Version>5.4.13</Version>`; `:23-29` `DependsOn` items.
   - `nicoco007-BeatSaberModTemplate/BeatSaberMod/BeatSaberMod.csproj:3-10` (PackageId/Title/Authors/GameVersion/Version/RepositoryUrl), `:13-15` DependsOn.
   The `GenerateManifest` target writes `obj/.../manifest.json` and embeds it as `<AssemblyName>.manifest.json`
   (`nicoco007-BeatSaberModdingTools.Tasks/BeatSaberModdingTools.Tasks/BuildTypes/BSMT.BSIPA.targets:37-48`).
   One source of truth for version, no drift between csproj and manifest. This is the modern pattern and the one to copy.

### 1.2 Target framework
- `net472` (CustomAvatars `Common.props:7`, MultiplayerCore, nicoco007 template `Directory.build.props:20`) or `net48`
  (Heck `Heck.csproj:4`, BeatLeader `BeatLeader.csproj:3`). Both work: the game runs Unity Mono with a .NET 4.x profile.
- `LangVersion` latest/12 everywhere, with polyfill packages for newer C# features: `PolySharp` (BeatLeader csproj :363),
  `IsExternalInit` (CustomAvatars `Common.props:37`). `Nullable` enabled in Heck and BeatLeader.
- The nicoco007 template goes further and does NOT use the .NET Framework reference assemblies at all:
  `Directory.build.props:10` `DisableImplicitFrameworkReferences=True`, `:19` `NoStdLib=True`, `:34-45` explicit
  `mscorlib`, `netstandard`, `System*` references, and `Directory.build.targets:29-33` forces
  `TargetFrameworkDirectory` to `$(BeatSaberDir)Beat Saber_Data\Managed\` so the compiler binds to the game's own Mono BCL.
  Commit `8004f9b` in CustomAvatars ("Build against game framework assemblies", 2024-08-07) is where this was adopted.
  Payoff: no Developer Pack / reference assemblies needed on the build machine, and you cannot accidentally call a BCL API the
  game's Mono lacks. The forked tasks package bakes the same in (`BSMT.BSIPA.props:10,24-38`, `BSMT.BSIPA.targets:30-35`).

### 1.3 BeatSaberDir, .csproj.user, Directory.Build.*
- Local path to the game install lives in `<Project>.csproj.user` (gitignored: `Heck/.gitignore:6` `*.user`). Template example:
  `nicoco007-BeatSaberModTemplate/BeatSaberMod/BeatSaberMod.csproj.user:4`. (Note: that template COMMITS a .csproj.user with a
  `D:\Steam Library\...` path, fine for a `dotnet new` template, wrong for a real repo.)
- `BeatSaberDir` is normalized to end in a slash (`BSMT.BSIPA.targets:4`) so HintPaths are written `$(BeatSaberDir)Libs\0Harmony.dll`.
  Older code writes `$(BeatSaberDir)\Libs\...` (Heck csproj :29); both resolve.
- CI sets `BeatSaberDir` (and `GameDirectory`) via `$GITHUB_ENV` from the deps action
  (`Aeroluna-download-beatmods-deps/src/main.ts:174-178`; nicoco007/setup-beat-saber `src/main.ts:106-108` (branch `next`)).
  The Goobwabber/beat-forge flow instead extracts to `./Refs` and relies on the csproj `..\Refs` fallback.
- `Directory.Build.props` in BeatLeader/MPCore exists only to flip CI switches when `GITHUB_ACTIONS == true`:
  `ContinuousIntegrationBuild`, `DisableCopyToPlugins`, `DisableZipRelease` (`beatleader-mod/Source/Directory.Build.props:4-8`).
  The Luna fork does the same inside the package (`Aeroluna-BeatSaberModdingTools.Tasks.Luna/BeatSaberModdingTools.Tasks/BSMT.DefaultProps.props:7-15`).

### 1.4 Reference style
- HintPath style (Heck `Heck.csproj:25-63`: `ItemDefinitionGroup` sets `Private=False` once at :25-27, then one-line
  `<Reference Include="Main" HintPath="$(BeatSaberDir)\Beat Saber_Data\Managed\Main.dll" Publicize="true"/>`). Compact; copy this
  `ItemDefinitionGroup` trick.
- No-HintPath style (CustomAvatars `Source/CustomAvatar/CustomAvatar.csproj:5-60`: just `<Reference Include="Main" Publicize="True" />`).
  Works because `TargetFrameworkDirectory` is overridden to the game's Managed/Plugins/Libs dirs
  (`BSMT.BSIPA.targets:31-35`). Cleanest csproj seen, but depends on the forked tasks package.
- Plugin dependencies (BSML, SiraUtil, SongCore) are referenced from `$(BeatSaberDir)\Plugins\*.dll`, Harmony/Newtonsoft from
  `Libs\`, BSIPA itself from `Beat Saber_Data\Managed\IPA.Loader.dll`. In CI these come from BeatMods zips extracted on top of
  the stripped game tree.

### 1.5 Publicizing
- `BepInEx.AssemblyPublicizer.MSBuild` 0.4.x on nuget.org (Heck `Heck.csproj:73`, BeatLeader csproj :358). Mark individual
  references `Publicize="true"` (Heck :32,36,45,50,52,56,61). Lets you access private game members without reflection.
- `BSIPA.AssemblyPublicizer.MSBuild 0.5.2` (CustomAvatars `Common.props:33`) is NOT on nuget.org (checked
  `api.nuget.org/v3-flatcontainer/bsipa.assemblypublicizer.msbuild` -> BlobNotFound); it comes from nicoco007's GitHub
  Packages feed. CustomAvatars also uses `SkipOverridableMethods="False"` on one ref (`CustomAvatar.csproj:51`).
- BSIPA's own built-in publicizer is not used by any of these mods.

### 1.6 Build-task packages and where they come from (matters for CI auth and Dependabot)
| Package | Source | Used by |
|---|---|---|
| `BeatSaberModdingTools.Tasks` 2.0.0-beta7 (latest; still "beta") | nuget.org | BeatLeader, nicoco007 template |
| `BepInEx.AssemblyPublicizer.MSBuild` 0.4.x | nuget.org | Heck, BeatLeader |
| `BeatSaberModdingTools.Tasks.Luna` 2.1.0, `LunaBSMod.Tasks` 1.1.0 | Aeroluna GitHub Packages (`Heck/NuGet.config:5`) | Heck |
| `nicoco007.BeatSaberModdingTools.Tasks` 0.1.0, `BSIPA.AssemblyPublicizer.MSBuild` | nicoco007 GitHub Packages (`BeatSaberCustomAvatars/NuGet.config:5`) | CustomAvatars |

GitHub Packages NuGet feeds require authentication even for public packages. CI does
`dotnet nuget update source "... Github Packages" --username ${{ github.actor }} --password ${{ secrets.GITHUB_TOKEN }}`
(`Aeroluna-workflows/.github/workflows/bs-build-version-release.yml:33-34`, `BeatSaberCustomAvatars/.github/workflows/build.yml:26-27`),
and every contributor must create a classic PAT with `read:packages` (owner's answer in nicoco007/BeatSaberCustomAvatars issue #156).
That is real contributor friction for an OSS repo. Only the BeatLeader stack is restorable anonymously.

### 1.7 Copy-to-Plugins on build
- `CopyToPlugins` target, skipped in CI: `beatleader-mod/Source/Directory.Build.targets:75-99`. Nice detail: if `Beat Saber`
  is running (`IsProcessRunning`, :87-89) it copies into `IPA\Pending\Plugins` instead (:92), which BSIPA applies on next launch,
  and warns to restart (:98). Also copies the `.pdb` (:97) for line numbers in stack traces.
- nicoco007 version: copies the whole Artifact tree (Plugins/, Libs/, extra files) into `$(BeatSaberDir)` when `$(CI)` is empty
  (`BSMT.BSIPA.targets:71-79`). Simpler, no pending-folder logic.

### 1.8 Analyzers / style
StyleCop.Analyzers (Heck `Heck.csproj:75`, template `Directory.build.props:69`), Microsoft.Unity.Analyzers (CustomAvatars
`Common.props:49`), Microsoft.CodeAnalysis.CSharp.CodeStyle, Meziantou.Analyzer (BeatLeader). `.editorconfig` in Heck and
CustomAvatars `Source/`. Unity analyzers are worth adding (catch `?.` on UnityEngine.Object, etc).

## 2. How CI builds without owning the game

### 2.1 Two inputs are needed: stripped game DLLs + mod dependency DLLs
**Game DLLs.** Public GitHub repos hold stripped copies of `Beat Saber_Data/Managed/*.dll`, one ref per game version:
- `nicoco007/BeatSaberReferenceAssemblies` (cloned): tags `v1.0.0` .. `v1.45.3` (104 tags), 277 DLLs, 33 MB tree.
  `Main.dll` is 792 KB in the clone and carries no `ReferenceAssemblyAttribute`; the repo purpose is stripped game assemblies,
  I did not verify whether method bodies are removed.
- `Aeroluna/beatsaber-stripped`: branches `version/<x.y.z>` up to 1.44.2. Produced with `Aeroluna-GenericStripper`.
- `beat-forge/beatsaber-stripped`: branches up to `version/1.42.0`; last push 2025-12. Lagging.
These are copies of a commercial game's binaries (stripped) on public GitHub. It is the community norm and has persisted for
years, but it is the user's call whether to depend on it; there is no official reference package.
History: earlier the refs were private and needed secrets: MultiplayerCore still has the commented-out
`ProjectSIRA/download-sira-stripped` step with `secrets.SIRA_SERVER_CODE` (`MultiplayerCore/.github/workflows/Build.yml:20-24`),
and nicoco007's refs needed a PAT (`nicoco007-BeatSaberModTemplate/.github/workflows/build.yml:21`
`access-token: ${{ secrets.BEAT_SABER_REFERENCE_ASSEMBLIES_TOKEN }}`; input removed from setup-beat-saber 2026-07-11, commit
"Remove obsolete access token input"). Today none of the three need a secret.

**Mod dependencies.** Read the dependency list (manifest.json or `DependsOn` items), query BeatMods for the game version,
download and extract zips into the same tree.

### 2.2 The actions, compared
| Action | Refs source | Deps source | Reads deps from | Notes |
|---|---|---|---|---|
| `beat-forge/init-beatsaber@v1` + `Goobwabber/download-beatmods-deps@1.3` (BeatLeader, MPCore) | beat-forge/beatsaber-stripped (default) | BeatMods `api/v1` only | manifest.json | Goobwabber action declares `using: node12` at tag 1.3 (checked `git show 1.3:action.yml`); runner force-upgrades it with a warning. No GitHub fallback for deps. init-beatsaber last pushed 2024-10. |
| `beat-forge/init-beatsaber@v1` + `Aeroluna/download-beatmods-deps@main` (Heck, via `Aeroluna-workflows`) | Aeroluna/beatsaber-stripped (`bs-build-version-release.yml:39-44`) | BeatMods, then GitHub releases of `additional-sources` repos (`Aeroluna-download-beatmods-deps/action.yml:34-37`, `src/main.ts:150-170`) | csproj via `dotnet msbuild -getProperty:GameVersion -getItem:DependsOn` (`src/main.ts:221-252`) | Fixes the "dep not on BeatMods" problem. Pinned `@main` by consumers. |
| `nicoco007/setup-beat-saber@v1` (CustomAvatars) | nicoco007/BeatSaberReferenceAssemblies zipball by tag (`src/main.ts:171-175` via API) | new BeatMods API `beatmods.com/api/mods` + `cdn/mod/<hash>.zip` | csproj (`project-path`) | One step does both. `using: node24`, has Jest tests and Dependabot. Supports `aliases` and `additional-dependencies` for build-only deps (`BeatSaberCustomAvatars/.github/workflows/build.yml:19-24`). Most current option. |

### 2.3 Hard-won CI gotchas
- **Every dependency must be downloadable by CI or CI is permanently red.** BeatLeader depends on `Reactive.BeatSaberSDK`
  (`beatleader-mod/Source/manifest.json:18`), which is not on BeatMods. The 2026-10-21 run logs
  `Mod 'Reactive.BeatSaberSDK' version '^0.2.0' not found` then `error CS0246: The type or namespace name 'Reactive' could not be found`.
  All 82 runs still retained by GitHub for that repo are failures; maintainers just ship from local builds. Aeroluna's
  `additional-sources` (GitHub release fallback) or setup-beat-saber's `additional-dependencies` is the fix.
- **The extract path must use a backslash on Windows.** Repeated in three places as "it is VERY IMPORTANT that this be a backslash
  or else ResolveAssemblyReferences will not recognize it properly as a TargetFrameworkDirectory"
  (`Aeroluna-workflows/.github/workflows/bs-build-version-release.yml:42`, `Aeroluna-download-beatmods-deps/action.yml:11`,
  setup-beat-saber `action.yml` input `path`).
- **Windows vs Linux runners.** Heck and CustomAvatars use `windows-latest`. BeatLeader/MPCore use `ubuntu-latest` with the
  `..\Refs` HintPath style; MPCore's PR build needed `FrameworkPathOverride: /usr/lib/mono/4.8-api`
  (`MultiplayerCore/.github/workflows/PR_Build.yml:37-38`), and its commit log shows a day of flailing over it
  (2025-01-25: "Downgrade dotnet version, attempt to get build action to work again", then revert, then "Try and see what happens
  without FrameworkPathOverride"). `windows-latest` avoids this class of problem; the nicoco007 NoStdLib approach also avoids it.
- **Stale action versions break silently.** MPCore's PR workflow still uses `actions/upload-artifact@v3`
  (`PR_Build.yml:48`), which GitHub has retired. BeatLeader uses `::set-output` via `<Message>` (`Directory.Build.targets:53-55`),
  deprecated in favor of `$GITHUB_OUTPUT`; the Luna fork switched to a `SetActionOutput` task (`Aeroluna-BeatSaberModdingTools.Tasks.Luna/.../BuildTypes/BSMT.BSIPA.targets:136-140`)
  and nicoco007 to `WriteLinesToFile File="$(GITHUB_OUTPUT)"` (`nicoco007-BeatSaberModdingTools.Tasks/.../BSMT.BSIPA.targets:82-89`).
- **BOM in manifest.json.** The deps action warns `BOM character detected at the beginning of the manifest JSON file` (BeatLeader
  run 2026-10-21). Visual Studio writes BOMs by default; see section 7.
- **Fallback to latest game version.** If the requested game version is unknown to BeatMods, Aeroluna's action silently uses mods
  for the newest version (`Aeroluna-download-beatmods-deps/src/main.ts:37-43`, a `warning`, not a failure). Watch for it.

### 2.4 Build outputs and the BeatMods zip
- Build writes an artifact directory mirroring the game folder: `Plugins/<Mod>.dll` (+ `.pdb`, `.xml`), `Libs/` for libraries,
  plus any extra files (`BSMT.BSIPA.targets:51-68`; CustomAvatars adds template avatars via
  `<ArtifactFiles Include="...\Packaging\CustomAvatar\**\*.*" />` at `CustomAvatar.csproj:66`). Zip that directory and users
  extract it into the game root. That is the BeatMods format.
- Targets publish `artifact-name` / `artifact-path` as step outputs so the workflow can do
  `actions/upload-artifact` with `name: ${{ steps.build.outputs.artifact-name }}` (`BeatSaberCustomAvatars/.github/workflows/build.yml:29-37`).
- Artifact naming encodes the game version: `CustomAvatar-v5.4.13+bs.1.41.1.zip`, `NoodleExtensions-1.7.21+1.42.1-bs1.42.1-3bbcaf6.zip`,
  `BeatLeader-0.10.0-bs1.42+.zip`. nicoco007 puts SemVer build metadata in the version: `+dev` locally, `+bs.<game>` on tag builds,
  `+bs.<game>.git.sha.<sha>` on other CI builds (`BSMT.BSIPA.targets:15-19`).
- Local Release builds also zip (`ZipRelease`, `beatleader-mod/Source/Directory.Build.targets:60-73`); Heck re-implements it to
  key off a `RELEASE` constant because its configurations are named `Release-1.42.1` (`Heck/Directory.Build.targets:2-23`).

## 3. Release flow

| Repo | Version source | Trigger | GitHub release | BeatMods |
|---|---|---|---|---|
| Heck | `<Version>` per csproj (`Heck.csproj:15`), commit "version bump" | push to `master` (`build.yml:3-11`) | `bs-create-release-from-date.yml`: tag `v<YYYY-MM-DD>` (moved if it exists, :19-36), zips each artifact (:44-47), DRAFT release with generated notes (:49-59), created by `github-actions[bot]`; maintainer publishes it | separate manual `workflow_dispatch` `upload.yml` with a map of mod id to BeatMods id and `secrets.PAT` (`Heck/.github/workflows/upload.yml:3-13`), via `Aeroluna/beatmods-upload` |
| CustomAvatars | `Common.props:18` | every push (no branch filter, `build.yml:3-9`) uploads artifacts; tag push builds a release candidate | human creates the release (v5.4.13 author `nicoco007`) and attaches 3 zips | manual |
| BeatLeader | `manifest.json:6`, commits "Version bump" | push `master`, tags `v*`, PRs (`.github/workflows/main.yml:3-8`) | `softprops/action-gh-release` on tag (`main.yml:43-49`), but CI is red so releases are made by hand (v0.10.0 author `NSGolova`) | manual; issue #75 "Outdated version on BeatMods": "Beat Mods has long and difficult review process" |
| MultiplayerCore | `AssemblyVersion` in csproj + manifest | push main / PR | manual | manual |

Key facts:
- **Tag must equal version.** nicoco007's `ValidateTag` target fails the build if `GITHUB_REF_NAME != v$(Version)` on tag builds
  (`nicoco007-BeatSaberModdingTools.Tasks/.../BSMT.BSIPA.targets:25-28`, also in the template `Directory.build.targets:23-26`).
  `Aeroluna-workflows/.github/workflows/bs-create-release-from-project.yml:19-23` reads the version from the csproj instead and
  creates the tag. Either is a good guard.
- **gameVersion bumps** are a one-line change in csproj/props (or manifest.json in older layouts). Heck shows a full game-version
  update in one commit: `c9b4353` "drop 1.39.1, add 1.40.8, update to 1.41.2" touches both workflow matrices, `.sln`, csproj
  `Configurations` and the version-specific code paths.
- **BeatMods.** The 2025 BeatMods rewrite has a REST API: `GET beatmods.com/api/versions?gameName=BeatSaber`,
  `GET /api/mods?gameVersion=...`, `POST /api/mods/<id>/upload` with multipart `file`, `modVersion`, `platform`,
  `dependencies`, `supportedGameVersionIds` and `Authorization: Bearer <GitHub PAT>`
  (`Aeroluna-beatmods-upload/src/beatmods.ts:54-127`; README: the PAT "does not require any permissions", it is identity only).
  The mod must already exist on BeatMods (you pass its numeric id). Uploads still go through human approval, so "auto-release"
  can only mean "auto-submit".
- Release notes: Heck relies on `generateReleaseNotes: true`, then edits the draft by hand.

## 4. Testing, logging, debugging

- **Unit tests: none in any of the four mods.** The only test projects are in tooling: `BSIPA/IPA.Tests`,
  `nicoco007-BeatSaberModdingTools.Tasks/BeatSaberModdingTools.Tasks.Tests` (run in CI with `dotnet test --logger GitHubActions`,
  `.github/workflows/build.yml:16-17`), `Aeroluna-BeatSaberModdingTools.Tasks.Luna/BSMTTasks_UnitTests`, and Jest suites in the
  TS actions (`nicoco007-setup-beat-saber/tests/*.test.ts`, `Aeroluna-beatmods-upload/__tests__`). CI for mods means "it compiles
  against game version X (or N versions)". Testing is manual in-game. If the user wants tests, put pure logic in classes with no
  UnityEngine/game types and test them in a separate net472/net8 test project; anything touching game types needs the real game.
- **Logging.** BSIPA injects `IPA.Logging.Logger` into the `[Init]` constructor (`beatleader-mod/Source/Plugin.cs:34`, template
  `nicoco007-BeatSaberModTemplate/BeatSaberMod/Plugin.cs:7-13`). Larger mods inject SiraUtil's `SiraLog` through Zenject
  instead (Heck `Heck/Heck/HarmonyPatches/PatchedPlayerInstaller.cs:27-29`). CustomAvatars wraps it in its own
  `ILogger<T>`/`ILoggerFactory` (`Source/CustomAvatar/Logging/ILogger.cs:19`). Logs land in `<game>/Logs/`.
- **Debug launch profiles.** `Properties/launchSettings.json` with two profiles, normal and FPFC, both
  `--verbose --debug` and env `SteamAppId=620980` (`nicoco007-BeatSaberModTemplate/BeatSaberMod/Properties/launchSettings.json:2-21`;
  same in CustomAvatars). `fpfc` = first-person flying controller, play in desktop mode without a headset;
  `--verbose` shows the BSIPA console window; `--debug` enables BSIPA debug logging. `SteamAppId` lets the exe start
  without going through Steam. Mods detect fpfc via the command line (`beatleader-mod/Source/7_Utils/Miscellaneous/InputUtils.cs:15`)
  or SiraUtil's `IFPFCSettings` (BeatLeader `SiraFPFCTweak.cs:12`).
- **Debugger.** Copying the `.pdb` next to the dll (BeatLeader targets :97, nicoco007 targets :61) gives line numbers in stack
  traces. Breakpoint debugging needs a Unity debug/development player swap plus the IDE's Unity debugger; none of these repos
  automate it. BeatLeader issue #58 "Exception with unity debug build" shows people do run it.

## 5. Handling multiple game versions

Three distinct strategies:

1. **One branch, many configurations + generated constants + CI matrix (Heck).** `Heck.csproj:8` declares
   `Debug-1.29.1;Release-1.29.1;...;Release-1.42.1`. `LunaBSMod.Tasks` runs as an `InitialTargets` target
   (`Aeroluna-LunaBSMod.Tasks/LunaBSMod.Tasks/LunaBSMod.Tasks.props:6`, `.targets:3-15`) and
   `GetConfigurationInfo.cs:32-73` turns `Release-1.37.1` into `RELEASE;V1_37_1;PRE_V1_40_8;PRE_V1_42_1;` and adds `LATEST`
   for the newest. It also sets `GameVersion` and `AppendGameVersion=true` (`.props:18`) so the version becomes
   `1.7.21+1.37.1`. Code uses `#if !PRE_V1_37_1` (89 occurrences), `#if !PRE_V1_40_8` (74), `#if LATEST` (26).
   CI: matrix `project-path x game-version` = 3 x 5 = 15 builds (`Heck/.github/workflows/build.yml:16-19`), each restoring that
   version's stripped DLLs and BeatMods deps, one release with 15 zips. Locally you need one game install per version
   (`BeatSaberDir` presumably set conditionally in the gitignored .csproj.user; not documented in the repo).
   Highest capability, highest complexity; fits a library many maps depend on.
2. **Branch per game version (BeatLeader).** 48 remote branches including `1.29.4`, `1.37.3`, `1.40.0` ... `1.42.0`,
   `1408support`. No version `#if`s in source. A new game version is a new branch, merged back into master
   ("Merge branch '1.42.0'"). Releases ship separate zips per game range (`BeatLeader-0.10.0-bs1.40.2+.zip`, `-bs1.42+.zip`).
   Simple per branch, painful to backport fixes.
3. **Single target version (CustomAvatars, MultiplayerCore).** One `GameVersion` (`Common.props:17`); old game versions keep
   old releases. This is what most mods do and what a new mod should do.

## 6. Lessons and gotchas (collected)

1. Generate the manifest from csproj items; a checked-in manifest.json plus csproj version drifts (BeatLeader needs a
   `GetManifestInfo` mismatch check to catch it).
2. Pick dependencies that CI can fetch. If a dep is GitHub-only, use an action with a GitHub fallback, or CI stays red (BeatLeader).
3. Prefer nuget.org packages; GitHub Packages feeds force every contributor and Dependabot to hold a PAT (CustomAvatars #156).
4. Use `windows-latest` unless you also adopt the NoStdLib/TargetFrameworkDirectory override; Linux needs Mono reference
   assembly hacks (MPCore).
5. Disable copy-to-game and zip-on-build in CI (`$(CI)`/`GITHUB_ACTIONS` conditions), otherwise the build errors looking for
   `Beat Saber.exe`.
6. Keep `Private=False` on every game/plugin reference or you ship copies of game DLLs in your zip.
7. Put the pdb in the release zip; users' logs then contain line numbers.
8. BeatMods review is human and slow; GitHub Releases is where users get hotfixes first (BeatLeader README tells users to install
   from Releases).
9. `BeatSaberModdingTools.Tasks` upstream (Zingabopp) has been at `2.0.0-beta7` for years; the two active forks (Luna,
   nicoco007) are on GitHub Packages only. The nicoco007 template sidesteps this by INLINING the targets into
   `Directory.build.targets` and setting `ImportBSMTTargets=False` (`Directory.build.props:15`), using the nuget.org package only
   for its `GenerateManifest` task. That is the most portable setup found.
10. The nicoco007 template workflow is stale: it passes `access-token` (`.github/workflows/build.yml:21`), which setup-beat-saber
    no longer accepts. Copy the CustomAvatars workflow instead.
11. Pin third-party actions to tags/SHAs. Heck consumes `Aeroluna/workflows/...@main` and `Aeroluna/download-beatmods-deps@main`,
    so upstream pushes change its build.

## 7. Clashes with the user's engineering standards

| Standard | Beat Saber norm observed | Verdict / how to reconcile |
|---|---|---|
| CI on every PR and push to main | Heck builds only on push to `master`/`dev` (`build.yml:3-5`, `build-dev.yml:3-5`); CustomAvatars on push to any branch; only BeatLeader and MPCore have `pull_request` triggers | No real clash. Public stripped refs + BeatMods mean CI needs no secret, so PR builds from forks work. Add `pull_request` + `push: main`. |
| CI needs game DLLs | Solved by community stripped-DLL repos; no secrets needed in 2026 | Works, but depends on third-party repos of the game's binaries (legal gray area, availability risk). nicoco007's is the most current (to 1.45.3). |
| Push to main releases automatically, gated on green CI | Heck auto-creates a DRAFT release on push to master; others release by hand | GitHub Release automation is fine: a release job with `needs: build`, tag from csproj `Version`, refuse if the tag exists. BeatMods publication cannot be automatic (human approval); at most auto-submit via `Aeroluna/beatmods-upload`. Consider releasing only when `Version` changed, otherwise every merge creates a release. |
| Default branch `main` | Heck, BeatLeader: `master`; CustomAvatars, MPCore: `main` | No clash. |
| Conventional Commits | None of the four use them ("version bump", "Version bump", "BS 1.42.0 Support") | No tooling depends on commit format; adopt freely. |
| ASCII-only files | BOMs are universal: Heck 210 of 271 tracked files, BeatLeader 505/712, CustomAvatars 142/390, MPCore 57/80. Beyond BOMs, real non-ASCII content: copyright sign in headers (CustomAvatars, 122 lines), Japanese/other UI strings (BeatLeader `Source/2_Core/Localization/TimeLocalizationUtils.cs:173`). Heck has zero non-ASCII past line 1 | Achievable for a new mod if you (a) set `.editorconfig` `charset = utf-8` (not `utf-8-bom`) so VS/Rider stop adding BOMs, (b) write "Copyright (c)", (c) keep localized strings out of source (e.g. JSON resource; but that file then is non-ASCII, so exempt localization files in the check). The ASCII check script will flag VS-generated files immediately. |
| LF via .gitattributes | Heck: `* text=auto` only (`Heck/.gitattributes:4`); MPCore has 41 CRLF files in the index; CustomAvatars none | Use `* text=auto eol=lf`. Mark `*.dll`, `*.png`, `*.zip`, `*.avatar`, asset bundles `binary`. .sln files are fine with LF for modern VS. |
| Dependabot + auto-merge minor/patch | No mod repo uses Dependabot; tooling uses Dependabot (`nicoco007-setup-beat-saber/.github/dependabot.yml`) or Renovate (GenericStripper, init-beatsaber) | `nuget` ecosystem covers build tasks, publicizer, analyzers; it cannot see game DLLs or BeatMods deps (those are HintPath refs and `DependsOn` items). GitHub Packages feeds would need a `registries:` entry with a PAT secret; stay on nuget.org. `github-actions` ecosystem works if actions are pinned to tags, not `@main`. Watch `BeatSaberModdingTools.Tasks` 2.0.0-beta7: prereleases are not offered by default, fine. Analyzer bumps can introduce new warnings; with `TreatWarningsAsErrors` that makes auto-merge PRs fail, which is the desired gate. |

## 8. Recommended scaffold for a new mod (derived from the above)

- SDK-style csproj, `net472`, `LangVersion` 12+, `Nullable` on, references via `ItemDefinitionGroup Private=False` + one-line
  `Reference ... HintPath="$(BeatSaberDir)..."` (Heck style) or the CustomAvatars no-HintPath style with the
  `TargetFrameworkDirectory` override inlined in `Directory.Build.targets` (nicoco007 template style).
- Manifest generated from `PackageId/Title/Authors/Version/GameVersion/Description` + `DependsOn` items; `ValidateTag` target.
- Packages only from nuget.org: `BeatSaberModdingTools.Tasks` 2.0.0-beta7 (for `GenerateManifest`), `BepInEx.AssemblyPublicizer.MSBuild` 0.4.x,
  `Microsoft.Unity.Analyzers`, `PolySharp`. No custom `NuGet.config` feeds.
- `.csproj.user` (gitignored) with `BeatSaberDir`; copy-to-game target disabled when `$(CI)` is set; `launchSettings.json` with
  normal and FPFC profiles.
- CI (`windows-latest`, on `pull_request` and `push: main`): checkout, `actions/setup-dotnet`, `nicoco007/setup-beat-saber@v1`
  with `project-path`, `dotnet build -c Release -warnaserror`, upload artifact from the `artifact-path` output.
  Release job `needs: build`, only on main, only when the csproj `Version` has no tag yet: create tag `v<Version>`, zip
  artifact, `gh release create`. BeatMods: manual first submission on the site, later `workflow_dispatch` with `Aeroluna/beatmods-upload`
  pinned to a SHA and a PAT secret.
- Target a single game version; branch only if you must support an older one.
