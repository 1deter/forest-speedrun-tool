# Savestates - how they work

The detail behind docs/areas/plugin-concepts.md *Savestates* (moved out 2026-09-26). IL and game internals are in game-notes *Saving and loading*.

- **Savestates** (no tab: Practice start states + the bridge; practice-only; game-notes *Saving and
  loading* has the IL). The game's own level serialization
  (`LevelSerializer.SerializeLevel`) written to
  `config/ForestOverlay/savestates/*.fosave` — never a save slot or Steam
  Cloud. Capture mirrors the game's save routine. Two restores:
  - **Quick load** = in place (~0.15 s on a fresh heap, no load): `LoadNow`, plus what
    `LoadNow` does not do for a full-level save — delete objects not in the
    save (walls built since; **never weapon-upgrade receivers**, v0.22.7),
    clear their build-mission HUD line, stash held items — and
    `Game/PickupKeeper` puts taken world pickups back. Afterwards the
    **cave state is sent outright from the file's `cave` flag**
    (`GameBridge.ForceCaveState`): the serializer restores the flag without
    its effects; since v0.24.123 it also switches the cave mouths' black
    walls as walking in / out does. Cave panels broken since come back
    from one intact copy each (`Game/PanelKeeper`, v0.24.123: a swing
    ran `CutDown` twice and the second, piece-less copy was rebuilt).
    Restart closes the game's pause menu / inventory first
    (`Game/MenuClose`, v0.24.123: their timeScale 0 stalled the restore). Spears and limbs left since the capture are removed;
    trees chopped since regrow, bushes / saplings cut since come back,
    and their new logs / sticks go (`Game/NatureKeeper`, v0.24.61-62); boss
    Megan is put back seated when she was at capture (`megan` header,
    `Game/MeganKeeper`, v0.24.35-37; game-notes *Megan after a Quick
    load*); the endgame elevators and active area as at capture
    (`ElevatorKeeper`, `AreaKeeper`, v0.24.40-41; a ride under way is
    stopped first, v0.24.64). A capture outside the endgame restored with
    the endgame flag set leaves it the game's way (`ExitEndgame`,
    `CapturedAreas.ShouldLeaveEndgame`, T-0075): the flag is not in the
    save, and with it set the caves' props scenes stay unloaded (a Cave 6
    spot restarted from the lab had no body piles, ropes or planks).
  - **Full load** = with a scene load (~5-15 s): `LoadSavedLevel` — the
    second half of the game's own load. Afterwards (v0.24.25-0.24.28): the
    player is held at the captured spot until every scene loaded at
    capture is back, the endgame area is force-loaded if the capture had
    it (`endgame_streaming` loaded or loading, or `endgame_animPrefabs`
    with `endgame yes` - the vault door's load caught mid-way; a lone
    `endgame_animPrefabs` outlives a tp out of the lab and does not
    count, nor is it waited for - `Data/CapturedAreas`, 2026-10-04; a
    Quick load loads it first by the same rule), placed pickups taken before the capture are removed, the captured
    cannibal families are rebuilt, the held items are equipped again
    for the animator (v0.24.43), bushes / saplings cut at capture are cut
    again (`cutbushes`, v0.24.65-66).
  Both: a sun still out of step with the restored time is snapped
  (`Game/SunSync`, v0.24.67). A cutscene capture is fast-forwarded (25x) with the held weapon's
  memory put back (`heldbefore`) and its sounds kept in step
  (`Game/CutsceneAudio`, v0.24.36).
  **From another save** (sharing): every game gives its objects its own
  `UniqueIdentifier` ids, so an in-place restore first **adopts** the saved
  ids — every live identifier the save lacks takes the id of the saved
  object with the same name, prefab class and parent id, shallowest first,
  unique matches only; identical siblings pair in order
  (`SavestateBridge.AdoptPlayer`; unmatched ones are named as `other
  misses:`). **Across Creative and survival** (the mode is not in the
  save) the restore is a Full load that switches the game to the
  capture's mode first (v0.24.211, author 2026-10-02; refused before, and
  the `AllowCrossModeRestore` testing switch is gone). Captures since
  v0.24.211 also write `basedifficulty`; a Creative file loads Peaceful
  underneath, as the game does (2026-10-05). Another difficulty is a
  Full load too (*Category start states* below). Confirmed over the bridge:
  Normal -> Creative (GodMode / InfiniteEnergy / NoSurvival on) and back
  (all off).
  The file header lists the world pickups at capture and whether streaming
  was unloaded; `Data/SavestateFile` is pure and tested. Sticks / rocks
  around pooled trees are given back as captured (`greebles` header,
  `Game/GreebleKeeper`, v0.24.70; game-notes *Greebles*). **Enemies**:
  capture writes `families` / `enemies`; after a Quick load on the
  surface and after a Full load, `EnemyKeeper.Rebuild` runs the game's
  `startSetupFamilies`, builds each captured family and places every
  member by kind with its health (sleepers back asleep); a cave capture's
  cave families are kept and moved back (`RestoreCave`). Open: does
  `updateSpawns` top up a random family; weapons are whatever the spawn
  gives (game-notes *Cannibal kinds and families*). **From the title
  screen** a restore is a Full load through the menu's own load
  (`Game/TitleLoad`, v0.24.212; game-notes *Saving and loading*); a Quick
  load there is still refused (v0.24.73).
  The nature guide's ticks are set back as the save lists them
  (`Game/NatureGuideKeeper`, v0.24.187) and the book's to-do list is set
  up again (`Game/TodoListKeeper`, v0.24.188) - both only prepare
  themselves once (game-notes *Survival book*). A capture with the book
  open takes the held items from what the book put away (v0.24.188).
  With *Logs in the inventory* on, the `logs` header keeps the stored
  count and it is set back when the restore ends (`LogStore.Apply`,
  v0.24.135; the game's own `_logs` round trip was unreliable once).
  **Rides** (v0.24.201-202, `Game/RideModes`, `ride` header =
  `Data/RideState`): a capture on a zipline, log sled, hang glider (held
  or flying) or cliff climb notes it (read before the capture's frames);
  a Quick load (after the hands are back - the climbing axe) and a Full
  load (after the hold) put the player back with the game's own entry
  calls: on the line at the captured spot and speed, pushing the sled at
  its captured place, gliding at the captured velocity (the save's
  dropped glider picked up), on the cliff. Before a Quick load a ride in
  flight is ended and a held glider dropped (one not in the save is then
  deleted by the restore). Cave ropes: `Game/RopeClimb` (`rope`). Not
  covered: a climbing rope on a wall (`PlayerClimbWallAction`, ended
  only), the raft.
  **Blueprints and menus** (v0.24.203, `Game/BlueprintKeeper`,
  `blueprints` header = `Data/BlueprintState`): the capture lists every
  placed blueprint's ingredient counts; a Quick load deletes a blueprint
  whose counts changed since (its HUD share cancelled) so LoadNow builds
  it from the save, then recounts the build HUD tally from the live
  blueprints. Capture and every restore close the game's inventory /
  pause menu first (`MenuClose`): the crafting cog is not in the save,
  and both run over game time.
  **Weather** (`Game/WeatherKeeper`, `weather` header =
  `Data/WeatherState`; 2026-10-04, unreleased): the game's save has no
  weather (only `LastRainTime`), so a Quick load kept the live rain /
  clouds / fog (maks's fog after a Quick load) and a Full load came back
  clear. The capture writes the weather's state and rain type, the rain
  rolls, every cloud value (current, target), the cloud materials as
  drawn and `TheForestAtmosphere`'s fog distance (`FogCurrent`,
  `Visibility`); a Quick load (with the other keepers) and a Full load
  (after the hold) put them back - the rain objects through the game's
  `AllOff()` / `TurnOn(type)`. Older files have no line and leave the
  weather as it is. Log: `weather: Raining (Heavy), overcast 1, fog 300 m
  put back (was Idle, overcast 0, fog 1294 m)` in the restore line (Quick)
  or `Savestate after the load: weather: ...` (Full). Not kept: a
  rainbow, a lightning flash, when the next roll comes (random anyway).
  **The frames after a Quick load** (T-0148, 2026-10-07, unreleased): every
  spot restart was followed by two `Load timing: hitch` lines (the author:
  ~380 + ~255 ms). The first is the restore's continuation (all the keepers
  in one frame): six scene walks (`FindObjectOfType` / `FindObjectsOfType`,
  20-37 ms each) - now kept between restores in `Game/SceneCache` (rules in
  `Data/LookupCache`, tested: until a scene loads / unloads or a kept
  object dies or goes inactive; nothing found is never kept). Single
  objects and the trees only; the elevators are still searched every
  restore (~24 ms - they could appear without a scene event; a teleport's
  "stop a ride under way" walks only the elevators that started one since
  T-0184, `Game/ElevatorRides`). The second
  is the plane wreck the Quick load re-creates running the game's crash
  clearing again (~165-195 ms): when a wreck already stands at that spot
  (`Data/WreckSites`, tested) its plant / LOD removal still runs as the
  game's does (~25 ms) and only the grass cut is skipped - it only ever
  writes 0, already there (`Game/WreckClearing`, `Plane wreck:` line).
  The restore line ends
  `after the load: N ms, S scene search(es), K kept`. Left: LoadNow's own
  frames (150 + 170 ms on the Labskip spot) - the game's deserializer
  (game-notes *The frames of an in-place restore*).
  **A Quick load's garbage** (T-0202, 2026-10-10, unreleased): a restart
  loop made ~36 MB of garbage a restore, a quarter of it the plugin's
  (game-notes *Performance*). The last state file read is kept parsed
  (by path, size and write time; each restore gets its own `Copy()`), the
  last level data read for its ids is kept (by content: the decompressed
  bytes, the id set, the saved objects) and handed to `LoadNow` as bytes,
  and the cave panels are matched by a number (`Data/PositionKey`), not
  490 strings: **27.8 MB a restore (19.1 on the main thread), from 35.9
  (27.2)**; the named savestate 28.9 (19.7) from 37.8 (28.6). Still one
  collection a restore - the game's loader forces it (left as it is:
  skipping it moves it into play; T-0202's question). An old wreck's
  root-level nav cutter now goes with the wreck (`plane: 1 old wreck(s)
  removed with 1 nav cutter(s)`; 12 had piled up after 12 restores; the
  navmesh is the same). With the allocation tracker counting (Debug
  views), the restore line ends `garbage (main thread) 8.6 MB: LoadNow
  7.6, ..., GC x1` and the line a few seconds later says the total since
  that restore started (`Game/RestoreGarbage`).
  **A Quick load during the game's own death** (T-0248, 2026-10-09,
  unreleased; runner maks): the death ran on through the restore - the
  capture went ahead, a real death's `GameOver` still loaded the title,
  a restart while hanging in the cave left the player at the spot upside
  down with the rope on the hips - and `DeadTimes` (not in the save) kept
  counting, so the death after a capture was a real one. Every Quick load
  now sets `DeadTimes` 0 / `doneDragScene` false as a load does, and one
  that finds the death in progress (`Data/DeathProgress`, tested: `Dead`,
  hanging, the drag-away's cannibals, the death view, a pending
  `BlackScreen` / `KillPlayer` / `GameOver`, a dead cam - each set only by
  the death chain in single player) ends it before anything else
  (`Game/DeathSequence`, game-notes *The death chain and a restore in
  place*): PlayerStats' coroutines and the chain's Invokes stopped, the
  cutscene clones and the rope destroyed, controls, look, body physics,
  animator, cameras (only what the chain changed) and HUD as the game's
  wake-ups leave them. If anything still shows the death after that, or
  a step throws, the restore is a Full load instead (author: only then).
  Go ends it the same way before it moves the player. Proved step by step over
  the bridge (the fall, the drag-away, the hanging, the dead cam) and
  diffed against a Full load of the spot: left over only `CamRotator`'s
  range (the game's own reset 135 vs a fresh player's 145) and what is
  not the death's (body temperature, cave flags; T-0269). Log:
  `Savestate restore <what>: ended the game's death (hanging in the cave;
  removed 1 cutscene object(s)).`, the fallback's warning `... - a Full
  load instead.`, and `death count 2 -> 0 (as a load)` in the done line.

## Category start states true to the game (2026-10-05, unreleased)

Backlog item (QA `1553867722964607110`): a start state - a run spot's
above all - must give the game it was captured in, as the game's own
load of that save would. Method (bridge, v0.24.245): a new game per mode
(Normal, Hard, Peaceful, Creative), captured in the plane right after the
intro; GameSetup, `Cheats`, `GameSettings.Survival / Animals / Ai`,
`Clock`, the player's stats, `mutantSpawnManager` and `mutantController`
read after each restore and diffed against the fresh game of the
capture's mode. Found:

| Restore | Before | Now |
|---|---|---|
| **Any Full load in a launch whose first game was New** (same mode or not) | **hung on LOADING for good** - the game's prefab list was empty (game-notes *The prefab list*) | the list is filled as the menu's load does (`Game/PrefabList`); a Full load from that state came up in 11 s (proved over the bridge) |
| A Quick load in such a launch | an object destroyed since the capture came back as empty `CreatedObject`s (a blueprint: identifiers +2, no `Craft_Structure`, its build HUD share missing) | the same list; with it the blueprint came back whole (bridge control) |
| Quick load into another difficulty (Normal capture in a Hard / Peaceful game, Peaceful capture in a Normal one) | kept the live game's difficulty: its `GameSettings` (Hard: cannibal damage x2, health regen 0.5, ...), spawn caps (Hard 4 skinny + 2 regular, Normal 6 skinny), Peaceful's no enemies (`Cheats.NoEnemies`, 0 spawners) | a Full load (`MustLoad`, `SavestateFile.ModeMismatch`), as for Creative <-> survival |
| Full load across Normal / Hard / Peaceful / Creative, either way | as the fresh game of the capture's mode (only `Init` Continue, as any saved game, and the clock's few seconds) | unchanged |
| Full load of a Creative file without `basedifficulty` (before v0.24.211) | kept the live game's difficulty under Creative (e.g. Hard) | Peaceful, the game's own rule for Creative (`LevelSerializer.Resume`) |
| A cheat left on by the console / bridge / a mod (`GodMode`, `InfiniteEnergy`, `NoSurvival`, `Creative`, `UnlimitedHairspray`) | came through any Full load (statics; the game's own load keeps them too) | **a run spot's start** turns them off once its load has started; a Creative capture's `GameMode_Creative` turns its four back on as it loads |

Log: `Savestate restore (load): difficulty Hard -> Normal for the load
(the capture's).`, `Savestate restore (load): prefab list filled as the
menu's load does (352 prefabs; empty after a new game this launch).` (also
`(in place)` and `Savestate slot load` - the death reload's `Resume` in
game), `Savestate: start state of '<spot>': cheats off for the run's start
...: GodMode, InfiniteEnergy.`, and `Savestate: ... restores with a Full
load - captured in a Normal game, this one is Hard - the load sets the
difficulty.`

