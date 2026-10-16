# Older handoffs

Pick up here entries replaced at later handoffs, kept for reference (moved out of CLAUDE.md 2026-10-04). Facts in them live in their own homes (game-notes, cards, run-mode.md); this is the narrative.

## Moved out 2026-10-17 (T-0400 session handoff)

- **2026-10-16 (run R-0017, author present):** 4 confirmed. v0.24.330
  T-0386 (replay bar health / stamina / speed), v0.24.331 T-0397 (run
  armed on the practice tick keeps stat names), v0.24.332 T-0387
  (first-person replay arms, changes-only anim track ~50 KB/min, picked
  from a live 30/10 Hz/changes comparison) + T-0399 (16 MB run uploads,
  site 20 MB on /api/runs; released, waits for a real long upload),
  v0.24.333 T-0388 (choose checkpoint marker fields). Parked by forest-ux
  for the author: T-0400 (82 raw channel toggles), T-0401, T-0402.
  Filed T-0398. T-0390 given back (built 9be94c08, reverted on main; needs
  a bridge proof of GetIndexCount on collision meshes). Stopped early:
  the author needed the computer.

## Moved out 2026-10-16 (run R-0016 handoff)

- **2026-10-16 (chain C-0001, run R-0015, unattended):** 3 rounds, 0
  progressed, stopped (no progress 3 in a row). v0.24.324 T-0383
  released (Only while pressed hides the still mouse box, wheel
  frame-true) but fails in game: the box blinks on slow moves and the
  path never builds (gotcha 119). Parked: T-0382 (may a run repro the
  resize that blanked the monitor?), T-0383 (frame-true vs hold-off),
  T-0385 (minimal mouse box: options A/B/C in tasks/notes/T-0385.md).
  **Next:** the author's answers; then the chain's next run
  (`loop.py chain next`).

## Moved out 2026-10-16 (chain C-0001 run R-0015 handoff)

- **2026-10-16 (chain C-0001, run R-0014, unattended):** 5 rounds, 3
  progressed. T-0392 confirmed (`chain next` re-dispatches a run no
  `begin` followed within 15 min; `chain status` is the read-only look),
  v0.24.322 T-0375 confirmed (+ Key capture holds plugin hotkeys off,
  frame-stamped; F6 / F12 not pressed), v0.24.323 T-0380 confirmed
  (mouse path split at the box edge, `Data/TrailClip`; seen at 1366x768,
  corner wrap unit-tested only). Parked: T-0372 (speed ramp: which end
  is fast, how the two lines differ), T-0381 (drag-resize handle).
  **Next:** the chain's next run (`loop.py chain next`).

## Moved out 2026-10-16 (chain C-0001 run R-0013 handoff)

- **2026-10-16 (chain C-0001, run R-0012; the author stepped in once):**
  5 rounds, 4 confirmed in game: v0.24.313 T-0358 (input overlay 'Only
  while pressed', frame-true), v0.24.314 T-0364 (Map category colours by
  legend order), v0.24.315 T-0367 (mesh colliders as GPU wireframes),
  v0.24.316 T-0371 (checkpoint dots + split / delta labels on the
  comparison line). T-0361 researched live and parked (game input: a
  key's action now in this runner's game, or its shipped default?).
  Author: tests are fully automatic, never "hold W" - forest-tester uses
  `scripts/sendinput.py` (brings the game to the front; gotcha 115).
  New: T-0383 (parked: mouse box / wheel in Only-while-pressed), T-0384
  (P4 checkpoint-label edges). **Next:** the chain's next run
  (`loop.py chain next`).

## Moved out 2026-10-16 (chain C-0001 run R-0012 handoff)

- **2026-10-16 (chain C-0001, run R-0011; the author stepped in at the
  end):** v0.24.310 T-0360 (F2 window drawn in screen pixels through
  `Core/WinPx`: sharp text) and T-0359 (forest-ux input overlay review ->
  T-0375..T-0378) confirmed. T-0363 (overlay crisp corners, one scale,
  mouse path): v0.24.311's path drew dashes outside the box; v0.24.312
  draws it with GL in pixel space - the author saw it right in game
  (smoke / 2560 / Edit HUD resize still open). Parked for the author:
  T-0358 (dynamic mode: idle tiles vanish or dim?), T-0361 (raw keys vs
  actions: which shape, which words) + T-0335, T-0366, T-0369, T-0370,
  T-0374. New: T-0379 (F2 mouse paths, testers), T-0380 (path stops
  short of the box edge), T-0381 (drag to resize a tile), T-0382 (a
  windowed 2560<->1920 bridge resize froze the game, cause unknown).
  **Next:** T-0380, then the chain's next run (`loop.py chain next`).

## Moved out 2026-10-16 (chain C-0001 run R-0011 handoff)

- **2026-10-15 (chain C-0001, run R-0010, unattended):** 5 rounds, all
  confirmed in game: T-0345 (ftse surf tool studied -> T-0368..T-0374),
  v0.24.306 T-0368 (run lines as px-wide ribbons, Width setting),
  v0.24.307 T-0353, v0.24.308 T-0354, v0.24.309 T-0355 (Input overlay
  editor fixes). Builder picks to glance at: run lines' 300 m draw
  distance and rim (T-0368), the new-tile slot 280 px from the right
  (T-0355).

## Moved out 2026-10-15 (chain C-0001 run R-0010 handoff)

- **2026-10-15 (chain C-0001, run R-0009, unattended):** v0.24.303 T-0338
  (HUD values docked into the splits panel) and v0.24.305 T-0341 (Map tab
  turned 180) confirmed in game; v0.24.304 T-0340 (mesh colliders, Hide
  behind walls) released - shipped meshes are not CPU-readable, so they
  stay boxes: T-0367 (hard). T-0342 was already done by T-0337. Parked
  for the author: T-0335 (HUD text contrast: outline / shadow / plate),
  T-0366 (ASL var viewer scope). **Next:** the chain's next run
  (`loop.py chain next`), then `tasks.py next`.

## Moved out 2026-10-15 (chain C-0001 run R-0009 handoff)

- **2026-10-15 (harness session, author present):** T-0007, T-0113,
  T-0319 confirmed (main input 9.0M a session from 14.3M); **Stage B
  built** (T-0016, checker accept): "start the routine" here ->
  `loop.py chain` drives fresh sessions (docs/areas/workflow.md *The
  unattended chain*); its first real chain run with the author is the
  last verify step. T-0311 waits for a 3rd researcher run. T-0358
  answered (no minimum shown time; the runner picks which inputs show
  and where). **Next:** T-0335 (HUD text contrast; findings in its
  notes, an outline / shadow / plate choice for the author), then
  `tasks.py next`.

## Moved out 2026-10-15 (T-0324 release handoff)

- **2026-10-15 (T-0322 released, author present):** the trajectory
  preview's key is rebound under its Experimental switch
  (`KeyGroups.BesideSwitch`, `Settings.DrawKeyRow`; its own capture), and a
  ticked HUD value that shows nothing says why under its row in Settings >
  HUD and Edit HUD (`OverlayModule.HudLineIdle`). Checker accept (after one
  revise), **released v0.24.299**, smoke PASS, confirmed in game by
  forest-tester. Author's ideas filed as notes: a CS-grenade-style throw
  preview (T-0088), watch back the last run in freecam with its trajectory
  (T-0344). **Next:** `tasks.py next`; T-0358 waits for T-0359's options.
  Unchanged: T-0333 needs a non-maker confirmation.

## Moved out 2026-10-15 (T-0322 release handoff)

