# cloud/dead-code - notes for the reviewer

Branch `cloud/dead-code` from origin/main (9aca2f5). One commit per task. Every
code commit: plugin builds as CI does (BepInEx stub UnityEngine) with 0 warnings,
`dotnet test tests/ForestOverlay.Tests` 920 pass, `python scripts/lint.py` ok.
T-0171 also `dotnet test bot/ForestBot.Tests` 55 pass. No version bump, no
CHANGELOG, no task-file edits. Not tried in game (none here).

| Task | Result | Commit | Look at |
|---|---|---|---|
| T-0170 | done | 79b12f4 | six `docs/x.md` links inside docs/ now `x.md`; worktree paths and `docs/ui-redesign.md` (exists only on branch ui-redesign) dropped from the text. `audit.py` no longer flags session-log.md |
| T-0171 | done | 36393d9 | HybridSearch `HasVectors`, `Similarity` removed (bot/ change: pushing it to main deploys the bot) |
| T-0172 | done | d495922 | CursorController.UsingGameFlag (+ its comment) |
| T-0173 | done | fc5aa7e | LogKeeper.CurrentFile |
| T-0174 | done | 060b7a4 | MoveDetector.LiftSoFar |
| T-0175 | done | 9eeb6a9 | DeathHooks.CanClearBlood |
| T-0176 | done | 84543d8 | InventoryReader.ReportedCount ("kept for comparison only"; no doc/task plans it) |
| T-0177 | done | cbcbb0a | PlayerStateReader.ChannelCount, TryGet |
| T-0178 | done | 48e6209 | ReplayDraw.BuildingCount, MarkerCount |
| T-0179 | done | f217ad8 | SetupHold.RequestedAfter |
| T-0180 | **skipped** | - | DebugViewModule.AerialStop: unreferenced by name, but it is the stop for the bridge-driven bake (`AerialStart` is called by name in scripts/aerial-bake.py and docs/website.md). Kept as a bridge entry point |
| T-0181 | partly | 69b7973 | SpotPosition removed. `ImportLiveSplitFile` **kept**: its doc comment says it is the bridge's way in (a click the bridge cannot make) |
| T-0182 | **skipped** | - | PracticeRunModule.ReplayCameraOn: comment says "the bridge reads this" (property read over the bridge) |
| T-0183 | done | 5fbe73f | save-diff.py nested `objname` removed; `an`/`bn` unpacking left |
| T-0188 | done | 74282f8 | LatePass.OnRenderImage catch now `Graphics.Blit(src, dst)` (guarded). Needs the in-game check (e2e smoke); a flag to skip a double blit was dropped because lint requires the body to start with `try` |

Reviewer: all removed members were plain properties/methods that grep (whole repo,
incl. tests, docs, scripts, tasks) found only at their definition. Because the
bridge can `get`/`call` members by name, a one-off bridge use of a removed
property (CurrentFile, LiftSoFar, SpotPosition, BuildingCount, MarkerCount,
CanClearBlood, ...) would not show up in a grep; none is in docs or e2e. Tasks
T-0180, T-0182 and the ImportLiveSplitFile half of T-0181 should be closed as
kept/wontfix with those reasons.

Also: docs/quality.md gets `cloud-notes/` added to the workflow area Paths (lint requires every tracked file to be in an area); drop it together with the notes file when you merge.