Not changed (vanilla does the same): `PermaDeath`, `NoEnemiesInternal`,
`NoEnemiesDuringDay` (the cheat codes, kept in PlayerPrefs), `DebugConsole`
(the report flags it), Creative's *Allow enemies* preference. Not tested
live: the death reload's `Resume` from a new-game launch (same hang
expected - the same list now filled first).

## Reload the save in place on death (2026-10-04, unreleased)

The author's idea (the author's old *Next up* list, item 3). Deaths tab, under the reload:
**Reload the save: with a load (as the game does) / in place (fast)** -
`[Deaths] ReloadInPlace`, **off by default** (the game's own load stays
the default: true to the game, and the only one run mode uses). A *Quick
load the slot's save* button did the same until v0.24.106; it went with
the Savestates test panel (author's call), not for a fault.

- **How**: the death is skipped as for the in-game reload (health back,
  `DeathAction.QuickLoadInGame`, now counted as a revive so a fall's hard
  landing is cancelled), then `SavestateModule.ReloadSlotInPlace` reads the
  slot's save (`SavestateBridge.ReadSlotData`, the data `Resume` loads) and
  runs the Quick load path on it with no file: streaming as the game saved
  it (`MemorySafeSaveMode`), every pickup taken since put back, the cave
  state from the save's own flag (`Data/SlotSaveFlags`), enemies restarted,
  nature / guide / to-do list as for any Quick load. Nothing outside the
  game's data is put back (held items stay stowed until the game's own
  re-equip, Megan, elevators, weather). Marks practice.
