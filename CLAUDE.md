<!-- stack-template: claude-md/project-stub@2 6d8a8c8b3f72 -->

# bsaber-mods

## Commands

- `dotnet build MaxTweaks.sln -c Release` - build; the mod also copies itself into the game set in `src/MaxTweaks/MaxTweaks.csproj.user` (`BeatSaberDir`, gitignored; CI sets it via setup-beat-saber)
- `dotnet test --project tests/MaxTweaks.Core.Tests -c Release --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml` - unit tests + coverage (Microsoft.Testing.Platform, see global.json)
- `node scripts/check-ascii.mjs` - fail on non-ASCII bytes in tracked files (run after `git add -A`)
- Launch for QA: `Beat Saber.exe fpfc --verbose --debug` from the instance dir with env `SteamAppId=620980`; log at `<instance>/Logs/_latest.log`

## Layout

- `src/MaxTweaks/` - the BSIPA plugin (net472, game assemblies publicized). `Plugin.cs` only wires installers.
  - `Features/<Name>/` - one folder per feature with its own installers, settings host and code
  - `Configuration/` - `PluginConfig` + one config section class per feature (BSIPA store, `UserData/MaxTweaks.json`)
  - `Common/` - `SafePatcher` (per-feature Harmony unit, self-disables on failure), `Guard`, `FeatureStatus`,
    `Playback/` (in-place seeking, tempo map), `Pointing/` (world-space ray-hover buttons shared by Chapters and Active pause)
  - `UI/` - Mod Settings page (`Settings.bsml`, one section per feature via `macro.as-host`; wrap every text in a horizontal, no tabs) and quick tab
- `src/MaxTweaks.Core/` - game-free logic; compiled INTO the plugin DLL and unit tested on .NET
- `tests/MaxTweaks.Core.Tests/` - xUnit v3 tests for Core
- `tools/` - helper CLIs (e.g. chapters-from-bookmarks)
- `docs/research/` - pre-build research notes (version target, frameworks, patterns, build/CI)

## Conventions

- Target game: Beat Saber 1.40.8 only. Deps: BSIPA 4.3.6, SiraUtil 3.2.1, BSML 1.12.5, SongCore 3.15.3. When reading
  reference clones in `../`, read the tag matching these versions (`git show v1.12.5:path`), not their default branch.
- Code that runs inside game callbacks must be wrapped with `Guard`; global Harmony patches go through `SafePatcher`.
- Each feature checks its own `Enabled` config flag; features never depend on each other except via `Common/`.
- Core must stay free of Unity/game types and limited to netstandard2.0 + Newtonsoft.Json 13.0.2.

<!-- stack:begin (managed by the stack plugin; edit via /stack:remember or by hand, keep the markers) -->

## Stack

- scope: pmaxhogan
- mode: deps.dependabot-mechanism=native
- mode: release.on-main=github-release-zip
- mode: repo.no-protection=exception-required-checks-ruleset-for-native-automerge
- mode: stack.type=csharp-bsipa
- mode: test.coverage-ratchet=compare-job

<!-- stack:end -->