- **2026-10-15 (T-0296 released, author present):** the author narrowed
  T-0296's contract to known limits (gotcha 110: far pool rocks T-0352,
  8 % cut band trade, old strokes on rock spheres vanish, +2.2 ms while
  painting); branch t-0296-paint-surface merged, checker accept on T-0296,
  T-0299, T-0351, **released v0.24.298**. Input overlay answers: T-0355
  (new tile bottom-left / -right), T-0356 (fold; no + button, drag to
  resize; any input), T-0357 (15-16 pt); new T-0358 dynamic mode (parked:
  where / how) and T-0359 forest-ux review of the overlay. Smoke PASS; all three
  confirmed in game by forest-tester (T-0351 on a rock; trunk unchecked).
  **Next:** `tasks.py next`.
  Unchanged: T-0333 needs a non-maker confirmation.

## Moved out 2026-10-15 (T-0296 release handoff)

- **2026-10-15 overnight (loop R-0008, 5 rounds, author away):** released
  **v0.24.293-297**: T-0302 input overlay (Edit HUD > Panels; .294 fixed
  the defaults at 1366x768; hand test -> testers, forest-ux follow-ups
  T-0353..T-0357, three parked), and T-0316 (When I die defaults to the
  game's death, a stored Automatic migrates), T-0320 (trend colours page),
  T-0321 (Reset profile lines) - all three confirmed in game. T-0296 paint
  parked for the author (checker block: T-0352 far rock collider, 8 % band
  trade-offs, old strokes on collider spheres vanish, probe +2.2 ms); its
  code stays on branch t-0296-paint-surface (4ac9858f, not merged). New:
  `loop.py begin --max-context`, `scripts/sendinput.py` (real OS input, no
  click). **Next:** the author's T-0296 / T-0355..T-0357 answers, then
  `tasks.py next`. Unchanged: T-0333 needs a non-maker confirmation.

## Moved out 2026-10-15 (overnight run R-0008 handoff)

- **2026-10-15 (T-0036 confirmed on v0.24.292):** with auto-restart a
  finish shows the results panel, not the 1.2 s toast (toast only with
  `[Splits] ResultsPanel` off); a checkpoint-resumed finish gets a
  practice results panel (`RunResults.BuildPractice`: live rows only, no
  PB). Author chose: panel still closes at the next clock start.
  Checker accept, smoke PASS, forest-tester confirmed. T-0337 confirmed
  too. **Next:** `tasks.py next`. Unchanged: T-0333 needs a non-maker
  confirmation; cloud/t-0320 and cloud/t-0321 to merge + check.

## Moved out 2026-10-14 (session-start handoff)

- **2026-10-11 (T-0025 merge + release, author present):** ui-redesign
  merged onto main by forest-integrator (b579bf71; 20 conflicts, Paint
  settings got their own Settings fold - author to confirm the home) and
  **released v0.24.288** (35 tasks, smoke PASS; the install runs it).
  T-0250 / T-0294 / T-0155 were already on main and deployed (a61a68d7);
  pending/site-held + cloud/fp-replay kept only for T-0281 (checker
  block). Merged cloud/* branches, ui-redesign and its worktree deleted.
  **Later (same day):** T-0025 QA done (forest-tester over the bridge +
  the author's drags) - confirmed. T-0320 sent to a cloud session (branch
  cloud/t-0320 to merge + check). **Last:** T-0333 released in
  **v0.24.289** - blurry HUD text was the scaled GUI.matrix stretching
  glyphs (author's reference 1366x768 -> x1.88); `Core/CrispText` draws
  at the real size (gotcha 109); bridge-checked at 2560x1440; needs a
  non-maker confirmation. **Next:** forest-ux findings T-0321..T-0324,
  T-0332 (Map slate), T-0316 (needs the author: what replaces Automatic
  as default), group 3's savestate / hang tasks.

## Moved out 2026-10-11 (T-0025 handoff)

- **2026-10-11 (loop R-0007 + a 12-session cloud batch, author away):**
  **v0.24.285** - checkpoint captures without the half-second freeze
  (T-0072, `CheckpointCaptureLean` on; `CheckpointCaptureSpread` on by default from
  v0.24.286 - author 2026-10-11, T-0305; ~48 ms worst frame, state ~7 frames late)
  and the light deleted-attempt check (T-0266); smoke PASS, both
  confirmed by forest-tester. Cloud batch (prompts were
  `Desktop/cloud-T-*.txt`, branches `cloud/*`): **on ui-redesign**
  (42fab3e+) T-0020 snapping, T-0303 Reset = whole profile, T-0023 trend
  colours, T-0259..T-0264 Settings sweep, T-0289 pin unheld items,
  T-0254 auto-refresh, T-0295 ModAPI flag - all checker-accepted except
  T-0020 / T-0023 (words + design Qs parked); every new word is
  *proposed*, collected with the CHANGELOG lines in
  `tasks/notes/T-0025.md` - **T-0025 waits for the author's OK**, then QA
  in game and the merge. **On main, deployed:** T-0084 security fixes
  (report `docs/security-audit-2026-10.md`; its open Qs + recs: T-0304). **Held off main** on `pending/site-held` (pushed, not
  deployed): T-0250 / T-0294 / T-0155 (accepted; admin words + T-0294's
  same-channel routing wait for the author) and T-0281 first-person replay
  (checker block: the attempt page has no look track - parked Q). Designs
  parked for the author: T-0292 surprise runs, T-0302 input overlay
  (their notes). T-0241 / T-0251 held (T-0314: `--hold`, next skips). The
  author's install: the ui-redesign DLL put back after the smoke (copy
  `plugins/ForestOverlay.dll.redesign-keep`). **Next loop run** would take
  T-0255 (Map legend, redesign), T-0267, **T-0274** (group 3 perf), T-0290
  (needs a `Stall:` line first).
- **2026-10-11 (T-0311, author present):** usage measured before any
  more harness work: `agent-cost.py --weeks --tail --tasks` (now main
  sessions too); baseline in docs/harness.md *Measuring the harness* -
  main sessions 85% of input, W41 14.3M/session (was ~33M). T-0311
  (researcher stops at the cause, forest-dev builds its plan) accepted,
  stays built until 3 researcher runs re-measure. Author: main-session
  cost first - **T-0319** (main context) ahead of T-0312 / T-0315 (P3).
  T-0319 (250k cap) and **T-0313** (tier easy / normal / hard + route
  override; `loop.py` names agent / model / effort; `agent-cost.py
  --tiers` measures it) confirmed - tiers get set at contract time from
  now on. T-0314, T-0312 (`Check T-a T-b ...`), T-0315 (`forest-integrator`: a batch merge costs main one report; replay matched 924aa22) done. T-0306 (redesign follow-ups) built + checker-accepted, ui-redesign at acd6249 (the install runs it; `plugins/ForestOverlay.dll.redesign-keep`); forest-ux findings T-0320..T-0324 parked. T-0307 (Import view closes on an import / any row click) built + checker-accepted, ui-redesign at 9ead2f6, deployed to the install; in-game click check rides T-0025's QA. T-0308 (any external plugin / patcher / DLL / foreign Harmony patch flagged like ModAPI: HUD `Mods: ...`, `Mods Detected: ...` on attempts, uploads, site tags) built + checker-accepted after one revise, ui-redesign at e759029 (pushed, not on the install); in game rides T-0025 - a vanilla install must show no `Mods:` line (BepInEx-owned game patches would false-flag). T-0317 (F2 alone moves only itself: column, splits, results, run code box drag only in Edit HUD - author: every piece) built + checker-accepted, ui-redesign at 0586a6c (pushed, not on the install); wording minors noted on T-0025. T-0318 (every placed piece keeps its place across a resolution change; sizes follow the screen by default, Settings > HUD 'Sizes follow the screen' off keeps pixels - author) built + checker-accepted, ui-redesign at 2b0d2ac (pushed, not on the install); in game rides T-0025; profile reference resolution -> T-0327. T-0326 (Settings > Network: 'Check online at startup' off = no request at startup, update check included; 'Refresh lists automatically' off = no automatic refresh, each list gets Check now - author's answers) built + checker-accepted after one revise, ui-redesign at 7235263 (pushed, not on the install); in game rides T-0025; open Q in its note: queued uploads still send at startup. T-0255 (Map tab: key of the marks on the map + hover hint, no instruction paragraph) built + checker-accepted (minor fixed), forest-ux saw the hint live, author answered: T-0332 (Map slate look A, line / ghost and Only cave spots removed, Runner spot, short hint; T-0328 / T-0329 / T-0331 folded in; decisions.md *The Map tab is a minor view*); ui-redesign at a42affe (pushed; the install runs 9dd92ef); narrow-map key rides T-0025. T-0267 closed wontfix (author 2026-10-11: no repro in 34 driven slides, a one-off PhysX quirk). T-0274 (endgame Go: the 27 AreaMembers kept between scene events, ~25 ms -> 2 ms) checker-accepted, **released v0.24.287**, smoke PASS; the install runs the redesign DLL again. T-0290 held (`next real Stall: line`). T-0327 (profiles record `@Reference.Width/Height`, one from another resolution converts its places / widths) checker-accepted, ui-redesign at 0bc7cd22 (pushed, not on the install); in game rides T-0025. **Next:** T-0296 (needs the game, high effort) or the author's T-0023 Qs + T-0025 QA.

## Moved out 2026-10-11 (T-0310, router trim)

- **2026-10-11 (T-0301, author present):** the stutter on every WASD /
  Space press was the HUD's placed Speed value (scale 6 = 96 px bold, its
  own font from T-0022) rebuilding its font texture (~20 ms) on each start
  / stop of walking - not the first-input watch (switched off live: no
  change). Fix on ui-redesign bdc83dd (`HudWidgets.KeptGlyphs`, gotcha
  107); proved on a hand deploy (16 injected W presses, no spike; old DLL
  kept as `plugins/ForestOverlay.dll.pre-T0301`); checker accepted; its
  CHANGELOG line is in T-0025's notes. The author's install runs this
  build. Seen on the way: right after a Slot 1 load, the bridge's one
  command a frame gives a ~85 ms GC every ~1.75 s (test-induced, not
  filed). **Next: T-0020** (snapping).
- **2026-10-11 (T-0258, author present):** preset themes on ui-redesign
  (5827004) - Settings -> Theme, 8 themes in `Data/UiPalette` (site,
  black, **warm near-black = default**, translucent, blue-grey = the first
  redesign, Catppuccin Mocha / Macchiato / Frappe), switched live (UiKit
  repaints its textures in place; modules build styles with
  `UiKit.Style`); decisions.md *Preset themes*. Checker accepted; waits for
  forest-ux (`Review T-0258`) + in-game: theme kept after a relaunch,
  Blue-grey / Macchiato, tooltips and the toast per theme. The author's
  install runs this branch build; its config still says `Window/Theme =
  site` until Warm is picked once. Customisable colours: T-0300 (after
  v1.0). **T-0301** (P2, new): a small frame spike on every game input
  (WASD / Space; RivaTuner) - the first-input start trigger (T-0282) is
  the author's suspect; the author opens a fresh session for it. QA:
  T-0299 (paint overhangs a face's edge).
- **2026-10-11 (author present, design answers):** every v1.0 task that
  waited on the author has its answers in its notes - T-0019 profiles,
  T-0020 snapping (Alt = free drag), T-0023 trend colour per value, T-0258
  palette variants (+ the author's box-art reference in its notes), T-0072
  (try all three capture ideas, measure, the author decides); their
  `needs` are now none / bridge, so `tasks.py next` hands out T-0019,
  T-0020, T-0023, T-0258, then group 3. **T-0021 / T-0022 built** on
  ui-redesign (8595993: checkboxes true 16x16 squares via `UiKit.Toggle`,
  outline off / solid yellow on, 3 px corners kept; values render at
  32/64/96 px with their own font, no shadow) - checker-accepted, author
  "all good" in game; they ride T-0025. The author's install runs this
  branch build. T-0293 moved after v1; T-0298 (native-UI concept) filed
  for after v1. Next: T-0258 in a fresh session (author picks a variant),
  then the author starts the overnight loop.
- **Next session:** group 3 of the v1.0 scope (decisions.md *What v1.0
  is*: plugin correctness and stability - T-0056,
  T-0058, savestate gaps (T-0029, T-0065, T-0067; T-0269 done in v0.24.282), perf (T-0072,
  T-0274, T-0198, T-0203; T-0277 done in v0.24.284, T-0280 wontfix - the forced GC
  stays, decisions.md); T-0290 waits for a `Stall:` line); group
  2 (cloud branches) is done. New QA task T-0289 (pin an item not yet
  held: the Filter box also lists unheld items, greyed x0 - author's
  answer recorded). Also
  waiting: the v0.24.272 smoke + forest-tester pass (below), and the
  live Discord look at a PB post once a PB lands (T-0232).
- **v0.24.284 (2026-10-11, T-0277):** in a cave (not the endgame) the
  main camera skips the terrain (layer 26 off its mask) and the 25
  `Tree_BillBoards` renderers, back in the frame you leave
  (`[Performance] CaveSurfaceOff`, on, also in run mode - author;
  `Game/CameraTrim` 5; game-notes *The main camera's draw calls*).
  Measured, Cave 6, force-gfx-direct: MainCamNew ~1.78 -> ~1.49 ms,
  picture the same; wording approved; checker accepted; smoke PASS. To
  confirm (not its maker): forest-tester - a restore / death reload while
  in a cave logs the cut again (`a load in the cave`), switching it off
  in a cave puts the terrain back at once, a cave mouth shows the outside
  missing (expected).
- **v0.24.283 (2026-10-11, T-0275):** the player's stats are found
  through `LocalPlayer.Stats`, not a `FindObjectOfType` walk (20-25 ms,
  measured) - no hitch on the frame practice mode comes on, nor on the
  first armed tick after each load; getters kept while the type is the
  same (`Game/PlayerStateReader`). Proved on a hand deploy: first
  Resolve 30 -> 5 ms, re-resolve 23-26 -> 2 ms; checker accepted. To
  confirm (not its maker): forest-tester - practice mode on with a
  timed segment selected logs no `Slow tick: practicerun`.
- **v0.24.282 (2026-10-10, T-0269):** a Quick load puts back the
  player's cold as a Full load gives it - body temperature 37, not cold,
  no screen frost, frost-damage timer 0 (`Game/ColdReset`, words in
  `Data/ColdNote`; game-notes *The player's cold is not in the save*);
  the done line says `body temperature 22 -> 37, cold off, frost 0.45 ->
  0 (as a load)`, nothing for a warm player. Proved on a hand deploy
  (`axe-held`, `tom-c6`); checker accepted. To confirm (not its maker):
  forest-tester - a Quick load into a cold place (night rain, the north)
  goes cold again by the game's own routine.
- **v0.24.281 (2026-10-10, T-0278):** a Quick load skips the re-created
  plane wreck's two navmesh updates (its cut, the old one's removal) while
  a wreck stands at the same pose (`[Performance] RestoreSkipSameWreckNav`,
  on; `Game/WreckNav`, `Data/WreckCuts`; game-notes *Performance*):
  `axe-held` 0 graph updates in 9 warm restores (off: 5 in 3), garbage
  22 -> 10 MB a restore, NodeHash the same on / off / after a Full load;
  the first Quick load after a load still runs the game's (Slot 1's load
  cuts by the other route). Checker accepted; e2e launch / restart /
  restores PASS. To confirm (not its maker): forest-tester - 5 Quick
  loads of `axe-held` log `plane wreck - the restore's new wreck skipped`
  + `the old wreck's removal skipped` from the 2nd on, no `Pathfinding:
  graph update queued` at (806.5, 96.1, 627.4). `tasks.py next` names
  T-0241 (QA tab removal) - it is group 4 (public release), not now.

- **v0.24.280 (2026-10-10, T-0297, QA):** a comparison run's replay
  lasts the run's time (`ReplayCamera.EndOf` had capped it at the last
  position sample: 11.186 vs the site's 11.197); checker accepted. To
  confirm (not its maker): forest-tester - the replay camera on a spot's
  comparison run shows `/ <the run's time>` as the site lists it.
- **v0.24.279 (2026-10-10, T-0279):** a surface Quick load cancels the
  `DelayedCleanUp` asset sweep its own streaming unload queued
  (`[Performance] RestoreSkipStreamingSweep`, on; game-notes
  *Performance*): hitch 459-488 ms -> none, restore 0.83 -> 0.48 s,
  memory flat, a cave's own sweeps kept; e2e launch / restart /
  restores PASS, checker accepted, smoke PASS. To confirm (not its maker):
  forest-tester - 5 Quick loads of `axe-held` log `its asset clean-up
  skipped, 2` and no `Load timing: hitch`. T-0295's word answered
  ("ModAPI Detected" or "ModAPI") - it is ready to build.
- **v0.24.278 (2026-10-10, T-0219):** paint, like KSF paint - in
  practice mode hold Mouse 4 to paint where the crosshair hits, Mouse 5
  erases, X undoes the last stroke (Z is the game's RestKey), a small
  crosshair while held (author's idea), Clear all / 8 swatches / size in
  Settings -> Paint; saved per spot (`config/ForestOverlay/paint/`),
  session-only with no spot (decisions.md *Plugin*). Settings can bind
  mouse buttons. The author painted with it on the branch build (true
  colours after the linear fix); wording approved; checker accepted.
  **T-0296** (new): paint partly hidden on some rocks / cliffs / bumpy
  ground (theories in its notes). To confirm (not its maker):
  forest-tester - erase, Clear all, a spot's paint back after a relaunch,
  hidden with practice mode off, nothing painted with the window open,
  `Perf (30 s)` while painting. Smoke PASS on vanilla
  files - the install had been left on ModAPI + UCM (UCM's NREs fail the
  smoke); vanilla Managed restored (author), the modded one kept at
  `G:/SteamLibrary/steamapps/common/TheForest-T0278-modapi-kept`.
- **v0.24.277 (2026-10-10, T-0246):** ForestOverlay runs with ModAPI
  mods (UltimateCheatmenu) - the preloader patcher `ModApiFix` repairs
  ModAPI's dropped parameter defaults (gotcha 106; release.md *How
  updates work*); the launch that first installs the patcher keeps the
  overlay off with "ModAPI found. Restart the game once ..." and a failed
  repair with "could not fix ModAPI's game files ..." (both worded by the
  author). Proved on the branch build under ModAPI + UCM 2.3.6 (3
  defaults repaired, overlay up, UCM menu open, Slot 1 loaded) and on
  vanilla (no patching). UCM's own NREs + "resources.assets is corrupted"
  also happen without BepInEx. To confirm (not its maker): forest-tester
  - with ModAPI's `gamefiles/modded` copied in (ModAPI cache in
  Downloads/modapi-2026_10_08_23_41_06; vanilla Managed + Mods backed up
  at `G:/SteamLibrary/steamapps/common/TheForest-T0246-launchB-vanilla`),
  launch -> `ModAPI fix: 3 parameter default(s) repaired`, overlay up;
  restore the vanilla files after. The failure notice is unit-tested
  only.
- **v0.24.276 (2026-10-10, T-0273):** a restore puts cave stalagmites
  back as captured - broken since -> whole, broken at capture -> broken
  again (in place and after a Full load), a later break's debris gone
  (`Game/BreakableKeeper`, `broken` header; author's rule: "exactly as
  it was during capture"). Proved on a hand-deployed build (10 restores
  in a row, no stacking - the author's worry). To confirm (not its
  maker): forest-tester - Cave 6 swim room (tp 1232 -33.5 527), break
  one, capture, break another, restore: the second whole, the first
  broken; the same after `restore <name> load`. The author's
  `ui-redesign` branch DLL was kept as
  `BepInEx/ForestOverlay.dll.redesign-keep` (the release replaced it).
- **v0.24.275 (2026-10-10, T-0202):** a restore of the same state keeps
  its read (state file parsed, level data, LoadNow given the bytes) and
  old plane wrecks' nav cutters go with them - two `[Performance]`
  switches, on (`RestoreKeepLastRead`, `RestoreRemoveWreckCutters`;
  `Data/KeptRead`). Measured live: 16.9 -> 9.2 MB a warm Quick load of
  `axe-held` (main thread); the forced GC stays (T-0280). Smoke PASS.
  To confirm (not its maker): forest-tester flips each switch in the
  Debug views tab once (the off numbers came from the statics) and
  repeats a 10-restore loop (tasks/notes/T-0202.md).
- **v0.24.274 (2026-10-10, T-0276):** Unity's own errors / asserts /
  exceptions (render thread included) are logged as `Unity: [Error] ...`
  (repeats differing only in digits counted, 30 lines a minute) and
  written at once to `logs/unity.log`; at the next launch a Unity crash
  folder (named after the *process start*, game-notes) gets
  `ForestOverlay-LogOutput.log` + `ForestOverlay-unity.log`. Proved on the
  branch build (managed + native errors, 50 repeats -> 2 lines, two test
  crash folders); smoke PASS, and a Slot 1 load logs one real engine line
  (`Setting mipmap mode of already created render texture`). Not proved:
  an off-main-thread line (the bridge cannot log from another thread) -
  the next d3d11 crash's folder should show it. To confirm (not its
  maker): forest-tester - bridge `call static:UnityEngine.Debug LogError
  "x id=1"` -> one `Unity:` line; a folder `<process start>` with
  error.log beside TheForest.exe gets the copies at the next launch.
- **v0.24.273 (2026-10-10, T-0284):** a main-thread stall watch
  (`Core/StallWatch`): a freeze of 10 s or more logs `Stall:` with the
  plugin hook / module / tab it was in, or "outside the plugin", and
  "back after"; during the hang into `config/ForestOverlay/logs/stall.log`
  (in the report zip). Proved by a forced 12 s freeze; the Slot 1 title
  load hang did not come back in 12 loads (10 fresh launches). The cause
  is **T-0290** (blocked on T-0284 until a hang is caught; if the line
  says "outside", try with `AllocationTrackerAtStartup` off - it is on in
  the author's config). Smoke PASS; to confirm (not its maker):
  forest-tester repeats the forced stall (bridge `call static:System.Threading.Thread
  Sleep 12000` -> `Stall:` naming `bridge`, then `back after`).
- **v0.24.272 (2026-10-10, cloud branches merged):** spots autosave, no
  Save / Reload (T-0217); a local Delete stays local, your own spot comes
  back from the site, Replace? / take-back disarm the old route (T-0265,
  T-0218); the run report lists the moves, tree cuts out (T-0244). Site
  (deployed with the push): official runs - each runner's best, recent 5
  + average, red runs hidden, a route replay on the attempt page (T-0223);
  PB posts are embeds from every spot with a WR / PB notification line,
  three spot kinds in the footer (T-0232, T-0285..T-0288). Every word
  approved by the author; all checker-accepted. **Waits for in game**
  (forest-tester): the steps in tasks/notes/T-0217.md and T-0265.md; open
  minors there (an invalid entry holds back its file's other edits;
  Replace? over an armed spot says "the spot was deleted").
  `cloud/on-now-wrap` / `cloud/results-escape` merged (notes only; both
  tasks wontfix).
- **v0.24.271 (2026-10-10, T-0212):** *Keep loaded* on a spot's Start
  state row - the start state loads once, then restarts put back the
  player + the endgame's elevators / doors and teleport (0.01 s vs a 0.4 s
  Quick load) until any scene load / unload, a restore or a death
  (decisions.md *Plugin*; plugin-concepts *Segment start states*). Proved
  on the branch build (elevator put back, item / health back, a scene and
  a death reload); the sliding doors have no line of their own yet - check
  them in the smoke / forest-tester pass. **T-0284** (new, P3): a Slot 1
  load from the title screen hung once in three (managed OnGUI loop after
  `Query state`; the author: vanilla never hangs) - its notes hold the
  stack sample.
- **v0.24.270 (2026-10-10, T-0282):** a run starts on `first-input` -
  every Rewired action but Esc / Mouse X / Mouse Y (game-notes *World
  events*); the velocity start `moving` is gone, a stored one reads as
  `first-input` and its old times retire (decisions.md *Plugin*). Proved
  on the branch build (mouse look + Esc silent, Space fires); waits for
  the smoke + forest-tester on the release. QA's bot-context request
  filed as T-0283 (bot paused).
- **Loop R-0006 (2026-10-10 night, author away; `loop.py report`):** 5
  rounds, 4 progressed. **v0.24.268** (T-0075): a cave spot restarted from
  the endgame leaves it as walking out does, so the cave's props load
  (body piles, ropes, planks). **v0.24.269** (T-0184): no ~24 ms hitch on
  Go / auto-restart (elevator rides tracked, not scanned). Both confirmed
  by forest-tester on the release. T-0190: the d3d11 texture crash is the
  engine's (gotcha 104). T-0199: no safe main-camera draw cut.
  **Author questions:** T-0277 (in-cave terrain cut), T-0280 (the forced
  GC per Quick load), T-0276 (Unity's own errors into the session log),
  T-0273 (stalagmites in normal play). New: T-0274 / T-0275 / T-0278 /
  T-0279. The loop's context cap is 300k (trial, author 2026-10-10).
  `AllocationTrackerAtStartup` was found on in the author's config (default
  off) - left as found.
- **v0.24.267 (2026-10-10, T-0214):** a savestate capture no longer
  rewrites the loaded slot's `info` file + its Steam Cloud copy
  (`GameStats.OnSerializing` runs on every `SerializeLevel`; game-notes
  *The slot's info file*; `Game/SlotInfoGuard`). Ends the smoke's
  "Slot 1 changed" hygiene problem. **Confirmed** by the v0.24.267 smoke.
- **Start:** skill `session-start`; its report says when `bot-review`,
  `weekly-cleanup` (next 2026-10-14) and `harness-review` are due.
- **T-0018 (2026-10-10, built + checker-accepted, ui-redesign acfe900 /
  392757a):** the HUD is a column of values (old order, title first,
  values only + the runner's text before / after), values dragged out /
  dropped back in Edit HUD, Info box option + `own` gone; ON NOW /
  PRACTICE are short orange flags, nothing while clean (decisions.md *The
  HUD is a column of values*, *Flags, not headings*). The author tried it
  in game over three rounds ("all good"); the author's install runs this
  branch build. Next in group 1: T-0020 snapping, T-0021, T-0022 (the
  blurry large text, author's finding), T-0019 profiles; icons = T-0293.
- **Redesign (2026-10-10):** main (v0.24.267) merged into `ui-redesign`
  (2997114). Built + checker-accepted on the branch: T-0024 (cursor),
  T-0226 (*Developer* tab, Settings as folds; `tasks/notes/T-0226.md`),
  T-0253 (one setting, one feature; `tasks/notes/T-0253.md`), **T-0256**
  (the Practice tab's start strip: Restart + a Practice mode copy + the run
  state; a runner's Restart on a timed segment turns practice mode on; F7 /
  Runs Restart follow the selection; Go on a row selects it - design in
  `tasks/notes/T-0256.md`, decisions.md *One journey, one place*) with
  follow-ups T-0270..T-0272. **T-0257** (2026-10-10, built + checker-accepted,
  08dbc24): the splits + results panels are set up, moved and resized
  in Edit HUD mode (decisions.md *Overlays are set up where they are
  shown*), no table in the Runs tab; waits for forest-ux + forest-tester
  in game on a hand-deployed branch build (its `qa`). **T-0256 / T-0270..T-0272 seen in game** by
  forest-tester on a hand-deployed branch build (f0e0707; the author's
  install still runs it - the next release replaces it). Hand deploys are
  fine now (rule 2 retired, author 2026-10-10). T-0025 (QA, merge,
  release) waits on T-0018 / T-0020 / T-0021 / T-0022 (author present);
  also for the branch: T-0254 / T-0257 (unblocked), T-0258, T-0259 /
  T-0260 / T-0263, T-0019, T-0023. T-0025's notes carry every checker's
  list and the branch's CHANGELOG lines.
- **Bot paused (author, 2026-10-10)** until it is trained on the message
  history and a much larger Forest data set (decisions.md *Knowledge bot*;
  session-start says "paused").
- **Harness roadmap** (docs/harness.md *Status log*): steps 1-5, 3e, 8c,
  9a, 9b, 10d, 10e, 12 Stage A, 12d done; every checkable gotcha has its
  check. Left: T-0016 Stage B (blocked until 2026-10-14: a week of Stage A
  without a fix). **First harness review open:** the Stop hook's "commits
  not pushed" line is off for the 5 tasks finished from 2026-10-08
  (docs/quality.md *Simplification log*); session-start counts them, then
  skill `harness-review` compares. **Order (author, 2026-10-07):** harness
  first, the redesign after.
- **Next loop run** would take `tasks.py next --bridge` (T-0199 main camera draw calls, T-0190, T-0184, ...).
  Built and waiting on a re-eval: T-0158, T-0163 (T-0209 has the misses).
- **Worktrees:** `ui-redesign` (`.claude/worktrees/agent-a9faea5bc9e1d1cb4`,
  pushed; T-0018..T-0025; T-0036 waits on it) plus merged agent worktrees
  (`python scripts/cleanup.py`). **Released:** v0.24.262 (T-0242: cave
  mouths log the cave - the crawl's 9 m snap had read as a teleport;
  rope-grab only on ropes, not cutscenes - gotcha 102; T-0144: deleted
  attempts leave the Runs tab, wording per the author; its lighter site
  answer is T-0266), nothing unreleased. Both wait for in-game
  confirmation (forest-tester: cave 6 crawl in / out, a cave 4 rope, a
  restore on a rope stays silent; a 404 drops a Runs row).
- **Nothing is published yet:** all live categories are drafts (the
  moderators publish); no community run spot exists (the author's call).

## Moved out 2026-10-10 (after loop R-0006)

- **v0.24.266 (2026-10-09, T-0248):** a restore in place / Go during the
  game's own death ends it (`Game/DeathSequence`: the chain's Invokes and
  coroutines, the drag-away clones, the hanging rope, controls, body,
  cameras), Full load if it cannot; every restore in place sets
  `DeadTimes` 0 as a load does (maks: menu after 2 deaths, rope stuck on
  the hips). Proved over the bridge step by step + a field diff against a
  Full load (gotcha 103). **Confirmed in game** by forest-tester (drag-
  away, hanging, death count, real death + restore, plain restore).
  New: T-0269 (body temperature not in the save).
- **v0.24.265 (2026-10-09, T-0247):** spot restart / quick-load from the
  title after a return to it (pause-menu exit). There `TitleScreen.Instance`
  is null and the component disabled (bridge; game-notes *Loading a save
  from the title screen*), so `TitleLoad.FindTitle` looks it up by path.
  Also maks's MARK (quick load "shot back to main menu"). Confirmed in
  game by forest-tester (restart + quick-load through the title).
- **v0.24.264 (2026-10-09, T-0245):** endgame-area-enter once per visit
  (the game publishes EnterEndgame at the box crossing and again after the
  door's load; a Publish prefix drops an enter while `IsInEndgame` is
  already true, a leave while already out). Waits for forest-tester: a
  vault-door entry logs one enter, and a first enter after a restore / Go
  still logs (the checker: AreaKeeper sets the flag by hand).
- **v0.24.263 (2026-10-09, T-0268):** run mode catches the log boost - a
  launch (55-80 m/s while E adds logs to a custom wall blueprint mid-jump,
  recorded live with the author), not the zero-speed lift. Waits for
  forest-tester in game. T-0243 is `needs: tester` now (the bouncy body
  slide, T-0267; wall climb + panel clip wait on maks's TAS recordings /
  log, asked in QA). The run in T-0243's QA message is maks's
  a-58adeca311c01f61.
- **T-0252 confirmed (2026-10-09):** `forest-ux` reviews what a runner sees
  (`docs/ux.md`; `open_tab` `scroll_tour`); spawn it with "Review T-n" for
  every UI task. Its first run filed T-0259..T-0264 (T-0263 / T-0264 parked).
  The look is yellow on black (decisions.md *Plugin: Look*; T-0258 on the
  branch). Cloud prompts for T-0216 / T-0144 / T-0221 are on the author's
  Desktop: T-0144 merged + checker-accepted (unreleased - needs a release
  + in-game check), T-0216 / T-0221 closed (author). New: T-0265 (local
  spot delete stays local; owner re-edits from the site).
- **Night run 2026-10-07/08 (author away; `loop.py report` R-0002..R-0005):**
  20 loop rounds + ~40 cloud-session tasks; releases v0.24.252..258 (the
  restart hitches, the endgame material leak, the red-elevator restart,
  the 100% tab's errors, another segment's red line, the armed log line,
  the Inventory log cap); bot full eval 285/353 and three record-holder
  answers fixed; Megan / multi-thrower / body-slide research live.
  Cloud sessions (one-time routines, `cloud/*` branches, merged after a
  checker) share the account's 5-hour limit. Task-file writes take a lock
  (T-0197 / T-0201). While a loop run is open every hook ask is a refusal.
- **Answered 2026-10-08** (ready to build): T-0194 runner spots keep
  their start state; T-0207 try the prompt proposal (branch
  `t-0207-prompt-proposal`); T-0198 / T-0203 perf switches (no sign-off
  needed - docs/decisions.md *Plugin*); T-0109 / T-0111 / T-0112 (run
  mode), T-0212 (lab-skip restarts). T-0210 closed (leftover redesign HUD
  position). Bot review 2026-10-08 (3): the trigger counts uses of the bot,
  not chat; T-0220 moves it to the answer log (runners `/ask` in the
  speedrun server's general chat, which the bot can't read - the author
  asks its admins).
- **T-0231 live (2026-10-08, built, author-eyes):** the site's Bot tab got
  a Discord-style unsaved-changes bar + server > category grouping; the
  author's channel untick had never been saved (rev 1 still ticks The
  Forest / #general). New QA tasks T-0232..T-0235 (PB webhook toggles,
  the nature guide map - answered, maks's route, registration research).

## Before that (2026-10-07, before the first loop run)

- **Start:** skill `session-start` (the SessionStart hook's report is
  its first step).
- **Harness roadmap** (docs/harness.md, author 2026-10-06: first; what
  each step built: its *Status log*): steps 1-5 done (5: the e2e suite,
  `scripts/e2e.py`, T-0010 - the release skill runs its smoke), 3e
  skills, 8c, 9a, 9b, 10d (T-0013), **12 Stage A (T-0015): the assisted
  loop**, `scripts/loop.py` + skill `work-loop`, not yet run on real
  tasks; T-0007 / T-0113 built. Left: T-0014 cleanup loop + review,
  T-0016 Stage B (after Stage A runs a week without a fix), 14 small
  lint / test checks (T-0122..T-0135). Bot direction (author,
  2026-10-07): the author out of the feedback loop - T-0141, T-0140.
  **Order (author, 2026-10-07):** harness first, the redesign after (the
  redesign tasks are `author-present`, so the loop skips them).
  **Next: the first loop run** - skill `work-loop`; it takes T-0026,
  T-0028, T-0090, T-0140, T-0141 by priority.
- **New from the 2026-10-07 game session:** T-0143 native crash on a
  title load (P2, bridge); T-0049 is a real bug (another segment's Go
  keeps the red line); T-0144 Runs-tab dead links (question parked);
  T-0027 marker half and T-0052 tally confirmed, their other halves open.
- **Worktrees:** only `ui-redesign` (pushed; tasks T-0018..T-0025). **Released:** v0.24.249. Nothing unreleased.
- **Nothing is published yet:** all live categories are drafts (the
  moderators publish); no community run spot exists (the author's call).

## Before that (2026-10-03, v0.24.234 released)

**Latest session: three cards - `categories-and-rules`, `routes`,
`crafting-and-building`** (no plugin code, no release). speedrun.com's API
(game `w6j5341j`): rules, subcategories, top 3, moderators. Unrestricted
Any% is Creative only; "explosives glitch" = the bomb boost; Glitchless
bans OOB + wall clips; "Sahara" = the runners' cave section to the vault
door. Peaceful = no enemies at all. `routes` from three guides'
transcripts (`yt-dlp --write-auto-subs`; the guides page needs the
built-in browser - Cloudflare). Stamina live (InfiniteEnergy off): sprint
3.5/s, regen 6/s after 0.4 s, capped by Energy; recipes and soda / mix
effects read from `ReceipeDatabase` / `ItemDatabase`; the hole cutter
destroys walls it touches (game-notes). The bot's glossary goes into
search in parts now (`Corpus` AddParts; it hit the 6 KB chunk limit).
9 eval questions (58). The planned cards are done; next is the bot's 👎
queue and the research queue (knowledge/README.md). Slot 1's game was a
Normal game with GodMode / InfiniteEnergy on (left as found).

**Before that: the `cannibal-ai` card** (no plugin code, no release).
A new Normal game over the bridge (Slot 1 is Peaceful Creative - no
cannibals; `GameSetup.Game` stays Creative across `OnNewNormalGame`, so
`call static:TheForest.Utils.GameSetup SetGameType Standard` first; skip
the plane intro with `press Jump`). Finding: the cannibals' motor / vision
/ search FSMs **do not exist** - that AI is C# (`mutantSearchFunctions`,
`mutantAI`, `pmSearchReplace`; the combat FSM dispatches to
`pmCombatReplace`), so nothing was left to export. Sight range is computed
on the player (`visRangeSetup`: 84 m standing / 67.2 crouched, day, open,
live); walking is silent, running is heard at 58.8 m (live); the player's
"stealth ranges" sent to cannibals are never read; the 3 / 5.5 m/s "enemy"
caps are not enemies (co-op rope overlap / no caller) - research queue item
closed. Card `knowledge/cards/cannibal-ai.md`, game-notes *Cannibal AI*,
docs/fsm/README corrected, 4 eval questions (49). Left: the card's open
questions (knowledge/README.md research queue).

**Before that: the `megan-boss` card** (her FSMs exported live from the
`ruben-megan` savestate; game-notes *Megan's boss AI*).

**Before that: the knowledge bot's eval run + tuning** (no plugin code,
no release; docs/knowledge-bot.md *Build order* 3). Eval 41% -> **87%**
(+ the new questions ~88%), all on Flash-Lite: Flash's free daily quota
runs out almost at once (gotcha 94), so the live bot is mostly Flash-Lite
too. Fixed: the eval waits out per-minute rests and retries the judge;
429s log their `quotaId`; `read_card` puts a **NOT CONFIRMED** list
(`[runner]` / `[inferred]` / not reproduced / Open questions) on top of
each card; LaTeX in answers becomes plain text in code; prompt rules from
the author's live thread (Discord `1555989862652313620`: the bot called
the runner-reported **Timmy forehead skip** "a joke" and repeated it
louder on pushback; denied, then invented a cause for, a ~50 m/s after an
axe clip): never dismiss a runner report, guesses only under "Possible
causes (guesses, not tested)", no "does not" beyond what was tested, no
developer motives, re-check on pushback, rules questions still looked up.
New card `timmy-forehead-skip` (runner report); the clip speed noted in
smash-clip / wall-and-log-boost; 3 new eval questions (43); `forest-bot
answer <id>` / `resolve <id>` for the 👎 queue. Builds on the bot built and
deployed earlier the same day (`knowledge/` 24 cards, `bot/`, the VPS
container; decisions in *Standing decisions* and docs/knowledge-bot.md;
decompiled source in `%LOCALAPPDATA%\ForestOverlay\game-src\`). **Next:**
the embedding model locally (author OK for ~130 MB, not asked), more
cards (categories, routes, crafting), the production server once the admins agree. The queue holds
none open (#1 Megan and #2 resolved). VPS: `ssh -i
~/.ssh/ssh-key-2026-08-13.key ubuntu@141.147.101.228 'sudo docker exec
forest-bot dotnet /srv/current/forest-bot.dll queue'` - run it without
asking (author, 2026-10-03: "just do everything you need to"; memory
`run-commands-no-ask`).
Earlier the same day: banned-move detection finished (v0.24.227-234:
bomb boost, huge speed, cave force load, fall damage cancel, lifts, clips;
gotchas 90-91), tech research round 2 - game-notes *Speedrun tech*.

## Handoff of 2026-10-06 (moved 2026-10-07, when its lists became tasks)

### Pick up here (2026-10-06, short session on a near-empty weekly quota)

**Start here, in order** (author, 2026-10-06: the harness work first -
"it will benefit all following tasks in the long term"):
1. `qa_read new_only` (the standing session start; file anything new,
   act on it after the harness unless it is critical).
2. **The harness plan, docs/harness.md**: first the deeper pass on the
   course (https://walkinglabs.github.io/learn-harness-engineering/en/ -
   every lecture, the projects, the templates `AGENTS.md` /
   `feature_list.json` / `claude-progress.md`, the frontier breakdowns),
   correcting the doc as its *Source caveat* says; then its roadmap from
   step 1 (the tasks file). Questions for the author go in the session,
   not into guesses (*Ground rule*). Needs high effort for the design
   parts - say so (memory `effort-level-switching`).
3. The author's two report zips (QA `1556842694758760559`, 2026-10-06
   01:57 / 02:36) - not read yet (the harness's report reader, 9b, would
   make this cheap).
4. The redesign fixes in docs/backlog.md *Author's notes in QA* (on
   `ui-redesign`, worktree clean + pushed), then the cursor re-check, QA, merge + release (the
   author's call, see below).
5. **Unreleased on main:** `706ac88` run lines off now stop drawing
   (`Game/LatePass`); ships with the next release - check in game with
   run lines off and a marker / replay up.
6. The bot settings page (*Next* below). Once the tasks file exists,
   these items move into it.

Worktrees / branches: only `ui-redesign` is left; the merged ones were
removed 2026-10-06.

*Earlier (2026-10-05 night):*

**v0.24.248** (released, confirmed over the bridge): Practice -> Import ->
*Website spots* (site `/api/spots.txt` + `/api/spots/{id}/foseg`,
`Data/SiteSpots`, `Modules/CommunityModule.Website.cs`, entries in
`segments/website.txt` under "Website"); a deleted spot disarms the run
(`PracticeRunModule.OnSpotDeleted`) and is deleted from the site by itself
(`uploads/deletes.txt` queue, `RunUploadModule.DeleteSpotQuietly`).
v0.24.246-247 confirmed over the bridge (docs/confirmed.md) except the
late pass at night and the ghost / replay overlays.
The runner PBs Discord channel (`1556505548269027399`, webhook "PB
Notifier") works: a test post went through 2026-10-05; real posts only for
community / published-category spots.

**UI / UX redesign DRAFT** (author, 2026-10-05: modern, UX friendly for
new runners, a Momentum Mod style HUD customiser - isolate a value like
speed, place and resize it): branch `ui-redesign` (pushed, NOT merged or
released), design in the redesign doc on that branch (ui-redesign.md); `Core/UiKit`
(palette, skin, sections, tooltips), `Data/HudLayout` (tested,
`hud-layout.txt`), HUD widgets + edit mode, window chrome, Runs tab
regrouped, results panel no longer overlapped. **Never seen in game** - a
build is on the author's Desktop (`ForestOverlay-ui-redesign-draft.dll`)
for them to try. Author tried it 2026-10-05 and gave a 10-item list (branch doc,
*Author's verdict*); the same evening most of it was built and seen in
game: yellow accent, filled toggles, value-only widgets, no drag-out,
total speed its own line, toasts, info box off by default, font-size
flicker and linear-texture colour fixes. Later: transparent widgets, toasts,
tooltips (`UiText.Note`), opaque window, ON NOW as a list. Left (branch
doc): the tooltips are done (Map / 100% keep live statuses); the cursor re-check, QA, then
merge + release (author's call). The author runs the draft.

**Read first:** the author's QA notes of 2026-10-05/06 (redesign fixes, run lines, trajectory, bot, two report zips) are sorted in docs/backlog.md *Author's notes in QA* - the redesign ones belong before its merge. Branches other than `ui-redesign` are all merged.

**Harness / autonomy plan** (author, 2026-10-06: "truly fully autonomous ... minimal human input aside from when new features are being added"): [`docs/harness.md`](harness.md) - the 12 harness-engineering principles scored, work items with checks, a roadmap (tasks file first). The author will do a deeper pass on the course before step 1.

**Next:** 0) the harness plan (docs/harness.md, author 2026-10-06: first); 1) the bot settings page on /admin (author, 2026-10-05;
design in docs/knowledge-bot.md *Bot settings page*); 2) the redesign's
remaining items (branch doc); then the backlog (colliders
that change between attempts, the Megan health check with the author, a
maintainability review).
A session picking this up mid-way: `git worktree list` / branches
`worktree-*` show unmerged work.

Older handoffs (2026-10-03 and before: the knowledge bot's cards / eval, banned-move detection, tech research) are in [`docs/session-log.md`](session-log.md).

**Session plan (author, 2026-10-02):** one item per session. Start each
session with `qa_read new_only`. Run mode and anti-cheat: every decision
is in [`docs/run-mode.md`](run-mode.md) - read it before touching run
mode, the report or anything a run uploads. **Earlier
(2026-10-03, research, no code):** the runners' tech read from IL and the
bridge - docs/game-notes.md *Speedrun tech and the endgame gate*: the bomb
boost is the knockback coroutine's per-frame `AddForce` piling up while the
pause menu stops physics (live: 1 s paused = 1,564 m/s); fall damage is
judged on the last collision ENTER's vertical speed (the slide cancel);
the cave force load is `doCave`'s timed `InACave`; the keycard bounty is
closed in code for single player (the end buttons live in
`endgame_streaming`, loaded only after the vault door or from a save made
inside the loaded lab). The swinging rock trap's knockback stacks
like a bomb (confirmed by the author 2026-10-04: works, but slower to build
and less versatile than a small bomb trap). Detection designs (not built):
docs/run-mode.md *Banned moves: detection*. Before that (same day): the
author looked at the website work of the last
sessions and confirmed all of it (docs/confirmed.md, 2026-10-03): the photo
map's lakes, the 3D middle lake, the south mountains' / lab textures, caves
on the 2D map ("a little bit hard to read but it's fine"), Follow's camera
in caves; the long N-S snow shadow is gone in the recapture. Two decisions
made (*Standing decisions*: teleports into the endgame; Quick load physics
is maks's call - asked him, QA `1555760732086476832`). Before that, site
only: the photo map's lakes and the 3D surface water (docs/website.md
*Terrain, sea and water*), the 3D view's culling and far copies (*Load
size*), caves on the 2D map, the photo map recaptured on v0.24.222 (the
sun back after a tp out of the endgame, gotcha 87). Run mode phases 1-4
(v0.24.206-220): docs/run-mode.md. **Nothing is published yet**: all live
categories are drafts - publishing is the moderators' job. No community run
spot exists yet - making one is the author's call.

**Next, in order (one per session):**
1. **Banned-move detection** - built (v0.24.227-234, every row of
   docs/run-mode.md *Banned moves: detection*); left: the moves done for
   real (item 5).
2. **Tech research, round 2** - done 2026-10-03 (*Pick up here*). Left
   from it: check *Reload save on death* gives the same game as a manual
   reload (docs/run-mode.md *Decisions*), the elevator skip done by hand
   with `anim watch`, the multi-thrower / bodies slide, Megan's FSMs.
3. **The game-knowledge Discord bot** (author, 2026-10-03; plan and
   decisions in [`docs/knowledge-bot.md`](knowledge-bot.md)): the
   knowledge base (29 cards) and the bot (`bot/`) are built and live in
   the QA server; eval + tuning done (87%, 2026-10-03); `megan-boss`,
   `cannibal-ai`, `categories-and-rules`, `routes`,
   `crafting-and-building` done (2026-10-03); next the research queue
   and the bot's queue (`knowledge/README.md`).
4. Then the main *Next up* list below. **Ideas waiting (author,
   2026-10-03):** a run audit log (every interaction, on the attempt
   page's timeline) and richer replays (buildings as schematics,
   first-person replays with animations, a trajectory / "grenade camera"
   view for bomb boosts and ziplines) - [`docs/run-audit-and-replays.md`](run-audit-and-replays.md)
   (decisions there: run mode only, a skimmable rundown, in game first).
   **After v1** (author, 2026-10-03): UI work, refactoring inefficient /
   bad code, and a lighter repo with only useful information - plus
   feature / QoL ideas as they come.
5. **Last, when every task is done** (author, 2026-10-03: "leave these for
   later when we're done with all tasks"): run mode by hand with the author
   - a real ESC + F2, the Runs tab section, End / Start run mode by
   clicking, a run spot's F7, the run code on a real recording (`CodeSize`
   40 px, top centre), the attempt page with a real attempt, /admin's
   *Categories* tab (investigations *Not seen by the author*); the
   detected moves done for real - a bomb boost, a cave force load (smash
   in the air), a fall damage cancel (sliding on bodies, Cave 6), a smash
   clip (a cave panel / the red elevator door), a log boost, the keycard
   cave wall clip (true any%) - read the `Move seen:` / `Move watch:` /
   `ClipWatch:` lines after each.
- Website: done apart from texture arrays in the export - only if frame
  times call for them (docs/website.md *Load size*).

**Decisions waiting for the author:** none open. Categories to publish
and their run spots are the moderators' (author, 2026-10-02: "i've given
them the tools").

**Waiting on testers** - the QA to-do list (`qa_todo`) is the record:
maks on Quick load physics (`1555760732086476832`: still different? which
move / spot / Quick or Full? else close it), the overnight lists
(`1555319960941756437`, `1555327671276273677` + `1555328852698333277`), the
per-tester lists (maks `1553807713593597984`, sxczurass
`1553811127278764167`), Tom's crashes (paused - author), maks's fog /
elevator / rope list, sxczurass's FPS answers + specs + the crouch fix
(v0.24.102), Cheesecake's Frame test, Ruben's inventory. Detail:
investigations.

**Investigations** (each stays in one session when picked up; detail in
investigations): raw FPS, performance / loads (garbage in play), Quick
load physics parity (maks decides - waiting on his answer), Tom's crashes
(paused).

**Awaiting an in-game check** — ask before building on these (the
current items are in docs/investigations.md):
- **v0.23.6's census off by default** - no hitch after a load.
- **Run lines cleared** on a plain spot / another segment (v0.22.7).
- **Weapon-upgrade receivers kept** on a cross-save restore (v0.22.7): the
  restore line says `kept N weapon-upgrade receiver(s)`; the adoption line
  lists `other misses:` - read it to see why they did not adopt.
- **Whether a timed run still arms after an F7 restore** — runs do not log
  arming; add a log line if it is ever in doubt.
- **"GATHER LOGS 0/4"** after an in-place restore (v0.20.2).
- **Savestates outside the easy case:** a busy surface area; the stick
  oddity (first stick picked up after an in-place restore went to the
  inventory, not the hand; seen once).
- **Boss-fight quick-load toggle** (v0.19.4) — a boss-fight death is rare.
- `end-shutdown`, `timmy-goodbye`, `raft-out-of-world` never seen in a log.

### Next up

The author's feature list, ordered by what runners feel soonest. Done
(detail in CHANGELOG.md): the load leak, updates under any name,
savestates + fix list + sharing, practice QoL, passengers, logs in the
inventory, god mode, freecam lighting, LiveSplit import, the website's
first versions. All dev/alpha: nothing is used in real runs until the
admins rule. The author: "work through the current list so we can move
onto expanding more features".

1. **Quick load physics parity** *(runner maks; up to him - author,
   2026-10-03; asked QA `1555760732086476832`)* - investigations.
2. **Performance: can patches make the game faster?** - raw FPS and
   loads, investigations. Done so far: v0.24.86-143 (profiler, tracker,
   load timing, PerfPatches 1-15, the reload freeze, the load crash).
3. Reload the slot **in place** on death (author's idea; a *Quick load the
   slot's save* button did it until v0.24.106 -
   `SavestateBridge.ReadSlotData` in git history). Also the flashed
   time's display (maks).
4. **forest.deter.cloud** *(runner)* - its *Next* in docs/website.md;
   maks's YouTube side-by-side (QA `1554074251831672943`, site only).
5. **TAS** - exploratory only, on savestates and the recorder.
6. **Speedrun tech research** (author, 2026-09-26, "later down the
   line"): placing ziplines precisely (the big schematic, a short window)
   and the expected trajectory; **bomb boosting** (explode, open the
   menu, wait, close - distance from the velocity at the menu, the time in
   it and fps; maybe a boost view, Experimental; sxczurass's measurements
   by fps: `1553447134911664168`, `Downloads\qa-reports\sxczurass\image.png`);
   panel / axe clipping.
7. Freeform zone shapes.

### Deferred runner feedback

Runner QoL / UX requests waiting until *Next up* is done (author: finish the list first, unless critical): [`docs/backlog.md`](backlog.md) - deaths clarity, runs / run lines, checkpoint savestates, status overlay, settings that persist, debug views, maks's list, and a *final exhaustive feature testing* section (checks to run before a wide release). New unscheduled requests go there.
