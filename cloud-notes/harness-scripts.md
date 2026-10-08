# Cloud notes: harness-scripts (branch cloud/harness-scripts)

No tasks.jsonl / loop.jsonl / docs/tasks.md edits; bookkeeping is main's. Each commit was run
with `python -m unittest discover scripts/tests` and `python scripts/lint.py` (both green).

| Task | Result | Commit |
|---|---|---|
| T-0147 | done | 4d71254 |
| T-0154 | done | 789d0db |
| T-0169 | done | 3a54c56 |

## T-0147 - CI runs every scripts/tests file
`.github/workflows/build.yml`: the seven per-file lines are gone; one `Script tests` step runs
`python3 -m unittest discover scripts/tests`. New `scripts/tests/test_ci.py` fails if a per-file
list returns. Look at: that `test_site_smoke.py` / `test_e2e.py` are fine on the CI runner (they
pass here with no game or site); `site.yml` still runs test_site_smoke separately. CI itself was
not run from here.

## T-0154 - loop.py release step
`scripts/loop.py`: `needs_release(t)` reads the commits (`git show --name-only`) and releases only
if one touches `src/ patcher/ locations/ collectibles/ qa/ ForestOverlay.csproj`. Otherwise a built
plugin task goes to the evidence step (the old "ship" branch, which would also have fired for
plugin because it is in CHECKED_AREAS, is now site/bot only). Unknown commits / no commits count as
plugin (safe default). Look at: the PLUGIN_PATHS list vs the release skill; and whether
`tasks.release_plan` (bump.py) should use the same rule - I left it alone.

## T-0169 - cleanup.py duplicate branch
`scripts/cleanup.py`: pure `local_to_delete(local, worktrees)` returns each name once. Look at:
the cause was a merged worktree's branch appearing both in `survey()["local"]` and in the
removed-worktree list. Not reproduced against real worktrees (none here).