- **The game's load instead** (`DeathPlan.InPlaceRefusal`, tested), with
  the reason on the `Quick-load: loading slot N from in game (no menu) -
  in place cannot apply: ...` line: run mode (docs/run-mode.md - a run's
  reload is the game's own load), the save and the player on different
  sides of the vault door or the lab not loaded (game-notes *A save slot's
  data*), the save unreadable, a savestate action running; and when the
  in-place restore itself fails (`... - in place failed after N s: ...`,
  e.g. a cross-save adoption refused).
- **Log**: `Reload save on death: slot N reloaded in place in 0.85 s (a
  Quick load of the slot's save).` plus the usual `Savestate restore slot
  N's save in place: ...` line.
- **Timings** (bridge, game-notes): the game's reload ~6-10 s, in place
  ~0.2-0.9 s.

## Checkpoint states and Restart from checkpoint (2026-10-04, unreleased)

"Saveloc" for long timed segments (author, QA Discord 2026-09-26):
`Data/CheckpointStates` (pure, tested), `Modules/PracticeRunModule.Checkpoints`,
`SavestateModule.CaptureCheckpointState` / `RestoreCheckpointState`.

- **Capture at checkpoints** (Runs tab, `[Runs] CaptureAtCheckpoints`,
  **off** by default): each checkpoint a practice run fires by its trigger
  (not an F12 split) is captured to
  `savestates/segments/<safe id>.cp<N>.fosave` + `.cp<N>.meta` (N = 1 for
  the first). The meta holds the route, the run's split times up to N,
  its item baseline (relative item triggers) and the clock **when the
  level was serialized** (a few frames after the split - a resumed run
  counts them). **Newest wins**: holding every run's captures until a PB
  is known would cost files and hitches, and the state you want is the
  one you just played into.
- **The hitch**: the level is still serialized on the main thread
  (Unity's serializer). A checkpoint capture is "light" -
  `SavestateBridge.Capture(light)` skips the save routine's
  `UnloadUnusedAssets` and forced GC (memory only; streaming is still
  force-unloaded and held items re-parented, as the data needs) - and
  the file's text is built and written on a ThreadPool thread (the old
  meta deleted first, the state via `.tmp` + move, the meta last, so a
  cut write is never offered). Never in run mode (`Ctx.Run.Active`),
  skipped with the pause menu / inventory open (`MenuClose.AnyOpen`, not
  closed mid-run) or another savestate action running. One log line
  each: `Checkpoint state checkpoint 2/5 of 's-...' captured: N ms of
  frame work on the main thread (serialize N ms, no memory clean-up), N KB
  written in N ms on a worker thread` (plus the usual `Savestate captured`
  line); a skip logs `Run '<id>': checkpoint N state not captured - why`.
- **Restart from checkpoint N** (Runs tab, a button per checkpoint; key
  `run.restartCheckpoint`, unbound = the last one used, else the latest):
  a Quick load of that state (a Full load only where `MustLoad` says),
  then the run resumes: clock at the meta's time (the first frame after
  the restore not counted), checkpoints 1..N fired
  (`SplitSequence.Resume`: N+1 is armed as next - fires at once if it
  already holds, as in the run that captured it), the splits table
  filled with that run's times. A state from another route (a zone or the
  start state changed) or for a checkpoint the segment no longer has is
  not offered and says why. Auto-restart after a resumed run goes back to
  its checkpoint.
