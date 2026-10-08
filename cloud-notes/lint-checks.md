# cloud/lint-checks - notes for the reviewer

All nine tasks done; none skipped. No edits to tasks/*.jsonl or docs/tasks.md.
`python scripts/lint.py` and `python -m unittest discover scripts/tests` were
green at every commit except one slip (see T-0128). The gotcha index lines for
T-0123..T-0129 and T-0131 now name the lint instead of `T-n` (the lint's own
rule: a closed task in a marker fails), so they will not break when you close
the tasks.

| Task | Commit | Result |
|---|---|---|
| T-0187 | 7eab4e8 | `strip_block_comments`; no hits |
| T-0123 | d9e8a16 | `check_mojibake`; 3 hits, all docs that quote the garbled form (`MOJIBAKE_QUOTED`, with reasons) |
| T-0124 | 886400f | `check_deploy`; no hits |
| T-0125 | 2188ba4 | baselined rule `findall`; 20 existing call sites baselined |
| T-0126 | 263965b | `check_render`; LatePass allow-listed (`RENDER_NO_DRAW`) |
| T-0127 | 3c75571 | `check_web_requests`; no hits |
| T-0128 | b0170c7, f92bacd | baselined rule `moveto`; 7 existing callers baselined |
| T-0129 | dc8c988 | baselined heuristic `cfgwrite`; 2 hits baselined |
| T-0131 | 02294d1 | `check_bot_globalization`; no hits |

## Look at these

- **T-0125**: 20 `FindObjectsOfTypeAll` sites are baselined unjudged (the commit
  message says 13 files; it is 11 - DrawingsReader, InventoryReader,
  MemoryCensus, NatureGuideReader, ObjectProbe, PerfPatches, RenderProbe,
  SavestateBridge, SlidingDoorKeeper, SurvivalBookReader, WorldDump). Dev probes
  (ObjectProbe, WorldDump, MemoryCensus, RenderProbe) are probably fine; the
  readers (DrawingsReader, SlidingDoorKeeper, SurvivalBookReader, NatureGuideReader,
  InventoryReader) should be once-and-keep - check they are.
- **T-0126**: commit message says "1 of 8"; there are 7 `OnRenderObject` methods
  (DebugDraw x3, LatePass, ReplayDraw, TrajectoryView, ZonePreview). LatePass is
  allow-listed because its body only records the render target - read from the
  source, not tested in game.
- **T-0128**: 7 baselined callers: DebugViewModule.cs:138, SavestateModule.cs
  (982, 1008, 1028, 1528, 1531), EndgameLoader.cs:366. Whether each must call
  `AreaKeeper.ForTeleport` is the author's call (a restore's pin stays in one
  area); I did not decide it. The T-0128 slip: the first commit was pushed with
  one failing unit test (my own test source had modifier-less methods); f92bacd
  fixes the regex and adds a test. No force-push, so both commits stay.
- **T-0129**: InventoryModule.cs:651 is a real gotcha-60 instance (`_logCapCfg.Value`
  per keystroke); PracticeModule.cs:2750 is a false positive (`f.Value` is a plain
  field). Heuristic only - a write in a helper called from the slider is missed.
- **T-0131**: the path is `bot/ForestBot/ForestBot.csproj` (the task text omits the
  inner folder).
- `lint: ok (N UI hit(s)...)` now reads `N baselined hit(s), all accepted`, and
  `ui_hits()` also returns the new source rules, so `audit.py`'s stale-baseline
  finder covers them.
- Baseline reasons: `lint-baseline.txt` has no per-entry reason field (the
  writer regenerates it), so the reasons live in the commit messages and here.
