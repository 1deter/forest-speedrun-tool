---
name: release
description: Release a ForestOverlay plugin version - git fetch and check origin/main, bump.py (csproj + Plugin.PluginVersion + CHANGELOG section), build + test, commit, tag, push, wait for the DLL asset, then the handoff. Use after any plugin change (src/, patcher/, locations/, collectibles/, qa/) that builds and passes tests, or when asked to release / ship / tag. Docs-only changes need no release.
---

# Release a plugin version

Facts behind each step (the updater, the 404 window, rollback):
`docs/areas/release.md`. Never deploy the DLL into the game by hand
(router rule 2) - the author's install updates through this release.

## Steps

1. **Sync first** (side-by-side sessions bump the same number):
   ```bash
   git fetch && git log --oneline HEAD..origin/main
   ```
   Anything listed: merge it (`python scripts/merge-keepboth.py <files>`
   for add/add conflicts), rebuild, retest. Then take the next number:
   `git tag -l "v*" --sort=-v:refname | head -1` + 1.

2. **Changelog bullets**: a few short runner-facing lines in plain words,
   no internals (`CHANGELOG.md` shows them; "What's new in vX" in game).
   A number in a bullet is measured, never estimated (gotcha 44).

3. **Bump, build, test, commit - one `&&` chain** so a failed step stops
   it (gotcha 65; a script edit that belongs to the release goes in the
   same chain):
   ```bash
   python scripts/bump.py 0.24.N "Bullet one." "Bullet two." && dotnet build -c Release -p:ForestManagedPath="G:\SteamLibrary\steamapps\common\The Forest\TheForest_Data\Managed" && dotnet test tests/ForestOverlay.Tests/ForestOverlay.Tests.csproj && git add -A && git commit -m "v0.24.N: <summary>" && git tag v0.24.N && git push origin main v0.24.N
   ```
   (`-f notes.md` instead of bullets for longer notes; commit message
   ends with the session's Co-Authored-By line.) `bump.py` only edits the
   three files - it does not commit, tag or push.

4. **Wait for the asset, not the release** (a tag publishes before CI
   attaches the DLL). Poll in the background, never `api.github.com`:
   ```bash
   until [ "$(curl -s -o /dev/null -w '%{http_code}' -L https://github.com/1deter/forest-speedrun-tool/releases/download/v0.24.N/ForestOverlay.dll)" = 200 ]; do sleep 30; done; echo attached
   ```
   Say in chat when it is attached. No asset after ~15 min: the build
   badge (`python scripts/session-start.py` prints it) - a missing
   CHANGELOG section fails the release.

5. **Handoff in the next push** (docs-only, no version): the task's
   `tasks.py set T-n --release v0.24.N` (and `--commit`), the router's
   *Where we are* (unreleased line), new gotchas in `docs/gotchas.md` +
   the area index, confirmed items moved to `docs/confirmed.md`.

6. **Behaviour change?** It is confirmed by a checker, never its maker
   (router rule 10): `forest-tester` for in-game behaviour, after
   `update_game` installs the release. Something for the QA team: the
   `bridge-test` skill's QA part (lists go out as plain numbered text).