- **A resumed run is practice**: never saved as an attempt - no PB, last,
  average, PB chance, unfinished entry, upload, run mode timer. The
  segments it runs live (every split after the resume point, and the end)
  are kept in `runs/<id>/checkpoint-segments.txt` and can be **golds**
  (best segments, sum of best - `PracticeGolds`): a real time from a real
  state on this route. Copied rows before the resume point never are.
- Not done: a death during a resumed run restores the spot's start state
  (Deaths tab rules), not the checkpoint; a Practice tab button.

## Quick load audit (2026-10-01, v0.24.187-188)

What a Quick load leaves different from the capture, found by diffing the
game's own serialization before and after (game-notes *Saving and
loading*: `JSONLevelSerializer.SerializeLevelToFile`).

Recipe (bridge, a `-f` script): `capture X`, dump `b0`, change things,
dump `changed`, `restore X`, wait 4 s, dump `b1`; diff `b0` against `b1`
with a control pair (two dumps 10 s apart, `--write-noise`) as noise - the clock, days
survived, time of day, the player's transform
(`scripts/save-diff-noise.txt`). `scripts/save-diff.py b0.json b1.json
scripts/save-diff-noise.txt` flattens
each component's JSON and groups changes by type; 2D arrays carry a
running counter in their key names (`contents126`), not a change. A
dump only sees saved fields - a field put back without its effect (the
nature guide's marks, the to-do list's set-up) shows only when the
component rebuilds the field from live state on serializing, so read the
component's DelayedAwake / OnDeserialized as well (gotcha 79).

