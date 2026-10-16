# Quality document

Where the project is strong and where it is weak, area by area, so a
session knows what it is walking into (docs/harness.md 10d, the course's
quality-document template). Every part of the project is in one row: a
new folder or file that no area's *Paths* covers fails `lint.py` until it
is graded (author, 2026-10-07: "grade anything relevant ... removes
blindsiding things that do need genuine work").

**Keeping it current.** A session that changes an area re-reads its row
and updates the grades, the evidence and *Reviewed* in the same push
(the handoff, docs/areas/workflow.md). `session-start.py` lists the rows
whose paths changed after their *Reviewed* date; the weekly cleanup
(skill `weekly-cleanup`, logged below) re-grades them. A C or D row names at least one open task that
works on it - that is how the lowest grades feed the task list.

## How a grade is set

Four dimensions, each A-D; **the area's grade is the worst of the four**
(author, 2026-10-07) - `lint.py` checks it.

| | Verification | Agent legibility | Test stability | Known gaps |
|---|---|---|---|---|
| **A** | Pure logic unit-tested, the main path run end to end by a machine (e2e journey, CI smoke, live check) and confirmed on a recent version | An area doc names the files and the behaviour; no file over 1,000 lines | Its suites run in CI on every push; nothing flaky in its history | No open bug |
| **B** | Unit tests, but the main path is confirmed by hand only (bridge, author, tester) | Documented, but a file over 1,000 lines or the behaviour partly only in code | Deterministic, but some of its tests run only locally, or its live check depends on something outside (quota, the game) | Open bugs, P3-P4 only, none a crash |
| **C** | Confirmed by hand only, or its main path waits on a check (`author-eyes`, `tester`) | A file over 2,000 lines, or no doc for the behaviour at all | No automated tests, or a known flaky test | An open P2 bug or a crash, with its task |
| **D** | Shipped and neither tested nor confirmed | No doc and code an agent cannot follow | No tests for behaviour runners rely on | The main path is broken in the current release |

## Grades

| Area | Grade | Verification | Legibility | Stability | Gaps | Tasks | Reviewed |
|---|---|---|---|---|---|---|---|
| Savestates | C | A | C | A | C | T-0046, T-0058, T-0065, T-0067 | 2026-10-14 |
| Timed runs | B | A | B | A | B | T-0365, T-0366 | 2026-10-15 |
| Practice | C | A | C | A | B | T-0046, T-0350, T-0352 | 2026-10-15 |
| Run mode | B | A | A | A | B | T-0109, T-0111, T-0112, T-0243 | 2026-10-14 |
| Information tabs | B | B | A | A | B | | 2026-10-15 |
| Plugin core and UI | B | B | A | A | B | T-0024, T-0302, T-0346, T-0347, T-0348, T-0349, T-0356, T-0357 | 2026-10-15 |
| Performance and loads | B | B | B | A | B | T-0034, T-0048 | 2026-10-14 |
| TAS and trajectory | B | B | B | A | B | T-0088 | 2026-10-14 |
| Dev tools | B | B | B | B | B | | 2026-10-16 |
| Bridge, e2e and QA | B | A | B | B | A | | 2026-10-14 |
| Release and updater | A | A | A | A | A | | 2026-10-14 |
| Site app | B | A | A | A | B | T-0285, T-0304 | 2026-10-14 |
| Site maps and 3D world | C | C | B | C | B | T-0061, T-0062, T-0191 | 2026-10-08 |
| Bot | B | A | A | A | B | T-0207, T-0209 | 2026-10-08 |
| Knowledge | C | B | A | B | C | T-0158, T-0163, T-0168 | 2026-10-14 |
| Harness | B | A | B | A | B | T-0016, T-0311 | 2026-10-15 |

Lowest first: Savestates, Practice, Site maps and 3D world, Knowledge (C).

## Areas

Paths are globs from the repo root (`*` within a folder, `**` across
folders, `{a,b}` either, a trailing `/` the whole folder). Not graded:
`docs/` and `tasks/` (they are each area's legibility), the test
projects (they are each area's evidence) and the root files.

### Savestates

Paths: `src/Modules/SavestateModule.cs` `src/Game/SavestateBridge.cs` `src/Game/*Keeper.cs` `src/Game/{AnimReset,BookClose,BookPages,BossHold,BuildMode,ColdReset,CutsceneAudio,DeathSequence,ElevatorRides,EndgameLoader,FullCapacityWatch,MenuClose,PathfindingWatch,PlayerHold,PlayerKeep,PrefabList,RideModes,RopeClimb,SceneCache,SetupHold,SlotInfoGuard,Stance,SunSync,TitleLoad,WreckClearing,WreckNav}.cs` `src/Data/{BlueprintState,ColdNote,DeathProgress,BookPageState,CapturedAreas,CheckpointStates,KeepLoaded,LevelText,LookupCache,StartedSet,WreckSites,WreckCuts,EnemyRecord,GreebleRecord,PickupMatch,PositionKey,KeptRead,RideState,SavestateFile,SlotSaveFlags,WeatherState}.cs` `scripts/save-diff.py` `scripts/save-diff-noise.txt` `docs/savestates.md`

- Verification **A**: SavestateFile (33), CheckpointStates (21) and the
  record tests; the e2e `restores` journey (in place, with a load, the
  chain); confirmed in game through v0.24.249 (docs/confirmed.md), the
  spread checkpoint capture since (T-0072, v0.24.285). Released, not yet
  confirmed: Keep loaded (T-0212), stalagmites (T-0273), the cold
  (T-0269), the wreck's navmesh and asset-sweep cuts (T-0278, T-0279).
- Legibility **C**: `SavestateModule.cs` 2,539 lines, `SavestateBridge.cs`
  2,355 (both grew ~360 since 2026-10-07); 14 keepers; 17 restore
  gotchas, 14 of them judgement only. docs/savestates.md and plugin.md's
  file map are good.
- Stability **A**: unit tests in CI; the e2e journey ran clean twice in a
  row (T-0010).
- Gaps **C**: native crashes on F7 in the vault door / Megan pickup
  cutscenes (T-0058, blocked on Tom); half-chopped trees regrow (T-0067);
  a wall rope is ended, not put back (T-0065); item drift after restores
  (T-0066); ~30 Quick loads then a Full load hung once (T-0056).

### Timed runs

Paths: `src/Modules/PracticeRunModule*.cs` `src/Modules/{RunUploadModule,TimerModule}.cs` `src/Game/{BuildWatch,GameEvents,ItemCounter,ItemCounts,LatePass,PlayerStateReader,ReplayDraw,RibbonMesh,RunnerIdentity,WorldEvents}.cs` `src/Data/{ArmCause,AttemptFormat,AttemptStore,BusEvents,ClockText,LineBuffer,LoadTimes,LssAutoSplit,LssFile,ReplayCamera,CheckpointMarks,ReplayLabels,ReplayMarks,RibbonPath,RunHistory,RunRecorder,RunResults,RunTiming,SiteBoard,SiteProtocol,SiteSpots,SplitSequence,SplitTable,TimerText}.cs`

Segments, checkpoints, splits, ghosts, run lines, replays, results,
LiveSplit, uploads.
- Verification **A**: RunCompare (29), LssFile (20), SplitTable,
  Trigger, RunHistory and more; the e2e `timed` journey; results panel,
  ghosts, replay camera, load-removed time confirmed (v0.24.241-249).
- Legibility **B**: `PracticeRunModule.cs` 1,402 lines, the rest split
  into eight partials under 810; plugin.md and plugin-concepts.md cover it.
- Stability **A**.
- Gaps **B**: (another segment's Go clears the red line since v0.24.257,
  T-0049); a deleted attempt leaves the list (T-0144, confirmed in game
  v0.24.262; the light site answer since v0.24.285, T-0266, confirmed in game); cave mouths log their cave
  and rope-grab is ropes only (T-0242, confirmed v0.24.262); replay
  labels done in one spot print over each other (confirmed.md,
  2026-10-04).

### Practice

Paths: `src/Modules/{PracticeModule,DeathModule,PaintModule}.cs` `src/Game/{PaintDraw,SurfaceProbe}.cs` `src/Data/{PaintSet,DepthMath}.cs` `src/Modules/CommunityModule*.cs` `src/Core/PracticeState.cs` `src/Game/{AreaReport,DeathHooks,FastBuild,ItemCapPatch,LogStore,ZonePreview}.cs` `src/Data/{CommunityIndex,DeathPlan,EditDebounce,HeldReport,ItemCaps,LocationLibrary,SafeFile,SegmentBundle,SegmentFormat,SegmentLibrary,Segments,StartStrip,ZoneDisplay}.cs` `locations/` `community/` `scripts/community-index.py`

Spots and their editor, teleports, Go / Restart, death reload and revive,
sharing and community packs, the gameplay mods ("ON NOW").
- Verification **A**: DeathPlan, SegmentFormat, PolygonZone, ZoneDisplay,
  CommunityPacks tests; the e2e `launch` and `restart` journeys; confirmed
  by the author and runners since v0.19.
- Legibility **C**: `PracticeModule.cs` 3,264 lines (the editor, Go,
  Restart, the Import view and the restart flow of savestates in one file;
  +430 since 2026-10-07). The paint tool (T-0219) is its own module.
- Stability **A**.
- Gaps **B**: Megan's health bar vs her death (T-0085); reload-on-death
  parity (T-0041); paint's known limits (gotcha 110: far pool rocks
  T-0352, the 8 % cut band's trade, old strokes on rock spheres vanish,
  +2.2 ms a frame while painting - accepted by the author 2026-10-15);
  the death default (T-0316); an unused member in
  PaintSet (T-0350). Released, not yet confirmed: paint (T-0219), Keep
  loaded (T-0212), the Import view (T-0254, T-0307).

### Run mode

Paths: `src/Modules/RunModeModule*.cs` `src/Modules/RunUploadModule.Attempts.cs` `src/Core/RunMode.cs` `src/Game/{AuditWatch,ClipWatch,ExternalModWatch,MoveWatch,RunIntegrity}.cs` `src/Data/{AttemptChain,AttemptOwners,ExternalMods,MoveDetector,RunAudit,RunCategory,RunReport,SentAttempts}.cs` `docs/run-mode.md`

- Verification **A**: MoveDetector (36 + 21 physics), RunAudit,
  RunCategory, RunReport, AttemptChain; the e2e `runmode` journey;
  confirmed v0.24.241.
- Legibility **A**: docs/run-mode.md; five partials, none over 450 lines;
  its gotchas carry checks.
- Stability **A**.
- Gaps **B**: integrity holes, all P4 and waiting on the author's design
  call (time scale T-0109, hashing the file not the loaded assembly
  T-0111, BepInEx patches skipped by assembly T-0112); the move events
  wait on in-game checks (T-0115..T-0118); the run report misses some
  movement tech (T-0243, P2 - detection not built yet, research first,
  not a broken path); the ModAPI / external-mod flags released in
  v0.24.288 (T-0295, T-0308), not yet confirmed.

### Information tabs

Paths: `src/Modules/{CollectiblesModule,InventoryModule,MapModule,RunInfoModule}.cs` `src/Game/{DrawingsReader,InventoryReader,MapRelief,NatureGuideReader,PassengerReader,PlaneSite,SurvivalBookReader}.cs` `src/Data/{CollectionList,HudLines,InventoryFilter,MapView,ReliefImage}.cs` `collectibles/`

HUD readouts, inventory, the 100% checklist, nature guide, To Do list,
the Map tab.
- Verification **B**: MapView (19), HudLines, ItemTrack tests; the e2e
  `tabs` journey takes shots but asserts nothing about their content; the
  Map tab and inventory confirmed over the bridge (v0.24.245).
- Legibility **A**: in plugin.md's file map; files under 910 lines
  (`MapModule.cs` 837 after T-0332).
- Stability **A**.
- Gaps **B**: the in-game map rotated 180 degrees (T-0341, P3); the Map
  tab's slate look (T-0332) released in v0.24.302, not yet confirmed by a
  non-maker; the relief's coast reads faint at map scale. The Map key (T-0255) and the
  Inventory filter (T-0289) released in v0.24.288, not yet confirmed.

### Plugin core and UI

Paths: `src/Plugin.cs` `src/CLAUDE.md` `tests/CLAUDE.md` `src/Core/{CrispText,CursorController,HotkeyMap,HudBuilder,HudLineTips,HudSettings,HudWidgets,HudWidgets.Profiles,HudWidgets.Snap,Lifecycle,ModuleContext,ModuleHost,NetworkSwitches,Notice,OverlayModule,UiKit,UiText,WinPx}.cs` `src/Modules/{MainWindowModule,SettingsModule,InputOverlayModule}.cs` `src/Game/{FastField,GameActions,GameBridge,GameInput,GameMessageLines,PlayerRef}.cs` `src/Data/{FolderStamp,KeyText,KeyGroups,HudTrend,HudLayout,InputActions,InputLayout,HudProfile,ProfileUnits,HudSnap,PageGrouping,TextMemo,TextOutline,UiPalette,UiScale,GameLinesState}.cs` `ForestOverlay.csproj`

The module host, window and tabs, hotkeys, cursor, input block, HUD
builder, notice, settings, the reflection helpers.
- Verification **B**: PageGrouping, TextMemo tests; `lint.py` label20 and
  alloc; the e2e `tabs` journey's shots are for eyes; the look is the
  author's (author-eyes).
- Legibility **A**: plugin.md *UI* and *Where things live*, src/CLAUDE.md;
  `ModuleHost.cs` 557 lines.
- Stability **A**.
- Gaps **B**: the redesign merged and released in v0.24.288 (T-0025
  confirmed); crisp text at 2560x1440 released in v0.24.289 (T-0333,
  waits on a non-maker check); the cursor re-check (T-0024, released);
  forest-ux's P3 follow-ups (T-0320..T-0325, T-0335, T-0342); unused
  members found by the 2026-10-14 cleanup (T-0346..T-0349).

### Performance and loads

Paths: `src/Core/{PerfMonitor,StallWatch}.cs` `src/Game/{AllocationTracker,CameraTrim,FrameTimer,GameLoading,GameProfiler,LeakedThreads,LoadTiming,LoadWatcher,MemoryCensus,PerfPatches,RenderProbe,RestoreGarbage,StaleSubscribers}.cs` `src/Data/{FrameTimeline,ProfileTable,StepBytes}.cs` `scripts/{launch-with-args,native-callers,sample-stacks,symbolize-crash}.py`

- Verification **B**: FrameTimeline, ProfileTable, LoadTimes tests; the
  `Perf` / `Frame` / `Load timing` lines read by hand; idle garbage and the
  load leak confirmed (v0.23.3-7, v0.24.244); the e2e `launch` journey
  loads but measures nothing.
- Legibility **B**: `PerfPatches.cs` 1,128 (over 1,000 since T-0072 /
  T-0277), `MemoryCensus.cs` 765; all seven performance gotchas are
  judgement.
- Stability **A**.
- Gaps **B** (was C; 2026-10-14: no crash of ours is open - both crashes
  are the game's / engine's and closed, Unity's own errors now logged by
  T-0276 - and the hitch tasks T-0202, T-0274, T-0275 and the surface off
  in caves, T-0277, are released). The history: the title-load native
  crash is the game's (T-0143 closed:
  LOD_SimpleToggle, 0 in 26 repeats; e2e watch T-0189); the render-thread
  texture-upload crash is the engine's too (T-0190: font glyph clear on a
  texture whose create failed; its cause unlogged - T-0276 parked); the
  Go / auto-restart hitch fixed (T-0184, v0.24.269), left: a Go inside the
  endgame (T-0274) and the practice-mode toggle (T-0275); garbage in play
  measured (T-0033: 50-150 KB/s, half Unity's; the small game patches
  wait on the author, T-0203; the restart loop's ~40 MB a Quick load, T-0202, and the
  strings' source open); the old world held after a load (T-0034); the census hitch
  check (T-0048); raw FPS (T-0030 done: no safe camera cut; T-0199 done: no safe main-camera
  draw cut, the in-cave surface parked for the author; T-0031, T-0032).

### TAS and trajectory

Paths: `src/Modules/TasModule.cs` `src/Game/{TasInput,TrajectoryView}.cs` `src/Data/{TasRecording,Trajectory}.cs`

- Verification **B**: TasRecording, Trajectory tests; record / replay and
  the trajectory preview confirmed once (v0.24.241); no e2e journey.
- Legibility **B**: plugin.md's file map and plugin-concepts.md cover TAS
  record / replay and the trajectory preview (T-0145, 2026-10-08).
- Stability **A**.
- Gaps **B**: the preview is off where a jump clips an edge (T-0088); TAS
  is exploratory (T-0038); its key sits away from its switch (T-0322).
  Unchanged since 2026-10-08 but for the checkbox look (T-0021).

### Dev tools

Paths: `src/Modules/{DebugViewModule,DeveloperModule,DumpModule,ExplorerModule}.cs` `src/{GameDumper,TypeExplorer}.cs` `src/Game/{AerialCapture,AnimProbe,DebugDraw,FsmExport,TerrainDump,WorldDump}.cs` `src/Data/{VolumeFilter,DumpText}.cs` `tools/ILScan/` `tests/ILScan.Tests/`

Debug views and freecam, the explorer, dumps, the FSM / terrain / world
exports, the offline IL scanner.
- Verification **B**: used in every research session; freecam confirmed
  by the author (v0.17.0); VolumeFilter tested.
- Legibility **B**: `DebugDraw.cs` 1,094 (also draws run lines and
  markers; the GPU mesh-collider wireframes, T-0367); ILScan is documented in game-notes and plugin.md.
- Stability **B**: ILScan's modes, errors and cap run in CI over a
  fixture assembly; the FSM export's value text and the dumps' cleaners
  are `Data/DumpText` with tests (T-0146). The game-walking parts of the
  dumps are proved only by use.
- Gaps **A**. Since 2026-10-09: the Developer tab gathers the dev
  features (T-0226), the stall watch names a hang (T-0284, released); since 2026-10-16 mesh colliders draw as wireframes, confirmed in game v0.24.315 (T-0367).

### Bridge, e2e and QA

Paths: `src/Modules/{BridgeModule,QaModule}.cs` `src/Game/{InputInject,ObjectProbe}.cs` `src/Core/{LogKeeper,UnityErrorLog}.cs` `src/Data/{BridgeCommand,CrashFolders,InjectedInputs,LogArchive,QaList,UnityLogFilter,ZipWriter}.cs` `tools/BridgeMcp/` `tests/e2e/` `scripts/{e2e,read-report,sendinput}.py` `scripts/bridge.sh` `qa/` `docs/bridge.md`

- Verification **A**: BridgeCommand, BridgeMcp, InjectedInputs, QaList,
  ZipWriter, LogArchive tests; the bridge drives every in-game test and the
  e2e suite runs it end to end.
- Legibility **B** (was A, against the table: a file over 1,000 lines):
  docs/bridge.md, docs/log-lines.md (generated); `Tools.cs` 1,027 and
  `ObjectProbe.cs` 1,050 lines.
- Stability **B**: every script test runs in CI (T-0147); the e2e suite
  runs only locally (it needs the game): release smokes clean through
  v0.24.289; Unity's errors and crash folders now land in the session
  log (T-0276).
- Gaps **A**.

### Release and updater

Paths: `src/Core/{UpdateChecker,UpdaterInstaller,WebRequest}.cs` `src/Game/ModApi.cs` `src/Modules/UpdateModule.cs` `src/Data/{ReleaseJson,ShippedData,UpdateStaging}.cs` `patcher/` `scripts/{bump.py,deploy.ps1}` `.github/workflows/build.yml` `CHANGELOG.md` `docs/areas/release.md`

- Verification **A**: ReleaseJson (16), UpdateStaging (8), `test_bump.py`;
  `lint.py` versions + the pre-push tag check; CI attaches the DLL; the
  release skill's e2e smoke installs it through the updater.
- Legibility **A**: docs/areas/release.md.
- Stability **A**: `test_bump.py` runs in CI with every script test
  (T-0147, 2026-10-08).
- Gaps **A**: the old-install problems are documented for runners
  (release.md *Known issues*), nothing open (2026-10-14: the changes
  since are release bumps, v0.24.270..289).

### Site app

Paths: `site/CLAUDE.md` `site/ForestSite/*` `site/ForestSite/GameCode/` `site/ForestSite/wwwroot/{admin.js,app.js,attempt.js,compare.js,index.html,items.json,style.css}` `site/deploy/` `.github/workflows/site.yml` `scripts/site-smoke.py` `docs/website.md` `docs/areas/site.md`

Spots, runs, attempts, categories, admin, the API, the PB webhook.
- Verification **A**: 99 tests (Api, Attempt, Category); the browser smoke
  in CI before every deploy; deploy-watch's live check.
- Legibility **A**: docs/areas/site.md -> website.md sections; files
  under 1,000 lines (`Attempts.cs` 983, `admin.js` 896).
- Stability **A**.
- Gaps **B** (was A): the Discord PB post fixes are built and wait on
  their check (T-0285..T-0288, P3-P4); the security audit's fixes are in
  (T-0084, built) and its open questions are filed as T-0304.

### Site maps and 3D world

Paths: `site/ForestSite/wwwroot/{map.js,map3d.js,world3d.js}` `site/ForestSite/wwwroot/terrain/` `site/ForestSite/wwwroot/vendor/` `scripts/{aerial-bake,aerial-upload,cave-bake,terrain-bake,world-extract,world_pack,world_checks,site-look,site-measure}.py`

The photo map, caves, the 3D world and the export / bake pipeline.
- Verification **C**: checked by eye (gotcha 68: render the page and
  compare with a game shot); the 2026-10-01 map items and the 3D lab /
  Cave 6 / texture packs wait on the author's look (T-0061, T-0062); the
  smoke loads the pages, not the picture.
- Legibility **B**: `map3d.js` 1,216, `world3d.js` 1,067; website.md
  *The photo map* / *The 3D world*; 19 site gotchas, 16 judgement only.
- Stability **C**: the bake / export scripts have not run since their two
  checks went in (duplicate placements, near-black textures:
  `scripts/world_checks.py` + tests); the next real export proves the
  wiring and the near-black threshold (T-0191).
- Gaps **B**: the exact world is planned work (T-0086).

### Bot

Paths: `bot/` `.github/workflows/bot.yml` `docs/knowledge-bot.md` `docs/areas/bot.md`

- Verification **A**: 53 tests; the CI eval on 7 questions after each
  deploy, the full eval by hand (bot.yml dispatch, T-0141); deploy-watch's
  live check; the /admin Bot tab's live settings confirmed (T-0028).
- Legibility **A**: bot/README.md, bot/CLAUDE.md, docs/areas/bot.md;
  files under 320 lines.
- Stability **A**: the tests are deterministic; the eval is warn-only and
  skips busy answers (gotcha 94).
- Gaps **B**: the 2026-10-08 full eval (285/353, 80.7%, 0 busy) proved
  short answers stay short (T-0090: 4 of 5 within their limit); two
  regressions (fall-how, categories-bombs-normal, T-0207) and three
  weak answers (T-0209) are open. A model timeout rests the model like a
  503 (T-0156, confirmed live).

### Knowledge

Paths: `knowledge/` `docs/game-notes.md` `docs/fsm/`

The cards, glossary and eval questions; the confirmed game notes and FSM
exports the bot reads.
- Verification **B**: the KnowledgeTests lint over every card; the full
  eval scored 81.5% (260/319, 80 questions, flash-lite on CI, 2026-10-07 -
  docs/bot-reviews/2026-10-07.md) - a model judges, a person has not
  re-checked since.
- Legibility **A**: knowledge/README.md (format, `[runner]` /
  `[inferred]` labels, the research queue).
- Stability **B**: the eval spends the free quota shared with the live
  bot, so CI runs a 7-question subset only.
- Gaps **C**: guesses stated as fact in runner answers (T-0163, P2, from
  the knowledge-testing channel); wrong / missing top-runner facts
  (T-0158, P2) - both built, waiting on a check; low scorers in the full
  eval (T-0168); eleven research items (T-0098..T-0108). All bot work is
  held (bot paused, author 2026-10-10); since 2026-10-07 the changes here
  are game-notes entries from plugin research (now 4,332 lines, a
  reference read by section).

### Harness

Paths: `scripts/{agent-cost,audit,cleanup,lint,log-catalogue,loop,merge-keepboth,session-start,tasks,watch-deploy}.py` `scripts/lint-baseline.txt` `scripts/audit-ignore.txt` `scripts/hooks/` `.claude/` `.githooks/` `.mcp.json` `CLAUDE.md` `docs/harness.md` `docs/quality.md` `docs/areas/workflow.md`

The task file, the loop, lints, hooks, skills, agents, session start, cleanup.
- Verification **A**: tasks, loop, lint, hooks, session and log
  catalogue tests in CI; `tasks.py check` in CI.
- Legibility **B**: docs/harness.md is ~960 lines of plan and status
  together; workflow.md is the working copy.
- Stability **A**: every scripts/tests file runs in CI (T-0147); task-file
  writes take a lock and replace the file atomically (T-0197, T-0201;
  200/200 writes kept under 8 writers).
- Gaps **B**: every checkable gotcha has its check (T-0123..T-0135,
  2026-10-08; T-0192, T-0193 confirmed); Stage B not built (T-0016,
  its wait date passed, the loop still changed 10-11 / 10-15 - author's
  call); the first monthly review ran 2026-10-08 (Simplification log),
  the weekly cleanup twice; the cost work is confirmed (T-0113, T-0319:
  main input 9.0M a session from 14.3M, start 65k from 73k; 2026-10-15)
  and the checker brief re-runs every script test (T-0007); the research
  / build split (T-0311) waits on its third researcher run (tail 27% from
  46% so far); the framework benchmark is held until 1-2 reviews after
  them (T-0334).

## Cleanup log

The weekly cleanup (docs/harness.md 10e, skill `weekly-cleanup`):
`scripts/audit.py` finds dead paths, stale baseline entries, unused code
and orphan files; each real finding becomes a task, each checked false
positive a line in `scripts/audit-ignore.txt`; the stale rows above are
re-graded. `session-start.py` says it is due 7 days after the last row.

| Date | Findings | Filed | Ignored | Re-graded |
|---|---|---|---|---|
| 2026-10-14 | doc-path 6 (all false positives), cs-unused 6, quality 13 | T-0346..T-0350 | 6 lines added | Savestates, Practice, Run mode, Information tabs, Plugin core and UI, Performance and loads (C -> B), TAS and trajectory, Dev tools, Bridge e2e and QA, Release and updater, Site app (A -> B), Knowledge, Harness |
| 2026-10-07 | doc-path 7, cs-unused 16, py-unused 1 (first run, T-0014) | T-0170..T-0183 | 3 lines added | Harness (changed by T-0014) |

## Simplification log

The monthly harness review (docs/harness.md 12d, skill `harness-review`):
one component switched off for the next 5 finished tasks, then
`tasks.py stats --since/--until` before and after (docs/harness.md
*Measuring the harness*); the author decides keep or remove. Never the
safety guards (rules 2, 4, 13, 15 and their hooks). *Decision* is `open`
while the component is off; `session-start.py` counts its tasks and says
the next review is due 30 days after the last row.

| Date | Component switched off | How to switch it back | Outcome | Decision |
|---|---|---|---|---|
| 2026-10-08 | Stop hook's "commits not pushed" line | `scripts/hooks/stop.py` `collect()`: delete the `ahead = 0  # harness review` line | off since 2026-10-07, counted from 2026-10-08 (today's tasks finished with it on); no recorded catch, it set off gotcha 97 (an unchecked merge pushed and deployed); the "no accept review" line, the release skill and session-start's ahead line stay. Compared 2026-10-08: 10-07 (on) 49 finished, checker first-time accept 7/13 (54%), 1.8 reviews per task, 4 loop interventions; 10-08 (off) 53 finished, 44/48 (92%), 1.1, 0 - nothing worse, 0 commits left unpushed; the gain is mostly the loop maturing, so it says little for the line itself | **removed** (author, 2026-10-08) |

## Change history

- 2026-10-07: the *Cleanup log* added and the *Simplification log* given
  its *How to switch it back* column (T-0014); Harness re-graded (still
  B), `scripts/audit.py` + `audit-ignore.txt` in its paths.
- 2026-10-07: first grading (T-0013). Seven areas at C, none at D; new
  tasks for the gaps with none: T-0145 (TAS docs), T-0146 (dev tools
  tests), T-0147 (CI runs every script test).
