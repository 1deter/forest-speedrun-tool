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

2. **Checker first** (author, 2026-10-07): every built plugin task going
   into this release has an accept review - `tasks.py list --status
   built`, and for each without one spawn `forest-checker` with
   `Check T-n` (docs/areas/workflow.md *The checker*). A revise is fixed
   and checked again before the bump; `bump.py` refuses (before it
   edits anything) a built plugin task in HEAD with no accept.

3. **Changelog bullets**: a few short runner-facing lines in plain words,
   no internals (`CHANGELOG.md` shows them; "What's new in vX" in game).
   A number in a bullet is measured, never estimated (gotcha 44).

4. **Bump, build, test, commit - one `&&` chain** so a failed step stops
   it (gotcha 65; a script edit that belongs to the release goes in the
   same chain):
   ```bash
   python scripts/bump.py 0.24.N "Bullet one." "Bullet two." && dotnet build -c Release -p:ForestManagedPath="G:\SteamLibrary\steamapps\common\The Forest\TheForest_Data\Managed" && dotnet test tests/ForestOverlay.Tests/ForestOverlay.Tests.csproj && git add CHANGELOG.md ForestOverlay.csproj src/Plugin.cs tasks/tasks.jsonl docs/tasks.md && git commit -m "v0.24.N: <summary>" && git tag v0.24.N && git push origin main v0.24.N
   ```
   (stage the change itself first if it is not committed yet;
   `-f notes.md` instead of bullets for longer notes; commit message
   ends with the session's Co-Authored-By line.) `bump.py` edits the
   three files and marks the release's tasks (built plugin tasks whose
   commits are all in HEAD -> `released`, with the version; it prints
   them) - it does not commit, tag or push.

5. **Wait for the asset, not the release** (a tag publishes before CI
   attaches the DLL). Poll in the background, never `api.github.com`:
   ```bash
   until [ "$(curl -s -o /dev/null -w '%{http_code}' -L https://github.com/1deter/forest-speedrun-tool/releases/download/v0.24.N/ForestOverlay.dll)" = 200 ]; do sleep 30; done; echo attached
   ```
   Say in chat when it is attached. No asset after ~15 min: the build
   badge (`python scripts/session-start.py` prints it) - a missing
   CHANGELOG section fails the release.

   **Then the post-release smoke** (docs/harness.md 8b; needs the game,
   nothing else - the suite launches it): installs the release the
   runners' way, restarts, loads Slot 1, checks the version and that the
   log has no exception, F7 on a spot with a start state; the result goes
   onto the release's tasks as a note:
   ```bash
   python scripts/e2e.py --smoke --update --release v0.24.N
   ```
   A FAIL is the next task, before anything new (the report in
   `tests/e2e/reports/` says which check).

6. **Handoff in the next push** (docs-only, no version): the router's
   *Where we are* (unreleased line), new gotchas in `docs/gotchas.md` +
   the area index, confirmed items moved to `docs/confirmed.md`.

7. **Confirm in game**: a plugin task is `confirmed` by in-game evidence
   from someone other than its maker (router rule 10): the e2e suite
   (`python scripts/e2e.py --evidence` records its journeys' tasks
   `--by e2e`) or `forest-tester` after `update_game` installs the release (`tasks.py evidence T-n
   "..." --by forest-tester`). Something only a tester can check: a
   `needs: tester` task with a `qa` line, then `qa_todo from_tasks`
   (docs/bridge.md *The QA Discord*; lists go out as plain numbered text).
