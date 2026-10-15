# Area: the plugin (`src/`, `patcher/`)

The BepInEx plugin itself: what lives where, the UI, what works, and the
lessons from building it. Read this before plugin work; the rules every
change follows are in [`src/CLAUDE.md`](../../src/CLAUDE.md) (loaded by
itself when you open a file under `src/`). How the features behave:
[`plugin-concepts.md`](plugin-concepts.md). Releasing: [`release.md`](release.md).

Deeper docs, read the section you need:

| Doc | Read it when |
|---|---|
| [`docs/game-notes.md`](../game-notes.md) | You touch a game system - every game name in `src/Game` comes from it or from IL |
| [`docs/savestates.md`](../savestates.md) | Before touching a restore (what each keeper does, by version) |
| [`docs/run-mode.md`](../run-mode.md) | Run mode, anti-cheat, categories (decisions and phases) |
| [`docs/run-audit-and-replays.md`](../run-audit-and-replays.md) | Audit log / replay ideas |
| [`docs/investigations.md`](../investigations.md) | A task's `notes` names a thread there (what was ruled out, test assets) |
| [`docs/backlog.md`](../backlog.md) | A task's `notes` points at deferred runner feedback |
| [`docs/confirmed.md`](../confirmed.md) | Before re-testing something: what was confirmed in game, by version and by whom |
| [`docs/bridge.md`](../bridge.md) | Before driving the game or posting to the QA Discord |

## Build

BepInEx packages are on BepInEx's own feed (`nuget.config`), not nuget.org.
With no local install the build falls back to BepInEx's stubbed UnityEngine —
that is how CI works. The build prints which source it used.

Commands (build with the path, tests): the router's *Commands*. Tests and
the Unity shim: [`tests/CLAUDE.md`](../../tests/CLAUDE.md).

## Where things live

Where things live:

