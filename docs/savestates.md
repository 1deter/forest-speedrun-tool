# Savestates - how they work

The detail behind CLAUDE.md *Key concepts - Savestates* (moved out 2026-09-26). IL and game internals are in game-notes *Saving and loading*.

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
    stopped first, v0.24.64).
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
  v0.24.211 also write `basedifficulty` (Peaceful under Creative); older
  Creative files keep the game's difficulty. Confirmed over the bridge:
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
