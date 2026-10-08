# cloud/harness-checks - notes for the reviewer

Branch `cloud/harness-checks` from origin/main (5a13f2e). One commit per task. No game, no secrets;
`scripts/lint.py`, `tasks/*`, `docs/tasks.md` untouched. dotnet 10 / 8 installed from dotnet-install.sh
(into ~/.dotnet). Run before each commit: `python scripts/lint.py` (ok); suites below.

| Task | Status | Commit | Look at |
|---|---|---|---|
| T-0130 | done | b73a210 | `site/ForestSite.Tests/UploadedSetUrlTests.cs`: scans wwwroot/*.js for "/aerial/" and "/world/" URL literals (the sets' own .json meta is no-cache, exempt) and fails unless the URL is versioned. Needs >= 3 URLs seen so a broken pattern cannot pass silently. The terrain/ relief files are in git (not an uploaded set), so not covered. `dotnet test site/ForestSite.Tests --filter UploadedSetUrlTests` 2/2. |
| T-0132 | done | a37b5d3 | `bot/ForestBot.Tests/ModelAndAgentTests.cs`: 4 tests added to the existing busy/judge-busy ones (busy text never judged or reported; busy question stays out of the others' score; busy follow-up adds no wrong facts; a short 429 rest is waited out and only the real answer scored). Test-only. Full `dotnet test bot/ForestBot.Tests` 59/59. Observation, not changed: a busy *follow-up* is not counted in `EvalScore.Busy` (only a busy first answer is). |
| T-0133 | done | 6a1649d | New `tools/BridgeMcp/ChildProcess.cs` (redirect + close stdin); `Discord.cs` (qa_todo from_tasks) now uses it, behaviour same. **Real fix too:** the ilscan `dotnet` child in `Tools.cs` did not close stdin before. Tests: `tests/ForestOverlay.Tests/ChildProcessTests.cs` (a python child reading stdin returns at once; a source scan fails on any `Process.Start` in tools/BridgeMcp that is neither `ChildProcess.Start` nor a `UseShellExecute = true` launch). The helper is linked into the test csproj. `dotnet test tests/ForestOverlay.Tests` 922/922; `dotnet build tools/BridgeMcp -c Release` ok. Not run: the server against a live MCP client. |
| T-0134 | done | 0b0712d | New pure `scripts/world_checks.py` + `scripts/tests/test_world_checks.py`; `export()` in `scripts/world-extract.py` exits (WHAT/WHY/FIX) before the long read when one object is placed by more than one `placed-*.txt`. Keyed on the object name from the `greeble` line (a bobbing yacht dumped twice sits at two positions). Added to docs/quality.md's Site maps and 3D world Paths (lint requires it). **Not run:** the export (needs game files + UnityPy); wiring only py_compile-checked. |
| T-0135 | done | 498358f | `Export.texture` records textures with mean RGB <= 2.0 (after the colour/alpha resize); `Export.finish` exits with WHAT/WHY/FIX before packing if more than `KNOWN_BLACK = 1` came out near-black, else prints the count. **Decide/verify:** the allowance is a count, not the name (I could not confirm from the repo whether `BlackFadeIntoCaves` is the texture or the material name); the 2.0 threshold is my choice - run one real export and check the lowest legitimate mean is above it. **Not run:** the export. |
| T-0189 | done | 24f40a0 | `scripts/e2e.py`: `crash_folders()` / `CRASH_BASELINE` / `new_crashes()` replace the mtime test (a dated `YYYY-MM-DD_HHMMSS` folder holding crash.dmp that was not there at suite start). `main()` fails the run with a hygiene problem naming every one, re-checks after the clean-up, and puts the names in the final summary line. 4 unit tests with a temp folder in `scripts/tests/test_e2e.py` (10/10). **Not run:** the suite (needs the game). |

## Process note
My first T-0134 commit was rejected by the pre-commit lint (new file in no docs/quality.md area) without my
noticing, and an `--amend` then landed on the already-pushed T-0133 commit locally; I reset to the pushed
6a1649d (nothing force-pushed) and redid T-0134 with the quality.md line. Branch history is linear and clean.

## Left for main
- Gotcha index lines in docs/areas/{site,bot,workflow}.md still say `[check: T-0130]` etc. - bookkeeping is yours.
- Task file untouched, as instructed.
- `cloud-notes/` was added to the workflow area Paths in docs/quality.md so lint passes; drop it with the notes file once merged.