| Changed after the capture | After a Quick load |
|---|---|
| Health, fullness, thirst, energy, stamina | as captured |
| Items added (rope, cloth, booze, map piece, manifest) | as captured |
| A passenger found, `GameStats._passengersFound` | as captured |
| A nature guide entry ticked | **was kept ticked** - fixed v0.24.187 |
| The book's to-do list | **stopped updating** (tasks never set up again) - fixed v0.24.188 |
| A capture with the book open | **restored empty-handed** - fixed v0.24.188-189 (the game hides the hands 1.5 s after the load: the restored slots get the items back) |
| The current cave (`ActiveAreaInfo._currentCave`) | as captured |
| The held items, the book open at restore | as captured |
| A long teleport, cave visit, a different item equipped | as captured |
| The cave map's visited areas | unchanged in the test (all false) |
| Worn clothing (`_wornClothingItems`, the visible outfit) | as captured |
| On a zipline / sled / glider / cliff at capture | **fell / stood there** - put back v0.24.201-202 |
| Pushing a sled at capture | **the sled went to the world origin** - fixed v0.24.201 |
| Gliding during the capture itself | **the capture dropped the glider** - fixed v0.24.201 |
| Blueprints placed, part-filled or built since (shelter, log holder, fire, workbench) | deleted, as captured (the diff is clean) |
| A blueprint finished since the capture | the blueprint back as captured |
| A blueprint at capture given more logs / sticks since | **counts back but the later logs still drawn, the build HUD's "GATHER" lines kept the later numbers** - fixed v0.24.203 (rebuilt from the save; HUD recounted) |
| Items on the crafting cog at capture (inventory open) | **the cog is not in the save - they would be lost; and the capture stalled until the inventory closed** - fixed v0.24.203 (the capture closes it first, as the game's save does) |
| The inventory open at restore, items on the cog | F7 closed it first; **a bridge / other restore stalled** - fixed v0.24.203 (every restore closes it) |

The audit's calls (bridge, Creative): a blueprint is placed with
`Create.CreateBuilding <type>` + `Create.PlaceGhost false`, filled with
`<ghost>/Trigger Craft_Structure.AddIngrendient_Actual <i> true null`
(the game's add, `i` = recipe index), finished with `Craft_Structure.Build`.
The inventory opens and closes by `LocalPlayer.Inventory.ToggleInventory`
(what the Inventory key and the options view call); the click on an item
moves it with `Inventory._craftingCog.Add <id> <n> null` + `Inventory.
RemoveItem <id> <n> true true` (`InventoryItemView.Update`). The build HUD
tally: `static:TheForest.Buildings.Creation.BuildMission ActiveMissions`
(per item, `_amountNeeded`).

Not covered yet (no bridge call, or needs hands): achievements, the
bestiary (same shape as the nature
guide, not runner-facing), a built structure damaged or destroyed since
(`BuildingHealth` is saved; untested).
