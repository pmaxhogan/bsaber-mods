<!-- stack-template: claude-md/project-stub@2 6d8a8c8b3f72 -->

# bsaber-mods

## Commands

- `node scripts/check-ascii.mjs` - fail on non-ASCII bytes in tracked files (run after `git add -A`)
- C# mod build commands: TBD once the target game version and build tooling are chosen

## Layout

- `scripts/check-ascii.mjs` - ASCII guard run in CI
- `.github/workflows/` - CI (ascii only for now) and Dependabot auto-merge
- Mod projects: TBD

<!-- stack:begin (managed by the stack plugin; edit via /stack:remember or by hand, keep the markers) -->

## Stack

- scope: pmaxhogan
- mode: deps.dependabot-mechanism=native
- mode: release.on-main=deferred-no-build-yet
- mode: repo.no-protection=exception-required-checks-ruleset-for-native-automerge
- mode: stack.type=csharp-bsipa
- mode: test.coverage-ratchet=deferred-no-tests-yet

<!-- stack:end -->
