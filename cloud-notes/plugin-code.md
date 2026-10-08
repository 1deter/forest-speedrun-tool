# Cloud session: plugin-code (2026-10-08 night)

Branch `cloud/plugin-code` from origin/main d471284. dotnet 8 SDK installed in the
container; CI-style build (BepInEx stub) 0 warnings, 922 tests, `scripts/lint.py` ok
at every commit. No version bump, no CHANGELOG edit, tasks.jsonl untouched.

| Task | Result | Commit |
|---|---|---|
| T-0192 | done | 225897b |
| T-0193 | done | dbff4b6 |
| T-0110 | done | 4a9fce0 |
| T-0145 | done | 4ca8d44 |
| T-0109 | skipped - question below | - |
| T-0111 | skipped - question below | - |
| T-0112 | skipped - question below | - |

Note: 225897b's `scripts/lint-baseline.txt` change also carries T-0193's `cfgwrite`
removal (I passed the file to the first commit). Harmless together; 225897b alone
would fail the lint on the InventoryModule hit.

## Done - what main checks in game

- **T-0192** `DebugViewModule` aerial capture's `MovePlayer` now runs
  `AreaKeeper.ForTeleport` (own `AreaKeeper`, as the bridge tp does) before `MoveTo`,
  logging `Aerial capture: <note>` when it left an area or stopped a ride
  (`docs/log-lines.md` regenerated). `moveto` baseline entry dropped.
  Check: run an aerial capture started from the endgame / during the red-elevator
  ride; no stale area flag or dark surface after, and it still returns home.
- **T-0193** Inventory log-cap field holds the typed value in `_capLive` and writes the
  config 0.5 s after the last keystroke (in `TickLogs`, itself on the refresh interval
  so the write can lag a little more) and on `Shutdown`. The cap applies at once.
  `cfgwrite` baseline entry dropped. Check: type a cap, no hitch, value kept after restart.
  Runner-facing bullet: "Typing the log cap in the Inventory tab no longer hitches."
- **T-0110** `PracticeModule.BridgeGo` / `BridgeRestart` return the run-mode refusal text
  (`... is locked during a run (End run mode in the Runs tab).`) instead of null.
  `TasModule`'s two `BridgeRestart` callers already show a returned error.
  docs/bridge.md and run-mode.md known-gaps updated. Check: during a run mode attempt,
  bridge `go` / `restart <id>` answer the refusal and the player does not move. The
  e2e `runmode` journey does not appear to rely on the old `ok` (grep found nothing),
  but run it. No runner-facing change.
- **T-0145** plugin.md file map rows and plugin-concepts.md bullets for TAS and the
  trajectory preview, from the code's own comments and config. Main: re-grade the
  "TAS and trajectory" row in docs/quality.md (I did not touch it).

## Skipped - open questions (docs/run-mode.md does not settle them)

- **T-0109 (watch Time.timeScale)** run-mode.md only says "could be added". Questions:
  is a changed `Time.timeScale` a flag at all (the game itself sets it to 0 in menus and
  during cutscenes)? What values or durations count, is it red or amber, and what does the
  attempt page / report line say? Is the debug console's `_speedyrun` the only target?
- **T-0111 (report hashes the file on disk, not the loaded assembly)** the doc says
  "per-type hashes can hash the loaded code instead" but not what the report should do.
  Questions: always hash the loaded types (cost: ~3,700 lines / a worker-thread pass on
  every report, today only for an unknown file hash) or only compare file hash with a
  loaded-code check? What does it flag if file and loaded code differ, and what colour?
  Mono gives no cheap way to hash the in-memory image, so this is a design choice.
- **T-0112 (BepInEx patches on .NET methods skipped by assembly)** `RunIntegrity.IsRuntimeCode`
  skips any patch on mscorlib / System* / Mono* whoever owns it, so a foreign mod patching
  e.g. `File.ReadAllText` is invisible. To narrow it, the plugin must know BepInEx's own
  Harmony owner ids; run-mode.md/gotcha 82 only say they include `harmony-auto-<guid>`
  ids, which are not distinguishable from a mod's. Questions: which owner ids are BepInEx's
  (a dump from a clean install would confirm), and should a foreign owner on a .NET method
  be red, amber, or listed only? The runner-facing wording in the report is also needed.