| Feature | Files |
|---|---|
| Window, tabs, player lock, cursor, **game input block** | `Core/ModuleHost`, `Modules/MainWindowModule`, `Core/CursorController`, `Game/GameInput` |
| Variable text in panels, on-screen notice | `Core/UiText` (wraps, returns height), `Core/Notice` (`Ctx.Notice`, drawn by `Plugin.OnGUI`) |
| Savestates, segment start states | `Modules/SavestateModule` (no tab since v0.24.106; its load options drawn in Settings > Loads and savestates via `DrawOptions`, the memory census in Developer via `DrawCensus`, T-0226), `Game/SavestateBridge` (incl. cross-save `AdoptPlayer`), `Game/PickupKeeper`, `Game/PanelKeeper` (cave panels; matched by `Data/PositionKey`), `Game/Stance` (crouched / standing, `stance` header), `Game/RopeClimb` (a cave rope climb, `rope` header; Go / tp let go), `Game/RideModes` + `Data/RideState` (zipline, sled, glider, cliff climb: ended on Go / tp / restore, put back from the `ride` header, v0.24.201), `Game/BlueprintKeeper` + `Data/BlueprintState` (placed blueprints filled since rebuilt from the save, build HUD recounted, `blueprints` header, v0.24.203), `Game/NatureKeeper` (trees, bushes, saplings), `Game/GreebleKeeper` + `Data/GreebleRecord` (sticks / rocks around pooled trees), `Game/BreakableKeeper` (cave stalagmites broken since / at capture + their debris, `broken` header, T-0273), `Game/BookPages` + `Data/BookPageState` (book page), `Game/BossHold` + `Game/MeganKeeper` (boss Megan), `Game/ElevatorKeeper` (endgame elevators; the red elevator's ride replayed; a ride stopped on Go / tp), `Game/SlidingDoorKeeper` (the endgame's sliding doors incl. the elevator car door, `doors` header, v0.24.226), `Game/EndgameLoader` (the endgame after a restore, loaded in the background - a transpiler on the game's trigger), `Game/FullCapacityWatch` (logs "can't carry any more"; hides the post-restore re-equip's one, v0.24.129), `Game/KeypadDoorKeeper` (a keypad door's cutscene replayed), `Game/AreaKeeper` (endgame active area; also on Go), `Game/CutsceneAudio` (fast-forward sounds), `Game/SunSync` (sun after a restore), `Game/SceneCache` (the keepers' scene searches kept between restores, T-0148), `Game/WreckClearing` (the re-created plane wreck skips the crash clearing, T-0148), `Data/SavestateFile`; restart flow in `Modules/PracticeModule` (`Restart`; `Teleport` is Go); retire warning via `Data/AttemptStore.CountOnRoute` |
| Practice spots / segments, teleport, cave switch | `Modules/PracticeModule`, `Data/Segments`, `Data/SegmentLibrary` (autosave: `Data/EditDebounce`, `Data/SafeFile`), `Game/GameBridge` (look angles, `SyncCaveState`) |
| Sharing, community packs | `Data/SegmentBundle` (`.foseg`: segment + start state + attempts), Practice's Share row / Import view, `Modules/CommunityModule` + `Data/CommunityIndex` (fetch from the repo's `community/`), `scripts/community-index.py`, `community/README.md` |
| 2D map caves (site) | `scripts/cave-bake.py` -> `site/ForestSite/wwwroot/terrain/caves.webp` + `caves.json`, drawn by `wwwroot/map.js` underground |
| Run uploads to the website | `Modules/RunUploadModule` (queue in `config/ForestOverlay/uploads/pending`, refused files + reason in `uploads/refused`; `[Site]` config; section drawn in the Runs tab), `Core/WebRequest` (POST by reflection), `Data/SiteProtocol` (answers, tested) |
| Timed runs, ghosts, lines | `Modules/PracticeRunModule`, `Data/RunRecorder` (`RunCompare`; `ItemChange` track), `Data/AttemptFormat` (`.run` text, tested), `Data/LineBuffer`, `Game/DebugDraw` (`RunLineBehaviour`), `Game/PlayerStateReader` (5 Hz stats), `Game/ItemCounter` (carried items, event-driven) |
| Splits panel (on screen; set up, moved and resized in Edit HUD mode via `OverlayModule.DrawHudEditor`, T-0257; its run timer in the panel or popped out as the HUD's Run timer value, `Data/TimerText` + `SplitTable.TimerColour`, T-0336), runner identity (Runs tab > Upload) | `Modules/PracticeRunModule.Splits.cs`, `Data/SplitTable` (tested), `Data/RunHistory` (PB chance, total playtime, `runs/<id>/unfinished.txt`; tested), `Data/LssFile` (LiveSplit `.lss` parse / match / links, tested), `Modules/PracticeRunModule.LiveSplit.cs` (Runs tab: file per segment, match, Compare to -> LiveSplit), `Game/RunnerIdentity` (Steam name, hashed id), `OverlayModule.DrawScreen` |
| Endgame split events | `Game/GameEvents` (Harmony postfixes + `endGameCutScene` poll) |
| The autosplitter's other splits (caves, clothing, passengers, starts) | `Game/WorldEvents` (polls the ASL's fields; events via `GameEvents.RecordWorld`), editor picker groups in `Modules/PracticeModule` |
| Reload on death / practice revive | `Modules/DeathModule` (Deaths tab; one choice, the fallback is always a reload - T-0226), `Game/DeathHooks` (Harmony prefixes; `HandleLanded` prefix/postfix for the fall revive) |
| Views (was Debug views), freecam, volume filters | `Modules/DebugViewModule` (also draws Settings > Performance and Developer's Benchmarks / Experimental folds), `Game/DebugDraw`, `Data/VolumeFilter` |
| TAS input record / replay (experimental, practice only) | `Modules/TasModule` (no tab: the Runs tab draws its section; `[TAS]` config; hotkeys `tas.stop` = `]`, `tas.record`, `tas.replayLatest`), `Game/TasInput` (Harmony postfixes on `TheForest.Utils.Input` via `Game/InputInject`: records what the game read each frame, replays by replacing the results; frame-rate lock with `Time.captureFramerate`), `Data/TasRecording` (`.tas` text in `runs/<id>/inputs/`, changes only; tested) |
| Paint (practice mode only, T-0219) | `Modules/PaintModule` (hold keys `paint.paint` Mouse 4 / `paint.erase` Mouse 5, `paint.undo` X; the aiming cross; per-spot files `config/ForestOverlay/paint/<spot id>.txt`; Settings > Paint fold: swatches, size, Undo, Clear all), `Game/PaintDraw` (aims at the drawn surface: `Game/SurfaceProbe` reads the depth texture under the crosshair, snapped to a terrain collider that agrees, or a mesh collider from 10 m; discs + ribbons as chunked meshes drawn then clipped in a depth band in the late pass (hidden 2 % behind the scene, cut 8 % in front), palette made linear), `Game/SurfaceProbe` (7x7 depth patch through a GL quad, no allocation), `Data/DepthMath` (depth value -> distance, normal; tested), `Data/PaintSet` (dots, strokes, erase, undo, file; tested) |
| Trajectory preview (experimental, practice only) | `Modules/DebugViewModule` (switch in Developer > Experimental, hotkey `debug.trajectory`, HUD `Flight` / `Boost` lines), `Game/TrajectoryView` (10 Hz, reads the player's flight numbers, capsule sweeps, GL lines), `Data/Trajectory` (the physics step and knockback maths; tested) |
| Perf log line, game profiler | `Core/PerfMonitor` (fed by `ModuleHost`, `Plugin.OnGUI`, `DrawTarget`; GC frame lengths), `Game/GameProfiler` + `Data/ProfileTable` (tested; Developer > Benchmarks), `Game/AllocationTracker` (Mono allocation profiler: exact bytes by type, by method with the profiler; Developer > Benchmarks), `Game/RestoreGarbage` + `Data/StepBytes` (a Quick load's garbage per step in its log line while the tracker counts, T-0202), `Game/PerfPatches` (behaviour-preserving allocation patches, `[Performance]` switches, Settings > Performance; the experimental ones in Developer > Experimental), `Game/LoadTiming` (`Load timing:` lines: asset unloads, forced GCs, the game's own load timers, scenes, hitches), `Game/MemoryCensus.RunScene` (scene census, bridge only), `Game/FrameTimer` + `Data/FrameTimeline` (`Frame (30 s):` line: waiting vs scripts vs each camera; tested), `Game/RenderProbe` (bridge: what a camera draws, who reads a texture), `Game/CameraTrim` (cameras that drew for nothing), `Core/StallWatch` (`Stall:` lines: a background thread names the hook / module / tab the main thread was in when it stopped for 10 s, also into `logs/stall.log` during the hang; T-0284) |
| Updates, changelog | `Core/UpdateChecker` (incl. `TidyPluginFolder`), `Modules/UpdateModule`, `Data/ReleaseJson` (`ExtractNotes`), `Data/UpdateStaging` (staging under any file name), `Core/UpdaterInstaller`, `patcher/`, `CHANGELOG.md` |
| Network switches (what is asked online by itself) | `Core/NetworkSwitches` (`Network.CheckAtStartup`, `Network.RefreshAutomatically`; T-0326), Settings > Network; every automatic request checks one, each list has a Check now when refresh is off |
| Load leak diagnostics and fix | `Game/LoadWatcher` (every load), `Game/MemoryCensus` (static + DontDestroyOnLoad roots, sizes, threads, Unity objects by type), `Game/LeakedThreads` (stops the two threads a load leaves), `Game/StaleSubscribers` (drops dead event subscribers), run from `Modules/SavestateModule` |
| Logs in the inventory (gameplay mod) | `Game/LogStore` (patches + transpiled holder / repair reads; `logs` savestate header), Inventory tab (`Modules/InventoryModule`: toggle, cap, HUD `Logs n / cap`); game-notes *Logs* |
| Timed run split order | `Data/SplitSequence` (pure, tested) |
| QA team tooling | `Modules/QaModule` (QA tab: list, answers, log-line evidence, Mark, report zip), `Data/QaList` (list / answers format, tested), `Data/ZipWriter` (stored zip, tested), `qa/*.txt` (shipped lists), `Core/LogKeeper` + `Data/LogArchive` (last 3 sessions' logs in `config/ForestOverlay/logs`; at startup a Unity crash folder gets its session's log + `unity.log`, `Data/CrashFolders`, tested), `Core/UnityErrorLog` + `Data/UnityLogFilter` (`Unity:` lines: Unity's own errors / asserts / exceptions, render thread included, also into `logs/unity.log` at once; repeats counted, 30 a minute; tested; T-0276) |
| **Live test bridge** (dev) | `Modules/BridgeModule` (file polling, queue, commands, `mark` / `shot` / `anim`), `Game/ObjectProbe` (generic reflection: find / inspect / get / set / call), `Game/AnimProbe` (player animator readout), `Game/DebugDraw` (`MarkerBehaviour`), `Game/InputInject` + `Data/InjectedInputs` (press / hold the game's controls, tested), `Game/FsmExport` (`fsm`; its value text in `Data/DumpText`, tested), `Data/BridgeCommand` (parsing, tested), `scripts/bridge.sh` (this end), `tools/BridgeMcp` (the MCP server over it, incl. the QA Discord bot) |
| **Run mode** (a run spot's Restart = a run, practice locked, integrity report, codes + receipts, categories) | `Core/RunMode` (`Ctx.Run`: `Refuse(feature, what)` at every practice entry point, `Locks` / `Forces` for gameplay switches, flags, `Use`), `Data/RunCategory` (the categories' text + features, tested, linked by the site), `Modules/RunModeModule.Categories` (fetch, cache, Runs tab pick), `site/ForestSite/Categories` (versions, speedrun.com sync, judging, tested), `Modules/RunModeModule` (attempts from a run spot / by hand; section at the top of the Runs tab; `EndRunMode`), `Modules/RunModeModule.Codes` (the hash chain, the on-screen code, the log), `Modules/RunUploadModule.Attempts` (nonce, checkpoints, outbox, links; attempts deleted on the site leave the list: `Data/SentAttempts`, tested), `Data/AttemptChain` (log + chain, tested, linked by the site; `move` lines), `site/ForestSite/Attempts` (endpoints' logic + `Judge` + `MoveNotes`, tested), banned-move detection `Data/MoveDetector` (tested) + `Game/MoveWatch` + `Game/ClipWatch` (clips, lifts) + `Modules/RunModeModule.Moves`, `Game/RunIntegrity` (game hash, other plugins / patchers / code, foreign Harmony patches, `Cheats` statics), `Data/RunReport` (findings in plain words, tested), `run-reports/`; design and phases: [`docs/run-mode.md`](../run-mode.md) |
| Cutting a player action on a reset | `Game/MenuClose` (the pause menu / inventory, before anything - they stop game time), `Game/BookClose` (the survival book, first), `Game/BuildMode` (a blueprint out: put away, the captured one back - `blueprint` header), `Game/AnimReset` (rest learned in `PracticeModule.Tick`; called after in-place restores and teleports), `Game/DeathSequence` (the game's own death in progress ended on an in-place restore / Go; the death count reset as a load does, T-0248) |

## Harmony

Prefer read-only Harmony `Postfix` observers — for update resilience and plugin
interop, not legality. Harmony is HarmonyX 2.7, via `BepInEx.Core`; no extra
package. The one place that **replaces** game behaviour is `Game/DeathHooks`
(prefixes on `PlayerStats.CheckDeath` / `Fell` that skip the original only when
quick-load or revive acts). Patches take `__instance`, `__args` and
`__originalMethod`; key lookups on `Type::Method` strings, not `MethodBase`
identity.

## UI

**`F2` opens one window; everything is a tab.** A hotkey per panel does not
scale. Per-feature keys still exist and are rebindable, but they open the
window on that tab and are **unbound by default**.

Tabs: Practice, Map, Runs, Inventory, 100%, Deaths, Views, Settings,
QA, Updates, Developer. **Developer** (`Modules/DeveloperModule`, last) holds
what a run never needs - test bridge, benchmarks, experimental features,
TAS, memory census, dumps, and the developer-only keys (dumps, type
explorer, TAS, QA; drawn by Settings, T-0263) - each fold drawn by the
module that owns it (T-0226). Settings is one page of folds: Keys, HUD,
Performance, Loads and savestates; its key list and HUD values sit under
short headings (`Data/KeyGroups`, `HudLines.HeadingAt`, T-0263). **One setting, one feature** (T-0253): Settings > HUD
puts the HUD-wide options (compact, text size, position) above the values
(T-0018: no info box - a column of values, placed values, orange flags); Runs keeps the run lines' options under *Run lines* (shown
only while on) and the ghost's switch + an opacity for the ghost and replay
under *Ghost and replay*; the results panel has its own load-time choice. The QA tab goes at the public release. The type explorer keeps its own window (`F10`) — it needs the
space and is a dev tool, not runner-facing.

While the window is open the player is held (`LockView`) and the game's key
map is switched to `Menu` (`Game/GameInput`), so clicks never reach the game.
Settings shows *Game input: blocked* when that is working.

| Default | Action |
|---|---|
| `F2` | Open the ForestOverlay window |
| `F5` | Show / hide **all** overlay UI |
| `F6` | Save spot here |
| `F7` | Restart the selected spot (restores its start state, if it has one; a timed segment turns practice mode on) |
| `F9` | Practice mode on / off |
| `F10` | Type explorer |
| `F11` | Write dumps |
| `F12` | Manual split / finish |
| `[` | Abort run |
| `Keypad *` | Freecam |
| `Mouse 4` / `Mouse 5` (hold) | Paint / erase paint (practice mode) |
| `X` | Undo the last paint stroke (practice mode) |
| *(unbound)* | HUD values only; each tab |

`F1` is deliberately free — the game's own dev console uses it.
All keys are rebindable in **Settings** (the developer keys in
**Developer > Keys**), or in
`BepInEx/config/com.deter.forestoverlay.cfg`.

## What works

Module host with tabbed UI, rebindable hotkeys, HUD, velocity, per-item
inventory, 100% checklist + nature guide + To Do list, type explorer, dumps,
unified practice spots/segments with an in-game editor and zone preview,
segment-driven timed runs with **ordered checkpoints**, ghosts, live deltas
and run lines, full player-state capture, **separate endgame split events**,
**quick-load on death (no menu)**, **practice revive** (no hard-landing
aftermath), cave-aware teleports, **savestates** (capture / restore in place
/ restore with load, no save slot used, **across saves**), **segment start
states** (in the route fingerprint), **sharing** (one `.foseg` file per
segment, Export / Import) and **community packs** fetched from the repo,
turned box zones, coordinates as text fields, debug views (freecam / colliders /
triggers / wireframe, with size and name filters), game input blocked while
the window is open, an on-screen notice, a 30 s perf log line,
self-installing updates **with a changelog in the Updates tab**, a
**memory census on every load**, offline IL
scanner, the live test bridge with its **MCP server** (drive the game,
screenshots, logs, restart / update the game) and the **QA Discord bot**;
a **LiveSplit-style splits panel** on screen (every column toggleable, set
up and sized in Edit HUD mode; Compare to:
PB / best segments / another runner / a LiveSplit file), the autosplitter's
events, one-click `.lss` import, run uploads to **forest.deter.cloud**
(spots, runs, comparisons, photo map, 3D world), gameplay mods (god mode,
item caps, logs in the inventory, fast building) under the HUD's "ON NOW".

## Performance reports

- **Performance reports**: another runner's slowdown with ghost lines /
  recording (not reproducible here, 4080 Super / 7800X3D), maks's
  background performance - read their `Perf (30 s):` / `Slow tick:` lines
  before changing anything. Author's own: overlay tick <= 0.02 ms, GC 0-1
  per 30 s; one-off `Slow tick:` lines for `collectibles` / `inventory`
  (15-100 ms) while the player binds during a load, and `deaths` /
  `savestates` (~100-490 ms) while a load starts, are the load itself -
  left alone.

## Gotchas

One line each, numbered as in [`docs/gotchas.md`](../gotchas.md) (full story, version and fix - read the entry before working near it). A new lesson gets the next number there and its one line here, in the area it belongs to, ending with its marker: `[check: <lint / test>]`, `[check: T-n]` (the task building it) or `[judgement]` (`lint.py` checks it; T-0009).

### Game and engine

1. **The game re-asserts state every frame** - set its own flag (`IsMouseLocked`, `LockView`), never "win the frame". [judgement]
2. **`OnGUI` runs several times per frame** - never allocate in it. [check: lint.py alloc]
3. **A throwing `Awake` silently kills the plugin** - try/catch every lifecycle method. [check: lint.py check_lifecycle]
4. **Don't trust assumed names** - everything in `src/Game/` comes from a dump or IL. [judgement]
5. **The F11 dump is metadata only** - behaviour questions go to `tools/ILScan` (`strings` finds `SendMessage` callers). [judgement]
6. **Cached component references go stale across a load** - re-resolve; prefer the game's statics. [judgement]
7. **Edge semantics** - a start zone fires on crossing, checkpoints / ends on entry. [check: CrossingTests]
12. **`OnRenderObject` runs once per camera** - GL overlays check `DrawTarget.ShouldDraw()`. [check: lint.py check_render]
13. **Search `strings` for every method of an action** - cutscenes start by `SendMessage`, invisible to `refs`. [judgement]
14. **Labels from memory are guesses** - confirm runner-visible names against a log. [judgement]
16. **The log and the bridge are the test harness** - every mechanism logs one line saying what it acted on; every prefix and its meaning: `docs/log-lines.md`. [check: lint.py log catalogue]
17. **A library method may do X in one mode only** - read the whole body (`ilscan body`). [judgement]
18. **Static or instance: check before binding** - `ilscan type` marks statics. [judgement]
22. **A hook runs mid-method; the caller carries on** - read the caller past the call; apply a sequence's end state. [judgement]
23. **Whose lock is it?** - note what the game already held; hand back only what you took. [judgement]
24. **A static walk cannot see every root** - count threads, read `OnDestroy`, ask what the title screen clears. [judgement]
25. **A theory from IL alone is a guess** - ship the log line that proves it first. [judgement]
27. **Read the whole "removed" line** - check every item a removal names was really taken. [judgement]
29. **A value the game fills in later reads as a default** - wait for the value, not a count; test several kinds. [judgement]
30. **Check when a file is created before planning to copy it** - BepInEx truncates the log first. [judgement]
32. **Look for the game's reverse operation first** - `Regrow` / `Respawn` / `Reset` / `Restore`. [judgement]
33. **Copying a scene object** - copy under an inactive holder, local values, reset runtime flags. [judgement]
39. **A pool object carries its first user's state** - compare handles / clone names across visits. [judgement]
41. **Where in the frame a call runs matters** - look for one-frame guards (`LockPlace`); bridge `call` runs in `Update`. [judgement]
43. **A switch can be latched off before you arrive** - ship a "did it see anything" count with a runtime hook. [judgement]
46. **A symptom that appears later can be a coroutine finishing** - look again after every pending timer. [judgement]
52. **A hook can run twice before the Destroy lands** - key "do once" on the instance; pairs of identical log lines are the tell. [judgement]
53. **A game's own database can be wrong** - spot-check it against live objects before building on it. [judgement]
54. **Drive a UI the way the game does** - `SetActive` pokes skip the game's teardown; show things through its own click path. [judgement]
55. **Read what the fallback changes, not only why it fires** - a harmless fallback's message can be the whole symptom. [judgement]
56. **`Camera.CopyFrom` copies the Camera only** - a game camera's look lives in its sibling components; move the real one. [judgement]
57. **The player's things are not all under the player** - the inventory's views are their own root (`INVENTORY`); list the player's roots before deleting "outside the player". [judgement]
58. **Switching a camera off changes Unity's "current" camera** - the last one drawn; `targetTexture` set on it outside rendering is a native crash. Native crash dumps are readable with Unity's player PDB. [judgement]
62. **Record what changed, when the game changes it** - not a fixed list per sample: `ilscan writes` finds every writer to hook (v0.24.161 items). [judgement]
100. **A dump's function name can be one of several folded functions** - check the PDB for others at the address; Mono JIT frames are read from the rbp chain, and a Vector3 left in a frame can name the object (T-0143). [judgement]
102. **List a flag's writers before reading it as one event** - `onRope` is also every cave-mouth crawl and four cutscenes; a game snap (a cave mouth, 9 m) is not a teleport (T-0242). [judgement]
103. **A restore in place keeps everything outside the save, running Invokes and coroutines too** - diff the player's fields after a Full load vs in place, then check `IsInvoking` (T-0248). [judgement]
104. **A render-thread crash names the command, not who queued it** - find a native call's senders by its vtable slot in the exe; Unity's own errors (the d3d11 line) are in no kept log (T-0190). [judgement]
110. **A collider is not the drawn shape** - a pool rock's collider is a sphere (Rock05, r ~2.6 m), far off the game may draw a coarser cave mesh (+-17 cm), and a cave's mesh collider is itself up to 10 cm off its near mesh; aim at what is drawn (the depth texture), snap to a mesh collider only from 10 m, and let paint show further in front of the scene (8 %) than behind it (2 %) (T-0296). [check: T-0296]

### Restores (savestates)

20. **A restore can bring back a flag without its effects** - send the game's message for the state (`InACave`). [judgement]
21. **Ids are per game, not per scene** - cross-save work maps ids (`AdoptPlayer`). [judgement]
26. **A restore runs frames** - judge the "before" state when the restore starts. [judgement]
34. **One teleport, many callers** - `grep MoveTo(` and cover every caller. [check: lint.py moveto baseline]
35. **Parity with Full load stops where the save stops** - decide against the capture. [judgement]
36. **A diagnostic read mid-rebuild reports the rebuild** - re-read a few seconds later. [judgement]
37. **"Left alone" is not "stopped"** - stop an action in flight, apply its end state, then restore. [judgement]
38. **Bookkeeping must survive the restores it serves** - test the chain, not one restore. [check: e2e restores]
96. **An absence check needs a presence control** - assert the object is there before the cut; `find all` also sees the keepers' inactive copies (NatureKeeper's cut bushes): look for the live object at its scene path. [check: e2e restores]
40. **A cutscene can parent the player** - test via `restore` (no teleport); set tests up the way a run reaches them. [check: e2e restores]
47. **A restore that throws the player: ask what held the body** - kinematic modes (rope, zipline, sled, climb, glider). [judgement]
48. **A frozen frame can count as game time** - `maximumDeltaTime` is 9; time the event, not the freeze. [judgement]
79. **A component that sets itself up once misses an in-place load** - `_initialized` + `DelayedAwake` (nature guide, to-do list); a save field back is not the state back. [judgement]
80. **Serializing has side effects** - `OnSerializing` writes live fields (the book open: hands recorded as stowed); decide what a capture records from live objects. [judgement]
81. **A carried object is saved where its parent puts it** - a pushed sled is the player's child (restored near the origin); read what an action parents / destroys and look at the object after a restore. [judgement]
84. **The same game call can need setup only one entry path does** - `LoadSavedLevel` from the title screen hung (no prefab list); drive the menu's own path (`Game/TitleLoad`). [judgement]
87. **An exit is an event, not a flag** - our tp out of the endgame cleared `IsInEndgame` only; the game's `ExitEndgame` event also turns the sun back on. Invoke the trigger's UnityEvents; test from a save loaded inside. [judgement]

### Performance and rendering

11. **`Resources.FindObjectsOfTypeAll` is a stutter** (and `FindObjectOfType`: 22-25 ms in ForestMain) - prefer the game's static handle, else find once, keep (`Game/SceneCache`) or note them as they start (`Game/ElevatorRides`), rate-limit re-searches; check `Slow tick:` lines first. [check: lint.py findall baseline]
42. **Measure the measurement** - ask what the instrument adds; baselines on a fresh launch (the profiler's hooks leave garbage until a restart). [judgement]
45. **Load waits are not their stated time, and diagnostics can be the hitch** - time in real seconds; cost every on-event diagnostic. [judgement]
49. **One heap reading after a load is not a trend** - read `GetTotalMemory(true)` over a minute, with a control. [judgement]
50. **A camera costs its culling whatever it draws** - count cameras (`Frame` line) before optimising what they draw. [judgement]
51. **The picture needs eyes** - a render change that measures right can still freeze the screen; ask the author to look before a release. [judgement]
59. **Log the work, not the queue** - a queue shows what waits; hook the enqueue (bounds + caller). Check a merged game list is ever cleared. [judgement]
101. **A swallowed exception is garbage nobody sees** - binding by shape, check the member's type too; an exception type in an allocation report is a bug looking for its catch. [judgement]
105. **A forced collection can be in the right place** - the collector's own trigger may be a couple of restores away; skipping the game's `GC.Collect` moved the pause into play (T-0202). Measure the trigger and where the pause lands; cut garbage instead. [judgement]
108. **A stopwatch around a call misses the frames it sets off** - `UnloadUnusedAssets` and a queued `Invoke` land frames later; time an action by its frames (`FrameTimer.Spikes`), then split the long ones (T-0072). [judgement]
111. **D3D11 flips textures under you** - `Graphics.DrawTexture` target to screen drew mirrored (use `Blit`); `Blit` ignores the material's texture scale / offset (draw a GL quad); a GL quad over a depth texture needs v flipped when `graphicsUVStartsAtTop` (T-0296). [check: T-0296]

### UI

31. **UiText covers the HUD and fixed labels too** - after UI work, sweep tabs with `shot` and push a long value through. [check: lint.py label20, e2e tabs (shots, for eyes)]
60. **A config write saves the whole file** (86 ms) - sliders / text fields keep the value and write once it settles; drags write on release. [check: lint.py cfgwrite baseline]
61. **A sentinel inside the value's range is reachable** - `PanelX = -1` ("right edge") was hit by a drag past the left edge; clamp live input, apply the sentinel only to the saved setting. [judgement]
107. **A dynamic font forgets glyphs not asked for lately** - a large font size whose characters change (Speed 0.00 <-> digits) rebuilt its texture, ~20 ms a time; keep the glyph set requested every repaint (`HudWidgets.KeptGlyphs`). [judgement]
109. **A scaled `GUI.matrix` stretches text** - glyphs are rasterised at the font size and blurred by the scale (x1.88 at 2560x1440, T-0333); text in the scaled HUD goes through `Core/CrispText` (draw and measure). [judgement]

### Run mode and detectors

82. **An integrity check must know what the platform and the game do themselves** - BepInEx patches .NET methods, Creative turns on GodMode / InfiniteEnergy / NoSurvival; run it on a clean install and every game mode before trusting a "NOT OK". [check: e2e runmode]
89. **Before calling something new tech, read what the runners already know** - QA history, report folders, speedrun.com guides; a code branch is not a mechanic until a real input reaches it (the "water wall jump"). Bridge `tp` stops elevator rides; spawn tests clear of steep slopes. [judgement]
90. **A speed and a distance in the same window can belong to different things** - a tp landing while the body held 300 m/s read as huge speed; a step longer than the speed allows is a teleport. Test detectors with tp / set mixed in. [check: MoveDetectorTests teleport cases]
91. **A world object can be a mover, and a teleport lands inside things** - the yacht's hull bobs on a kinematic body (a "clip" + "lift" while walking on it); every tp is pushed out of what is there. Check a collider's pose twice; settle after teleports. [check: MoveDetectorPhysicsTests teleport cases]
