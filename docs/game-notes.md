# The Forest — internals reference

Everything here is confirmed from a live `F11` dump or from IL via
`tools/ILScan`. Nothing is guessed.

> The one entry that *was* a guess — `VirtualCursor` filed as
> "gamepad-related" — was wrong, and cost a release. If it is not in here,
> go and look.

---

## Player

Root GameObject **`player`**, tagged `Player`.

### `FirstPersonCharacter`

| Member | Notes |
|---|---|
| `walkSpeed` 6.5 / `runSpeed` 13.5 / `maximumVelocity` 55 | |
| `maxVelocityChange` 4, `gravity` 10, `jumpHeight` 8 | |
| **`Locked`** (bool) | read by `Update`, `FixedUpdate`, `FirstPersonHeadBob` and **`SimpleMouseRotator.Update`** — stops movement *and* camera look |
| `MovementLocked` (bool) | movement only |
| `rb` | the player rigidbody; velocity is best read here |
| `Grounded` | `<Grounded>k__BackingField` |

**Use `LockView(bool)` / `UnLockView()`, not the raw flag:**

```
LockView(true):                    UnLockView():
  rb.Sleep(); isKinematic = true     Locked = false; CanJump = true
  useGravity = false                 Input.LockMouse()
  Locked = true; CanJump = false     isKinematic = false; useGravity = true
  Input.UnLockMouse()                rb.WakeUp()
```

Writing `Locked` by hand skips the rigidbody handling (you sag through the
floor), `CanJump`, and the cursor. Both also drive `Input.IsMouseLocked`, so
apply the player lock **before** asserting the cursor in a frame.

### Crouch (IL + bridge, v0.24.102)

`crouch` (wanted) and `crouching` (in effect) are plain fields, not in the
save. `Update`: while `crouch` is false it is set from
`GetCrouchInput()` (**toggle crouch**, `PlayerPreferences.UseCrouchToggle`:
`GetButtonDown("Crouch")`; hold crouch: `GetButton`); while true,
`standUp = GetStangUpInput()` (toggle: button down again; hold: button
released). `crouch` and not `crouching` and grounded starts
`EnableCrouch` (capsule 4.7 -> 3, `crouchIdle`, FSM crouch bool, vision
range 12, crouch layers); `standUp` starts `DisableCrouch` (the reverse).
`disableToggledCrouch()` is the game's own stand-up. So with toggle
crouch the stance outlives any restore; with hold crouch the button
decides every frame - setting `crouch = true` without it stands again
the next frame. `Game/Stance` puts the captured one back.

### Camera / look angles

`SimpleMouseRotator` does not read the transform — it **recomposes** it every
frame as `originalRotation * Euler(-followAngles.x, followAngles.y, 0)`, where
`followAngles` damps towards `targetAngles` + `xOffset`/`yOffset`. Writing
`transform.rotation` during a teleport never sticks.

Yaw is on the body rotator, **pitch on the camera rotator** (`cameraRotator ==
true`; also the static `LocalPlayer.CamRotator`) — two different transforms,
and they store the view differently. `UpdateRotation` every frame:

| Rotator | Zeroes of `originalRotation` | So the view lives in |
|---|---|---|
| body | `.x`, `.z` | yaw in `originalRotation.y` |
| camera | `.x`, `.y`, `.z` | pitch **only** in `targetAngles.x` / `followAngles.x` |

**Yaw:** set the body rotation and raise **`resetOriginalRotation`**.
`CheckResetOriginalRotation` adopts the current rotation and zeroes the angles.
It is consumed inside `UpdateRotation`, which only runs while unlocked — so
raising it during a locked teleport applies on the first unlocked frame.

**Pitch: never reset the camera rotator** — zeroing its angles *is* zeroing
the pitch (v0.17.0 and earlier snapped the view level on every window close).
Write `targetAngles.x = -pitch - xOffset`, `followAngles.x = -pitch`, and
raise **`fixCameraRotation`**, which snaps `followAngles` instead of damping.
That is what the game does itself in `survivalBookController.FinalCloseBook`.
Pitch here is Unity euler x, positive looking down.

---

## Cursor

`TheForest.UI.VirtualCursor.LateUpdate` owns it:

```
if (TheForest.Utils.Input.IsMouseLocked) {
    Cursor.lockState = Locked;   // WARPS the pointer to screen centre
    Cursor.visible   = false;
}
```

`lockState = Locked` recentres the pointer, so a later write can restore
visibility but never position — which is why "win the frame in OnGUI" failed.

The switch is **`TheForest.Utils.Input.IsMouseLocked`** (`LockMouse()` /
`UnLockMouse()` are one-line flag setters, no side effects). With it false,
`VirtualCursor` unlocks the cursor itself, every frame. Assert it from
`Update()`, not `LateUpdate()` — all Updates run before any LateUpdate.

### Input states — blocking game input while a UI is open

`TheForest.Utils.Input` keeps `static Dictionary<InputState,bool> States`.
`SetState(InputState, bool)` stores one state and recomputes which Rewired
maps are active from all of them, so states stack. Every game overlay uses
it — `HudGui.TogglePauseMenu`, `SurvivalBook.OnEnable/OnDisable`,
`PlayerInventory.Open/Close`, `ChatBox`, `DebugConsole.ShowConsole`.

`InputState`: `Locked`, `Menu`, `World`, `Inventory`, `Chat`, `Book`,
`RadialWorld`, `SavingMaps` (0..7). The dev console passes `4` (`Chat`); the
pause menu `Menu`.

`SetState` returns early if the value is unchanged, otherwise stores it,
sets `World` on when no other state is on, **`Debug.Log`s every state**, and
calls `ForceRefreshState`. That picks one map by priority —
`Locked` > `Chat` > `Menu` > `RadialWorld` > `Book` > `Inventory` > `World` —
and enables it exclusively (`SetMappingExclusive`). The only readers of
`States` are `SetState`, `ForceRefreshState` and two VR display helpers, so
holding a state switches the key map and nothing else.

The plugin holds `Menu` while its window is open or freecam is on
(`Game/GameInput.cs`), calling `SetState` only on a transition because of the
log line. The game clears `Menu` itself when the pause menu closes, so the
plugin re-checks with `GetState` each frame.

---

## `timeScale`

Written from 22 places. The one that beats an external write every frame is
**`InventoryItemView.Update`**. Also `HudGui.TogglePauseMenu` (ESC menu),
`PlayerInventory.PauseTimeInInventory` / `.Close`, `MenuMain`, `LoadSave`.

Do not try to freeze the game with `timeScale`.

**Correction (IL, v0.24.3):** `InventoryItemView.Update` writes
`timeScale = 1` only when an item is **equipped from the open inventory**
(`BubbleUpInventoryView`, unless `_preventClosingInventoryAfterEquip`), not
every frame. With the inventory closed nothing re-asserts it, so the
savestate cutscene fast-forward (`SavestateModule.FastForwardCutscene`)
can drive it; the ESC menu's 0 is left alone.

---

## Inventory

`TheForest.Items.Inventory.PlayerInventory`

| Field | Notes |
|---|---|
| `_possessedItems` | `List<InventoryItem>` — read via non-generic `IList` |
| `_possessedItemsCount` | **does not track reliably**; derive totals from the list |
| `_itemDatabase` | `ItemDatabase` |
| `_equipmentSlotsIds` | `int[]` — currently equipped |

`InventoryItem`: `_itemId`, `_amount`, `_maxAmount`, `_maxAmountBonus`.

`ItemDatabase`: static `_instance`, `Items` (`Item[]`), **static**
`ItemById(int)` (reads `_instance._itemsCache`; throws on an unknown id).
Bound as an instance method it is never found — the Inventory tab showed
`item <id>` for every entry until v0.19.4.
`Item`: `_id`, `_name` (internal PascalCase, e.g. `SketchArtifact`).

**Do not cache the inventory component** — it goes stale across a save load and
counters freeze. Read the static `TheForest.Utils.LocalPlayer.Inventory` each
time. Likewise find `ItemDatabase` independently of the player.

### Phantom entries

`_possessedItems` contains things that are not real contents. The author's
LiveSplit autosplitter filters them as `id < 29 || id > 311 || id == 302`
(302 is a dev item; 122 is the MP radio, listed in singleplayer). Observed:

- **An equipped item reads `_amount = 0`** while held — cross-reference
  `_equipmentSlotsIds` to tell "equipped" from "gone".
- That id range describes `_possessedItems`, **not the database** — applying it
  to the catalogue hides real items from search.

### Counting an item, and who changes counts (v0.24.160-161)

- `_possessedItems` holds **only what the player owns** (6 entries in a
  mid-game Slot 2 save, bridge 2026-09-27), not every item type.
- `PlayerInventory.AmountOf(int id, bool allowFallback)` (instance) is the
  count to use: it includes the held item (bridge: 2 Molotovs read 2
  before and after `Equip`). `ItemIdByName` answers the `_name` form
  (`Soda` 109, `EnergyMix` 100, `BombTimed` 29, `Meds` 49, `Booze` 37,
  `Coins` 91, `Log` 78); a display name ("Energy Mix") answers 0.
- **Every writer of `InventoryItem._amount`** (ilscan `writes`):
  `InventoryItem` `Add` / `Remove` / `RemoveOverflow`; `PlayerInventory`
  `AddItemNF` / `RemoveItemNF` / `FixMaxAmountBonuses` /
  `AddMaxAmountBonus` / `SetMaxAmountBonus`, the `OnDeserialized`
  coroutine; plus `CoopSharableStorageProxy.RefreshStorage` (co-op
  storage, not the player). `Game/ItemCounter` postfixes all but the last
  to know when to re-read.

### Item names

Internal names are nothing like published ones: `MorgueReport` = Autopsy
Report, `RecurveBow` = Modern Bow, `TennisRaquet` = Tennis Racket,
`shippingManifest` = Cargo Manifest, `Walkman` = Cassette Player.

Dump the full catalogue from the **100% tab → Write dumps**
(`ForestOverlayDumps/items_*.txt`, 231 items).

Multi-piece items are **one item holding pieces**, not several items:
`TimmyDrawing` (208) holds all eight drawings, `MapFull` / `MapPiece_*`
likewise. `Toy_Arm` / `Toy_Leg` are single items held **x2**.
`DrawingsInventoryItemView._ids` / `._usedIds` would give per-piece progress.

### Logs (IL + bridge, v0.24.134-135)

Logs are **not** an inventory item. `PlayerInventory.AddItemNF` /
`RemoveItemNF` / `AmountOfNF` / `OwnsNF` short-circuit item 78 (`Log`;
database `_maxAmount -1`, `Equipment, Droppable`, no view on the
inventory mat) to `TheForest.Items.Special.LogControler`
(`PlayerInventory.Logs`):
- `_logs` (`[SerializeThis]`, the save keeps it; `OnDeserialized` sets it
  to 0 and calls `Lift()` that many times) - the count, at most 2
  (`Lift` refuses at 2, while swimming, pushing a sled, rafting, carrying
  a body, in root motion).
- `Lift()`: `_logs++`, shows `_logsHeld[_logs-1]` (LogHeld1/2 on the
  spine), whoosh; the first log also puts the weapon / utility away
  (`MemorizeItem` + `UnequipItemAtSlot`), sets the animator bools and
  enables the component (its `Update` drops a log on the drop key).
- `PutDown(fake, drop, equipPrevious, preSpawned)`: not `fake` = take one
  (`RemoveLog`: hide the model; at 0 clear the bools, re-equip); `drop` =
  spawn `_logPrefab` in front (a raycast to the ground). `FakeDrop(78)` =
  `PutDown(true, true, ...)`. Building / fires / repairs take through
  `RemoveItem(78)` -> `PutDown(false, false, true)`.
- Readers of `Amount` / `HasLogs`: the log sled `LogHolder.Update` (take
  while `Amount < 2`, add while `> 0`, holds 7), `MultiHolder`
  (`LogContentUpdate`, `GrabEnter`), `RepairTool`, `BuildingRepair`; and
  the forced drops before an action (rope top, zipline, crane, glider,
  bench, skinning, eating, cave entry, `FallDownDead`, swimming), energy
  (`GetTired`: 1 log 0.15, 2 logs 0.25) and calories.
- A world log: `Log(Clone)` (layer PickUp), its `Trigger` child's
  `PickUp.Collect` is the pickup.

`Game/LogStore` (the *Logs in the inventory* mod) keeps the count in
`_logs` and patches around it - see its header.

---

## Endgame splits — the separate triggers

The autosplitter reads one bool, `playerAnimatorControl.endGameCutScene` (via
`LocalPlayer.AnimControl`), set by **every** endgame cutscene — which is why
those splits could not be separated from outside the process. Its split list:
Vault Door, Finding Timmy, Approaching Megan, Putting Megan in Artifact, Gold
Keycard (Automatic Door), Gold Keycard (Red Elevator), Game End.

**The shared flag carries no identity; the call site does.** Each cutscene is
a method on the player, started by an `activate*` trigger with
`SendMessage("<routine>")` (`ilscan strings`):

Confirmed against two real endgame runs (2026-09-22, author) unless marked.

| Split | Method (event name) | Flag set |
|---|---|---|
| Vault door | `playerOpenKeypadDoorAction.openDoorRoutine` via `openKeypadDoor`, keycard 210 (`vault-door`; also `keycard-door`, `keycard-door-210`) | after the walk-up, in its `lockPlayerParams` |
| Gold keycard: automatic door | same, keycard 242 (`gold-door`; also `keycard-door`, `keycard-door-242`) | same |
| Gold keycard: red elevator | the same `openDoorRoutine`, sent directly by `ElevatorSystem.Goto` (`red-elevator`) | same |
| Finding Timmy | `PlayerPickupTimmyAction.pickupTimmyRoutine` (`timmy-pickup`) | before first yield |
| Approaching Megan (she transforms) | `PlayerGirlTransformAction.doGirlTransformRoutine` (`megan-transform`) | after first yield |
| Putting Megan in the artifact | `PlayerGirlPickupAction.girlToMachineRoutine` (`megan-to-machine`) | after first yield |
| Game end | `PlayerEndCrashAction.doEndPlaneCrashRoutine` / `doShutDownRoutine` (`end-crash` / `end-shutdown`, both `game-end`) | after first yield (`end-crash` confirmed) |
| Goodbye Timmy | `PlayerGoodbyeTimmyAction.goodbyeTimmyRoutine` (`timmy-goodbye`) | before first yield |
| Raft out of world | `RaftPush.outOfWorldRoutine` (`raft-out-of-world`) | before first yield |
| — | `PlayerGirlPickupAction.pickupGirlRoutine` (`megan-pickup`) | never — fires at routine start |

Other writers of the flag: `playerAnimatorControl.lockPlayerParams`, called
only from `PlayerStats.EndgameWakeUp`.

Because several routines set the flag after a `yield` (and keypad doors only
once the player has walked to the keypad), the plugin fires a split on the
flag's **rising edge**, polled each frame, and uses the postfix only to say
which cutscene it is (`Game/GameEvents.cs`). That keeps split times identical
to the autosplitter's. `endgame-cutscene` fires on every rising edge, exactly
as the autosplitter did.

**Keypad doors and the red elevator share one action.**
`activateKeypadDoor.DoActorAnimation` sends `setKeycardId(_keycardId)`,
`setShortSequence(shortSequence)`, `setDoorAnimator`, then
`openKeypadDoor(playerPos)`, which calls `openDoorRoutine`. The red elevator
(`TheForest.World.ElevatorSystem.Goto`, when `_playKeycardAnim`) sends
`setKeycardId`, `setShortSequence(true)` and **`openDoorRoutine(_playerPos)`
directly**, skipping `openKeypadDoor`. So the plugin hooks `openDoorRoutine`
and marks calls made from inside `openKeypadDoor`; unmarked means elevator.

Logged in game:

| Door | Path of `playerPos` | Keycard |
|---|---|---|
| Vault | `keypadDoor_animate/keypadDoor_ANIM_base/playerPos` | 210 (Keycard) |
| Gold automatic door | `ElevatorCardReader/Trigger/playerPos` (short sequence) | 242 (Keycard 2) |
| Red elevator | `HellCorridor/Elevator_01a/playerPos` (short sequence) | 242 |

`vault-door` / `gold-door` are keyed on the **keycard id**, not the object
name — `keypadDoor_animate` is a prefab and could appear more than once.

Lesson: search `strings` for **every** method of an action, not just its
entry point — `openDoorRoutine` was sent by name from a second place.

Also present: `TheForest.Tools.TfEvent+Endgame` with static `Completed`,
`FireDetected`, `Shutdown2ndArtifact`.

### The autosplitter's other splits (IL + bridge, v0.24.184)

The LiveSplit ASL (1deter/auto-splitters) reads these; `Game/WorldEvents`
polls the same fields once a frame:

| Event | Field | Notes |
|---|---|---|
| `cave-enter-<cave>` / `cave-exit-<cave>`, `cave-enter` / `cave-exit` | `LocalPlayer.ActiveAreaInfo._currentCave` (`CaveNames`) | Written only by `SetCurrentCave` (from `activateCave` / `CaveTriggers.Update`, `EnterSnowCaveHelper`, `PlayerStats.KillPlayer`) and `SetInCaves`. **A teleport does not change it** (our tp / Go use `GotoCave`: `_currentCave` stays `NotInCaves`), and the endgame lab is `NotInCaves` with `IsInCaves` true - the ASL never splits there. Since v0.24.193 a Go sets it from the spot's `cave` (`GameBridge.SetCurrentCave`); a bridge `tp` still does not. **`CaveOptimizer.Update`**: in caves with `CurrentCave == NotInCaves` it streams **every** cave's props in (16 scenes, `Cave_01_Props_Streaming` ...); with a cave set, only that one (bridge: 2 scenes). |
| `clothing-<id>` | `LocalPlayer.Clothing._wornClothingItems` (List<int>) | Names: `ClothingItemDatabase._instance._items[i]._displayName` ("RED BEANIE"), 33 items. |
| `passenger-<n>`, `passenger` | `LocalPlayer.PassengerManifest._foundPassengersIdsCount` | The ASL splits on the count, not on who. |
| `hold-interact` | static `TheForest.Utils.Input.DelayedActionIsDown` | Set by `GetButtonAfterDelay` on the button-down frame of any hold action (27 callers: pickups, the plane meal, fires, Timmy / Megan pickups). The ASL's "plane meal start" is this flag rising while `Scene.FinishGameLoad`. A bridge `set` is cleared by the game's `Input.LateUpdate` the same frame - only a real hold shows it. |
| `moving` | the player's Rigidbody speed > 0.15 m/s | The ASL reads `FirstPersonCharacter` + 0x168 (a velocity Vector3); after 0.25 s still. A placement settles for a frame or two (bridge: -0.87 m/s after a restart from a cave), so since v0.24.188 a move of more than 2 m in one frame resets it - still again first. |
| `first-input` | Rewired `Input.player.GetAnyButton()` or `Input.GetAxis("Horizontal" / "Vertical")` | After 0.25 s with none; not while `Cursor.visible` (menus, the overlay window); placement resets it (v0.24.193). |
| `rope-grab` / `rope-leave` | `playerAnimatorControl.onRope` (`RopeClimb.IsOnRope`) | v0.24.193. |

### Segment events from the game's event bus and the rides (IL, not yet seen live)

`Game/AuditWatch`'s one postfix on `EventRegistry.Publish(object, object)`
(the run audit log's) also raises these, in or out of run mode, while
`WorldEvents.Live` and not within 1.5 s of an overlay placement
(`PlayerRef.JustPlaced`: a Go into the vault entrance sends the game's
`EnterEndgame`). Names in `Data/BusEvents`; a specific name fires first,
the general one as its **companion** (one occurrence: a run moves on once,
`OccurrenceGate` - the same now holds for `cave-enter-<cave>` + `cave-enter`,
`passenger-<n>` + `passenger`, `keycard-door` + `keycard-door-<id>` +
`vault-door`, `endgame-cutscene` + the named cutscene).

| Event | `TfEvent` (publisher) | Specific part |
|---|---|---|
| `built`, `built-<structure>` | `BuiltStructure` (`Craft_Structure.Build`) | `BuildingTypes` value, e.g. `built-log-cabin` |
| `crafted`, `crafted-<item>` | `CraftedItem` (`CraftingCog.DoCraft`, the product id) | ItemDatabase `_name`, e.g. `crafted-bomb-timed`; the id if no name |
| `used`, `used-<item>` | `UsedItem` (`InventoryItemView.UseEdible`, the decaying view, `EdiblePickUp.MainEffect` - eating / drinking from the world too) | as crafted |
| `kill-enemy` | `KilledEnemy` (`EnemyHealth.Die` / `dieExplode` / `DieTrap`) | - (the object name in the log) |
| `kill-animal`, `kill-<animal>` | `KilledRabbit` / `Lizard` / `Raccoon` / `Deer` / `Turtle` / `Bird` / `Shark` | the animal |
| `hit-by-enemy` | `EnemyContact` (`PlayerStats.hitFromEnemy`) | - |
| `tree-cut` | `CutTree` (`TreeHealth.DoFallTree` / `DoFallTreeExplosion`) | - |
| `bomb` | `UsedBomb` (`Bomb.Explode`) | - |
| `slept` | `Slept` (`PlayerStats.GoToSleep`) | - |
| `story`, `story-<element>` | `StoryProgress` (`releaseFromHanging`, `redmanSpawner.removeRedman`, `activateCliffClimbSheen`, Timmy, `activateGirlToMachine`) | `GameStats.StoryElements` value (hanging-scene, red-man-on-yacht, found-climb-wall, timmy-found, megan-found) |
| `endgame-area-enter` / `-leave` | `EnterEndgame` / `ExitEndgame` | - |
| `<ride>-start` / `-end`, `ride-start` / `ride-end` | `RideModes.Current()` (`zipline`, `sled`, `glider`, `cliff-climb`), read at 10 Hz for 3 s after a ride's enter / exit method ran | the ride |

The ASL's settings as a segment (v0.24.186, `Data/LssAutoSplit`): a
LiveSplit split is "the next thing the autosplitter splits on", so an
imported spot's checkpoints are all `event autosplit` and its `autosplit
=` list names what counts. The ASL's per-run memory is kept
(`AutoSplitWatch`): an item splits on its first appearance in the run
(`itemTracker`), again on every change only with `multiItemSplit`; a
clothing id once per run (`equippedClothes`); one split per update.

---

## Survival book (100%)

`TheForest.Player.SerializableSurvivalBookTodo` — one `TodoTask` field per
objective (`_son`, `_camp`, `_cave1..10`, `SinkHoleTodoTask`,
`PassengersTodoTask`, …). Each inherits `ACondition`, which carries `_id` and
**`_done`**. Discover them **by shape** (any field whose type has `_done`)
rather than by name, so a game update adds objectives for free.

The older `SurvivalBookTodo` also exists and adds `FindTimmyTodoTask` /
`FindMeganTodoTask` — check which is live.

### The open page (IL, v0.24.0)

There is **no page number**. Pages are GameObjects switched on and off by
`SelectPageNumber` (one per link, index entry and tab) in `OnClick`:
a plain link deactivates its own page (`ThisPageOverride`, else
`transform.parent`) and activates `MyPageNew`; an **index** entry runs
`TurnOffAllPages` (every child of its `Pages` container off) and activates
`MyPageNew`; a **tab** does the same, also hides `IndexPage`, and shows
`HighlightedPage` instead when highlighted. Every click ends by copying
`MyPageNew`'s `Renderer.sharedMaterial` onto the static
`LocalPlayer.AnimatedBook` (a `SkinnedMeshRenderer`: the book model seen
while opening and closing). `survivalBookController` only opens and closes
the book (animator, `bookIsOpen`), not the page. A load rebuilds the
player, so the page returns to the prefab's default. `Game/BookPages`
records the on/off of every page object (children of each distinct
`Pages`, then each `IndexPage`, in hierarchy order under
`LocalPlayer.GameObject`) as the savestate's `book` header and restores it
the way a click does. Not yet confirmed in game.

### The to-do list after an in-place load (IL + bridge, v0.24.188)

`TheForest.Player.SurvivalBookTodo` (`player/ControllerObjects/
SpecialItems/TodoList`) saves its tasks whole (`_son`, `_camp`, `_food`,
`_cave1`..`_cave10`, `_megan` ...: `TodoTask : TaskSystem.Task :
ACondition`), so an in-place LoadNow puts **new task objects** in its
fields. Only `DelayedAwake` prepares tasks (`Prepare(GOs, OnStatusChange)`:
the page entry, the status callback, the conditions' subscriptions), and
only once (`_initialized`). Bridge: after a Full load `_son.GOs` is set and
`OnStatusChange` a delegate; after one Quick load both are null - the list
stopped updating until a Full load. `OnDestroy` Clears every task (the
game's unsubscribe). `Game/TodoListKeeper` keeps the old tasks before the
restore, Clears the replaced ones and re-runs DelayedAwake (for a loaded
game it only prepares; the new-game messages wait on `GameSetup.Init`).
`SurvivalBookBestiary` has the TickOff shape (`_doneConditions` int[],
`FoundEnemyInfo[]` not saved) - not runner-facing, not handled.

### Opening and closing the book (IL + bridge, v0.24.44)

Open = `PlayerInventory.CurrentView == Book`, `FpCharacter.Locked`, the
animator's `bookHeld` on, `survivalBookController` active with
`bookIsOpen` / `realBookOpen` and `survivalBookReal` shown; `setOpenBook`
also sets `CamRotator.xOffset = -20`, `rotationRange = 0` and
`dampingOverride = 1`. The book is not equipped, so a Quick load's
`StashHands` left all of it as it was (bridge, v0.24.43). The game's fast
close is `Create.CloseBookForInventory()` (opening the inventory from the
book): `CloseTheBook(true)` -> `showEquipped` at once (view `World`,
`RestoreEquipement`) and `fastCloseBook` (`resetBook`, controller off after
0.2 s). Read live 1 s after it: every value above back to normal
(`xOffset` 0, range 135, `clampSpine` off); `targetAngles.x` stepped -20
as the game's own `FinalCloseBook` does. The cave hanging and endgame
wake-up cutscenes close it with `CloseTheBook(false)` (animated, 0.65 s).
`Game/BookClose` runs the fast close on every reset.

**The pitch lock (v0.24.91, runner sxczurass: "can look sideways, not up
or down").** `setOpenBook` is an animation event as the book reaches its
idle pose; only `FinalCloseBook` (the close animation's event, or
`playerAnimatorControl.runGotHitScripts`) puts `rotationRange` /
`xOffset` back. A reset in the ~0.5 s between `setOpenBook` and the real
book opening takes the fast close without that event: `rotationRange`
stays (0, 0) for good (bridge: reset 0.6 s after `OpenBook` stuck; 0.2 s
and 1.2 s fine). Yaw is the body's `MainRotator`, pitch the camera's
`CamRotator` - hence sideways only. `xOffset = -20` is written only by
`setOpenBook` (and the cave hanging cutscene), so it marks the book's
lock. `BookClose.Tick` applies `FinalCloseBook`'s camera lines once the
reset is done.

### A blueprint in the hands (IL + bridge, v0.24.47)

Build mode lives on `Create` (`LocalPlayer.Create`): `CreateMode`,
`_currentBlueprint` (`BuildingBlueprint._type` = `BuildingTypes`) and
`_currentGhost` - a `Ghost_<X>(Clone)` under
`player/player_BASE/Build/BuildingPlacerClose`, not in the save. Read live:
a Quick load leaves it all as it was (the ghost survives the delete of
unsaved objects); a Full load clears it with the scene. Out =
`CreateBuilding(BuildingTypes)` (the book's pages via `CreateGui.PollInput`,
the console's `_selectBlueprint`): ghost instantiated under the placer,
`CreateMode`/`LockPlace` on, `EquipPreviousUtility(true)`. Away =
`CancelPlace()` (also `PlayerStats.KillPlayer`): destroy the ghost,
`ClearReferences(true)` (placer off, construction HUD icons shut,
`RestoreEquipement`, `CanJump`), `CreateMode` off. `Game/BuildMode` puts
it away on a Quick load and pulls the captured one out after the hands.

### Nature guide — `TheForest.Player.TickOffSystem`

The book's tick-off pages (animals, birds, fish, plants). A component on the
player (`## TickOff`), found by the author in dnSpy via `DoneMessage` and
confirmed from IL:

| Member | Notes |
|---|---|
| `Entry[] _entries` | one per tick-off line |
| `Entry._type` | `EntryType`: `CollectItem` / `InspectAnimal` / `InspectPlant` |
| `Entry._animalType` | global `AnimalType` enum — 44 species incl. plants and mushrooms (`MuhshroomPuff` is the game's typo) |
| `Entry._itemId` | for `CollectItem` |
| **`Entry._ticked`** | set by the entry's handler; the live state |
| `Entry._tickGo` | the tick mark on the book page; activated on tick |
| `_tickedEntries` | `int[]` of ticked `_id`s, written only in `OnSerializing` |

Each entry subscribes itself in `Init` to `EventRegistry.Player` —
`TfEvent.AddedItem`/`UsedItem` (payload item id), `TfEvent.InspectedAnimal`
/ `InspectedPlant` (payload `AnimalType`) — and publishes
`TfEvent.TickedOffEntry` when ticked. `InspectedPlant` unboxes `AnimalType`
too, so plants are species in the same enum.

**After an in-place load (v0.24.187).** LoadNow writes `_tickedEntries`
back (bridge: nulled, restored, read back) and OnDeserialized calls
Awake -> DelayedAwake, which applies the array only on its first run
(`_initialized`) - so the entries kept their live ticks. A tick does what
the handler does: `_ticked`, `_tickGo.SetActive(true)`, then
`Entry.Clear()`, which unsubscribes and **nulls `_tickGo`**; an entry
ticked this session keeps its mark only through a prefix on Clear
(`Game/NatureGuideKeeper`). Entries are the same objects across the load
(`_entries` is not saved).

**Which page an entry is on is not recorded.** The plugin derives it from
where `_tickGo` sits in the hierarchy (`Data/PageGrouping.cs`); the
100% tab's **Write dumps** writes `natureguide_*.txt` with each tick's path.

`SurvivalBookBestiary` exists (one component per page, `FoundEnemyInfo[]`,
names from the `EnemyType` enum on `_availableConditionStorage`) but **is not
part of the 100% requirement** and the game carries two of them, so it renders
twice. Not used.

---

### Nature guide page names (bridge, v0.24.132)

Five pages, `SurvivalBook/Pages/15 0 Info_TickOff 1` ... `15 4 Info_TickOff
5`. Each page's `TrTextMesh - Title - NatureGuide` reads "NATURE GUIDE";
the page names are on the links (`SelectPageNumber`, target
`MyPageNew`, text on a `TrTextMesh - Link - ...` child): PLANT LIFE 1,
PLANT LIFE 2, ANIMALS 1, ANIMALS 2, ANIMALS 3. Links to page 1 from the
index, tabs and crafting guide say "NATURE GUIDE". Entry names as
printed: `Texts Left` / `Texts Right` children `TrTextMesh -
NatureGuideL - Aloe` etc. (the L / R in the name does not say which
side; text e.g. "OVAL-LEAVES BLUEBERRIES"); each entry's tick
(`Root/Checkmark (n) <name>`) sits ~5 cm from its name - 43 ticks, 43
texts, 44 `TickOffSystem` entries (one without a tick). Setting a page
active by hand leaves the main index layer drawn over it - switch pages
through a link.

### Passengers (IL + bridge, v0.24.130)

`TheForest.Player.PassengerManifest` on
`player/ControllerObjects/SpecialItems` (`PlayerInventory._specialItems`):
`_displayName` (43 seat names, game order "8C", "1A"...), `_foundGOs`
(43), `_foundPassengersIds` (saved), `_itemId` 197 (the manifest).
`FoundPassenger(id)` (called by `PassengerView.OnTriggerEnter`) counts
only while `Inventory.Owns(197, true)`, not upside down, and for id - 1 <
43; seat = `_displayName[id - 1]` (bridge: id 4 = 6C, 18 = 6D, as the
game's HUD said). `PassengerDatabase._passengers` (56 entries, ids to 59)
has **wrong scene paths**: Cave 6's main cavern holds `PassengerView`
ids 18, 14, 4, 20, 6 (`Greeble_HangingBodies*`); the database puts
those in Caves 1 and 9. Locations would need a per-cave `type
PassengerView all` scan.

## Deaths

All in `PlayerStats` (IL):

| Method | What it does |
|---|---|
| `CheckDeath` | Returns under `Cheats.GodMode`. `Health <= 0` and not `Dead`: swimming → `DeathInWater`, else `Dead = true` → `FallDownDead`. Called from `Hit`, `Explosion` |
| `Fell` | `Health -= 200`; if `<= 0`, `Dead = true` → `KillPlayer`. No IL callers — sent by name, from fall triggers |
| `hitFromEnemy` | **The last stand**: above `GreyZoneThreshold` (10), a hit that would kill is clamped to leave `Health` just over 1 (bridge: 50 - 80 -> 1.02), and `AdrenalineRush` starts; only the next hit can kill. An "empty" health bar that does not die after cannibal hits is this (runner Tom, v0.24.112) |
| `DeathInWater` | drowning; `Invoke("KillMeFast", 7)` — **always** a real death |
| `KillMeFast` | death animation, `Invoke("GameOver", …)`, `KillPlayer` |
| `KillPlayer` | `DeadTimes++`. SP: in the endgame and `IsFightingBoss` → `EndgameWakeUp`; `DeadTimes > 1` → dead cam, `Cheats.PermaDeath` deletes the save, `Invoke("GameOver", 6)`; otherwise the **capture** (wake in a cave) |
| `GameOver` | `SceneManager.LoadScene("TitleScene")` |

Full health is 100 (`Health`, `HealthTarget`).

**The blood overlay** is `BleedBehavior`: static `BloodAmount`, faded in
`Update` by an amount scaled by static `BloodReductionRatio`.
`PlayerStats.Awake` resets them to `0` / `1`; `KillPlayer` sets the ratio to
3, `hitFallDown` to 1.

**Loading a save from the title screen** (`TitleScreen`, static `Instance`):
`OnSinglePlayer` → `GameSetup.SetPlayerMode(SP)`; `OnLoad` →
`SetInitType(Continue)`; `OnSlotSelection(int)` → `SetSlot`,
`LoadSave.ShouldLoad = true`, activates `MyLoader`. The current slot is static
`GameSetup.Slot`.

The plugin's quick-load and practice revive (`Game/DeathHooks.cs`,
`Modules/DeathModule.cs`) prefix `CheckDeath` and `Fell`, so nothing of the
death sequence has run when they act.

**The hard landing runs after the fall damage.**
`FirstPersonCharacter.HandleLanded` (IL) calls `PlayerStats.Hit` for fall
damage — where a death, and so a revive, happens — and then, for a hard
landing, carries on: `Animator.SetTrigger("landHeavyTrigger")`,
`LocalPlayer.HitReactions.StartCoroutine("doHardfallRoutine")` (sets
`FpCharacter.clampInputVal = 0` and zeroes the rigidbody velocity every
frame for 1 s, then `clampInputVal = 1`), `prevMouseXSpeed =
MainRotator.rotationSpeed; rotationSpeed = 0.55`, layer weights, and
`CanJump = false`, arm layers 1–4 weighted to 0, and
`Invoke("resetAnimSpine", 1)`. `resetAnimSpine` sets `jumpLand = false` and
starts `smoothEnableSpine`, which lerps layer 4 (unless `drawBowBool`) and
layer 1 back to 1 over 0.5 s, then `jumpCoolDown = false`, `CanJump = true`,
`HitReactions.disableControllerFreeze()` (walk/run/strafe speeds back,
`hitByEnemy = false`, rigidbody drag 0) and `MainRotator.rotationSpeed = 5`.
So a revived player still staggered, then had no jump and arms down for
about a second. The plugin's postfix on `HandleLanded`, when a revive
happened inside that call, stops the trigger and the routine,
`CancelInvoke("resetAnimSpine")`, and applies `smoothEnableSpine`'s end
state at once (v0.22.5 did only the first part; v0.22.6 the rest).

**What a landing hurts from (IL, v0.23.9).** `FixedUpdate` calls
`HandleLanded` on `Grounded && !prevGrounded` and `HandleStartJumping` on
the opposite edge. `HandleStartJumping` starts the `startJumpTimer`
coroutine (zeroes `jumpingTimer`, sets `jumpTimerStarted`, then adds
`deltaTime` every frame) and `Invoke("fallDamageTimer", 0.35)`, which sets
`allowFallDamage`. `OnCollisionEnterProxied` stores the collision's
`relativeVelocity.y` in `prevVelocity` (and x/z in `prevVelocityXZ`).
`HandleLanded` stops the coroutine (the timer keeps its last value) and
deals damage only when `prevVelocity > 28`, `allowFallDamage`, and
`jumpingTimer > 0.75` (no damage while riding a shell or gliding faster
than 32): `0.9 * v * v / 27.5`, or **1000 when air time is over 3.8 s**
(5 on a shell). So a restore in mid-air carried the air time and speed
into the landing at the restored spot. `GameBridge.EndFall` zeroes the
body's velocity, `prevVelocity`, `prevVelocityXZ` and `jumpingTimer`, and
leaves the coroutine and `allowFallDamage` alone: the game's own landing
then runs soft, and a real fall after the restore still counts from zero.

## Held items across an in-place restore (IL, v0.24.1)

`PlayerInventory._equipmentSlotsIds` is **only filled when the game
saves** (`OnSerializing`: each `_equipmentSlots` view's `_itemId`, 0 for
none); live code reads `_equipmentSlots` (InventoryItemView[], slot order
`Item.EquipmentSlot`: RightHand, LeftHand, Chest, Feet) and compares
against `_noEquipedItem`. On a load, `OnDeserialized` runs
`HideAllEquiped`, then per saved id `UnlockEquipmentSlot` + `Equip(id,
true)`, and when `Equip` fails, `AddItem(id, 1, ...)` - with the lighter
already in the bag that is "CANNOT CARRY ANY MORE LIGHTERS".

`StashLeftHand` with the lighter held calls
`LighterControler.StashLighter`, which only **starts** the animated
`StashLighterRoutine`: `IsBusy = true`, `LockEquipmentSlot(LeftHand)`,
animator `lighterIgnite` / `lighterHeld` off, `TurnLighterOff`, and at its
end `UnlockEquipmentSlot` + `UnequipItemAtSlot`. The plugin's in-place
restore stashed the hands and restored at once, so the game's re-equip met
a locked slot (the fallback message), and the routine then put away the
lighter the restore had equipped (runner maks: the lighter came back away,
unlit, every reset). Since v0.24.1 the restore waits (up to 2 s) until
`IsBusy` is false and the left slot unlocked, the capture writes the held
ids to the file (`held = ...`), and 0.3 s after the restore
`SavestateBridge.ReEquip` calls `Equip(id, false)` for each one not held.

**Keeping the hands (IL + bridge, v0.24.191).** The step below runs
only when `_equipmentSlotsIds` is non-null (`brfalse` over the whole
HideAllEquiped / Equip block). When the hands already hold the capture's
`held` items, the in-place restore skips its StashHands and nulls the
restored array right after LoadNow: nothing is hidden or re-equipped and
the lighter stays lit (bridge: lit at 0.35 s and 2 s after the restore).
A capture with the book open saved `[-1, 0]` (stowed): the slots get the
"previously equipped" ids back after the load (v0.24.189) so the game's
step equips them.

**The fallback's message after restores (IL + bridge, v0.24.129).** The
step runs 1.5 s (scaled) + one frame after the load: `HideAllEquiped`,
then per saved slot `_equipmentSlots[i] = null`, `UnlockEquipmentSlot`,
`Equip(id, true)`, and `AddItem(id)` when that returns false. Before it
the same coroutine adds the default light (48) / default weapon
(`_defaultWeaponItemId`, 80 = Axe Plane) when neither possessed nor in a
saved slot. `Equip(id, true)` refuses when `AmountOf(id) + 1` exceeds the
cap, when logs are carried (right hand), `AnimControl.carry`, build mode
(weapons), or the slot is locked; bridge: `Equip 80 true` on a held axe
returns false. `AddItemNF` at the cap only calls
`HudGui.ToggleFullCapacityHud` and returns false - nothing changes. A
load from the title starts with empty hands, so only a restore reaches
it: Cheesecake's v0.24.97 log had item 80 (a wall blueprint out at F7)
and item 48 twice (hands still busy 2 s, cause unknown). Reproduced by
starting `PlayerInventory.OnDeserialized` over the bridge and lifting a
log inside its 1.5 s wait - the same stack. `FullCapacityWatch` hides
that one message during a restore + 8 s; since v0.24.129 a restore
whose hands stay busy 2 s logs `hands still busy after 2 s - <lighter
routine | left-hand slot locked> | <PlayerHold>`.

### Held weapons and the hit trigger (IL + save + log, v0.24.8)

A hit is one trigger, `hitTrigger` under the player's arm: its
`weaponInfo` has `mainTrigger = true`, and its collider
(`animEventsManager.mainWeaponCollider`, wired in the prefab -
`SetUpWeapons` returns while playing) is switched on and off by the swing's
animation events (`enableWeapon` / `disableWeapon`). `OnTriggerEnter`
returns at once when `currentWeaponScript` is null. That field is set by
the **held weapon's own** `weaponInfo` (`setupHeldWeapon`, from its
`OnEnable`: `mainTriggerScript.currentWeaponScript = this`), which sits on
a child named `collide (n)` (Transform, MeshFilter, SphereCollider,
Rigidbody, touchBendingPlayerListener, weaponInfo, StoreInformation - read
from a savestate's data). A held model that is not in hand is **unparented
to the scene root** (`FakeParent.UnParent`: `parent = null`; `OnEnable`
re-parents it under `target` with its saved local pose), so it is outside
the player's hierarchy. The in-place restore's delete step (objects whose
id the save lacks, skipping the player) deleted ~20 of these `collide`
objects on a cross-save restore (author's log, v0.24.7), and hits stopped
for the rest of the session - re-equipping cannot bring back a destroyed
`weaponInfo`, a load rebuilds it. Since v0.24.8 the delete step skips
anything under a `FakeParent` whose `target` is under the player
(`kept n held-item object(s)` on the restore line).

## Loading the endgame area (IL + bridge + a runner's log, v0.24.25)

The endgame is one additive scene, `endgame_streaming` (root `Sections`,
plus `endgame_animPrefabs` for the boss), loaded by the world's only
`TheForest.World.SceneLoadTrigger`: `EndgameEntrance/LoadEndgame`, tag
`EndgameLoader`, `DelayedLoad` forwards / `DelayedUnload` backwards
(`OnTriggerExit` + a dot product with its forward), `_loadDelay` 0.5. After a
load, `LoadSave.Activation` calls `SetCanLoad(true)` + `ForceLoad()` on it
only if `LocalPlayer.ActiveAreaInfo.HasActiveEndgameArea` - `_isInEndgame`
(saved from `LocalPlayer.IsInEndgame` in `OnSerializing`) **and** an active
area hash (`Area.GetActiveAreaHash()`, `long.MinValue` = none). Out of
bounds in the invisible section after the lab no area is active, so a load
there leaves the endgame out: the runner fell through the map, the area
report read `endgame no` without `endgame_streaming` after every load
restore (at capture: `endgame yes`, with it). In place was fine. A load
restore now force-loads it when the capture had it (`Game/EndgameLoader`).
**The red elevator and the endgame areas after a Quick load** (bridge +
IL, 2026-09-24). Neither is in the save; the ride's effects are scene
state, not scenes (areas: `same as at capture`).
- `TheForest.World.ElevatorSystem` (`Sections/HellCorridor/Elevator_01a/
  Trigger_Elevator`; also `ControlRoom/Elevator_ToSnowCave EG`): the ride
  (`Goto` coroutine) sets `_useCount` (`_useLimit` 1), moves the kinematic
  car `_rb` to the other stop in one step (`_downPosition` p2 is up at the
  overlook, y 705) and ends with `_moving` false. Its UnityEvents only do
  sound, the icons filter, the door and a keycard light. Kept by
  `Game/ElevatorKeeper` (v0.24.40, confirmed: ridable again). `Goto`
  in full: `_useCount++`, `_moving` true, moving / idle objects swapped,
  the keycard animation and **5 s wait**, then the car and the player
  (a `_tracker_` child of the car) moved up in one step, `_duration`
  (25 s) wait, `_moving` false. A restore inside a ride left it running:
  the car and player went up after the restore (maks, v0.24.52;
  reproduced by bridge with `call ... ElevatorSystem.GotoRemotePoint`,
  which starts the ride - `MoveToDownPosition` only moves the car).
  v0.24.64 stops it and applies the end state.
- **A capture during the keycard cutscene** (bridge, v0.24.79-80).
  `playerOpenKeypadDoorAction.openDoorRoutine` parents the player to the
  `playerPos` it is given (red elevator: `Sections/HellCorridor/
  Elevator_01a/playerPos`, a child of the car) for the whole animation,
  so the save holds the player's local position: a Quick load put the
  player ~1300 m off near the world origin, a Full load on the surface
  (`caves no`, the cave scenes never loaded - maks's "textures
  unloaded"). Restores now move the player to the header position when
  they land > 3 m off. The ride is started by the button (`Update`, on
  `Take`: `_sequence.BeginStage(_sequenceStage)` - stage 1 of
  `ElevatorAll`, else `GotoRemotePoint`), and **the stage starts it only
  with the player at the car** (from the overlook: stage set, nothing
  moved). `ElevatorSystem` itself is disabled away from its trigger
  (`GrabEnter` / `GrabExit`, facing check). The cutscene flag rises ~1 s
  after `Goto` starts (the walk to the reader); the car moves 5 s after
  (`_upPosition` = the lab stop, `_downPosition` = the overlook). Replay:
  car at the ride's start, `_useCount` - 1, player at `playerPos`, 0.2 s,
  `BeginStage`, then the usual fast-forward (`ElevatorKeeper.Replay`,
  the capture's fifth `elevators` field = the start stop, from
  `GameEvents.RedElevatorAt`).
- **Keypad doors the same way** (bridge + IL, v0.24.81-82).
  `activateKeypadDoor` (vault `EndgameEntrance/keypadDoor_animate/
  doorTrigger`, gold `Sections/ArtifactRoom/ElevatorCardReader/Trigger`
  - no `doorAnimator`, the yacht's panel): Update on `Take` with the
  keycard owned (within 4.75 m, idle / walking) -> `sequence.BeginStage(0)`
  -> DoActorAnimation (the player's `openKeypadDoor`) + DoEnvironmentAnimation
  -> DoLateCompletion at once (`doorOpen`, door animator `open`,
  `onDoorOpen`), `Finished` 6 s later (CompleteStage -> GlobalDataSaver
  `<geohash>_0_completed`, which DelayedAwake replays on a load). The
  vault door's `onDoorOpen`: a light (`DoAfter.BeginDelay`),
  `PlayerInEndgameTester.DoPositionningTest`, **`LoadEndgame.SetCanLoad
  (true)`** and a sound. `SceneLoadTrigger.SetCanLoad` only sets the
  flag: the load is the **crossing** of `EndgameEntrance/LoadEndgame`
  (a box around (147, -405, 1290), forward = towards the door) on the
  way in, whose DelayedLoad runs and waits for the flag - so the
  endgame loads the moment the door opens, and a capture 3 s in had
  `endgame_animPrefabs` but not yet `endgame_streaming`. A teleport to
  the door skips the crossing: nothing loads (`ForceUnload` +
  `SetCanLoad false` + teleporting into the box and out towards the
  door reproduces a first visit). Replay: `doorOpen` false, the animator
  to `Base Layer.closed` (as Awake), player at `playerPos`, `BeginStage(0)`.
- The endgame `Sections/*` carry `TheForest.World.Areas.Area` +
  `AreaMembers` (renderers, lights, probes). `OnEnter` makes an area the
  static `Area.ActiveArea` and `Load()`s it and its `_neighbours`
  (`NeighbourTokens`); `OnLeave` unloads those whose tokens reach 0.
  `Area.Awake` re-enters the area in `LocalPlayer.ActiveAreaInfo` on a
  saved game (hence a Full load is right). The "invisible section" has
  **no** active area (the hallway's textures are off; colliders work). After the ride a
  Quick load kept `ControlRoom` active, so its neighbour `HellCorridor`
  stayed loaded (textured). `ControlRoom.OnLeave(null)` unloaded it (author
  confirmed). Kept by `Game/AreaKeeper` (v0.24.41).
- `LocalPlayer.IsInOverlookArea` is not the elevator's cause (cleared by a
  restore since v0.24.26), but it **hides the Sahara cave's outside**
  (its corridors show through the ControlRoom area instead). After the
  ride a Go to the vault door cave looked wrong and the rock outside the
  Sahara cave was invisible; clearing the flag brought the outside back,
  leaving ControlRoom removed the corridors (bridge screenshots, author
  confirmed). A Go landing outside every section's renderers clears both
  (`AreaKeeper.ForTeleport`, v0.24.42) - except the endgame flag in the
  vault entrance (on the `LoadEndgame` box's forward side, towards the
  door), which a run has set and the door's load needs: kept or set there
  since v0.24.112 - **only there**: a Go from the surface straight into a
  section (the lab) still does not set the flag (untested what breaks).
  The box's `EnterEndgame` (Player registry) reaches
  `LocalPlayer.SetInEndGame` through an `EventListener` on
  `player/ControllerObjects` (and a snow-cave artifact listener).

**Held items after a Full load** (bridge + IL, 2026-09-24). After a Full
load the inventory had the axe and lighter equipped (`RightHand` /
`LeftHand`, held models active) but the animator's item flags were off
(`axeHeld`, `lighterHeld`): the axe hung at the side, the lighter clicked
without light. `playerAnimatorControl.Update` sets layer 1 (`upperBody`)
to 1 only in a `held`-tagged state and fades it to 0 towards `idle`, so
the arm layers stayed at 0. `StashEquipedWeapon(false)` +
`StashLeftHand()` then `Equip(id, false)` set the flags and the layers
came back; the Full load does that at its end (`SavestateBridge.
RefreshHeld`, v0.24.43, confirmed). Swinging through the restart,
the lighter's animated put-away (`StashLighterRoutine`, left hand
locked) outlasted RefreshHeld's fixed 0.5 s and `Equip` was refused -
the lighter gone (sxczurass; bridge with scripted swings, 1 in 3).
v0.24.46 waits until the hands are free (`HandsBusy`) and retries a
refused Equip for 2 s (0 of 20 after). Author's theory, unproven: the second
load after the scene load (here the endgame force-load, `EndgameLoader`)
freezes the arms.

**A Full load drops the player before the endgame is there.** "In game"
comes before streaming ends, and `ForceLoad` on the endgame trigger goes
through its 0.5 s `_loadDelay`, so right after it nothing is loading yet.
The player is held at the captured spot until every scene loaded at
capture is loaded again (v0.24.28; confirmed by the runner).

## Placed pickups across a load (bridge, 2026-09-24, 2026-09-25)

Placed world pickups (`PickUps/Cash (n)`, `WorldStorySpots/.../Tape_Roll`)
are not in the save: every load re-creates them, so cash taken before a
capture came back after a Full load. So do pooled greebles: cave 5's
piles are `Pooling/Pool_Greebles/Cash(Clone)` and `Coins(Clone)` (items
38 and 91, two distinct items) spawned by `Greeble_CashUnderBody` under
the hanging bodies, and taken ones come back on the same seeded spots.
Positions are not exact across loads: a `BoozeSmir(Clone)` settled
0.27 m away, the modern axe (at -269, -68, 1008, not in cave 5) 1.2 m;
an item may also use another spawn point (author). The capture's pickup
list is a set of keys (item @ position to 0.1 m) without identifier
pickups; one object with two `PickUp` components (skulls, a Timmy
drawing, `PhotoCache9`) is one key. Since v0.24.54 a Full load
(`PickupKeeper.RemoveTakenAfterLoad`, `Data/PickupMatch`) dedupes the
live pickups by the same key, pairs each captured key with the nearest
live one of its item (within 2.5 m, then any distance) and removes the
unpaired: placed ones by `Destroy` in scenes loaded at capture, clones
within 50 m of the captured spot through the game's `PickUp.ClearOut(false)`
(back to the pool) unless the capture had any item on that spot (a
greeble re-rolled). `PickUp.Collect()` is the game's pick-up (the bridge
takes a pile with it). A Quick load puts pickups taken after the capture
back (`PickupKeeper.Restore`) and leaves ones taken before it gone
(bridge, v0.24.52: both directions, cave 5).

## The weapon across a cutscene (IL, v0.24.28)

Megan's transformation (`doGirlTransformRoutine`) calls
`PlayerInventory.HideAllEquiped` at its start - `MemorizeItem(slot)`
copies each held item into `_equipmentSlotsPrevious` and unequips - and
`ShowAllEquiped` at its end, which runs `EquipPreviousWeapon` /
`EquipPreviousUtility` from that memory. The memory is not in the save; a
savestate records it (`heldbefore`, "slot:itemId") and writes it back
during the replayed cutscene (confirmed: the spear comes back).

## The player's animator (bridge `anim watch`, 2026-09-24)

`LocalPlayer.Animator`, 6 layers: `Base Layer`, `upperBody` (arms),
`fullBodyActions`, `leftArm`, `spineAddititve2`, `ClientPredictionLayer`.
`playerAnimatorControl.Start` hashes state **tags** `idling`, `held` (the
armed idle), `attacking`, `smash`, `block`. Plane axe: arms rest in
`stickIdle` (`held`, 1388274476); a swing is the arms layer
(`stickHeavyAttackWindup` / `stickHeavyAttack` / `swingLeftReturn` /
`swingRight`; bools `stickAttack`, `chargingBool`, `doAttackHeavyBool`,
int `hitDirection`); the downward smash is the full-body layer alone at
weight 1 (`axeAttackGround1`, tag hash -1474250830, bool `smashBool`) with
the arms still `held`; at rest the full-body layer sits in `stickIdle`
(-721604655) at weight 0. `playerAnimatorControl.resetAnimator` fires
`resetTrigger` (also used by `PlayerStats.WakeFromKnockOut`): it sends
the arms and full-body layers to their UNARMED idles with the full-body
layer still at weight 1 - a frame of the unarmed pose under the swing's
camera (you look into the neck) - and it stays set when nothing consumes
it (at rest), so it would cut the next swing. `Game/AnimReset` blends
back to the learned armed rest instead.

The swing's windup (`stickHeavyAttackWindup`, arms 1979354121 / full body
2015270653) is tagged **`held`** like the idle - a tag check alone reads a
swing's first ~0.2 s as rest (a v0.24.32 reset 15 ms into a swing left it
running). The attacks run in the player's PlayMaker FSM
`playerScriptSetup.pmControl` (`controlFSM`; bridge: `get
static:TheForest.Utils.LocalPlayer ScriptSetup.pmControl.ActiveStateName`):
rest `waitForInput`; smash `checkAngle 2` -> `waitForCombo2` ->
`axeSmashAttack 2|3` -> `resetSpine 2` -> `waitForIdle2` / `waitForReset3 2`;
swing `doCharge` -> `resetDelayLyr2`. After a reset mid-smash it walks on to
`waitForInput` by itself and the next swing works (bridge, v0.24.32).
During a smash the `spineAddititve2` layer is at weight 0 (`resetSpine`
puts it back). `lookDownBlend` is not smash state: `playerAnimatorControl.
Update` lerps it to `clamp(normCamX * 12, 0, 10)` - the look pitch - every
frame; a smash needs you to look down, so after a cut you still do.

`stickAttack` (state 1 of 164) leaves on `goToStickCombo` (sent as the
swing's animation plays), `toSmash`, `toChargeAttack`, or a 3 s `Wait`
(its finish event is `goToStickCombo` too); it polls `Fire1`. A swing cut
in its windup never sends it, so clicks are eaten for 3 s. The FSM's
global event `toReset2` enters `resetDelayLyr2` - the end of every attack:
spine layer (4) weight 1, ~30 action bools off (`doAttackHeavyBool`,
`chargingBool`, `clampSpine`, ...), `FINISHED` -> `waitForInput`. Sent
after a cut it gives an immediate next swing (bridge, 3 of 3).
`toResetPlayer` -> `resetAnimator` calls the game's animator reset first,
then `toReset2`.

## Savestates during an endgame cutscene (v0.24.3)

The endgame cutscenes are coroutines on the player's action scripts
(`PlayerGirlTransformAction.doGirlTransformRoutine` for Megan's
transformation: `Invoke`-free, yields on animator states of the player and
Megan, sets positions from its `mark`), and none of it is in the save.
Runner maks: a savestate taken during Megan's transformation restores
(either way) to the cutscene's **start**. `GameEvents` now keeps which
cutscene runs (`CutsceneRunning`, from the routine postfix that armed the
`endGameCutScene` rising edge), when it began (`Time.time`) and a start
counter; a capture during one writes `cutscene = <event>@<game s>`.
After a restore, the next start of the same cutscene (within 20 s) is run
at `timeScale` up to 6, easing to 1 as it reaches the captured time. It
replays the game's own script, so it lands in the same state every time
(within a frame). **Unconfirmed:** that the cutscene does start again
after a restore (the log says so if not), and that 6x keeps root-motion
positions identical.

**Megan needs ~7 s after a load (bridge + IL, v0.24.24).**
`activateGirlTransform.OnTriggerEnter` -> `DoActorAnimation` -> `InitAnim`
sends `setGirlAnimator` (its own `girlAnimator`, else
`Scene.SceneTracker.EndgameBoss`'s Animator) and `doGirlTransformRoutine`
to `LocalPlayer.SpecialActions`; the routine crossfades Megan to
`Base Layer.transform` when the player reaches
`fullBodyActions.girlTransformReaction`. `EndgameBoss` is set only by the
boss's `mutantAI.Start` (`creepy_boss`, object `girl_base`) - seen live
**null for 7.0 s after a load**, then set. A cutscene started before that
has no Megan: she keeps her pre-cutscene swing, the player plays it alone
and ends stuck (runner maks; why runners walk about the boss room first).
`Game/BossHold` skips the trigger's enter while it is null (for 60 s after
a savestate restore) and enters again once she is there. The cutscene ran
at 25x `timeScale` from its start (bridge) and Megan transformed with the
player (author).

**Megan after a Quick load (bridge + IL, v0.24.35, `Game/MeganKeeper`).**
Single player's boss Megan comes from `setupGirlMutant` on
`girlTransformPrefab1` (scene `endgame_animPrefabs`): within 350 m its
`Update` instantiates `realPrefab` (`girlMutant`) at `placedPrefab`,
destroys the placeholder, sets the trigger's `girlAnimator`, turns itself
off (the Bolt branch is multiplayer only). The trigger
(`girlTransformPrefab1/Trigger`, a 29 m sphere) sets `pickup` and turns its
collider off on enter; the `AnimationSequence` goes from stage -1 to 0. The
cutscene transforms **the same** `girlMutant(Clone)` into the boss
(`creepyAnimatorControl.activateGirlMutant`: `girlFullyTransformed`, a new
`girlSpawnGo` root at her seat, `girlStartPos`). None of it is in the save.
She spawns babies as `bossBabySpawner(Clone)` + `mutant_baby(Clone)NNNN`
roots (`girlMutantAiManager.spawnedBabies` = the spawners); they die a
moment after her. Her death leaves `girlMutant_RAGDOLL(Clone)` +
`girl_Pickup(Clone)` and destroys `girlMutant(Clone)`; destroying her
directly drops nothing (`creepyAnimEvents.OnDisable` stops the boss music).
Resetting the trigger / sequence and re-arming `setupGirlMutant` with a new
placeholder gave a seated Megan and a normal cutscene and fight (bridge).
The overworld Megan is a different path (`spawnMutants.spawnGirl` ->
`activateGirlMutantInWorld`).

**Cutscene sounds under a fast-forward (IL + log, v0.24.36,
`Game/CutsceneAudio`).** FMOD plays in real time whatever `timeScale` is,
so every sound the skipped part starts begins at its own start.
`FMODCommon.CleanupOneshotEvents` stops a one-shot flagged `useMaximumAge`
once `Time.time - startTime > 10` (game time - consistent with a normal
run); `FMOD_AnimationEventHandler.playFMODEventNoTimeout` skips that. The
boss music (`event:/endgame/music_endgame/boss_fight_music`) is
`creepyAnimEvents.startBossFightMusic`, an animation event after the
transformation; the cutscene's own music is
`music_endgame/music_transformation` (starts ~8 s in). Megan's seated
sound is her `girlIdleEmitter` (`sfx_endgame/crying_girl`). A 50 s skip
starts ~15 sounds; 3 are still due at the captured moment.
Thrown spears are `SpearThrown_Dynamic(Clone)` roots with a `PickUp`
(item Spear) on `spear_High/Trigger`; picked back up they stay as
inactive copies.

## Cannibal AI: what runs, sight, noise (code + bridge, 2026-10-03)

Explained for runners in `knowledge/cards/cannibal-ai.md`; the facts:

- A live cannibal has 4 PlayMaker FSMs (brain, combat, encounter,
  sleeping). `global_motorFSM` / `global_visionFSM` / `action_searchFSM` /
  `action_inTreeFSM` are **not on the object** (`mutantScriptSetup.pmMotor`
  null) - events sent to them are lost. Their work is C#: sight in
  `mutantSearchFunctions` (`toLook` / `toTrack`), motor in `mutantAI`,
  search in `pmSearchReplace`. The combat FSM dispatches: 29 states start a
  `pmCombatReplace` coroutine. Combat FSM disabled while asleep, enabled
  awake (bridge).
- **Sight range lives on the player**: `visRangeSetup.updateVisParams`
  (0.65 s): `(100 - crouchOffset - bushOffset + movementPenalty) x
  clamp(1 - trees/12, 0.4, 0.8) x offsetFactor (1.05) x (1 - Stealth x
  StealthRatio / 75) x amountOfLight + litWeapon (70 x light) +
  lighterRange (50, 70 dark surface)`, clamped 4-100 -> `unscaledVisRange`.
  Light: 1 -> 0.5 over TimeOfDay 50-90, 0.5 until 310 (the 270-310 ramp
  stays under the clamp), caves 0.65. Crouch 40 x (1.5 - light); bush
  (`SmallTree` trigger) 50 crouched / 20 standing; movement +35 when
  `overallSpeed` > 0.5 (walking reads 0.34). Bridge, day, open: 84 standing,
  67.2 crouched.
- A cannibal's look (`toLook`, ~0.25 s): one ray from the head to the
  player's collider centre + 1.2 m, within 100 m and +-65 deg; valid when
  closer than `unscaledVisRange` and not `targetDown`; locks at once within
  30 m or when `playerAware`, else after 3 valid sightings (the counter is
  not reset by misses). Losing a target = `playerAware` for 30 s.
- Crouch: `enableCrouchLayers` -> close enemies `setVisionLayersOn` ->
  `visLayerMask` 104208384 (adds layer 12 `treeSmall`); standing 104204288;
  the spawn default 104212480 has layer 13 `ReflectBig` instead (bridge).
  The ranges the player sends (`setVisionRange` 12 crouched,
  `setLighterRange`, `setMudRange`, `setcrouchRange` - wrong case, never
  lands) are stored on `mutantSearchFunctions` and never read.
- Noise: `playerNoiseDetection` pulses every 0.5 s to cannibals within
  `soundRange`; `noiseDetectFSM` sets it from `overallSpeed` (walk > 0.9,
  run > 1.5; 0 crouched). Bridge: walking 0, running 58.8 (42 x 1.4;
  caves x 2.2 running, x 1.65 otherwise). `mutantSoundDetect`: 5 s
  cooldown, distraction 60 s, torch light 12 s.
- Mood: brain `chooseMood` - aggression >= 5 aggressive, else passive
  (stalk). `mutantDayCycle`: aggression 1 (days 0-1), 2, 3, 4 (day 4+);
  `setAggressive` sets 10. `maxAttackers = 3`. `doStalkRoutine`: < 8 m
  attack; < 24 m weighted roll (run away 1, attack 0.25 default / 0.1 day /
  0.15 skinny, ...); > 53 m close in.
- Spawns: `updateSpawns` starts at the 5th nearest spawn point
  (`spawnCounter = 4` after a distance sort); caps by day in
  `setDayConditions` (days 6-9 have no branch); families per day in
  `mutantSpawnManager.setAmountDay*` (`setAmountDay15` unreachable - the
  10-24 branch comes first); dawn despawns cannibals beyond 75 m.
- The player's 3 m/s cap (`hitByEnemy`) is set only by
  `PlayerClimbRopeAction.restorePlayerCollisions` (co-op players
  overlapping after a rope); the 5.5 m/s `doClampVelocity` has no caller in
  code (`ilscan strings` finds none).

## Megan's boss AI (FSM export + code + bridge, 2026-10-03)

The runner-facing version is `knowledge/cards/megan-boss.md`; FSMs in
`docs/fsm/megan-*.txt`. PlayMaker checks **global transitions before the
state's own** (`Fsm.ProcessEvent`, IL) - blank local transitions in an
export never apply. The loop: `randomIdle` -> `setToPlayer` ->
`worldCheck` -> `moveToPlayer`, whose every-frame check sends her to
attack (`checkTurns` -> `chanceToDodge` -> `chooseAttack`) **as soon as
the player is within 35 m**; only after 1-2 s still beyond 35 m does she
reach `chooseAction`, the weighted roll `girlMutantAiManager.setAiParams`
feeds (births 6, attack 0.5 + 5, walk forward 2 beyond 35 m). `chooseAttack`
picks by `targetDist` alone (8 / 13 / 27 / 38 / 50 m bands).
`chanceToDodge` checks `gettingHit` (1.3 s after a hit) first: 1 : 0.4
attack / walk back, no 15 s lock; else dodge weight 0 for 15 s after
`activateGirlMutant`, then 0.25 : 1. A hit -> `gotHit` -> counter after
0.2 s (live). `SendRandomEvent` normalises its weights
(`ActionHelpers.GetRandomWeightedIndex`, IL): the spin roll (close / mid /
counter only) is 0.03 / 0.63 = 4.76%. Co-op health: `Health + Health/3 x n`,
n = every player within 350 m incl. you (616 / 739 / 800 cap). Thrown spear
(plain and upgraded) `ArrowDamage.damage` 40 (live), no head bonus. Explosion: flat 30 (live 370 -> 340), 25%
stagger for 10 s. 370 health on Normal (live). Births stop once
`spawnedBabies` (the spawners, never destroyed) holds more than 2 - live 6
spawners / 5 babies in `ruben-megan`. Melee 28 x `creepyDamageRatio`.

## Enemies across an in-place restore (IL, v0.24.5, corrected v0.24.10)

Enemies are spawned and despawned by `mutantController` (static
`Scene.MutantControler`), not kept by the serializer: families
(`activeFamilies`, `allWorldSpawns`), cave spawners (`allCaveSpawns`, each a
`spawnMutants`), `activeCannibals`, day-based setup (`setDayConditions`,
`updateSpawns` / `updateCaveSpawns`). `startSetupFamilies` (returns in horde
mode) starts `setupFamilies`, which despawns every active cannibal
(`despawnGo`), destroys the world spawns, disables the cave spawners'
`spawnMutants`, sets the day's conditions and restarts `updateSpawns`. It
has **no guard against a second run** beside the first.

`restartEnemiesFromPauseMenu` (run by `RefreshMaxActiveMutants` when
Creative's enemy option changes) waits out the pause view, then
`startSetupFamilies()` when `currentMaxActiveMutants > 0`, **else
`removeAllEnemies()`**. `currentMaxActiveMutants` is `Cheats.NoEnemies ? 0 :
maxActiveMutants` (`setDayConditions`, `RefreshMaxActiveMutants`; every
`maxActiveMutants` branch is 15-20). `Cheats.NoEnemies` = the internal
cheat, or peaceful mode, or **in Creative `!PlayerPreferences
.AllowEnemiesCreative`** (registry `AllowEnemiesCreative_h...`; the author's
is 0). v0.24.5 ran that routine after every in-place restore, so in the
author's Creative game (enemies off, yet fighting cave enemies) every
restore removed them. Since v0.24.10 (`SavestateBridge.RespawnEnemies`):
enemies off -> nothing; else when the restore sent `NotInACave` (a surface
state) and `PlayerStats.delayedMutantSpawnCheck` is false, the game already
ran `startSetupFamilies` (`PlayerStats.NotInACave`: with the delayed check
set it only runs `removeCaveMutants`) -> nothing more; else
`startSetupFamilies()`. Not the captured positions - nothing records those.

**Bodies.** A dead cannibal is `Instantiate(clsragdollify.vargamragdoll)`
at the scene root (`clsragdollify.metgoragdoll`), with no save identifier,
so LoadNow and the delete step never touch it. v0.24.10 collects the
ragdoll prefabs' names from every loaded `clsragdollify`
(`Resources.FindObjectsOfTypeAll`, once a session) and destroys root objects
named `<prefab>(Clone)` without an identifier (`bodies: n removed`).
Severed limbs are cut from the ragdoll's mesh at runtime
(`clsurgutils.metdismemberpart`, not a prefab); whether they stay children
of the ragdoll root is unconfirmed - the restore logs world pickups not at
capture 0.5 s later (`n world pickup(s) not at capture (...)`).

### Seen live through the test bridge (2026-09-24, Hard, one kill)

- **The live cannibal** is a root `mutant_male(Clone)NNN` (tag
  `enemyRoot`, layer `enemy`): `mutantTypeSetup` (`spawner` = its
  `spawnMutants`, `dummyMutant` = the body prefab, `health`), `enemyType`
  (`Type = regularMale`), `mutantFamilyFunctions`; the AI and
  `EnemyHealth` (`Health` / `maxHealth` 130) on the `mutant_male_BASE`
  child. Each has root helpers `mutantWorldPosition`, `lastPlayerSighting`,
  `currentWaypoint`. **A kill deactivates the cannibal, it is not
  destroyed**; it leaves `activeCannibals` and the pool reuses the object
  on the next setup (renamed, e.g. `...0040` -> `...00400`).
- **The body** is `Instantiate(mutantTypeSetup.dummyMutant)`: a root
  `mutant_male_Dummy(Clone)` with `dummyTypeSetup` (`_type`), `destroyAfter`
  (`destroyTime 600`, `destroyDistance 35`), `setupFeeding`,
  `spawnEncounter`, `CoopMutantDummy` (present in single player too), no
  save identifier; a child `encounterFeedingGoFake(Clone)` (another
  cannibal family's feeding scene). v0.24.14 clears `*_Dummy(Clone)` roots
  without an identifier after an in-place restore.
- **A family** is a root `mutantSpawner(Clone)` (tag `mutantSpawn`) with
  `spawnMutants`: `amount_male` / `amount_female` / ..., `leader`, `pale`,
  `paintedTribe`, `skinnedTribe`, `sleepingSpawn`, `allMembers`,
  `leaderGo`. `mutantController` (on `_mutantSetup`, static
  `Scene.MutantControler`): `activeCannibals` (the live list),
  `allWorldSpawns` (the families), `allSpawnPoints` (23),
  `maxActiveMutants` 15. Families follow the player: a 1 km teleport
  moved one family's members ~150 m within a minute.
- **The families' setup dies inside an in-place restore.** The restore's
  `NotInACave` -> `startSetupFamilies` -> `setupFamilies` ran (setupBreak
  set, every cannibal despawned, every spawner destroyed within 1 s), but
  `updateSpawns` never ran - its first step removes destroyed entries from
  `allWorldSpawns`, and 7 destroyed entries stayed for minutes, with no
  cannibal anywhere. Calling `startSetupFamilies()` again a few seconds
  later (from the bridge) rebuilt 6 families / 12-17 cannibals within 5 s,
  twice. Which frame kills the coroutine is not known (both run on
  `Scene.ActiveMB` = `LoadSave`); v0.24.14 re-runs the setup 6 s after an
  in-place restore when nothing is alive, and logs the count 6 s later.
  In the v0.24.14 test the setup died after **one** family (of 5-6), twice;
  v0.24.15 re-runs it when fewer families are alive than just before the
  restore. A body's weapon prop carries a `StoreInformation` deep inside
  (`.../LeftHandWeapon/FireStick/StickFlame/Sparks`), so the bodies rule
  checks only the root's own identifier (v0.24.15).
  `setupBreak` clears itself after 1 s (`Invoke("resetSetupBreak", 1)`).
- `Cheats.GodMode` is what `PlayerStats.Hit` / `CheckDeath` read;
  `DebugConsole._godmode on` also runs `_setstat full`, `_survival off`,
  `_energyhack on`. The `DebugConsole` component only exists with the
  title screen's `developermodeon` (author) - set the static directly.

### Putting cannibals back where they stood (v0.24.16)

The game's rebuilt families spawn at spawn points of its choosing. Seen
live: `spawnMutants.fixMutantPosition(Transform m, Vector3 newPos)` (a
coroutine: sets the position every frame for ~1 s and sends
`updateWorldTransformPosition`) moved a sleeping family leader 745 m to
the player; he stood asleep (`global_brainFSM` `setSleeping`,
`action_sleepingFSM` `sleeping`), woke when approached (`aggressive`,
`action_combatFSM` attacks), stalked, climbed a tree and came back - a
normal cannibal. The AI state lives in PlayMaker FSMs on `_BASE`
(`action_combatFSM`, `global_brainFSM`, `action_sleepingFSM`,
`action_encounterFSM`; `PlayMakerFSM.ActiveStateName`). v0.24.16 records
each live cannibal at capture (`Data/EnemyRecord`: family, `enemyType.Type`,
position, yaw, `EnemyHealth.Health`) and after an in-place restore moves
the game's cannibals there - whole families matched by make-up first so
leaders keep followers (`Game/EnemyKeeper`). The AI state is not set.

### Cannibal kinds and families (bridge + IL, 2026-09-24)

`enemyType.Type` is **not** a cannibal's kind: a skinny one read
`regularMale` (a pooled leftover), and a "regularMale"'s body said
`skinnyMale`. All males are the pooled `mutant_male` prefab (females
`mutant_female`, pool `"enemies"`); the family's spawner makes each its
kind by message (`spawnMaleSkinny` -> `setMaleSkinny`,
`setSkinnyLeader`, ...), which leaves `mutantTypeSetup.storeSkinnyBool`,
`storePaleMutantBool`, `storeMutantType`. The family kind is the
spawner's settings, rolled at random by `mutantController.setup<Kind>Spawn`
(`setupRegularSpawn`: `amount_male` 1-3, `amount_female` 0-2 (0-3 in one
branch), firemen in Hard, `leader` by chance; then
`numActiveRegularSpawns++`, `numActiveSpawns++`; the spawner goes into the
kind list, e.g. `allRegularSpawns`). `updateSpawns` builds a family:
`Instantiate(spawnGo, pos, rot)`, `setup<Kind>Spawn(spawn)`, list add,
`enabled = true`, `invokeSpawn()` (`InvokeRepeating("checkSpawn", 0, 3)`),
`addToWorldSpawns()`. `checkSpawn`: a **cave** family (`spawnInCave`)
spawns with a player in the caves within 130 m and `!alreadySpawned`, a
sinkhole family within 200 m; a **surface** family spawns at once, on the
first call, whatever `alreadySpawned` says, then cancels its invoke
(v0.24.17 also started `doSpawn` and got every member twice). `doSpawn` clears
`allMembers`, sets `doLeader` and starts one routine per kind, each
spawning from the pool at the spawner within `range` and placing with
`fixMutantPosition`. Teardown: `mutantController.despawnGo(go)` (pool
despawn + `activeCannibals.Remove`); `setupFamilies` just `Destroy`s world
spawners (`spawnMutants.OnDestroy` only cancels its invokes).
v0.24.17 rebuilds captured families this way (`Game/EnemyKeeper`).

**Sleep.** A family rebuilt 20 m from the player spawns awake. Asleep
= `action_sleepingFSM` in `sleeping` (`global_brainFSM` `setSleeping` on the
way). `mutantFollowerFunctions.switchToSleep()` sends `toSetSleep`
(`sendSleepEvent` does a whole family): the cannibal goes `gotoCave` /
`runToCave` to the sleeping FSM's `sleepPos` (Vector3 variable; the
spawner's area by default; also `sleepAngle`) and sleeps. Set `sleepPos`
to where it stands first and it sleeps on the spot (bridge, 2026-09-24).
`mutantTypeSetup.setSleeping` sets the FSM bool `sleepOnAwake` at spawn
(creepies only).

**Who decides sleep (IL + bridge, v0.24.21-22).** `mutantDayCycle.OnEnable`
does `Invoke("initWakeUp", 5)` (from `Start` too). `initWakeUp`, for a
non-creepy: family `spawnMutants.sleepingSpawn` -> `fsmSleep = true`,
brain `toActivateFSM` (asleep, stays asleep); otherwise on **day 0**, in
daylight, no horde, not `instantSpawn` -> `sendWakeUp(250)` (asleep, wakes
after 250 s - why a fresh game's families all sleep); any other day or
dark -> `sendWakeUp(0)`; and **`sleepBlocker` set -> `sendWakeUp(0)`**.
`sleepBlocker` is set by `pmSearchReplace`'s search routines /
`enableSleepBlocker` and **never cleared** - a pooled cannibal that once
searched wakes on every later spawn (read `True` on all three of a rebuilt
family). `invokeSpawn` -> `updateSpawnConditions` re-rolls `sleepingSpawn`
(about half the world families, not in the dark), and the periodic
`updateSpawns` SendMessages it again. For the first 5 s a spawned member
is awake, sees a player 20 m away and runs about. `switchToSleep` on one
already `sleeping` does nothing; **setting `Transform.position` on an
asleep one keeps it asleep there** (bridge, 10 s watched).

**Cave families (IL + bridge, v0.24.49-50).** Cave spawners
(`allCaveSpawns`, e.g. `mutantCaveSpawnerCave6 (11)`) are scene objects
that stay. `PlayerStats.InACave` starts `mutantController.updateCaveSpawns`
(enables the spawners, nearest first, `invokeSpawn`; `checkSpawn` spawns
a family within 130 m unless `alreadySpawned`) and invokes
`doRemoveWorldMutants` 30 s later. `doSpawn` clears `allMembers` and
spawns anew. `setupFamilies` despawns `activeCannibals` but **never
`activeBabies`**, so a setup run in a cave orphaned the babies (7 -> 14
per Quick load); `removeAllEnemies` despawns both with `despawnGo`. The
armsy is `male_creepy(Clone)` from its own cave spawner; it can appear
first when a restore sends `InACave` (a teleport into a cave skipped the
entry). `Game/EnemyKeeper.RestoreCave`: a Quick load keeps the live cave
cannibals (no setup run), moves each captured family's members back or
spawns the family anew when some are missing, and despawns orphans; a
Full load runs it 6 s after in game (the game's setup ~5 s in despawns
everything first). Confirmed in cave 6: 3 of 3 placed after both, the
babies stay at 7.

**The game's setup after a load (IL + bridge, v0.24.45).**
`mutantController.Start` -> `Invoke("doStart", 2)`; `doStart` fills the
spawn lists and calls `startSetupFamilies` (day > 0, or day 0 with
`skipInitialDelay`; else `disableStartDelay` 100 / 270 s later; never
with `Clock.planecrash`). `setupFamilies` (on `Scene.ActiveMB`; in a cave
it waits 3 s first) returns without doing anything while `setupBreak`
(cleared 1 s after each run) or `startDelay` / `NoEnemies` holds, else
despawns, destroys the world spawners and starts `updateSpawns`, which
waits 3 x 1 s before rolling a family. Seen live after a Full load: the
game's call 0.7 s after "in game", its first family 3.8 s after, one
call only. `Game/SetupHold` skips it for 10 s after a Full load's "in
game" and the rebuild runs the setup itself; a family's members all
spawn in the spawn routine's first frame (`spawnRegularMale` yields only
after its loop): 0.08 s.

## The plane wreck across an in-place restore (bridge + IL, 2026-09-24)

`PlaneCrashController.OnDeserialized` does `Invoke("setupCrashedPlane",
0.3)`; `loadCrashPlane` instantiates `savedHullPrefab` at `savePos` into
`spawnedHullPrefab` - it never destroys the previous one. A load starts
from none; every in-place restore added one more root `Hull(Clone)` (three
after two restores), each with its own copy of every wreck pickup - the
growing `Axe Plane xN` in the "not at capture" line. The roots have no
save identifier. v0.24.14 destroys every root named like the current
`spawnedHullPrefab` except that one, 1.5 s after an in-place restore.

Until then (bridge, 2026-09-25, Slot 1, plane axe taken): both wrecks'
`Axe_Plane_High` are **active**; both go inactive about 1 s later (what
hides a taken wreck pickup is unchecked), then the old wreck is
destroyed. Nothing is left, but a pickup listing in that window reads
`Axe Plane x2` - since v0.24.63 the "not at capture" listing waits for
the wreck clear.

## Blood on the player (bridge + IL, 2026-09-24)

`PlayerStats.IsBloody` (a plain auto-property) set by `GotBloody()`,
which repaints the skin (`CoopPlayerVariations.UpdateSkinVariation(bloody,
mud, red, cold)`) and the weapon (`PlayerInventory.BloodyWeapon()`). The
save does not restore it. The wash is `GotCleanReal()`: `resetSkinDamage`,
`CleanWeapon`, `StopBurning`, `CloseBloodyTut`, `coveredInMud = false`,
`IsBloody = false` - it does nothing while `BuildingWarmth != 0`.
Confirmed in game through the bridge: body and axe clean (author).
v0.24.14 washes after every in-place restore (mud too).

## Greebles (IL, 2026-09-24)

Small world pickups (sticks, rocks) come from `GreebleZone`s. Positions are
**seeded**, not random per spawn: `SpawnIndex(i)` sets `Random.seed =
GetRandomSeed() + i`, where the zone seed is `GreebleZonesManager.GZData
._seed` (set once from the zone's position + `RandomSeed`; the manager is
in the save), then draws the position with `Random.Range`
(`GreebleUtility.ProceduralValue`). What can differ between spawns: the
type pick `ProceduralGreebleType(defs, AllowRegrowth && Destroyed,
Clock.ElapsedGameTime - CreationTime)` (a regrowing instance can draw a
different number of randoms), the per-instance `Destroyed` flags, and the
ground raycast. Unconfirmed which one moves a stick in game.

Live (bridge, 2026-09-25): a zone's objects come from the `Greebles`
`SpawnPool` (`GreeblePlugin.Instantiate` / `Destroy` = `Spawn` /
`Despawn`), so one object shows up at different spots over time - a
handle is not an identity, the seeded spot is. A greeble stick's
`PickUp` has `_poolManagerDespawnCreature = false`, so a normal pick-up
**destroys** it (`ClearOut`); `GreebleZone.Despawn` (zone out of range)
marks a missing or inactive instance `Destroyed` and hands the object to
`GreeblePlugin.Destroy`. Taken sticks came back after leaving and
returning (regrowth), and a teleport into a cave reset the zones.
`PickUp.OnSpawned` resets `Used` but not `_disableInsteadOfDestroy`.

**The ground sticks and small rocks are random per spawn (IL + bridge,
2026-10-01).** Most loose sticks (`Stick_Greeble_0N`) and small rocks
(`Rock`, `SmallRock`) on open ground do not come from a `GreebleZone` but from
the player's own `GreebleLayer`s (`player/ControllerObjects/GreeblesRoot/
Standard`, two: 100 x 100 m in 8 x 8 cells, 30 x 30 m in 3 x 3; `Snow` / `Mud`
siblings by season). `InstantiateGreeble` calls `ProceduralSeed(worldX, worldY,
worldZ)` - **an empty method in this build** (`ret`) - then draws the type,
the offset in the cell and the rotation from whatever `Random` holds, and
casts down from 5 km. So they land somewhere new on every spawn (two launches
at the same spot gave different sticks); a map cannot place them. The zone
greebles are deterministic: `WorldDump.Greebles` (v0.24.174-176) replays
`SpawnIndex` + `GreebleUtility.Spawn` and matched the live objects to 1 cm
(plane debris, lavender, Cave 6's body piles, sticks, stalactites, spikes).
A greeble prefab can itself be an LOD placeholder (`Cave_SpikesSmall` ->
`Pool_Caves/CaveSpikesCluster1_High`, `Fern2_Loader`, `Bush07_loader`).

**Why sticks move (bridge + IL, 2026-09-25, fix list 3).** Many zones sit
on **pooled trees** (`Pooling/Pool_Trees/PineTreeMoss_High(Clone)00N/Greeb`),
not in `GreebleZonesManager`'s arrays. Such a zone has no manager `GZData`:
`OnEnable` makes its own (`_seed = -1`) once, and the first `GetRandomSeed`
fixes `_seed` from the position the pool object stood at **then**
(`(int)x + (int)y + (int)z + RandomSeed`). It is never reset, so the pool
object carries its first tree's seed to every tree it later serves - and
its `_instancesState` / `InstanceData.Destroyed` too (`OnDisable` ->
`Despawn` marks missing or inactive instances destroyed, 255). Which pool
object serves a tree depends on the order trees spawn, so leaving and
coming back re-rolls the sticks / rocks around it: the same tree at
(501.23, 76.37, 90.3) had seed 11525 on `(Clone)002` and 11680 on
`(Clone)001`, three different stick layouts in three visits, the captured
sticks inactive. With `AllowRegrowth = false` a taken stick's `Destroyed`
index follows the pool object to another tree. Vanilla behaviour, not the
restore's; a restore only makes it visible. Positions: `SpawnIndex(i)`,
`Random.seed = seed + i`, sphere point (x, z) within `Radius`, ray down
from `TransformPoint`.

## Cave wooden panels (IL, v0.24.2)

A panel is `BreakWoodSimple`: `int Health`; `Hit(damage)` subtracts and at
`<= 0` calls `CutDown` (in multiplayer it sends `BreakPlank` with the
panel's index instead). `CutDown` plays `breakEvent` once, activates the
three pieces `Cut1..3`, unparents them, pushes each with a random force of
30-100 per axis, and **Destroys the panel**. `Explosion()` is
`Hit(Health)`. Every panel is in the scene-authored array
`CoopWoodPlanks.Instance.Planks` (`Awake` only sets `Instance`; multiplayer
syncs broken ones through `CoopWeatherProxy.BreakableWallsChanged`, a
`CutDown` per index). An in-place restore leaves `Health` alone (runner
maks: axe clips wore a panel down over restores until it broke), and a
destroyed scene object cannot come back through the serializer.
`Game/PanelKeeper`: the capture writes every live panel's health by
position (`panels` header); while armed a prefix on `CutDown` copies the
intact panel under an inactive holder (so the copy does not wake) before
the game breaks it; an in-place restore sets the health back and moves a
kept copy into place (and into `Planks`), deleting the flying pieces. A
load restore only sets health. Whether panels carry a `UniqueIdentifier`
is unknown. Health restore seen acting in maks's v0.24.13 log (`7 healed`,
`7 rebuilt`), but the panels still **looked** damaged: `LocalizedHit`
(throttled to one per 0.5 s real time) turns every `Renderer` transform
under the panel within 4 m of the hit by `Euler(Random.Range(-1,1) x3)`
(ints: -1 or 0 degrees per axis) and nothing turns them back - the look is
not state, `Health` alone decides the break. v0.24.23 records the boards'
rotations at a panel's first hit and straightens them on restore.

**CutDown runs twice per break (bridge, v0.24.123; runner Tom "the panel
would not break").** A panel has two `BoxCollider`s (solid + trigger), and
`weaponInfo.OnTriggerEnter` hits each in the same physics step, so `Hit`
and `CutDown` run twice before the `Destroy` lands (Tom's log: every break
logged in pairs). The second call finds the pieces already unparented: a
copy taken then has none of its own (`Chunks` empty, `Cut1..3` on the
flying ones). Rebuilt, with those pieces deleted, its next `CutDown` threw
on `Cut1.SetActive` before its `Destroy` - a panel that stayed up and
"broke" on every hit (222 copies in one session). `PanelKeeper` copies a
panel once per instance and never rebuilds a copy without its pieces.
A smash does ~50 (one break), a swing ~10.

## Trees, bushes and saplings (IL + bridge, 2026-09-25)

**Trees.** A scene tree is `Nature_Spawned/<Kind>_<n>` with `LOD_Trees`,
`CoopTreeId` (`Id`, disabled component) and a `BoltEntity`; its view is a
pooled `Pool_Trees/<Kind>_High(Clone)` carrying `TreeHealth`. Chopping is
two stages: the first `Hit` (`DamageTree`, `Health` 1 on the standing
view) calls `CutDown`, which swaps in the **chopped model**
(`<Kind>Cut(Clone)`, `TreeHealth.Health` 4, chunks `TreeDmg1..4`, `Lower`,
`Upper`) and disables `LOD_Trees`; four more hits call its `CutDown` ->
`DoFallTree` (returns the falling `TrunkUpperSpawn`, sets `CurrentView`,
fires `TreeHealth.OnTreeCutDown`) -> `DestroyTrunk`, which moves the
`ExplodeTreeStump` child (`Lower`, the stump) **under the `LOD_Trees`
object**. The fallen top breaks into `Pool_PickUps/Log(Clone)` pickups
(item 78) over the next seconds. Bridge: `call <view> TreeHealth.Hit`
chops like a player.

**The save** (`MassDestructionSaveManager`, `mass_v016`): `OnSerializing`
lists every `LOD_Trees` that is disabled with `CurrentView == null` as
`CutDownTreeIds` (negative id = its `LOD_Base` destroyed); `OnDeserialized`
only enables the manager, whose next `Update` **cuts** each listed tree
(destroys the view, a `StumpPrefab` only for scale >= 1, fires
`OnTreeCutDown`, disables `LOD_Trees`) and logs `Turning off N trees`
(Unity's log only). Nothing ever stands a tree up, so an in-place LoadNow
left every tree cut since the capture down; the list itself does come
back (bridge: 37 -> 36 entries). A half-chopped tree is not on the list:
a Full load stands it up. Loose logs are **not** saved: a Full load had
none of the ten lying at capture.

**Regrowth** is the game's own (`ShelterTrigger.CheckRegrowTrees`, the
sleep option): per cut tree `DontSpawn = false`, `enabled = true`,
`RefreshLODs()`, `TreeLodGrid.RegisterTreeRegrowth(pos)`, and every child
of the tree object destroyed (a `LOD_Stump` child despawned first; the
game's `SpawnStumpLod` also clears all children, so they are only ever
stumps). Enabling `LOD_Trees` alone brought the tree back and it chopped
and fell normally again (bridge). `Game/NatureKeeper` (v0.24.61) runs
that for every disabled tree not on the restored list after a Quick load,
destroying its chopped model or falling trunk (a postfix on `DoFallTree`
remembers the trunk) first.

**Bushes and saplings.** `GreenBush_*` (`LOD_Bush`; view `BushDamage`,
`Health` 5, its `MyCut` is flying debris only), ferns (`LOD_Bush`, view
`CutBush2`) and saplings (`Sapling1_*`, `LOD_Sapling : LOD_Trees`, no
`CoopTreeId`; view `CutBush2`, `sapling`, `Health` 8) - the sapling is the
bush that drops two sticks: its `MyCut` `Sapling1_Cut(Clone)` holds two
`Stick_High` pickups (item 57, `destroyAfter`). Cutting despawns the view
and nulls `LodBase.CurrentLodTransform`; the LOD's next `RefreshLODs`
sees it spawned with no view (`lodWasDestroyed`) and, as these answer
`DestroyInsteadOfDisable = true`, **destroys the scene object**. None of
them is in the save: a bush cut **before** a capture was back after its
Full load. A copy of the scene object (under its parent with local values
- `Instantiate` copies the local transform) spawns its own view and cuts
like the original (bridge). `NatureKeeper` keeps such a copy under an
inactive holder at each cut (prefixes on `BushDamage.CutDownReal` /
`DespawnBush`, `CutBush2.CutDown` / `DespawnBush`; greeble-owned bushes
under `Pooling` skipped). A Quick load puts back the ones cut after the
capture (v0.24.62: the file's `bushes` mark is this world - launch and the
tree save manager's instance id - and the last cut's number; one cut
before stays cut, its sticks kept); a file from another world puts every
copy back, as a load would. The logs and sapling sticks not at capture
are removed (`PickupKeeper.RemoveExtra`, sticks only under a cut).

**A Full load and cut bushes** (v0.24.65-66, bridge): every cut this scene
is recorded by scene path and place (`Nature_Spawned/GreenBush_40@x,y,z` -
names repeat, so the nearest within 0.5 m); capture writes the ones still
cut (`cutbushes`). After a Full load - and a Quick load from another
world - each is cut again: `DespawnCurrent` if its view is up, then the
LOD object destroyed, which is what the game's cut leaves. Scene bush
objects are there as soon as the load ends (`Nature_Spawned` is in
`ForestMain_v08`). Not done: a sapling's two sticks are not put back.

## Time of day and the sun (IL + bridge, 2026-09-25)

`TheForestAtmosphere.Instance.TimeOfDay` (degrees, 0-360; ~12 in the
morning of the test save) is the clock; the sun's rotation follows
`DelayedTimeOfDay`. `Update`: when the player's inventory is missing or
disabled (and a few `CurrentView` cases), when `ForceSunRotationUpdate`
is set (sleeping sets it each frame) or `Clock.Dark`, it **snaps**
(`DelayedTimeOfDay = TimeOfDay`, catch-up off). Otherwise it eases: while
the camera turns or the player stands still, a 5 s `LerpToTimeOfDay`
(EaseInQuad); more than 5 degrees behind otherwise sets
`CatchUpTimeOfDay` (EaseInOutQuad, 5 s) - which is the sweep round
through the night after a jump back. Bridge: a Quick load and a Full
load from `TimeOfDay` 250 back to 12 both snapped (in step 1 s later),
and a `set ... TimeOfDay 120` in play snapped within 0.5 s; the sweep was
not reproduced. `Game/SunSync` (v0.24.67) sets `ForceSunRotationUpdate`
0.5 s after a restore if the sun is still > 5 degrees off or catching
up, and logs `sun: ... snapped` only then.

## The ESC menu and the player lock

**A menu stops a restore (bridge, v0.24.123, runner Tom).** The pause
menu and the inventory both set `timeScale` 0; a Quick load runs over
game time (hands put away, keepers waiting), so F7 with either open sat
"busy" for 120 s+ and finished within a second of the menu closing. The
options screens (graphics, audio...) are children of `HudGui.PauseMenu`
(`LeftScreenAnchor/Panel - Options`), so one close covers them.
`PlayerInventory.TogglePauseMenu` from the `Pause` view = the ESC key's
close (view World, input state back, timeScale 1, `UnLockView`);
`PlayerInventory.Close` for the inventory. `Game/MenuClose` runs both
first on Restart and Go. `PlayerViews`: Pause = 6, World = 2, Inventory = 3.

`HudGui.TogglePauseMenu` (IL) opens with `FpCharacter.LockView(true)` and
closes with `UnLockView()`. A panel opened over it found the player already
locked; releasing "our" lock on close called `UnLockView` under the menu,
whose `Input.LockMouse()` hid the cursor (author). `ModuleHost` now leaves a
lock the game already held to the game.

## Caves

Entering a cave on foot, `CaveTriggers` / `CaveDoor` send
**`SendMessage("InACave")`** (leaving: `"NotInACave"`) to
`LocalPlayer.GameObject`. Receivers: `PlayerStats.InACave` —
`Clock.IsCave()` (cave lighting), `SetInCave(true)`, cave audio, and
**`IgnoreCollisionWithTerrain(true)`** (caves are under the terrain) — and
`playerAiInfo.InACave`. A save made in a cave restores the same way: `Clock`
and `ActiveAreaInfo.OnDeserialized` send `InACave`.

**The black walls in the cave mouths (IL + bridge, v0.24.123; runners
Tom and Cheesecake).** Each entrance has a `caveEntranceManager`:
`blackBackingGo` (`BlockCaveInsideView`), `blackBackingFadeGo`,
`fadeToDarkGo` (`FadeIntoDark`) - the black that hides a cave's inside
from the surface. All 22 register in `Scene.SceneTracker.caveEntrances`
(`Start`, which also sets them from `IsInCaves` / `IsInEndgame`). Walking
through a mouth, `CaveTriggers.Update` (and `activateCave` for climb /
rope entrances) starts `disableCaveBlackRoutine` / `enableCaveBlackRoutine`,
which switch **every** entrance (`disableAllCaveEntrances`). `InACave` /
`GotoCave` never touch them, so a teleport in from the surface kept the
wall up (bridge: `BlockCaveInsideView` active after `tp` into the Cave 6
mouth), and one out left the caves see-through. `LoadSave.Activation` and
`WakeInCave` call `disableCaveBlack`. `GameBridge.CaveBlack` does the
walk's switch from `SyncCaveState` / `ForceCaveState`.

State: static `LocalPlayer.IsInCaves` (→ `ActiveAreaInfo.IsInCaves`).

**The flag and the effects can disagree after a restore.**
`ActiveAreaInfo.OnDeserialized` sends `InACave` when the saved
`_isInCaves` is set (and the player is below the terrain) and **never sends
`NotInACave`**. `GotoCave` acts only when the flag differs. So an in-place
restore of a surface save while in a cave left the cave effects on — no
terrain collision, cave streaming — with nothing to switch (author,
v0.22.0). `PlayerStats.NotInACave` has no flag check of its own
(`Clock.IsNotCave`, ocean on, `SetInCave(false)`, MP bookkeeping), so the
plugin sends the message the save's flag calls for outright
(`GameBridge.ForceCaveState`). Senders of `NotInACave` (`ilscan strings`):
`CaveDoor.OnTriggerExit`, `CaveTriggers`, `playerEnterCaveAction`,
`PlayerRespawnMP.Respawn`, `DebugConsole.GotoCave`, `LocalPlayer.GotoCave`.

**The game's own teleport**, `LocalPlayer.Goto(Vector3)` (instance method;
`LocalPlayer` is a component, no static instance): a target where
`Terrain.activeTerrain.SampleHeight(pos) - pos.y > (IsInCaves ? 3 : 6)` is in
a cave → `GotoCave(inCave)`, which sends `InACave` / `NotInACave` only when the
state changes → velocity zeroed → position set. The plugin's practice
teleport does the same (`GameBridge.SyncCaveState`), before moving.

`StreamCaveIn.LoadIn` additively loads `CaveProps_Streaming`; nothing in IL
calls it.

### Rope climb entrances (IL + bridge, v0.24.104-105)

A rope entrance (cave 4: `Caves/CaveRopeClimbDowns/Cave4Rope`, top trigger
`ropeTriggerTop` = `activateClimbTop`, hole `Caves/Cave4ClimbEntrance_Altexit/CaveHole`
with `CaveTriggers` + `caveEntranceManager`, tag `CaveDoor`). *Take* at the
top: `activateClimbTop.Update` sends `enterClimbRopeTop(<its transform>)` to
`LocalPlayer.SpecialActions` (`TheForest.Player.Actions.PlayerClimbRopeAction`):
FSM `climbBool`, `lockGravity`, the body **kinematic, no gravity**,
`_currentRopeRoot` = the rope's parent, the yaw set to the rope's (7.72 for
cave 4 - how a capture on the rope was recognised). State: `LocalPlayer.AnimControl.onRope`.
The whole exit is `playerAnimatorControl.exitClimbMode()` (body back to
physics, FSM bools, `toExitClimb`, then `SendMessage("resetClimbRope")`);
`PlayerClimbRopeAction.resetClimbRope` alone leaves `onRope` set. Going down
the rope crosses the hole's `CaveTriggers`, which sends `InACave` - the cave
state comes from the game, not from a teleport.

**Not in the save.** On the rope the body sits inside the rock around the
hole; a restore after leaving the rope put a free body there and physics
threw it out at ~48 m/s upwards (maks's "shoots me up", cave 4); from inside
the cave it hung, then fell 43 m/s. A plain `tp` to the spot shoves the
player ~3 m sideways out of the rock. A restore made **while on the rope**
keeps the player on it at the captured spot. A `MoveTo` does not end a climb
(`onRope` stayed set after `tp`). The plugin: `Game/RopeClimb` (`rope`
header; before a Quick load, leave the current climb and enter the captured
rope; after a Full load, after the hold - its per-frame pin undid a climb
entered before it; Go / `tp` leave a climb).

### Rides: zipline, sled, hang glider, cliff climb (IL + bridge, v0.24.201-202)

All four built in Slot 1 (Creative) over the bridge and ridden by calls
(no input). None is in the save; the plugin: `Game/RideModes` (`ride`
header, `Data/RideState`).

- **Building over the bridge** (Creative): `call
  static:TheForest.Utils.LocalPlayer Create.CreateBuilding <BuildingTypes>`
  (`Zipline`, `LogSled`, `HangGlider`, ...), `Create.PlaceGhost false`, then
  `call <ghost> <Architect>._craftStructure.Build` (or `type
  Craft_Structure` -> `Craft_Structure.Build`) builds it at once. A zipline
  ghost (`ZiplineArchitect`) places its gates by Fire1 - instead `set
  <ghost> ZiplineArchitect._gate1.transform.parent null` where gate 1
  goes (the far end), walk / `tp` to the start, the same for `_gate2`,
  then PlaceGhost + Build. The rider grabs at gate 2's `EnterTrigger`
  (`activateZipLine`) and slides toward gate 1; up to 470 m.
- **Zipline** (`playerZipLineAction` on SpecialActions): `activateZipLine.
  GrabZiplineUpdate` sends `EnterZipLine(<trigger transform>)` on Take.
  It sets `_onZipLine`, stows the weapon, puts the body at the trigger's
  box centre and starts `StickToZipLine`: velocity held at zero until the
  animator's layer-0 state is `_zipIdleHash` (`idleToZip` past 0.39), then
  once `_fixPlayerPosition`: body to `_onRopeAttachPos`; every
  FixedUpdate the body is projected onto the trigger's local z (x 0, y
  -2.5), pushed along its forward (10, Acceleration), capped at 50 m/s.
  It lets go after 1.3 s at low speed, near the ground, or on Take /
  Jump. `ExitZipLine` over 10 m/s starts `PreserveExitVelocity` (a push
  for 1 s, `FpCharacter._doingExitVelocity`). Line paths repeat
  (`Ex_ZiplineBuilt(Clone)/EnterTrigger` for every line): find a line by
  its trigger's position.
- **Sled** (`PlayerPushSledAction`): `activateSledPush.enableSled`
  (needs a Rigidbody on the sled's root) sends `enterPushSled(trigger)`;
  1 s later `connectRigidBody` **destroys the sled's Rigidbody and
  parents its root under the player** (`lookAtTerrain`, local (-1.32,
  -1.3, 3.155)). `exitPushSled` unparents it and adds a Rigidbody (mass
  110, layer 28). A save while pushing therefore holds the local offset:
  a restore put the sled at about (-1.3, -1.3, 3.2) in the world with no
  Rigidbody (bridge, v0.24.200; fixed v0.24.201).
- **Hang glider** (`PlayerHangGliderAction`, disabled until held): a
  built glider's `activateHangGlider.SendPickupGlider` -> `pickupGlider`
  (`holdingGlider`, weapon stowed, the world copy destroyed). Flying
  starts by itself after 0.9 s in the air (`FlyWithGlider`,
  `flyingGlider`, `wasFlying`) and stops on landing. `DropGlider`
  (Drop / AltFire, a rope, raft, sled, sitting, outside +-2200, ...)
  spawns a world glider at the hands. **The game's save drops it first**
  (held gliders are not saved); the capture did the same and so ended a
  flight - since v0.24.201 it drops on the serialization frame only and
  takes it back (the dropped copy is in the save: a same-frame
  instantiate registers in time).
- **Cliff climb** (`PlayerClimbCliffAction`; the climbing axe, item 138,
  carries `activateCliffClimb`): any layer-13 rock (`Collision` meshes,
  2264 of them) within 5 m of the camera, Take with the axe held ->
  `setEnterClimbPos(hit)` + `enterClimbCliff(axe)`, and
  `AnimControl.cliffEnterNormal` / `cliffEnterPos`. It also sets
  `onRope` (enterClimbMode), so `RopeClimb.Leave` ends it too.
  `playerAnimatorControl.updateCliffClimb` moves the body by root motion
  along five rays; **after 2 s it ends the climb itself** when the
  averaged normal is under 30 degrees from up or the forward ray misses
  (12 m) - the sloped rock at (465, 62.5, -60) near the Slot 1 spot ends
  every climb that way; a steep wall is needed for a long one (Cave 10's
  climb entrance is near, ~(480, 48, -80), not tried).

## Saving and loading

The game uses **UnitySerializer** (`LevelSerializer`, `LevelLoader`,
`UniqueIdentifier` / `PrefabIdentifier` / `EmptyObjectIdentifier`). A
`JSONLevelSerializer` twin exists; the game uses the binary one. All IL.

**A text dump of any moment (bridge, 2026-10-01).**
`JSONLevelSerializer.SerializeLevelToFile("<name>")` (static, ~0.2 s)
writes what a save would hold, as JSON, to
`%USERPROFILE%\AppData\LocalLow\SKS\TheForest\<name>` (a full path is
taken as relative to that folder). `StoredItems` lists every saved
component (`Type`, `Name` = its UniqueIdentifier, `Data` = its fields as
JSON; `None` for a null). It runs the components' `OnSerializing` hooks,
which **write live fields**: with the book open, `PlayerInventory`'s
`_equipmentSlotsIds` becomes `[0, 0]` (the hands stowed) - a dump or a
capture taken with the book open records nothing held. The Quick load
audit diffs two of these (docs/savestates.md).

**Where a save lives.** `PlayerPrefsFile.SetString(PlayerName + "__RESUME__",
base64, useSlots: true)` writes `SaveSlotUtils.GetLocalSlotPath()` +
`__RESUME__` — the path is built from `GameSetup.Mode` and `GameSetup.Slot`
(`Slots`: `Slot1`..`Slot5` only, in `TheForest.Commons.dll`). The previous
file is kept as `…prev`; with Steam Cloud on it is also uploaded
(`CoopSteamCloud.CloudSave`). `CanResume` = that file exists (or the cloud
copy).

**Saving** — `PlayerStats.OnSaveSlotSelectedRoutine` (from `JustSave` /
`OnSaveSlotSelected`), in order: drop the glider, close the inventory/pause
view, hide HUD and cams, **force-unload streamed content**
(`GreebleZonesManager.ForcedUnload(true)`, every
`Scene.SceneLoaders[i].ForcedUnload(true)` — `SceneUnloadInCave`),
`ResourcesHelper.UnloadUnusedAssets` + `GCCollect`, `FakeParent.ReParent`
on held item slots, `SaveSlotUtils.CreateThumbnail`, **`LevelSerializer
.Checkpoint()`**, `SaveGameDifficulty`, (MP: `SaveHostGameGUID`), then undo
the force-unload and the reparent. Refused in the overlook area.

`Checkpoint` → `SaveGame(<difficulty or "Creative">, false,
PerformSaveCheckPoint)` → `CreateSaveEntry(name, urgent)` (a `SaveEntry`:
`Name`, `When`, `Level` = loaded scene, `Data` = `SerializeLevel(urgent)`)
→ `SerializeLevelToBytes` → base64 → the slot file. `GC.Collect()` four
times on the way.

**Loading** — the title screen sets `LoadSave.ShouldLoad` and loads the game
scene. In it, `LoadSave.Awake`: `ShouldLoad && CanResume` →
`ShouldLoad = false`, `LevelSerializer.Resume()` → reads the slot file, sets
difficulty from `SaveEntry.Name` → `SaveEntry.Load()` →
`LoadSavedLevel(Data)`: a `DontDestroyOnLoad` `LevelLoader` holding the
data, then **`SceneManager.LoadSceneAsync(Data.Name)` — the same scene
again**. `LevelLoader.OnLevelWasLoaded` runs the restore; the second
scene's `LoadSave.Awake` finds `ShouldLoad` false and starts
`Activation(true)` ("Game Activation Sequence"). So **a save load loads
the game scene twice.**

**In-place restore exists:** `LevelSerializer.LoadNow(data,
dontDeleteExistingItems, showLoadingGUI, complete)` builds a `LevelLoader`
in the current scene and runs its `Load` coroutine — no scene load. The
game itself uses `SerializeLevel` / `LoadNow` for `OnlyInRangeManager`
(hide/show item streaming). With `DontDelete` false the loader:

- **only when `LevelData.rootObject` is set** (a partial "object tree"
  save), destroys every live `UniqueIdentifier` whose id is not in the
  save's `StoredObjectNames` (unless `LevelLoader.OnDestroyObject` vetoes —
  nothing in the game subscribes). **A full-level save has no
  `rootObject`, so nothing is deleted** — walls built after a capture
  survived an in-place restore (author's test, v0.20.0: identifiers
  172 -> 172). The plugin does this delete itself;
- recreates stored objects missing from the scene by `ClassId` from
  `LevelSerializer.AllPrefabs` (`Instantiate`), finds the rest by
  `UniqueIdentifier.GetByName` ("Could not find …" if a scene object is
  gone);
- restores components, strips components not in the save, sends
  `OnDeserialized` to each object, and sets `IsDeserializing` around it.
  `Resources.UnloadUnusedAssets` + `GC.Collect` run only when the load's
  time scale argument is 0.

**World pickups are not in the save.** The keycard
(`C6_Props/C6_secretRoom02/Keycard`) has no `UniqueIdentifier` on itself,
its parents or its `_destroyTarget` (logged in game). Taking a pickup runs
`PickUp.ClearOut(fakeDrop)`: fake drop / multiplayer destroy aside,
`_disableInsteadOfDestroy` → `Used = true`, `GrabExit`,
`target.SetActive(false)`; otherwise unparent, `TryPool()` or
`Destroy(_destroyTarget)` (never for `_infinite`). So a taken pickup is
gone until a scene load re-creates it — which is why every menu load
respawns pickups, and why `LoadNow` cannot.

**Streaming comes back by itself.** `ForcedUnload(bool)` on
`GreebleZonesManager` and `SceneUnloadInCave` only sets `_forcedUnload`;
`SceneUnloadInCave.Awake` registers `CheckInCave` with
`WorkScheduler.RegisterGlobal`, which re-evaluates it (in caves or forced →
`Unload()`, else `Load()`). Despite its name, `SceneUnloadInCave` unloads
**surface** scenes while you are in a cave.

**In game (author, v0.20.0, Creative, capture 252 KB in 312 ms):**
restore in place took 130 ms and put the inventory back, but left new
walls and did not bring taken pickups back; restore with load (4.7 s,
"notably very fast") put everything back. Reloading the slot save in place
teleported to the save's spot and reset the inventory; loading the slot
without the menu (5.2 s) put everything back. Held items survive an
in-place inventory restore.

**v0.20.1 in game (author):** in place (122 ms) deleted exactly the 5 new
objects (`Ghost_Ex_WallChunk(Clone)` x2 and their `Trigger`s,
`Ex_WallChunkBuilt(Clone)`), put back 3 kept pickups, emptied the hands and
the inventory - but left the HUD's "GATHER LOGS 0/4". Restore with load:
5.0 s to `FinishGameLoad`; a menu load of the same save, click to in game
by stopwatch, 6.95-7.45 s.

**Build missions** (that HUD line): `BuildMission` keeps a static
`ActiveMissions` tally per item. `Craft_Structure.Initialize` /
`SwapToNextGhost` / `AddIngrendient_Actual` add to it through static
`AddNeededToBuildMission(itemId, amount, isCancelling)`; cancelling a ghost
runs `SpawnBackIngredients`, which for each ingredient calls
`AddNeededToBuildMission(required._itemID, -(required._amount -
present._amount), true)` and then spawns the committed items back as
pickups. Destroying a ghost directly skips it, so the plugin makes the same
call (without the spawn) before deleting one. The tally is floored at 0
(`Mathf.Max`), so it can drift below the real sum and never recover;
`BuildMission.ActiveMissions` is static and a Full load gives exactly the
sum over the blueprints' `Initialize` (bridge, v0.24.203).

**A blueprint across a Quick load** (IL + bridge, v0.24.203):
`Craft_Structure.OnDeserialized` sets `enabled = false` (normal - it is
enabled while grabbed) and calls `Initialize`, which returns at once when
`_initialized` - so a blueprint kept through LoadNow gets its saved
`_presentIngredients` but neither its pieces redrawn nor its HUD share
changed. `BuildIngredients.SetBuilt(n)` only switches pieces ON (layer
21, the built material); there is no reverse. A blueprint LoadNow creates
from its prefab is set up properly - hence `Game/BlueprintKeeper` deletes a
changed one first. A blueprint lives on `Ghost_<X>(Clone)` (a
`PrefabIdentifier`) with the `Craft_Structure` on its `Trigger` child (a
`StoreInformation` of its own). The crafting cog (`CraftingCog`,
`_ingredients`) is not in the save; closing the inventory returns its
items (`IngredientCleanUp`), and the game's save routine never runs with
the inventory open (it saves from the pause menu and closes that first).

**A savestate from another save duplicates the player.** Restoring in
place a Hard save's state inside a Creative game produced a second player
beside the first, mirroring input, with its own inventory — Tab closed one
inventory and opened the other (author, v0.22.0). The player's
`UniqueIdentifier` id differs between saves (a GUID, e.g. `9164e836-…`,
logged in v0.22.1), so `LoadNow` does not find the saved player and
instantiates it from its prefab, while the live one is never deleted.
v0.22.1 refused such restores; **v0.22.2 adopts the saved player
instead**: before `LoadNow`, every identifier under the player that the
save lacks gets the id of the saved object with the same
`GameObjectName`, `ClassId` (prefabs) and `ParentName`, shallowest first,
on a unique match only (`SavestateBridge.AdoptPlayer`). The `Id` setter
re-registers the object with `SaveGameManager.SetId`.

`LevelSerializer.StoredItem` (IL, `SerializeLevel` lambda): `Name` = the
object's `UniqueIdentifier.Id`, `GameObjectName` = its name, `ParentName`
= the **direct parent's** identifier id (none when the parent has no
identifier), `ClassId` = the `PrefabIdentifier`'s class, prefabs only.

**Not only the player has per-game ids.** The inventory's item views
(`Spear_Upgraded_Inv`, `CraftedBomb1`...`5`, ...) are identifiers
**outside** the `player` hierarchy (the delete step, which skips the
player, deleted 140 of them in a Normal game restoring a Hard state), and
the save rebuilt its own set: a second inventory with the saved items
(author, v0.22.2). So once the player is foreign, the plugin adopts ids for
every live identifier the save lacks, not just the player's (v0.22.3).

Creative is chosen before the game loads and is not in the save data:
`LoadSave.Activation` instantiates `Prefabs.GameModePrefabs[GameSetup.Game]`
(`GameMode_Creative` for Creative), and the menu sets `Game` from the
slot's `difficulty` pref (`Resume`; `SaveSlotUtils.SaveGameDifficulty`
writes "Creative" or the difficulty on every save). So a Full load after
`GameSetup.SetGameType` comes up in that mode; leaving Creative, its
`OnDestroy` / `RestoreSettings` puts the three cheats back (v0.24.211,
bridge: both ways clean).

**Loading from the title screen needs the menu's loader** (v0.24.211-212).
`LoadSavedLevel` called at the title screen loads the game scene and hangs
on LOADING: `Activation` waits for `LocalPlayer.Rigidbody` and the player
is never built. The menu's loader (`TitleSceneMain/Loading`, `LoadAsync`;
`TitleScreen.OnSlotSelection` activates it) first instantiates Resources
`PreloadingPrefabs` and calls `LevelSerializer.InitPrefabList` - the
prefabs a save's objects are made from - then `Resume()` when `CanResume`
(the slot has a `__RESUME__` key) and `IsSavedGame` (Init Continue). In
game they already exist. `Game/TitleLoad` presses the title screen's own
`OnSinglePlayer` + `OnSlotSelection(slot)` and prefixes `Resume` /
`CanResume` while a load is pending (11 s to in game). The load uses
`GameSetup.Slot` (0 on a fresh launch -> Slot 1); once in game it is set
back to 0 ("none", v0.24.215). **The in-game save always opens the slot
picker** (`PlayerStats.JustSave` -> `HudGui.SaveSlotSelectionScreen`,
single player) and saves to the slot picked (`OnSlotConfirmed` ->
`SetSlot`); `OnSlotSelection` skips its "overwrite?" panel for the
current slot (same slot + `SaveUserId`, Init not New). `SetSlot` clamps
to 1-5 - 0 only through the property's setter (bridge: with 0, Slot 1
asks).

**Weapon-upgrade receivers are never deleted.** A cross-save restore in
the author's v0.22.5 log adopted 107 ids and deleted the 51 it could not
match, mostly `ToothReceiver`, `FeatherReceiver` and `glassReceiver`. These
are `TheForest.Items.Craft.UpgradeViewReceiver`: scene objects on the
inventory's weapon views that keep each weapon's implanted upgrades
(`_currentUpgrades`, `UpgradeViewData` = item id + local position and
rotation). `UpgradeCog.NextIngredient` looks through `_receivers` for one
that accepts the ingredient ("No upgrade receiver for ..."), so with them
gone the upgrade cog has nowhere to implant until a real load.
`PlayerInventory.OnDeserialized` calls each receiver's `OnDeserialized`,
which **destroys its own GameObject when it carries an
`EmptyObjectIdentifier`**. That is the game cleaning up a stand-in the
loader built for a saved object it could not find. So a receiver is never
meant to be removed by a load. The plugin exempts them from its delete
step (v0.22.7): unmatched receivers keep this game's upgrades, and the
save's copies come back as stand-ins that destroy themselves. Why they did
not adopt is still open. The adoption line now names a few unmatched
non-player objects with their parent path and candidate count
(`other misses:`).

Killed enemies do **not** come back with an in-place restore (author,
v0.22.0). A load restore is the reference for what should.

### A save slot's data, and restoring it in place (bridge + files, 2026-10-04)

**The file.** `__RESUME__` is the base64 of a UnitySerializer `SaveEntry`
(binary, starts `SerV10`); its `Data` string is the level data in the
same form a savestate's `data` line holds: `NOCOMPRESSION` + base64 of the
binary `LevelData`. So the slot's save can be fed to `LoadNow` like a
`.fosave` - `SavestateBridge.ReadSlotData` reads it the way `Resume` does
(`PlayerPrefsFile.GetString(PlayerName + "__RESUME__", "", true)`, then
`UnitySerializer.Deserialize<SaveEntry>`). Slot 1 (the lab) and Slot 2 (a
Normal game on the surface) both restored in place from it.

**Where the save's player was, without deserializing.** Each component's
data is UnitySerializer binary: the type's name (length byte), an int32
field count, the field names, then per field `<index uint16> FF FF
<value>` - a bool is `Y` / `N`. `TheForest.Player.ActiveAreaInfo` has 5
fields (`_activeAreaHash` long, `_isInEndgame`, `_isInCaves`,
`_currentCave` int, `IsLeavingCaves`); its two bools agreed with the
`cave` / `areas` header of all 28 savestates and with Slot 1's live
values (`Data/SlotSaveFlags`). `_activeAreaHash` was `long.MinValue`
(no area) everywhere except the two Megan-room captures.

**The endgame across an in-place restore.** `LoadNow` puts
`ActiveAreaInfo._isInEndgame` back but not `LocalPlayer.IsInEndgame`, and
loads no scenes: after a `tp` out of the lab (which unloads
`endgame_streaming` and sends ExitEndgame), Slot 1's save restored in
place put the player at the lab's spot with the lab unloaded and
`IsInEndgame` false. Restored with the lab loaded and the player in it,
everything matched a load. Hence the death reload's rule
(`DeathPlan.InPlaceRefusal`): in place only when the save and the player
are on the same side of the vault door, with the lab loaded when inside.

**The mode is not in the save.** A bridge-driven title-screen load of
Slot 2 (`OnLoad` + `OnSlotSelection`, no `OnSinglePlayer`) left the game
Creative, though the slot is Normal; `Resume` and an in-place restore both
keep the running game's mode, so a death reload in place cannot differ
from the game's own reload there.

**Timings (2026-10-04, v0.24.236 + the bridge, 7800X3D).** Reload save on
death with the game's load (`Resume`, no menu), death to
`Scene.FinishGameLoad`: Slot 1 (lab) ~6-10.5 s, Slot 2 (surface, Normal)
~10 s. The same save in place (`restore` of a `.fosave` holding the slot's
data): 0.68-0.92 s in the lab (0.45 s of it putting the held lighter
away), 0.12-0.17 s on the surface, LoadNow itself 0.14-0.19 s. Health came
back as saved (28 in Slot 2, as the load gave - with GodMode on the game
holds it at 100); the families restarted (0 -> 14 active, 6 families).

---

## The load leak

Runner logs (2026-09-23): every reload of the game scene keeps ~100 MB of
Mono heap (20 load restores: 340 -> 2293 MB, 6.4 -> 15.2 s each); two loads
through the title screen gave most of it back (~600 MB). The author: it
predates the tool and hits every load, cave streaming included.

**`Scene.FinishGameLoad`** (`TheForest.Utils.Scene`, static bool) is the
load signal: cleared by `LoadSave.Awake` (game scene starting) and by
`ClearStaticVars.Awake` in the title scene, set when `LoadSave`'s
activation sequence ends. The plugin's `LoadWatcher` watches it.

**`ClearStaticVars.Awake`** (IL) - a component in both scenes, told apart
by its `MainScene` field:

| Always | Title scene only (`MainScene` false) |
|---|---|
| `BuildMission.ActiveMissions.Clear()`, `Clock.Day = 0`, `AssetBundleManager.Initialize()`, `Time.timeScale = 1` | `RainEffigy.RainAdd = 0`, `Scene.FinishGameLoad = false`, `LoadingProgress.Progress = 0`, **`InsideCheck.ClearStaticVars()`** (clears the static `_grid`), `SteamClientDSConfig.Clear()`, `CoopLobby.HostGuid = null`, `OverlayIconManager.Clear()`, `Cheats.SetAllowed(true)` |

So a same-scene reload (`LoadSavedLevel` / `Resume`, a quick-load, a load
restore) never clears `InsideCheck._grid`: a
`Dictionary<GridPosition, GridCell>` of wall chunks
(`AddWallChunk(start, end, height)` -> token, `RemoveWallChunk(token)`) and
`IRoof`s (`AddRoof` / `RemoveRoof`) - building pieces. A lead, not a
verdict: whether pieces unregister on scene unload is not checked, and the
magnitude is unknown. **The census cleared it** (below): the grid does not
grow.

**The census (author, v0.23.0, 21 load restores of a Creative save):** heap
+122 MB per load, flat (261 -> 2741 MB; 4.8 -> 14.8 s per load), while what
statics reach grew by ~90 objects a load, destroyed-but-referenced Unity
objects by ~8, and Unity objects stayed at ~443k. So the scene itself is
released and no walked static holds the old world: the root is something a
static walk cannot see - a live object's fields, or a **thread**. 21
in-place restores after that: heap +0, but 841 -> 1157 ms each (a bigger
heap makes every GC slower).

**The culprit: `AstarPath.OnDestroy`** (A* Pathfinding Project, IL):

```
if (!Application.isPlaying) return;
if (AstarPath.active != this) return;      // <- skips ALL of the below
BlockUntilPathQueueBlocked(); FlushWorkItemsInternal(false);
pathProcessor.queue.TerminateReceivers(); graphUpdates.DisableMultithreading();
pathProcessor.JoinThreads(); pathReturnQueue.ReturnPaths(false);
astarData.OnDestroy();                      // the graphs
OnAwakeSettings = OnGraphPreScan = ... = OnThreadSafeCallback = null; active = null;
```

A reload of the game scene over itself loads the new world before the old
one is destroyed; the new `AstarPath.Awake` -> `SetUpReferences` has already
set `active`, so the old instance returns at once: its path threads keep
running and its navigation graph stays alive, every load. Through the title
screen there is no new instance, so the cleanup runs - which is exactly why
a menu trip gave the memory back. `PathPool.pool` jumping to the census cap
(150k objects) on the first reload was the graph showing through pooled
paths. `GraphNode.Destroy` returns node indices through `AstarPath.active`,
so the old instance must be `active` while it cleans up.

The plugin's fix (`Game/PathfindingCleanup`, v0.23.1-0.23.2, removed in v0.23.3, switch
`Fixes.PathfindingCleanupOnReload`, on): a prefix makes the dying instance
`active` when it is not, and a finalizer restores `active` and every static
callback the cleanup nulled (the new world registered them already). Log:
`Pathfinding: the previous world's AstarPath was destroyed while the new
one was active - running the cleanup the game skips (n this session).`

**It did not act on a reload (author, v0.23.1, 2026-09-23).** 21 load
restores of a Creative save: `pathfinding cleanups 0` after every one, heap
262 -> 2746 MB (+122 a load, same as v0.23.0; 5.3 -> 14.0 s a load). So
on a same-scene reload the old `AstarPath` was `active` when its
`OnDestroy` ran (the game's own cleanup ran) or it never ran - **the
ordering theory is unconfirmed, and pathfinding is probably not the
leak.** The fix logged once, at quit: `OnApplicationQuit` (IL) calls
`OnDestroy()` then `pathProcessor.AbortThreads()`, which nulls `active`,
then Unity calls `OnDestroy` again - a false positive, ignored since
v0.23.2. v0.23.2 logs every `AstarPath` `Awake` / `OnDestroy` with which
instance was active, to settle the order.

**Settled (author, v0.23.2, 22 load restores):** every reload logs
`AstarPath #old destroyed, active: this one` and then `#new awake` - the old
world is destroyed **before** the new one wakes, and the game's own
pathfinding cleanup runs. The v0.23.1 fix was removed in v0.23.3.

**Threads (v0.23.2 census):** OS threads **+2 a load** (151 -> 189 over 21),
heap still +123 MB a load, while statics (~36 MB, +0.2 a load) and
DontDestroyOnLoad objects (+1 a load, tiny) stayed small. `ilscan refs
"System.Threading.Thread::.ctor"` gives the game's thread starters; two
leak per load:

- **`WorkScheduler`** (world task scheduler, one per scene). `OnEnable`
  starts `ThreadedUpdate`: `while (secondaryThreadState < 3) {
  mutex.WaitOne(); if (state == 1 && ...) ProcessArea(...); mutex.Reset(); }`.
  Only `LateUpdate` calls `mutex.Set()`. `OnDisable` sets state 2,
  `OnDestroy` sets 3 and `Clear()`s the batches (`WorkSchedulerBatch.Clear`:
  `tasks`, `tfTasks`, `tfTasksChanged` lists). The thread is parked in
  `WaitOne` and a destroyed object gets no more `LateUpdate`: it never
  wakes, and its stack keeps the old scheduler alive for good.
- **`FocusLostAudio`** (the "Focus Lost" FMOD snapshot). `OnEnable` starts a
  worker (`Monitor.Wait` on `commandQueue`), unparents itself and calls
  `DontDestroyOnLoad`; `OnDisable` issues `Shutdown`, which ends the
  worker. `OnLevelWasLoaded` destroys it only when `TitleScene` loads (and
  only a copy that has seen a game load). Every game scene has one, so
  every reload adds a DDOL copy and a thread; the census saw
  `[DDOL] FocusLostAudio` and `DontDestroyOnLoad: n objects` +1 a load.

Fix (`Game/LeakedThreads`, v0.23.3, switch `Fixes.StopLeakedThreadsOnLoad`,
on): a postfix on `WorkScheduler.OnDestroy` sets `mutex` once more (the
loop wakes, skips its work as state is not 1, and exits); a postfix on
`FocusLostAudio.OnEnable` destroys the older copies' GameObjects, as the
game does at the title. Logs `Threads: woke the destroyed WorkScheduler's
thread ...` and `Threads: removed 1 older FocusLostAudio copy ...`; the
census label says `leaked threads stopped: scheduler n (k still running),
focus audio n` - `still running` means a woken thread did not exit.
**Unconfirmed that the threads are what holds the ~120 MB**: after
`Clear()` the old scheduler's own fields look small, so a thread may pin
more through its stack (Mono's Boehm GC scans stacks conservatively) or
may not. Only the heap line after the fix will say.

**Threads fixed, heap not (author, v0.23.3, 21 load restores):** every load
logs both `Threads:` lines, OS threads stay flat (151 -> 146), DDOL objects
flat (16), no `still running` - and the heap still +122 MB a load
(262 -> 2726 MB, 4.7 -> 12.6 s). The threads were a side leak.

### The event bus

**IL, v0.23.4.** `TheForest.Tools.EventRegistry` has static
registries `System`, `Game`, `Player`, `Enemy`, `Animal`, `Endgame`,
`Achievements`; each holds `_eventSubscriptions`
(`IDictionary<object, EventSubscription>`), each subscription
`_callbacks` (`IList<SubscriberCallback>`) and `_publishingEventIndex`
(`Publish` walks the list backwards by it; -1 when idle). `Subscribe` adds
if not already contained. **`EventRegistry.Clear()` (empties every
registry's lists) is called only from `TitleScreen.Awake`** - so a title
trip frees the memory and a same-scene reload never does. Objects
unsubscribe their named handlers in `OnDestroy` (`GameStats`,
`AchievementsManager`, ...), but `GameStats.Awake` also subscribes
lambdas (`<Awake>m__0..6`, instance methods) that nothing removes. Every
reload leaves callbacks targeting destroyed objects; through their C#
fields they hold the old world's managed side. The census saw it as
`Achievements.Data` (`AchievementData.Registry` is one of these
registries) holding one dead `GameStats`, `AchievementsManager`,
`PlayerInventory`, `StoryCluesFolder` per load, and its depth limit (7)
hid the rest. (`Achievements.Reset`, called by `AccountInfo.Load` from
`SetSaveGame`, clears `AchievementData` - also a menu path.) Side effect in
the unpatched game: dead subscribers still run on every publish.

Fix (`Game/StaleSubscribers`, v0.23.4, switch
`Fixes.PruneDeadSubscribersOnLoad`, on): when `LoadWatcher` sees a load
finish (before the census), drop every registry callback whose target is
a destroyed Unity object or a compiler closure holding one, skipping a
subscription mid-publish; also dead listeners of the static
`TreeHealth.OnTreeCutDown` (+2 dead `TreeLodGrid` a load). Log:
`Events: removed n event-registry subscription(s) and m tree-cut
listener(s) left by destroyed objects (k this session).`

**It works (author, v0.23.4, 21 load restores; built, chopped a tree and
killed an animal every 5):** heap 262 -> 396 -> ... -> 890 MB over loads
1-6 (+120 a load, prune removing 3-4 a load), then after the author's
actions **779, 531, 604, 605, 606, 413, 487 ... 414 MB** - bounded at
~410-610 MB; loads 4.6-5.4 s all session (v0.23.3: 4.7 -> 12.6 s). OS
threads flat (143-147). `Achievements.Data`'s dead `GameStats` /
`AchievementsManager` / `PlayerInventory` dropped -11 at load 7, when the
prune removed 17.

Why loads 2-6 still grew (first theory - corrected below): v0.23.4 skipped any subscription whose
`_publishingEventIndex` was not -1. `Publish` (IL above) resets it to -1
only after its loop ends, so a subscriber that throws (a dead one, most
likely - Unity's own log is not written by this game, so unconfirmed)
leaves the index stuck and the skip kept that list's dead callbacks -
until the author's kill / build / chop published those events again,
reset the index, and the next prune dropped them. v0.23.5 prunes
regardless (it runs from a Tick, never inside a `Publish`), resets a stuck
index and logs `n event list(s) were stuck mid-publish (a subscriber
threw)`. Also: `TreeHealth.OnTreeCutDown` is a `UnityEvent<Vector3>`
(`TreeHealth/TreeCutDownEvent`), not a delegate - v0.23.4 removed 0 of its
+2 dead `TreeLodGrid` a load; v0.23.5 reads `UnityEventBase.m_Calls` ->
`InvokableCallList.m_RuntimeCalls` -> `InvokableCall\`1.Delegate` and calls
the protected `UnityEventBase.RemoveListener(object, MethodInfo)` (Unity
5.6 names, from `UnityEngine.dll`). And the plugin's own
`PickupKeeper.TakenList` (196 dead after 21 loads) is pruned on every load.

**Correction (v0.23.5 log + IL):** the "stuck" lists were not a throw.
`EventSubscription`'s constructor never sets `_publishingEventIndex`, so it
starts at **0** and is -1 only after the first `Publish` finishes; the
first load of v0.23.5 found 43 such lists, before any reload. v0.23.4's
skip therefore kept the dead callbacks of every event not yet published
since the load - until the author's kill / build / chop published them.
`Unsubscribe` adjusts the index only when it is above -1, so setting a
never-published list to -1 (the idle value) is safe.

**Fixed (author, v0.23.5, 20 load restores with nothing in between):**
heap 262 (first load into the save) -> 394 -> 475 MB, then **475 -> 483 MB
over the next 18 loads** (421 MB after *Memory census now*); every load
5.0-5.1 s; OS threads 145-151; `Events: removed 6-9 event-registry
subscription(s) and 2 tree-cut listener(s)` each reload; no destroyed
object left growing in the census. The +131 / +80 MB of the first two
reloads is a one-time warm-up (pools and caches that fill on the first
reloads over the same scene - `PathPool.pool` jumps 150k objects on the
first one), not a leak.

**The menu route (author, v0.23.7, 10 trips exit to title -> Continue):**
heap 262 -> 276 -> 277 -> 351 MB (one-time +74 at trip 3), then **351-352
MB for the other 7**; threads 145-150. It never had the big leak (the title
clears `EventRegistry`, and `FocusLostAudio.OnLevelWasLoaded` destroys the
copies there), but it shared two small ones, both now caught every trip:
the old `WorkScheduler`'s thread (`Threads: woke ...` once per trip - its
thread strands on any destroy) and 2 dead `TreeLodGrid` listeners on
`TreeHealth.OnTreeCutDown` (a UnityEvent the title does not clear:
`removed 0 event-registry subscription(s) and 2 tree-cut listener(s)`).
The plugin also logs 2 `FocusLostAudio` copies removed per trip (the title
scene's own, then the game scene's) - harmless either way.

Still growing by a little on both routes, left alone (objects, not MB):
`DepthBufferGrabCommand.m_data` (+1 destroyed `Camera`, +4 objects a load)
and `MecanimEventManager.globalLastStates` (+13 objects a load). Worth a
look only if a census ever shows them in `Grown in size`.

**In-place restores (no load) are a different path.** They never leaked
(v0.23.1: +12 MB over 20), but they allocate a lot, and every garbage
collection walks the whole heap - so on a heap bloated by earlier load
restores they slowed down (841 -> 1157 ms over 21 in v0.23.0). The leak fix
removes that cause. A separate step on a fresh heap (~150 ms for 12
restores, then ~330 ms from the 13th, v0.23.1) is unexplained - re-test on
v0.23.6+.

**The load leak, in one paragraph:** a reload of the game scene over
itself (quick-load, load restore) kept the whole previous world's managed
side alive, ~120 MB a load, because the game's `EventRegistry` is only
cleared by `TitleScreen.Awake` and objects leave lambda subscriptions
behind; two worker threads (`WorkScheduler`, `FocusLostAudio`) also leaked
per load. Fixed by `Game/StaleSubscribers` and `Game/LeakedThreads` in the
plugin (v0.23.3-0.23.5). Not pathfinding (v0.23.1's fix, removed).

The census itself costs ~0.6-1.0 s about 1.5 s after each load, growing
with the heap (`GC.GetTotalMemory(true)` is a full collection) - the hitch
the author noticed after loading.

What the census cannot see yet (v0.23.0-0.23.1): it counted **objects,
not bytes** (a 100 MB array is one node), stopped at every live Unity
object (a `DontDestroyOnLoad` object's fields were never walked), read
only `Assembly-CSharp(-firstpass)`, and had no thread count. v0.23.2 adds
an estimated size per root, `DontDestroyOnLoad` objects as roots, the
UnityScript / PlayMaker / `TheForest.Commons` statics, and the OS thread
count. Small real holders it did see growing every load:
`Achievements.Data` (+~55 objects, +4-5 destroyed), `TreeHealth.OnTreeCutDown`
(+12, +2 destroyed), `MecanimEventManager.globalLastStates` (+13),
`DepthBufferGrabCommand.m_data` (+4, +1 destroyed).

The in-place restores in the same session: heap +12 MB over 20, but the
restore time went from ~150 ms to ~330 ms partway through (restore 13) and
stayed there.

Every other `OnDestroy` in the game with a singleton guard (18 of them:
`Sunshine`, `InsideCheck`, `OverlayIconManager`, `VirtualCursor`, `Mood`,
`Prefabs`, `GrassModeManager`, ...) only does
`if (Instance == this) Instance = null` - nothing skipped that matters.

## Performance: garbage, allocations and loads (bridge + IL + mono.dll, 2026-09-26)

**The GC.** Mono's Boehm collector, non-generational, stop-the-world.
The pause follows the live heap: ~0.3 ms per MB (36 MB at the title,
7 ms; ~265-280 MB in game, 80-90 ms). It runs when enough has been
allocated since the last one, so **less garbage = fewer pauses**; the
pause length only drops with a smaller live heap. `mono.dll` exports no
`GC_*` tuning (no free-space divisor, no incremental mode).

**What the live heap is** (scene census, v0.24.89): ~820k objects reached
from `PathPool.pool` - really the A* navmesh (205k `TriangleMeshNode`,
each with a `PathNode`, a `GraphNode[]` and a `uint[]` of costs). The
game needs it; not a leak, nothing to free. Then PlayMaker FSMs, LOD
components' `Dictionary<..., LOD_Stats>`s, `MecanimEventManager`.

**Allocation profiling** (`Game/AllocationTracker`): `mono.dll` exports
the profiler API (`mono_profiler_install` prepends to a list - safe to
add one). Two traps, read from the machine code: (1) the allocators report
only while the static `profile_allocs` (RVA 0x262300 in this build) is
set, and `mono_class_get_allocation_ftn` clears it for good the first
time the JIT compiles an allocation with the event off - long before any
plugin loads; the tracker finds it from the `cmp [rip+X], 0` in
`mono_object_new_alloc_specific` / `mono_array_new_specific` and sets it.
(2) a plain `new T()` JIT-compiled before the event was on takes the fast
path and never reports - install at startup for full coverage.
Under the Game profiler's Harmony hooks, `foreach` enumerators of hooked
methods show up boxed (`List.Enumerator<...>`, `Dictionary.Enumerator<...>`)
- an artifact of the hooks, absent without them.

**Idle allocation** (Slot 1 surface, ~200 fps): ~420 KB/s, ~11k objects/s,
all on the main thread. Unity's IMGUI layout pass for every enabled
`OnGUI` behaviour with `useGUILayout` on (a `GUILayoutGroup` + list +
`RectOffset` each frame, even for an empty `OnGUI` - `VRSwitcher`'s is
just `ret`); `Ceto.ProjectedGrid.m_grids` (`Dictionary<MESH_RESOLUTION,
Grid>`, default comparer boxes the enum, ~21/frame); two `Vector3[4]` per
camera per frame in `TheForestAtmosphere.UpdateShaderParameters`. All
patched (`Game/PerfPatches`, v0.24.92-94): ~216 KB/s left. Not patched
(behaviour): `MaterialTween.Output.SendMessage` boxes a float for
`Component.SendMessage` each frame; Unity's own `Collision` /
`ContactPoint[]` per physics callback; strings (~1100/s, source not yet
found). During play it is ~2 MB/s (maks: a GC every ~5 s).

**Asset clean-ups.** `TheForest.Utils.ResourcesHelper.UnloadUnusedAssets`
(`Debug.Log` + `Resources.UnloadUnusedAssets`) walks every loaded object;
it does **not** run a managed GC here (`GC.CollectionCount` unchanged), so
the `GCCollect` many callers do after it is a real second pause. Callers:
`SceneUnloadInCave.DelayedCleanUp` and `GreebleZonesManager.DelayedCleanUp`
(both 0.1 s after entering a cave - two sweeps at once before v0.24.94),
`CaveOptimizer.CleanUp`, `SceneLoadTrigger.UnloadScene` (+ `GCCollect`),
`TriggerCutScene.CleanUp` / `ShowEnemies`, `animClipMemoryManager.Start ->
UnloadEndGameAnimation` (every load: 3 animation unloads + a sweep, ~1 s,
a 550-730 ms frame), `LoadAsync` / `PlayerStats.OnSaveSlotSelectedRoutine`.
**The endgame-animation sweep frees nothing** (IL + A/B, v0.24.109):
`UnloadEndGameAnimation` starts three `AnimationLoadManager.
UnloadAnimation(clip, refreshAssets: false)` coroutines, each of which
first waits on `Resources.LoadAsync("CutScene/<clip> Empty")` and only
then swaps the override controller's clip; the sweep is called in the
same frame, before any swap. Unity objects after a Full load with and
without it: 497431 / 497451 vs 497250 / 497458; time to "in game" 5.4 /
5.7 vs 5.0 / 5.2 s. The 750-900 ms frame at that point is the scene's
own start-up either way. Skipped by `SkipEndgameAnimSweepAtLoad` (on).

**`PostProcessingBehaviour.OnGUI` is game logic**, not a debug view: on
Repaint it calls `EnableScionEyeAdaption` / `CheckScionEyeAdaptation`
from the user's post-effects setting, then draws debug textures only when
a debug view is on. So its `OnGUI` must keep running; only its layout
pass (`useGUILayout`) is waste. Empty or draw-only `OnGUI`s are the safe
ones to switch off; `PerfPatches.UsesLayout` refuses any that call
`GUILayout` / `GUI.Window`.

**Reading `mono.dll`'s machine code** (how the profiler facts above were
found): `python -m pip install --target <scratchpad>/pylib capstone`,
`sys.path.insert(0, ...)`, parse the PE export table for a function's RVA
and disassemble from there (x64). Do not name the script `dis.py` - it
shadows the standard module `inspect` imports and capstone fails with a
circular import. Rip-relative `cmp [rip+X], 0` resolves a static's
address (instruction end + X).

**Entering a cave** loads all 16 cave prop scenes (`CaveProps_Streaming`,
`Cave_01`-`10`, `HC`, `Snow`, `Junk`, `IE`, `IW` - the caves connect) and
unloads `MainSceneGreebles` + `MainSceneWorldStorySpots`; leaving does the
reverse, with no sweep.

**The endgame** (`EndgameEntrance/LoadEndgame`, the only `SceneLoadTrigger`):
`StreamSceneRoutine` calls the **synchronous** `SceneManager.LoadScene
(name, Additive)`, yields one frame and carries on, then
`loadEndBossScene`. A load of it after a Full load froze 5.2 s in one
frame. Going async would let the player move during it (a gameplay change
in runs).
**Async measured** (bridge, 2026-09-26, the same unloaded state after
`ForceUnload`): the game's `ForceLoad` 5233 ms in one frame;
`LoadSceneAsync` no frame over 200 ms. In v0.24.99 our restores load it
async (`EndgameLoader.PatchStream`, a transpiler on the routine's
`LoadScene` + the yield after it; switch `EndgameAsyncForRestores`, on):
Full load of `phantom-a` 2.04 s / 332 frames / longest 100 ms (hold 6.0 ->
4.5 s); Quick load of `elevPre` with it unloaded 1.71 s / longest 18 ms
(switch off: a 5000 ms frame, in after 5.7 s). After it the trigger reads
as after the game's load (`_loadedSceneRoot` = `Sections`, action None,
loading HUD off), `endgame_animPrefabs` follows, the red elevator rides.
`backgroundLoadingPriority` is `BelowNormal` in this game (High while
ours runs).
**In a run** (live UnityEvent wiring - IL `refs` cannot see it): the
forward crossing's `_onCrossingForwards` starts `InstantLoad` (a `DoAfter`,
0.01 s) -> `EnterEndgame` event + **`LoadEndgame.ForceLoad`**, which does
nothing until `_canLoad` (set by the vault door's `onDoorOpen`); the
routine's `_onBeforeLoad` runs `SetPoolMasterCulling.Set` and the vault
door's **`DoEnvironmentAnimation`**, then 0.5 s, then the one-frame load;
`_onFinishedLoading` sends `EnterEndgame` again. So the freeze sits inside
the vault door sequence.
**Timed** (bridge, 2026-09-26, a first visit recreated: `ForceUnload`,
`SetCanLoad false`, tp into the box and out towards the door, tp to
`playerPos`, `IsInEndgame` set back - our tp cleared it before v0.24.112 -, door closed,
`sequence.BeginStage(0)`): onDoorOpen -> `PlayerInEndgameTester.
DoPositionningTest` (needs `IsInEndgame`, set by the crossing's
`EnterEndgame`) -> `DelayedLoad` **4.35 s** -> `ForceLoad` -> 0.5 s ->
the frame: 5078 ms, ~4.9 s after the press. The cutscene flag rises ~1 s
after the press and falls at 17.3 s. **`Time.maximumDeltaTime` is 9** in
this game, so the frozen frame counts as ~5 s of game time: with the load
async (`EndgameAsyncInRuns`, v0.24.107: 1.09 s, 293 frames, longest
12 ms, all inside the cutscene) the flag fell at 16.7 s. The freeze costs
no run time - only the picture.
**A crossing outside the door's cutscene** (maks: Go, door opened, the
endgame not in, ran back through the box) also loads it - `_canLoad` is
already set, so the forward crossing's `ForceLoad` goes through. Async
there pinned the player for the whole load; since v0.24.145 a load that
starts outside a cutscene is the game's own synchronous one (bridge:
`ForceUnload`, `SetCanLoad true`, tp into the box and out forwards ->
one 4610 ms frame, no hold).

**A Quick load reloads the streamed scenes** (bridge, 2026-09-26,
`elevPre` from a cave): `ForcedUnload(true)` on the greeble zones and
cave loaders before `LoadNow`, `(false)` after - as the game's own save,
and required: the capture is taken with them unloaded, and
`DeleteUnsaved` would otherwise delete their stored objects. Cost: three
hitches of 233-267 ms here (the cave prop scenes unloading and
activating again); maks's log shows two of ~520 ms per restart at the
red elevator. Not changed - it is the restore's correctness.

**A save load** (`Load timing:` lines): `LoadAsync` 'Resume' ~2 s (one
~1.8 s frame), the scene ~1 s frame, `LoadSave.Activation` ~2.4-2.8 s. The
game's `PerfTimerLogger` times these stages but logs to Unity's log, which
this build never keeps (`Debug.Log` output does not reach BepInEx at all).

**`LoadSave.Activation` step by step** (IL + `activation steps:` line,
v0.24.95; the iterator's `$PC` = where it resumes, names in
`LoadTiming.ActName`). Each early step is one load frame of 230-680 ms
(step 0, Astar on, the two activation lists, step 3). The
`WaitPointFiveSeconds` after the game-mode prefab is **not** hit in single
player (only for a Bolt client or a missing prefab; an earlier note here
said it was). The one fixed wait is `WaitPointSixSeconds`, after
`sceneTracker.waitForLoadSequence = true` and before the loop on
`doingGlobalNavUpdate` - and it runs **slow**: 0.56-1.35 s real, because
`WaitForSeconds` counts scaled time and each long load frame counts at most
`maximumDeltaTime`. What it covers: `gridObjectBlockerManager.NavCutRountine`
waits on `waitForLoadSequence`, then calls `doNavCut` on every registered
blocker, whose `StartCoroutine(doGlobalStructureBoundsNavRemove)` sets
`doingGlobalNavUpdate` at once (then waits 0.5 s itself and cuts the
graph). Spawns, animals, birds and `astarPreRuntimeSetup` also start on
the flag. **A/B (bridge, v0.24.95):** ending the wait after 3 frames once
the manager is idle - Activation 2.70 -> 1.35 s from the title screen,
2.41 -> 1.47 s on a Full load; player, time, animals, spawners, picture
the same, **but** a surface Full load ran a 339 ms / 69-frame nav update
inside the wait (blockers registering during it) that then runs after
the hand-over. So it is `SaveLoadNoFixedWait`, experimental, off.

**After a Full load of a state captured with the endgame loaded** (Slot 1
states taken on the surface after leaving the lab still read
`IsInEndgame` true and list `endgame_streaming`): the restore loads the
endgame itself - the 5.2 s one-frame freeze - while the player is held
("held ... 6.0 s until the captured scenes were loaded"). That, not the
hand-over, is the freeze seen after those Full loads (author, 2026-09-26).
Also `animClipMemoryManager.UnloadEndGameAnimation` (~1.1 s, a 760-870 ms
frame) runs inside every load.

**The heap across restores** (bridge, 2026-09-26, v0.24.108, fresh
launches, Slot 1; `call static:System.GC GetTotalMemory true` = live
bytes after a full collection, and the bridge's reply time for it = the
full-GC pause). **No lasting step:**
- Title load: 276-281 MB, pause 73-85 ms. 20 Quick loads of `phantom-a`:
  284 MB (+4), pause 74-80 ms.
- **Every Full load** (after 20 Quick loads or none) goes to ~410-420 MB,
  pause ~100-115 ms - and **drops back** to ~290-300 MB in two steps
  (e.g. 419 -> 375 -> 302 over two collections), once **30 s** after the
  load and once **68 s** after: the old world is held for a while, then
  released (the two steps look like finalization). No log line at the
  drop, nothing of ours waits that long (our after-load coroutines stop
  within 30 s), statics reach only ~33 MB (census) - the root is not
  known (a thread stack or a coroutine of the game's). Earlier readings
  ("+118 MB that stays", 437 -> 549 -> 556) were taken inside that
  window.
- **Collections come from volume, not headroom:** 218 MB of garbage
  (40 x `File.ReadAllBytes` of a 5.45 MB file) = 1 collection on a fresh
  heap, 2 on one grown by Full loads - so pre-growing the heap would not
  space GCs out. In play at ~2 MB/s that is one every ~50 s.
- **Each Quick load forces ~1 collection** (the streamed scenes' asset
  clean-ups). maks's 3-7 GCs per 30 s (report 2026-09-26, i7-9700KF /
  RTX 2070 Super, 150-170 fps) were his restart loop (~every 15 s), at
  120-150 ms each on his machine.
So maks's longer pauses after many restores are not explained by a heap
that stays grown; the elevator-physics lead (Next up 5) loses its best
candidate.

## Frame time: where the main thread goes (bridge + IL, 2026-09-26, v0.24.114-116)

**The instrument.** The game profiler times the game's scripts only
(1.47 ms/frame on the surface, of which ~0.5 ms is its own hooks: ~0.3 us
x 1575 calls). `Game/FrameTimer` cuts every frame at marks seen without
patches (our FixedUpdate / Update / LateUpdate, `Camera.onPreCull` /
`onPostRender` for every camera, `WaitForEndOfFrame`, the frame start from
`Time.unscaledTime`) and writes `Frame (30 s):` + `cameras:` beside the
Perf line; `FrameTimer.Snapshot` (bridge) reads a short window. The game
subscribes nothing to `Camera.onPreCull` / `onPostRender` itself.

**The author's machine is CPU-bound** (7800X3D / 4080 SUPER, D3D11,
graphics jobs / MT rendering on, deferred, quality level 0 "High": 4
cascades, shadow distance 374, vsync off; windowed 1366x768): 203 fps
at 1366x768, 202 at 640x360, 191 at 2560x1440. "Waiting" (end of frame
-> next frame's start: present, the render thread, the GPU) is 0.01 ms.
Slot 1, standing still:
- **Surface** (428, 78, -4), endgame loaded (Slot 1 starts at the vault
  door): 5.0 ms = start to Update 0.28 (a physics step 0.7 ms, in 30% of
  frames) + Update to LateUpdate 0.77 + to rendering 0.36 + cameras and
  OnGUI 3.6.
- **Cave** (Cave 6 spot): 4.06 ms, cameras 2.67.
- **Cameras** (surface, ms/frame, "+after" = its image effects):
  MainCamNew 1.19 +0.09, the endgame's `redcircles/Camera` 0.30 +0.10,
  ParticleCam 0.26 +0.11 (rendered from `OffScreenParticleCamera.
  OnRenderImage`), ActionIconCamera 0.26, `__Far_Shadow Camera` 0.27
  (rendered in `FarShadowCascade.OnPreCull`), terrain
  AFSGrassDisplacementCamera 0.27, Camera_HUD 0.27, `Ceto Reflection
  Camera` 0.20 (when the ocean passes culling), AFSGrassDisplacementCameraTest
  0.18. Six to eight cameras besides the main one = ~2.3 ms of 5.0.

**Each camera render costs ~0.2 ms whatever it draws - Unity's own
overhead, not the world** (`RenderProbe.TimeRender`, v0.24.124: a bare
64x64 forward camera rendered 200 times back to back). Surface, mask 0:
0.22 ms; mask HUD: 0.23; with all 4209 enabled renderers switched off:
0.17; with the lights off / the terrain off: unchanged; **on the title
screen (92 renderers): 0.21**. (Enabled renderers: ~4-5k on the surface
and in Cave 6 alike; the "20.5k" above counted disabled components.)
Camera_HUD by hand 0.29, ActionIconCamera 0.27. Only few game scripts
have `OnRenderObject` (Ceto's notifier, MeshBaker, a wire-frame debug
renderer). So the one lever is fewer camera renders; trimming the world
does nothing for the small cameras. On the runners' machines the three
small cameras cost 0.10-0.46 each - the overhead depends on the machine
/ driver, not only the CPU's speed.

**Two cameras drew for nothing** (`Game/CameraTrim`, v0.24.116, both on,
measured together: 5.11 -> 4.47-4.54 ms/frame on the surface, ~12%):
- `_TerrainEtc_/AFSGrassDisplacementCamera` (scene object) renders layer
  5 into a texture of its own from a fixed spot, 0 renderers in view. The
  grass reads the global `_AfsGrassDisplacementTex`, which
  `AfsGrassDisplacementController.Update` sets every frame to its own
  runtime camera's texture (`AFSGrassDisplacementCameraTest`, created in
  `CreateComponents`, follows the main camera at +50 m). No loaded
  material has either texture in any property (`RenderProbe.TextureUsers`,
  16 property names over 17.5k materials). A leftover.
- `Sections/ControlRoom/redcircles/Camera` renders a diorama (layer 27,
  post-processing) into 'EndPLane', shown only by
  `endPlaneCrashPrefab1/consoleDisplay` (material `EndPlaneMain`, its
  MeshRenderer disabled until the end-crash ending; nothing in the IL
  refers to it - probably an animation). `redcircles` has a
  `LOD_GroupToggle` listing the camera for a 100 m switch, but
  `LodLevel.RefreshComponents` handles Transform / MonoBehaviour / Light /
  Collider / ParticleEmitter / ParticleSystem / Rigidbody - **not
  Camera** - so it renders every frame while the endgame is loaded. Now
  rendered from the screen's `OnWillRenderObject` (the mirror pattern):
  screenshots with the patch on / off / on showed the same screen.

**Switching cameras mid-frame** (bridge, v0.24.117-118, `RenderProbe.
TestLateEnable` / `TestLateDisable`): a camera **enabled** in an earlier
camera's `onPreCull` is not rendered that frame (the frame's camera list
is built up front). One **disabled** there is skipped - but doing that
to ActionIconCamera (depth 95, the last camera drawing to the screen)
**froze the picture**: the game ran at 230 fps, audio played, the image
only moved when the author tabbed out; the Frame line and bridge
screenshots looked normal. v0.24.119 shipped it as a patch, v0.24.120
withdrew it ten minutes later. Never skip a screen camera mid-frame.

**Candidates left, and why not yet:**
- **ActionIconCamera** (0.26 ms, depth 95, perspective, far 40): draws
  NGUI widgets under it (action icons, plane icon, ranged hit target,
  translation overlay). NGUI turns its `UIDrawCall`s on / off in
  `UIPanel.LateUpdate`, so "nothing to draw" is known only after every
  LateUpdate - and a mid-frame skip freezes the screen (above). Left: keep
  it disabled and `Render()` it by hand from Camera_HUD's post-render
  when an active draw call is in view (untested; check the picture with
  the author's eyes, not screenshots).
- **ParticleCam** (0.37 ms): `factor` Full copies the frame to a
  temporary, renders layer 1 (TransparentFX, not in the main camera's
  mask) over it, copies back. Layer 1 holds the held **lighter's flame**,
  pickups' **sheen billboards** and cave waterfall particles
  (`LayerContents 1`: 18 renderers on the surface, 2 in view) - so it
  nearly always has something to draw; not worth a skip. Also: it sets
  the Sun's and Moon's shadows to None and back to **Soft** every frame,
  whatever they were.
- **Far shadow** (0.29 ms): re-rendered every main-camera OnPreCull
  (`refresh` 1) along the sun's direction, which moves every frame - a
  skip would change the picture.
- Camera_HUD always has a draw call in view; Ceto's reflection is the
  ocean's own visibility logic.
- Scripts: ~1 ms real; the heaviest are camera work above.
  `PhysicsSfx.Update` runs 781 times a frame (0.09 ms).

**Sunshine** (`TimeAndWeather/R10/Sunshine`, the game's sun-shadow
system; bridge + IL, v0.24.125): 1 cascade, 256x256, 60 m, occluder
layers 11 + 17 (mask 133120), `SunLight` = the Sun or the Moon. It is
the sun's shadows - `SunshineCamera.OnPreCull` renders the cascade,
then sets the sun light's Unity shadows to None while it runs - and the
light-shaft occlusion. Always on: `ImageEffectOptimizer.Update` enables
it every frame; the **Sunshine occlusion** option only empties its
occluder mask on the surface (`IsInCaves ? mask : 0` when Off), so the
camera still renders (no saving measured); **Volumetrics type** is read
by the options menu only. It renders in caves too (0.55 ms). Its own
`UpdateInterval` (`EveryFrame` / `AfterXFrames` with
`UpdateIntervalFrames` / `AfterXMovement`) is set by nothing in the
game (only the constructor); `AfterXFrames` 2 re-renders on even frames
(`SunshineCamera.NeedsRefresh`): 0.66 -> 0.32 ms a frame. Shipped as
the Experimental switch `SunShadowsEveryOtherFrame` (v0.24.125, off).

**Far shadow** (`FarShadowCascade.SetShadowCamera`, from MainCamNew's
OnPreCull): renders `__Far_Shadow Camera` every call with
`QualitySettings.shadowDistance` set to 0 around it; the `refresh` /
`c_refresh` fields are not used. The option Far shadows Off removes it
(-0.33 ms). None of the runners measured has it on.

**Graphics options, measured live** (bridge, v0.24.123, surface (428,
78, -4), cannibals near, author's machine: main-thread bound; each
option set on `TheForestQualitySettings.UserSettings`, 6 s windows vs 6 s
before - most are read every frame by `ImageEffectOptimizer.Update`,
`PlayerPreferences.Update`, `LOD_Manager` etc.): **Far shadows Off
-0.35 ms**; **Ocean Flat -0.2 ms while the ocean is in view** (it drops
the `Ceto Reflection Camera`; Reflexion mode Off does not); **Unity
shadows off** (`QualitySettings.shadows`): -0.18 ms in a cave (the main
camera only). No measurable change (under ~0.1 ms, the noise): SSAO,
SSR, bloom, CA, film grain, anti-aliasing, volumetric clouds, Sunshine
occlusion, volumetrics type, grass distance / density, draw distance
(needs a LOD refresh to show - standing still), terrain / material
quality, light distance, cascade count, scatter resolution. Those are
GPU work: they matter only on a GPU-bound machine, and every runner
measured so far is CPU-bound. `PlayerPreferences.LowQualityPhysics`
sets `fixedDeltaTime` 1/30 instead of 1/60 - half the physics steps, a
gameplay change (movement tech). **Hidden**: `MenuOptions` still has the
widget field and the menu asset still names "Option - Low Quality
Physics" (`LOW_QUALITY_PHYSICS`), but the current options menu does not
show it (author); the game reads the saved pref at startup
(`PlayerPreferences.Load`) and has the console command `physics30Fps`.
`treeHitTrigger` and `RaftPush` read the flag. A save load resets the
step (`LevelLoader.Load`). Measured 5.26 -> 4.98 ms a frame (steps in
32% -> 15% of frames). Experimental switch `Physics30Hz` (v0.24.128),
**removed** in v0.24.210 (it changes physics noticeably - maks, author);
a config that had it on is put back to 60 Hz once, pref cleared.

**The grass-bending camera in caves** (`AFSGrassDisplacementCameraTest`,
AfsGrassDisplacementController's own): it draws bend trails for the
terrain grass and the "Touch" foliage shaders (`AFS/Foliage Shader
Deferred SingleSided Touch v4 Stipple VFACE`, ferns) and kept drawing in
caves; within 60 m in Cave 6 no renderer uses a Touch / grass shader
(`ShadersNear`, v0.24.126). From inside, cave mouths are open (the
black walls are for outside), so grass beyond a mouth can be in view.
The controller never sets the camera's `enabled`; its camera clears to
its background colour every render. Experimental switch
`GrassBendingOffInCaves` (v0.24.127).

**Runners' machines** (QA, 2026-09-26, v0.24.116, surface, `Frame`
lines): both **CPU-bound**, "waiting" ~0.1 ms, GPUs at 20-64 %.
- sxczurass: i5-9400F (6 cores / 6 threads), GTX 1650, 16 GB (87 % in
  use), recording video; ~110 fps, 8.8-9.6 ms = start to Update 1.1
  (a physics step 1.7-1.9 ms, in 53 % of frames) + Update to LateUpdate
  1.7 + to rendering 0.6 + cameras 5.3: **AFSGrassDisplacementCameraTest
  2.6-3.1**, MainCamNew 1.2-1.5, `Sunshine Cascade Camera 0` 0.7-0.8,
  Camera_HUD / ParticleCam / ActionIconCamera **0.10-0.13 each**.
- Cheesecake (connor): Ryzen 9 7845HX laptop, RTX 4070 Laptop (20-28 %)
  plus the Radeon iGPU (44-61 %, hybrid graphics); ~125 fps, 7.8-8.2 ms:
  cameras 5.6: grass camera 0.6 **+1.3 after**, MainCamNew 1.6, Sunshine
  0.8, the three small ones 0.40-0.46 each.
- Reading: the grass camera is the first camera of the frame (depth -1),
  and its 2.6-3.1 ms on the i5 while the HUD cameras cost 0.1 there
  looks like the main thread **waiting for the render thread** inside
  the first render, not the camera's own work - the "waiting" phase
  (end of frame -> start) does not catch it. Unproven. Test: add a fixed
  main-thread cost (e.g. a 1 ms spin a frame); if the frame grows by
  less than 1 ms, the main thread had slack and the render thread (D3D11
  submission: draw calls) is the limit.
- **sxczurass's Frame test** (v0.24.125, 2026-09-26, surface, quality
  'Ultra Low', DrawDistance UltraLow, 1680x1050): 9.8-10.1 ms/frame off,
  **9.6-9.8 with +1 ms of main-thread work** - the frame did not grow, so
  his main thread waits on rendering (render thread or GPU), inside the
  first camera (grass camera 2.5-3.3 ms vs ~0.15 here). Main-thread-only
  savings (scripts, physics) do not raise his fps; fewer camera renders /
  draw calls do. GPU vs render thread: not told yet (a lower resolution
  would). His title screen: a physics step of ~9 ms in 99% of frames (the
  author's too - the title scene's own FixedUpdate work).
- Both use **Sunshine** shadows (the game's own cascade system) - the
  author's quality level 0 does not (`Sunshine Cascade Camera 0` x0/f).
  Measure at their quality level before judging shadows.

## The grass-bending cameras and Unity's "current" camera (dump + IL + bridge, v0.24.138-140)

- `AfsSetupAndSkin` carries the one `AfsGrassDisplacementController`
  (A* is unrelated). `Awake` -> `CreateComponents` makes
  `AFSGrassDisplacementCameraTest` at runtime (depth = main - 1 = -1),
  unless `GameObject.Find` already finds one. `createDisplacementTexture`
  runs before the camera is made, so it sets the texture on whatever
  `DisplacementCamera` held then - the scene's
  `_TerrainEtc_/AFSGrassDisplacementCamera` (depth 0) keeps an unnamed
  runtime texture nothing reads (why `TerrainGrassCameraOff` exists).
- `Update` rebuilds the texture when `DisplacementCamera.targetTexture`
  or `DisplacementTexture` reads null (or `RenderTexSize` changed;
  nothing writes it, the scene has `_128`) and sets
  `camera.targetTexture` again. It happens around a load's activation.
- Unity 5.6 keeps the last camera drawn as its current camera until the
  next render (`Camera.current` in `Update` = last frame's last camera:
  `ActionIconCamera` in play, `CameraGhostTint` on the title screen).
  `Camera::SetTargetTextureBuffers` (TheForest.exe RVA 0x1cfb60): an
  enabled camera that is the current one and had a target, given a new
  target, writes it into the render loop's context
  (`GetRenderManager()+8`, then `+0xf8`) - null outside rendering: the
  access violation at `+0x1bd`. So setting `targetTexture` on the current
  camera outside rendering crashes; only order of cameras decides it.
- Symbols: `TheForest.exe` is Unity's `player_win_x64.pdb`, GUID+age
  `4A35955D96D04F0A89A4669DC0C913D11`, on symbolserver.unity3d.com
  (`scripts/symbolize-crash.py`, cached in
  `%LOCALAPPDATA%\ForestOverlay\symbols`).

## Pathfinding (A*) and the reload freeze (IL + bridge + stack walks, 2026-09-27)

- A* Pathfinding Project 3.8.4 (`AstarPath.Version`, branch
  `rvo_fix_Pro`), one `Astar` object; 6 path threads
  (`pathProcessor.queue.numReceivers`), a graph-update thread
  (`graphUpdates.graphUpdateThread`).
- `AstarPath.OnDestroy` (the old world going, any scene load):
  `BlockUntilPathQueueBlocked` (Block, then `Thread.Sleep(1)` until all
  receivers park), `FlushWorkItemsInternal(false)`, terminate receivers,
  `GraphUpdateProcessor.DisableMultithreading` (`Join(5000)`),
  `PathProcessor.JoinThreads` (`Join(50)` each, then Abort), return
  paths, destroy graphs. It waits for work in flight.
- A Quick load of the Megan-fight start state (`ruben-megan`) starts a
  graph update 2 s after the restore that runs 16-35 s
  (`graphUpdates.IsAnyGraphUpdateInProgress` true, one
  `graphUpdateQueue` item, `pathProcessor.queue.blocked` true - no
  paths meanwhile). A death in that window: the reload's one frame waits
  for it (26-51 s, main thread in a managed wait, one other thread busy
  in managed code; `scripts/sample-stacks.py --snapshot`). After it:
  1.6 s. Source not found yet; no `DynamicGridObstacle` /
  `GraphUpdateScene` in the world. `UpdateGraphs` callers in the game:
  `gridObjectBlocker` nav cuts, `navRemoveRoot` / `navRemoveReceiver`,
  `sceneTracker.doStructureBoundsNavRemove` /
  `doGlobalStructureBoundsNavRemove`, `stumpRemove`,
  `RecastTileUpdateHandler`.
- **Solved 2026-09-27 (v0.24.141-143, `Game/PathfindingWatch` lines).**
  The long update was ONE graph update of **1533 x 81 x 1407 m** from
  `sceneTracker.doStructureBoundsNavRemove`: a Quick load re-creates the
  save's structures; `gridObjectBlocker.Start` sees
  `Scene.FinishGameLoad` and registers without
  `doingOnGameStartCheck`, so each structure takes the one-at-a-time
  route, which merges every structure waiting at that moment
  (`currentNavStructures`) into one box - across the map for Ruben's 5
  fires / drying racks. A load sets `doingOnGameStartCheck`
  (`loadGameNavSetup`, written nowhere else) -> `doNavCut` takes
  `doGlobalStructureBoundsNavRemove`, which groups structures within
  100 m (`sqrMagnitude < 10000`), one update per group, then
  `FlushWorkItems` on the main thread. v0.24.142: during an in-place
  restore (+2 s) `Start` takes its load branch -> 5 small updates, 0.6 s.
- Second source: `setupNavRemoveRoot.OnDestroy` (added by `doNavCut` to
  structures, and on the plane wreck) -> `sceneTracker.startDummyNavRemove`
  adds the bounds to `dummyNavBounds`, waits 7 s, spawns
  `dummyRootNavRemove` -> `navRemoveRoot.doRootNavRemove` ->
  `startRemove` (0.5 s) -> one update over ALL of `dummyNavBounds`.
  **The game never clears `dummyNavBounds`** (only
  `dummyNavStructures`): every removal re-covers every earlier one. The
  old wreck our restore removes after a cross-save restore merged both
  wreck sites: 988 x 930 m, 16.2 s. v0.24.143 PerfPatches 15
  `NavRemovalOwnArea` clears the list when no batch is gathering.
  **And one batch is one box however far apart its removals are**
  (`Encapsulate` over the 7 s): Tom's logs (v0.24.173) show a 1536 x
  1406 m `navRemoveRoot.startRemove` update queued shortly before both
  sessions that ended in a native crash (a Quick load from another save
  deletes structures across the map). Two removals 780 m apart (clones
  of the wreck's `collision_hull`, moved, `gatherBounds`, destroyed):
  one 500 x 600 m update, 6.8 s on a 7800X3D. v0.24.177: switch 15 runs
  the batch itself (same 7 s, the game's `dummyRootNavRemove`) grouped
  by place (150 m) - the same test: 2 updates, 0.3 s, log `building
  removals - 3 at once recalculated in 2 places instead of one area of
  500 x 601 m`.
- Result (bridge, Slot 2 + `ruben-megan`): every graph update after a
  Quick load <= 0.6 s; a repeat Quick load 0.65 s (was 31.5 s); a death
  in the Megan fight (`Death (BossWake)`): `AstarPath ... destroyed in
  116 ms` (was 28584 ms), biggest hitch 1.6 s - the surface reload's.
- A Quick load does **not** re-create `AstarPath` (one `awake` line per
  launch). `graphUpdates.graphUpdateQueue` keeps the next GUO waiting
  while a batch runs on the thread; the running one is not readable -
  log the enqueue (bounds + caller) instead. A lone wreck cut
  (`call <collision_hull> gridObjectBlocker.doPlaneNavCut`) takes
  < 1.3 s; the recast graph's tiles are 45 m (60 cells x 0.75 m).
- Scene unloads by themselves are cheap: `UnloadSceneAsync` of
  `endgame_streaming`, `endgame_animPrefabs` and the six cave prop
  scenes cost no hitch; in the cave state the game streams the cave
  scenes straight back.

## Terrain, and the world from above (bridge + IL, 2026-09-27, v0.24.162-166)

- **Terrain:** one `MainTerrain`, 3500 x 250 x 3500 m at (-1750, 0,
  -1742.63), heightmap 2049^2, 8 splat layers at 512^2 (mud, moss, big
  rock, lake edge, sand, cliff, leaves, grass), shader `Nature/Terrain/CMU_3
  5_2 boosted`. Dumped by `Game/TerrainDump`; heights match
  `Terrain.SampleHeight`. Sea level: Ceto `Ocean.level` 41.5.
- **Plane crash sites:** `PlaneCrashLocations.finalPositions` (12 `HullRef`,
  static); the save's own is `PlaneCrashController.planePosition` /
  `planeRotation` (saved with the game; `Game/PlaneSite`).
- **Detail follows the player twice** (`Game/AerialCapture`): the LOD an
  object shows is measured from the static `PlayerCamLocation.PlayerLoc`
  (`LOD_Settings.GetLOD`, 2D for trees), written by a script on the main
  camera (paused by our freecam); whether it is refreshed at all follows
  the real player - with PlayerLoc alone, 500 m from the player, a tile
  stayed bare and the terrain drew its glossy far shading.
- **LOD ranges:** `LOD_Manager.Update` recomputes every frame: base ranges x
  `RangeMultiplierPerQuality(Small)[quality]` x an fps-based quality
  (`FpsQualityScaling`, target 30 fps - ranges shrink below 30 fps). At the
  author's settings: trees 15 / 115 / 212 m (then a billboard - invisible
  from straight above), bushes 25 / 132, small bushes 10 / 60, rocks 20 /
  77 / 176, small rocks 10 / 30 / 65, pickups 10 / 80.
- **Camera cull distances:** `CullDistanceManager.Update` re-writes the
  main camera's `layerCullDistances` every frame (spherical): Default 85,
  pickups 100, PropSmall 120, treeSmall (bushes, layer 12) 300, trees
  (layer 11) 487, Prop 450.
- **Shadows** reach ~200 m from the camera (`QualitySettings.shadowDistance`,
  re-set every frame by `TheForestAtmosphere.Update` and others);
  Sunshine's own cascade is 60 m / 256 px at the author's settings.
  `Sunshine.OvercastTexture` drifts cloud shadows over the ground.
- **Fog:** `TheForestAtmosphere.Visibility` (~1 km) unless
  `overrideVisibility`; a sun overhead (`TimeOfDay` near 0-45) makes the
  terrain's specular glare from above; `TimeOfDay` 320 = sun in the west,
  ~40 degrees up, no glare.
- **HUD:** NGUI cameras under `HudGui` (`Camera_HUD`, `ActionIconCamera`).
- **The ocean from above (2026-09-28, corrected 2026-10-01):** Ceto's
  ocean (`CetoTF/Ocean`, `Ceto.Ocean`, level 41.5) draws for the freecam
  turned orthographic and straight down, and switching its GameObject off
  hides it within a frame - **but not inside `Game/AerialCapture`**: there
  the sea never shows (a sandy floor with rippled light), on or off. Every
  setting the capture changes was put back one at a time over the bridge
  (fog, sun hold, post effects, LOD ranges, pixel error, HUD cameras,
  player / `PlayerLoc` position, god mode, overlay UI) - the ocean stayed
  away; cloud shadows (`Sunshine.OvercastTexture`) could not be set from
  the bridge and remain untested. Not `IsInClosedArea` (false), not
  `OceanQualitySettings` (quality only). So v0.24.170-177's "-dry" tiles
  were the same pictures; since v0.24.178 the bake draws the sea from the
  heights. `ImageEffectOptimizer.Update` switches `Scene.OceanCeto` /
  `OceanFlat` by the Ocean quality option and off in a closed area.
- **Weather** (`TheForest.World.WeatherSystem`, `Scene.WeatherSystem`,
  2026-10-01): rolls rain and clouds on its own; an overcast sky
  (`CloudOvercastCurrentValue` 1, `State` Raining) lit the ground ~1.6x
  darker from above than a clear one (0.1). `AllOff()` stops the rain;
  in `Idle` the overcast does not ease back - set `CloudOvercastCurrentValue`
  itself. `Game/AerialCapture` sets it clear and disables the component
  for a capture (v0.24.179).
  **Not in the save, and what holds the look** (decompiled source + bridge,
  2026-10-04): the class is `[DoNotSerializePublic]`; only `LastRainTime`
  is `[SerializeThis]`. A Quick load of a clear capture kept the live rain
  (`State` Raining, `CurrentType` Heavy, overcast 1, fog 300 m); a Full load
  builds it afresh (`Idle`, overcast 0, `FogCurrent` 1294 - the scene's
  value). The look: `State` / `CurrentType` and the rain objects under
  `Scene.RainTypes` (`RainLight` / `Medium` / `Heavy`, `Snow*`, switched by
  `AllOff()` / `TurnOn(type)`, which picks snow by the player's place);
  the private cloud floats (`Cloud{Overcast,OpacityScale,AlphaSaturation,
  SkyColorMultiplyer}{Current,Target}Value`, `VCloudCoverage*`, each with a
  `*Velocity` for its SmoothDamp); and what is drawn - the shared material
  `CloudOvercastMat` (`Cloud_Blendable`: `OvercastAmount`,
  `CloudOpacityScale`, `AlphaSaturation`; the easing reads these back every
  frame) and `vClouds.materialUsed._Coverage` (`RaymarchedClouds` on
  `MainCamNew`). `ForceRain(4)` went `GrowingClouds` at once and `Raining`
  (Heavy, `RainDice` 4) ~40 s later, when the overcast reached its target
  (`DoRain` from `GrowClouds`). `RandomClouds` re-rolls the Idle targets
  every 30 s (from 30 s after `Awake`); `RainChance` rolls rain every 60 s
  (from 150 s). Setting all of the above over the bridge turned a heavy
  rain into a sky that matched a fresh Full load, and a clear sky into the
  rain (darker ground, the dark cloud band) - shots `wx-p1/p2/p4/p5`,
  `wx-q1/q2` in the bridge folder. Put back by `Game/WeatherKeeper`
  (`weather` savestate header). `Wind` and `TerrainWetness` (which ease
  with `Raining`) have no live instance in the forest scene.
- **Fog distance** (`TheForestAtmosphere`, 2026-10-04, source + bridge):
  `FogCurrent` (not saved) is re-rolled by `ChangeFogAmount` every 600 s
  from 500 s after `Awake` - 700-2000 on the surface, 3000 in a cave, 900
  in the endgame; `Visibility` (the drawn distance, a shader global) steps
  1 per frame towards it (x1.2 in the overlook area, x0.5 in the snow), unless
  `overrideVisibility` (the cave exit's fade sets it, then clears it).
  Setting both moves the fog at once. At the test spot / time (322, a
  heavy rain) 150 m and 2000 m looked alike from the forest floor - the
  fog shows in open views, not under trees.
- **Eye adaptation can't be switched off** (2026-10-01): setting
  `PostProcessingBehaviour.profile.eyeAdaptation.enabled` false is undone
  by `PostProcessingBehaviour.OnGUI` (`EnableScionEyeAdaption(PostEffects
  System == 0)` + `CheckScionEyeAdaptation`: one frame of Scion's own auto
  exposure, then eye adaptation back on). To hold one exposure, clamp its
  `settings` (a struct - write back a changed copy): `minLuminance =
  maxLuminance = EV`, `adaptationType = Fixed`. From 500 m up at midday
  light, EV -3.5 looks like the adapted forest; the auto exposure lifts
  dark forest and holds snow down (the photo map's brightness bands).
- **Lakes' far stand-in** (bridge + IL, 2026-10-01): each lake under
  `Water_placed` (`Lake 4`, ... 20 of them) carries a `LOD_GroupToggle`
  - level 0 the lake itself (`TheForest.Graphics.Lake`, shader "The
  Forest/Water", `SnowLake` in the snow) within **150 m**, level 1 its
  child `LakeLod` (material `LakeFake`, Standard Specular, black) within
  400 m. `LOD_GroupToggle.ThreadedRefresh` (a worker thread) compares the
  horizontal distance from `PlayerCamLocation.PlayerLoc` with its own
  `_levels[i].VisibleDistance` (5 m hysteresis) - **not** LOD_Manager's
  ranges. 963 toggles (lakes, boulders, cliffs). Changing a
  `VisibleDistance` takes effect within a second (checked both ways).
- **The sinkhole is a hole in the terrain** (2026-10-01): the heights go
  to 0 (the terrain's lowest) over a ~10k-sample pit centred near (161,
  52); the game draws no terrain there, and the pit's floor, cliffs and
  water are models down to y -304 (`Cave_SH_Streaming ... sinkhole_floor`,
  `Nature_Placed/SinkHole`, `SinkHoleCenter` -274). A teleport to (120, 90,
  20) falls into it (a death). The only other height-0 samples inland of
  the coast are under the open sea (the yacht's cove).
- **The yacht moves at run time** (2026-10-01): the scene's `Yacht` root
  (level2, at about (469, 71, 1315)) is reparented under a spawned
  `yachtWobblePrefab(Clone)` and stands at (367, 41, 1390), bobbing - its
  parts keep their scene handles (positive) under the clone's negative
  one. Hull `BodyHigh` ("BoatHull deferred", `Custom/NewSurfaceShader`, no
  texture; teak decking), `Exterior_Tophull` (`MoldyWall`), `LodFar` (LOD
  1, Simplygon). `Game/WorldDump.Placed` dumps it as it stands.
- **The player's camera carries the hurt / weather overlays**:
  `MainCamNew` has `BleedBehavior` (screen blood: `BloodAmount`, set by
  `Hit` - starvation and thirst damage hit even in god mode), `Frost`
  (`coverage` with the cold), `Grayscale` (low health), `WaterBlurEffect`,
  `Blur`, `Ceto.UnderWaterPostEffect`. Anything that flies that camera
  (our freecam, the aerial capture) shows them.
- **Meshes are not readable** (`Mesh.isReadable` false, collision meshes
  too): geometry is read offline from the game's files
  (`scripts/world-extract.py`, UnityPy). Scene order = BuildSettings:
  level2 ForestMain_v08, 7 endgame_streaming, 10 MainSceneGreebles
  (empty), 11 MainSceneWorldStorySpots, 15-30 the cave prop scenes.
  Meshes live in `sharedassets*.assets`.
- **Pooled objects:** trees, bushes, saplings, plants, rocks and the
  caves' walls are **not** in the scene files - their placeholders are
  (`LOD_Base` subclasses: LOD_Trees 17.5k, LOD_Bush, LOD_Sapling,
  LOD_Plant, LOD_Rocks, LOD_SmallRocks, LOD_Cave, LOD_CaveEntrance; 30.6k
  outside `Pooling`). `LOD_Base.SetLOD` spawns `High` / `Mid` / `Low` at
  `_position` with the placeholder's rotation and the prefab's own scale
  (a tree placeholder 9.79, its spawned Mid 11 = the prefab's). Greeble
  rocks under `Pooling/Pool_Greebles` are spawned again per visit.
- **The endgame's props are switched off in the scene file** (7.2k
  renderers, 5.3k of them disabled; Area turns them on). Switched-off
  Cubes on the Blocker layer with `Base_Orange` are invisible volumes;
  the empty `Walls` / `Ceiling` objects in level7 are leftovers.
- **Rock / cliff look (Lux "Standard Specular Custom Ambient Water
  Flow"):** `_MainTex` is the grey rock; `_WnAlbedoSmoothness` (snow
  `FlatWhite`, grass, moss) is laid over faces that look up; the albedo
  alpha is smoothness, not a cut-out. Foliage uses AFS shaders (alpha
  cut-out).

## The game ships a debug console — 256 methods

`TheForest.DebugConsole` (static `Instance`, `_availableConsoleMethods`) is a
full developer console in the retail build. Methods are instance methods named
`_<command>` taking a `String` or `Object`, invokable by reflection without the
console UI.

Useful: `_godmode`, `_invisible`, `_capsulemode` (closest thing to a hitbox
view), `_speedyrun`, `_timescale`, `GotoPosition(Vector3)` (typed),
`_additem` / `_spawnitem` / `_removeitem`, `_setDrawDistance`,
`_eval(sCSCode)` (runtime C#).

There is a `CheatsAllowedSet` gate on the console UI; reflection should
sidestep it but that is **untested**.

**No wireframe, trigger or collider view, and no freecam** — those are drawn by
the plugin (`Game/DebugDraw.cs`, `Game/ZonePreview.cs`) with `GL` lines and
`Hidden/Internal-Colored`.

---

## Cheats and Creative (IL + bridge, 2026-10-02)

`Cheats` (MonoBehaviour) keeps the switches as statics: `GodMode`,
`InfiniteEnergy`, `NoSurvival`, `UnlimitedHairspray`, `DebugConsole`,
`Creative`, `PermaDeath`, `NoEnemiesInternal`, ... A **Creative** game sets
`GodMode`, `InfiniteEnergy` and `NoSurvival` itself
(`TheForest.Player.GameMode_Creative`; `RestoreSettings` / `OnDestroy` put
them back). Read on Slot 1: `GameSetup.Game = Creative`, `Difficulty =
Peaceful`. `GameSetup` (static) also says `IsNewGame` (Init = New; a menu or
our Full load sets Continue) - run mode started an attempt on it until
v0.24.213 (now a run spot's Restart).

---

## Speedrun tech and the endgame gate (IL + bridge, 2026-10-03)

The runners' common tech, read from the code; what can be detected is in
[`run-mode.md`](run-mode.md) *Banned moves: detection*. Marked **live** where
the bridge reproduced it, otherwise IL only.

**Bomb boost** (live). An explosion within 15 m (`Explode.RunExplode` ->
`PlayerStats.ExplosionPlayer` / `Explosion(dist)`, ignored while the view is
below `World`, in the enter-cave animation or a cutscene) does 25 damage,
closes the inventory and sends the damage FSM `toHitFall` -> `gotHitFall`,
which starts `playerHitReactions.enableExplodeCamera`. That coroutine runs
`while (timer < 0.5) { timer += Time.deltaTime; rb.AddForce(-forward * 8,
VelocityChange); yield null; }` - **8 m/s per rendered frame**, gated on game
time. The pause menu (`HudGui.TogglePauseMenu(true)`, timeScale 0) stops
physics and `deltaTime` but not coroutines: the timer never runs out and the
push piles up each frame, all of it applied by the first physics step after
the menu closes. Bridge, ~200 fps: a knockback alone peaked at ~95 m/s and
moved the player 4 m; with 1.0 s in the pause menu the velocity after closing
was **1,564 m/s** (8 x ~196 frames) - 270 m and 350 m up in 4 s. So the
distance goes with fps x time paused (sxczurass's table,
`Downloads\qa-reports\sxczurass\image.png`); the direction is the player's
back at each frame (the mouse rotators are off during the knockback).
`FirstPersonCharacter.Update` zeroes the horizontal velocity while the pause
menu is up, but forces waiting for the physics step are untouched.
Other senders of `Explosion` to the player start the same knockback, so the
same stacking: the **large swinging rock trap** (confirmed by the author
2026-10-04: the trap boost works, but takes longer to build and is less
versatile than a small bomb trap)
(`trapHit.registerTrapHit`: `largeSwingingRock`, rock speed > 11 m/s,
`Explosion(-1)` to whatever it hits - `Player` / `playerHitDetect`
included), enemy thrown rocks (`thrownRockDamage`), the fat creepy's charge
(`fatCreepyCharger`) - not melee (corrected in the overnight sweep below). No other
per-frame push on the player exists (every `AddForce` on its rigidbody
checked: zipline exit, glider drop, raft are one-off or other bodies).

**Fall damage and the slide cancel** (IL). `FirstPersonCharacter.HandleLanded`
runs on the `Grounded` rising edge in `FixedUpdate`; damage =
`0.9 * v * v / 27.5` (1000 if `jumpingTimer` > 3.8 s) only when `prevVelocity`
> 28, `allowFallDamage`, `jumpingTimer` > 0.75 and not `jumpLand`.
**`prevVelocity` is the `relativeVelocity.y` of the most recent
`OnCollisionEnter`** of any collider - written nowhere else. A landing that
is not a new collision enter (contact kept while sliding off a smooth edge),
or one preceded by a new enter at low vertical speed (grazing a seam between
colliders), is judged on that small value: no damage, and no 3.8 s death
either. Likely the runners' slide cancel; not reproduced. Steep terrain
does not give one by itself (live, 2026-10-03): the game holds the player
on a 55-80 degree terrain slope at ~3 m/s, so no speed builds while the
contact is kept. Detected since v0.24.230 (docs/run-mode.md). `jumpingTimer`
counts `Time.deltaTime` (stops in the pause menu; one 9 s frame counts 9 s).
**A fall is capped at 55.43 m/s** (live, 2026-10-03: from y 1,500 the
speed held there for 16 s; rigidbody drag 0, mass 7). Air control also
pulls a forced horizontal speed down within a physics step or two (a
300 m/s set every frame moved the player ~78 m/s) while
`FirstPersonCharacter` is enabled - the knockback disables it.

**Cave state force load** (IL). `playerEnterCaveAction.doCave` (crawl /
climb entrances): locks the player, sets `enterCaveInt`, then sends
`InACave` **on a timer** (2.5 s, then 0.5 s) and afterwards waits only while
layer 0's tag is `enterCaveHash`. If the enter animation never plays (a
smash in the air holds the animator) the player is not moved, the timer
still sends `InACave` (terrain collision off, cave lighting, cave streaming).
Correction (IL re-read + live, 2026-10-03): after `InACave` the coroutine
waits until layer 0 or 2 is tagged `enterCave`, then holds the player
while it stays tagged and lets go the frame it is not - so the let-go
comes wherever the root motion had got to when the animation stopped.
Live: an `Animator.Rebind` 1.7 s in let the player go standing at the
Cave 1 mouth, 1.9 m above the terrain, in cave state - the runners'
description. A normal entry lets go 7.5-300 m under the terrain (survey of
every crawl / swim entrance; `activateCave` needs the look raycast -
`set <trigger> activateCave.enabled true` + `press Take` drives one). Falling through the terrain from there is
`InACave`'s terrain collision being ignored.

**Axe / wall clips and log boosts** (not read in depth). Both look like
PhysX depenetration: the capsule grows back from crouch
(`DisableCrouch` / `ScaleCapsuleForCrouching`) or is squeezed by a log wall,
and the solver pushes it out on the far side / upwards. No game script
moves the player through.

**The endgame gate - can the game be beaten without the vault keycard?**
Beating it = Take twice on `Sections/ControlRoom/endPlaneCrashPrefab1/
TriggerFINISHGAME` or `TriggerDEACTIVATE` (`activateEndCrash.Update`: no
item, Megan or flag check - only in range, view `World`), at (-446, 707.5,
-1757) / (-430, 705.7, -1766). Those exist only in **`endgame_streaming`**.
It loads only through `EndgameEntrance/LoadEndgame` (`SceneLoadTrigger`):
`ForceLoad` does nothing until `_canLoad`, and `SetCanLoad` has exactly two
callers - the vault door's `onDoorOpen` (UnityEvent) and
`LoadSave.Activation` when the save says `HasActiveEndgameArea`
(`_isInEndgame` and an active area hash). The door: `activateKeypadDoor.Update`
needs `Owns(210, allowFallback)` within 4.75 m; the Keycard (210) and
KeycardElevator (242) have **no fallback items**; `Owns` asks an
`ItemFilter` first, set only by the dev console's `itemhack` /
`LocalPlayer.UnlimitedItems` (no caller; Creative leaves it null). The
save path: `_isInEndgame` is set by crossing the box forwards (no keycard),
but the active area only by `Area.OnEnter`, whose callers are the
`AreaGate`s (all 23 inside `endgame_streaming`), `EndgameWakeUp` (boss
fight), `Area.Awake` on a save with that hash, and the dev console. The two
`Area`s in the main scene (`EndCollision/Collision_StairCase`,
`Collision_StorageRoom`) are only entered by gates in the lab. So in single
player the lab - and the buttons - load only after the vault door opened
with keycard 210 in this save, or from a save made inside an already loaded
lab. There is one keycard 210 in the world (`C6_Props/C6_secretRoom02/
Keycard`, a world pickup that respawns on every save load). Not covered:
multiplayer (a client of a host who opened it) and anything physical that
reaches the buttons with the lab loaded.
Facts on the way (the red elevator itself checks no keycard - *Overnight sweep* below): **the lab's collision is in the main scene**
(`EndCollision`, layer 25 `Blocker`, which the player collides with:
artifact room, boss room, offices, corridors...) - always there, drawn or
not (the "invisible section"); `ConsoleMacros/loadEndGame_macro` is the dev
console's loader; `SceneLoadTrigger` with `DelayedLoad` forwards waits in
`_runningAction` until a `ForceLoad`.

**The game's own teleports** (for the 2026-10-03 teleport decision).
`LocalPlayer.Goto(Vector3)`: cave state by terrain height, velocity zero,
position - nothing else. `Goto(Transform)` (the console's `goto <target>`):
also `GotoArea` (enters the target's `Area`) and, within 150 m on the lab
side of `LoadEndgame`, invokes its `_onCrossingForwards` (`EnterEndgame` +
`ForceLoad`, which still needs `_canLoad`). The console never loads the lab
without the door either.

**First death** (`PlayerStats.KillPlayer`, `DeadTimes == 1`, single player):
in the endgame, `LoadEndgame.ForceUnload` + `ExitOverlookArea` /
`ExitEndgame`; then a teleport to a random `DeadSpotController.DeadSpots`
entry, cave state (`SetCurrentCave(1)`, `InACave`), `WakeInCave`, starvation
0, thirst 0.35, `TimeOfDay` 1. In the endgame's boss fight it is
`EndgameWakeUp` instead.

### Overnight sweep (IL + bridge, 2026-10-03/04)

The rest of the avenues from the tech sweep. Method: the game's assembly
decompiled to C# (ILSpy, `ilspycmd`, into a scratch folder - faster to read
than `ilscan body`), UnityEvent wiring read live over the bridge
(`m_PersistentCalls.m_Calls[i].m_Target` / `m_MethodName`), tests at
~220-240 fps on Slot 1 (Creative, god mode). **live** = reproduced over the
bridge; **IL** = read only. The runners' own descriptions come from
sxczurass's *Creative Bombless Any% Guide (2025)* (speedrun.com guides,
YouTube `qGNfvSwsdII`, transcript) and the chapter list of fruich's 2023
Creative tutorial; the other guide videos have no speech or no transcript.

**Player physics numbers** (live). `Physics.gravity` is (0, -16, 0) and
`FirstPersonCharacter.FixedUpdate` adds its own `-gravity * mass` (10 m/s²):
**26 m/s²** in all. Jump = `sqrt(2 * jumpHeight 8 * gravity 10)` = 12.65 m/s
up, ~3.1 m, ~1 s in the air. Physics runs at 60 Hz (`fixedDeltaTime`
0.0167). Speed cap `maximumVelocity` 55 (applied each physics step while
grounded or `jumping`, inside `if (!MovementLocked && (!Locked || landing))`).
Walk 6.5, run 13.5 (x up to 1.3 with athleticism), crouch 4.5 (run-crouch
x1.75), swim 3.75, glider cap 40. The player's rigidbody is **Discrete**
(Continuous only during an explosion knockback), no interpolation, and
`maxDepenetrationVelocity` is 1e32 (unlimited).

**Bomb boost, refined** (live). Not new to the runners: the author's QA
post of 2026-09-26 (15:02 on the bot's clock) already says the distance
comes from fps x time in the menu (+ the velocity when pausing), and
sxczurass's table (QA `1553447134911664168`,
`Downloads\qa-reports\sxczurass\image.png`: frame-by-frame steps with a
"last usable frame", and distances at 180 / 210 / 240 fps for 1-5 s paused)
measured it. What follows is the *why*. The knockback coroutine pushes 8 m/s per
rendered frame, but the push only builds freely until the player's animator
(layer 2) enters the `explode`-tagged state, about **0.13 s of game time**
(~30 frames at 230 fps); from then on `playerAnimatorControl.Update` sets the
horizontal velocity to 0 every frame while that state plays. Measured:
- knockback alone: up to 215-264 m/s, 25-40 m (stopped by the first tree);
- **pause early** (within those ~0.13 s): the frames spent paused stay in the
  velocity after the menu closes, for the rest of the free phase: 0.5 s
  paused (~120 frames) = ~1,000 m/s for several physics steps, **120-170 m**;
- **pause late** (the explode animation already playing, which lasts 0.5 s +
  up to 0.25 s of game time): the piled-up push is applied for **one physics
  step** and then zeroed: a hop of 8 x frames paused / 60 m = **0.13 m per
  paused frame** (0.5 s at 240 fps = 16 m).
So the long boost is "pause the instant the bomb goes off". The menu's
`LockView` makes a grounded player kinematic (forces would be lost), but
during the knockback `OnAnimatorMove` sets `isKinematic = false` every frame
(root motion is on), so the push keeps piling up even from the ground. The
direction is the player's back at the blast (both mouse rotators are off).
Against the runners' open questions (author's post: "why sometimes they are
hitting game objects or flying off course", what the velocity at the pause
does, a way to visualise it):
- **"Last usable frame"** = the end of the free phase: the frame the explode
  animation state starts and the horizontal velocity is zeroed each frame.
  sxczurass's per-frame steps (growing each physics step, then stopping)
  are exactly this. It is ~0.13 s of *game time*, so more frames at high fps.
- **Velocity at the pause**: in the late regime the explode state zeroes the
  horizontal velocity every frame, so only the vertical part survives; in the
  early regime the existing velocity adds to the piled-up push (IL + live).
- **Hitting objects**: the knockback is Continuous (CCD) - the sweep stops the
  player at the first collider in the path (a tree 23 m out ended one live
  test) instead of passing it.
- **Measured with real pauses (2026-10-03, v0.24.225, bridge `press Esc`
  = the game's own pause menu; `Stats.Explosion 5`; ~235 fps; fixed step
  1/60 s, gravity -16).** In the air (no ground, nothing to hit), the
  velocity after closing the menu is 8 m/s x frames paused (+ the frames
  before the pause), held for **~10 physics steps (~0.163 s of game time)**
  and then zeroed horizontally - every time, whatever the pause length:
  0.5 s / 1 s / 2 s paused (123 / 237 / 475 frames) = 170 / 308 / 652 m,
  **1.30-1.38 m per paused frame** (8 m/s x 0.163 s). So the distance is
  linear in frames paused when nothing is hit: **~1.3 m x fps x seconds
  paused**.
- **Pausing late costs a third per 0.05 s.** The free window is counted
  from the blast in game time: pause 0 / 0.05 / 0.10 / 0.15 s after it ->
  317 / 239 / 91 / 51 m for the same 1 s paused (window left 0.163 / 0.11 /
  0.04 s, then one physics step = the late regime). sxczurass's table sits
  at 0.8-0.9 of the air figure (240 fps 1 s: 276 m vs 312), what a pause
  ~0.02-0.03 s after the blast gives - a human reaction.
- **The "less than linear" and the drift are the ground.** Same test on
  the ground where sxczurass ran (from x 772, z 0, back to +x, 1 s and 2 s
  paused): both flew straight at ~1,870 / ~3,700 m/s for 2 steps, then hit
  the same terrain rise at x ~887 - the CCD contact turned the velocity
  into **sideways (-z) and up** (1,870 m/s -> (315, 198, -633)): only
  ~170 / ~250 m of horizontal travel instead of ~310 / ~620, and the
  vertical part is **not** zeroed by the explode state (only the horizontal
  is): a launch of 540 m / 1,100 m straight up. A faster boost covers more
  ground inside the same 0.163 s, so it is more likely to meet a slope or
  an object - the longer pauses lose more (the table's 3-5 s rows), and the
  hit decides the sideways drift. Off course = something touched inside
  the window; a level runway (or a jump just before) is the optimal setup.
- **A boost visualiser** (not built; Experimental if wanted): the path is a
  straight line along the player's back, length 8 x frames paused x the
  window left (0.163 s - game time since the blast), swept with the
  player's capsule (`Physics.CapsuleCast`) to show the first hit - where
  the boost bends and launches.
The knockback uses **Continuous** collision detection: a boost cannot tunnel
through static colliders (gold door test below: 200 and 1,500 m/s stopped).
The inventory cannot replace the pause menu: it refuses to open while
`useRootMotion` (the knockback) or `jumping`.
Knockback sources (`Explosion(dist)` with dist < 15 to the player): bombs and
explosives (`Explode`), the large swinging rock trap (`trapHit`, rock faster
than 11 m/s, `Explosion(-1)`), thrown rocks faster than 12 m/s
(`thrownRockDamage`: enemies' rocks and the player-built multi-thrower's
projectiles, `MultiThrowerProjectile`), the fat creepy's charge
(`fatCreepyCharger`), and in co-op the server's explosion event. A second
explosion within 2.2 s is ignored (`isExplode`); a swimming player only gets
the hit state. **Correction:** `enemyWeaponMelee` sends `Explosion` to *trees*
(creepy male / boss), not to the player - melee does not knock the player
back this way.

**Position writers** (2026-10-03, decompiled C# + live; tech round 2d).
Every script that sets the player's position or parent directly (97
methods reference the player's transform and a position / parent write;
~30 really move the player). For each: where it puts the player, what
starts it, and whether it checks walls - a snap is a free teleport through
whatever the start check cannot see.
- **Cliff climb (climbing axe, item 138) - goes through walls, live.**
  `activateCliffClimb.scanForCliff` casts **5 m** from the camera with
  layer mask 67117056 = **ReflectBig (13) + Terrain (26) only**; Take
  snaps the player to `hit.point - forward` (`enterClimbCliff`). Anything
  on another layer between the camera and a climbable surface is
  invisible to the ray: live, a 0.3 m wall (a Default-layer cube) at 1.9 m
  in front of a `Cliff_Rock` 3.8 m away - Take put the player 0.5 m past
  the wall's far face, climbing. On the surface any ReflectBig rock
  counts; in caves the hit must be tagged `climbWall`. Which real walls
  qualify (layers: player-built walls, doors, the lab) is not checked -
  read a wall's `layer` before trying one.
- **Rope grab** (`enterClimbRope`): from up to **6.1 m** (camera to the
  rope's bottom trigger), and if the player is more than 2.5 m below the
  attach point, the position is set to it (a pull up to ~6 m). The wall
  check casts trigger -> camera on Default, ReflectBig, Cave, Wall, Prop,
  Blocker, Terrain - only small props / trees / animals let it through.
- **Keypad walk-up** (`openKeypadDoor`): parents the player to the door's
  `playerPos` from anywhere within **4.75 m** of the keypad while the
  Grabber touches its trigger, idle / walking - no line-of-sight check.
  A walk-up from behind or below a wall is plausible; untested, and the
  pull goes to the door's front (where a runner already is).
- **Rafts, houseboats, cranes** (`DynamicFloor.UpdatePlayerPosition`):
  carry the player by writing the position every physics step (no
  collision) while on the floor's extents or counted on it.
- **Bench / chair** (`PlayerSitAction`): getting up puts the player back
  where they sat down - no gain. **Skinning**: moves to 1.3 m (3 m for
  type 5) from the animal, then back up to 2 m toward the start - stays
  near the start. **Zipline grab**: within 2.5 m, snapped 2.5 m under the
  line. **Rock thrower**: held at its seat, released there. **Cave crawl
  entrances** (`doCave`): parented to the entrance and moved by the
  animation (the force load above). **Crane climb**: x / z held on the
  rope.
- **Dead code**: `CaveTriggers.CaveDoorRoutine` (mirror the player through
  a door, depth x 1.25 / 1.5) is never started.
- Cutscenes (`TriggerCutScene`, `PlayerStats` drag-away / hanging / wake-up,
  Timmy / Megan pickups, the endings) place the player at fixed markers -
  no player-chosen destination.

**Diagonal running is 10% faster** (live). `DetermineVelocityChange` clamps
the input vector to length **1.1**, not 1: W alone = 1.0, W+A / W+D = 1.1.
Walking: strafe 6.23 m/s, diagonal 6.91 m/s on the same floor. This is the
runners' "always run diagonally".

**Air keeps speed ~5x longer than the ground** (live). Extra speed (40 m/s
set by hand, W held): on the ground 40 -> 6.3 m/s in 0.1 s (up to 4 m/s
removed per physics step plus friction); in the air 40 -> 6.5 m/s over
~0.6 s (`HandleJumpSpeed`'s air control, fading with `clampAirTouch`). A real
jump also zeroes all input for 0.2 s (`clampInput`), so the first 0.2 s of a
jump loses nothing. Why runners jump after a zipline exit or a boost.

**Jump and grounding rules** (IL). A jump needs `allowJump`, `CanJump`, not
crouch-blocked, not on a rope / sled / climbing / diving / locked / in the
inventory, and `Grounded` **or** within 0.21 s of the last grounded physics
step (`fauxGroundedTimer`, coyote time; then `blockFauxJump` for 0.5 s).
`allowJump` turns off 0.25 s after leaving the ground; a jump blocks the next
press for 0.2 s. `Grounded` is set by any collision with a contact below the
capsule's lower sphere (or 3+ contacts in its lower 0.8 m) - **there is no
slope-angle check**, except for CapsuleColliders (normal steeper than 45° does
not ground) and surfaces marked slippery (`getWalkableSurface`: there the jump
is 1/9 high). Above 65° (`extremeAngleGroundedLimit`) friction drops to 0, so
the player slides - but stays grounded and may jump. Live: sliding down an
~80° terrain face read `Grounded` and `allowJump` true. Jump-climbing (bridge
`press Jump` every 0.1 s, 2026-10-03): against the sinkhole's steep west wall
(a mesh - terrain collision is off there) walking and jump spam both stopped
at the same height (y 19.9): no gain. An open terrain face is not tested.

**Water** (IL). `FirstPersonCharacter.Update` has a swim-jump branch
(touching a wall at the side or a low mesh contact = 1.5x the land jump, no
cooldown; else a small water jump with a 1 s block). The author: "there is
no way to jump in water". Live with a real Jump (bridge, 2026-10-03): at the
surface the small jump works - 6.3 m/s up, a ~0.3 m hop, once a second -
too small to notice; it is gated on `!Diving`, and `Diving` (head sensor
0.25 m under the surface) only clears once the sensor is back above it, so
after sinking (a fall in, a tp) no jump works until the player surfaces.
The 1.5x wall-side jump is not tested. Surface swimming is capped at 3 m/s
(`maxSwimVelocity`) even when sprinting (target 3.75 x 2.2 = 8.25), **except
while touching a wall at the side or with the head under water** - the cap is
not applied then (diving has its own 6.5-7 m/s cap). So sprint-swimming along
a shore or wall, or diving, is up to ~2.5x faster than open-water swimming
(untested; the author: "might be gimmicky").
The inventory cannot open under water.

**Looking down moves the player's colliders** (live). Every frame
`playerAnimatorControl` sets the body capsule's and the head sphere's centre
`z = Clamp(normCamX, 0, 0.4)`: looking down shifts both **0.4 m forward**.

**The axe ground smash and the panel / elevator clip** (live, the clip itself
not reproduced). The runners' recipe: crouch, face the wall / panel,
Shift+W, jump, smash the axe into the ground at the top of the jump (looking
down), and as the axe hits the ground move the mouse up and **uncrouch**; it
needs uncapped fps. Measured pieces:
- during a ground smash (`axeAttackGround1`, `doingGroundChop`) the head
  sphere (r 0.6) follows the head bone: centre forward 0.4 -> **1.63 m** and
  down 1.76 -> 0.97 m over ~30 frames. Pressed against the gold door it pushed
  the body back 0.24 m (no clip, standing);
- `ScaleCapsuleForCrouching` does nothing while `doingGroundChop`. Standing up
  during the smash lets the stand-up routine finish without resizing: the
  player stands (`crouching` false) with the **body capsule still crouch-size
  (3 m, centre -0.85) until the next crouch**, and the head sphere snaps back
  to standing height when the smash ends - a 0.5 m gap between body and head
  colliders. This is the "uncrouch when the axe hits the ground" step.
`doingGroundChop` is set in `playerAnimatorControl.OnAnimatorMove` while
the full-body layer plays `axeGround2` or `axeAttack`, and stays true ~1.5 s
per smash (live; it comes on ~0.2 s after the swing event). Facing a wall,
the smash's head sphere shoves the player ~1.5 m back from it within 0.1 s.
Why fps matters and how it ends up on the far side were not found; a real
input recording (or the author doing it with `anim watch` and per-frame
position reads) is the next step.

**Movers in the world** (live, 2026-10-03). The yacht
(`yachtWobblePrefab(Clone)/.../yacht_alec_collision/Object40`, a
non-convex MeshCollider) sits on a **kinematic Rigidbody that bobs**: ~0.1 m
and ~0.2 degrees over half a second. A player on it is pushed with no
velocity (a 1.0 m rise while walking). The game also unhooks collision per
pair with `Physics.IgnoreCollision` in 50+ places (terrain at cave doors /
zones / holes, ropes, ziplines, structures on rafts, bodies on sleds); Unity
5.6 has no `GetIgnoreCollision` to read them back - only contacts
(`OnCollisionEnterProxy` / `OnCollisionExitProxy` on the player) show what
the player really collides with.

**Depenetration** (live). A static collider appearing inside the player (a
box moved into the feet by 0.3 / 1 / 2 m) lifts the player out by exactly
that depth in one step, with **no** velocity left over. So a wall or log
placed where the player stands lifts them onto it - the likely core of the
runners' custom-wall "climbing wall boost" (and log boosts) - but it is a
lift, not a launch.

**Tunnelling** (live). The player (Discrete) through the gold door's 0.11 m
leaves: 20 / 40 / 55 m/s stopped; with the speed cap off (as during a
knockback) **100 / 200 / 500 m/s passed through**; the same with Continuous
(the knockback's mode) stopped at 200 and 1,500 m/s. The 55 cap and the
knockback's CCD close this in normal play; a way to be over ~60-100 m/s
while Discrete and not capped (Locked / MovementLocked when the knockback
ends) was not found.

**Red elevator: no keycard anywhere** (live wiring + IL).
`Sections/HellCorridor/Elevator_01a`: the **Enter Button**
(`ButtonDoorSystem.ActivateButton`) opens and unlocks the car door; Take on
`Trigger_Elevator` (car door closed) -> `AnimationSequence` stage 1 ->
`PlayerPositionTest1` (within 7 m of the car) -> `PlayerPositionTest2`
(inside, in front of the trigger) -> `ElevatorSystem.GotoRemotePoint`.
`setKeycardId(242)` there only picks the animation; nothing calls `Owns`.
Live without keycard 242: 5 s wait (no animation played, the player was not
moved) -> car and player teleported together (relative offset kept) to the
overlook (-542.44, 704.79, -1967.46) -> **25 s** with the car door
**locked** (`MovingDummy` is active during the ride: its `OnEnableProxy` sends
`Lock`, `OnDisableProxy` `Unlock`) -> door opens. Entering the door's trigger
mid-ride did nothing; after the ride it opened the door. The door leaves are
0.1 m boxes - the runners' "elevator skip" clips out of the locked car early
(up to 25 s). The ride is a teleport (`ElevatorSystem.Goto`: the car is set
to the top and the player put at the same offset from it, twice, a physics
step apart): a player outside the door plane at that moment arrives outside
the top door too (live). The door is `AutomatedDoorSystem` (`_alpha`,
`_state`, `_locked`); `Lock()` only sets the flag - an already open door
stays open while "locked" (the leaves move in `Update`, off once a door
finishes). Live with real input (2026-10-03, v0.24.226, door shut): from the
arrival spot a sprint stops at x -538.27 (capsule against the leaf); the
runners' recipe scripted (crouch, look down, axe smash, uncrouch 0.25-0.55 s
later, Jump every 0.05 s; at the seam and at the corner) never got through -
the smash's head sphere pushes the player *back* ~1 m. A capsule set 0.07 m
past the leaf's middle is pushed out the far side by depenetration, so the
clip needs only a few cm past mid-leaf; how the runners get there (fps?
the exact corner?) is still open - the author or maks doing it with `anim
watch` and per-frame reads is the next step. **Before v0.24.226 a Quick load
kept the door as the last ride left it (open): the car stayed open for the
whole ride and a plain sprint left it** - fixed (`SlidingDoorKeeper`). The keycard is checked only at the gold door
(`ArtifactRoom/ElevatorCardReader/Trigger`, `activateKeypadDoor`, 242), which
unlocks `LabDoor_Door (4)` between the ArtifactRoom and the BrokenCorridor.
Area gates (route order): ... GlassOffice_C -> ArtifactRoom -> CorridorBasic
-> Meetingrooms -> Lab Corridor -> BossRoom (Megan), and ArtifactRoom ->
**(gold door)** BrokenCorridor -> EndgameCaves -> HellCorridor (red elevator)
-> ControlRoom. Getting past the gold door any other way skips Timmy, Megan
and the boss: that is the runners' **lab skip** (rocks, ledges and seven jumps
on invisible collision - the lab's `EndCollision` is always present, the
sections draw only when entered through their gates - then a smash clip into
the elevator corridor), which the keyless elevator makes work. Use limit 1.
Also: the overlook's second elevator (`Elevator_ToSnowCave EG`) is one-way
down to the snow cave (the car starts at the top, its trigger is disabled
once it moves) - no way up from below. With the lab **unloaded** (after
leaving it backwards, a tp out, a first death) the main-scene collision has an
**open doorway** where the gold door stands (only the side wall
`Collision_ArtifactRoom/collision (31)`); the door leaves, the elevator and the
end buttons all live in `endgame_streaming`. `_canLoad` stays true once the
vault door opened, so re-entering reloads the lab fresh (doors locked again).

**Keypad prompts** (IL). `activateKeypadDoor.Update` shows the Take prompt
only within 4.75 m, not on a rope, and with the base animator layer in an
idle- or walk-tagged state - not jumping, falling or landing. Hence "jump from
the lowest point so the keycard button shows up instantly". In co-op the door
opening (`DoEnvironmentAnimation` -> `onDoorOpen` -> `SetCanLoad`) runs on
every player, so one keycard opens the lab for all.

**The game's timers and pauses** (IL). The inventory sets `timeScale` 0 (and
caps fps at 60) only in Normal / Peaceful / Creative single player - not Hard,
Hard Survival, co-op or VR. While on a zipline the inventory component is
disabled: no inventory and no pause menu on a zipline. Real-time timers that
keep running while paused: the adrenaline rush cooldown (120 s,
`realtimeSinceStartup`; the rush gives back half the missing stamina when
health drops into the grey zone) and the crafting / upgrade animation.
`jumpingTimer` and the 0.35 s fall-damage arm (`Invoke`) are game time; a
load hitch counts in full (`maximumDeltaTime` 9 s), so a hitch mid-fall can
turn a survivable landing into the 3.8 s "fell too long" death.

**Deaths** (live + IL). All seven `DeadSpotController.DeadSpots` entries are
the same `Cave2DeadPlace` (-692.29, 110.44, 1110.9): the first death's
"random" warp is always there (single player, outside the boss fight; the
first death outside a cave plays the drag-away cutscene first, then the
same warp). IL: a death **while swimming** (drowning, or killed in the water)
goes to `DeathInWater` -> `KillMeFast` after 7 s - the game-over camera, no
warp, even on the first death (single player).

**Rides** (IL). Zipline: +10 m/s² along the line per physics step, capped
50 m/s; Jump or Take lets go and keeps the velocity (`PreserveExitVelocity`
adds a fading push along the line for 1 s) and restarts `jumpingTimer`. The
runners' zipline boost is that exit speed kept by staying in the air.

**The runners' words, mapped** (sxczurass's guide; mechanism status):
uncapped fps for the panel clip (fps dependence not explained yet);
"plane clip" / "panel clip" / lab-skip clip / "elevator boost" = the
crouch-smash-uncrouch above; "slide on the bodies to not get fall damage"
(cave 6 drop to the keycard) = the fall-damage rule above (*Fall damage and
the slide cancel*: damage is judged on the last collision enter, and a body's
steep collider turns the fall into a slide; untested); "custom wall ... boost
yourself to the top" = the depenetration lift; "spam 1 after the keycard
pickup" = equipping cancels the pickup animation before the book opens;
"a trigger loads the rest of the caves" (cave 4) - pass it or the cave stays
unloaded; "jump from the lowest point" = the keypad prompt rule; "always run
diagonally" = the 1.1 input clamp; zipline boost = the kept exit speed.

**Dead ends** (this sweep): the inventory as a bomb-boost pause (refused, see
above); other per-frame pushes (the zipline, the glider and the shell sled
push once per physics step, not per frame); leaving the red elevator car
through its door trigger mid-ride (locked); calling the red elevator from
outside the car (the "in front" test needs the player inside); riding the
snow-cave elevator up; saving mid-air (the game saves only at shelters);
falling "through" steep terrain (it was the player walking off a ledge into a
hole - and a capsule spawned inside a 77° face by our own tp).

### Knowledge base pass (decompiled C#, 2026-10-03)

Read while writing the bot's cards (`knowledge/cards/`); code only unless
marked.
- **The fall cap is the speed cap.** Leaving the ground sets `jumping`
  (`HandleStartJumping`); in the air `HandleJumpSpeed` clamps the speed to
  `maximumVelocity` (55 on the player; the script default 25 is overridden),
  on the ground `ClampVelocity` does. The step's gravity (26 x 1/60 = 0.43)
  is added after the clamp: 55.43 m/s, the measured fall speed.
  `ClampVelocity` also has 3 m/s (`hitByEnemy`) and 5.5 m/s
  (`setNearEnemyVelocity`, `doClampVelocity`, 0.65 s) caps - no caller of
  either found in C# or the exported FSMs.
- **Peaceful = no enemies at all.** `Cheats.NoEnemies` is
  `NoEnemiesInternal || (!IsCreativeGame && IsPeacefulMode) ||
  (IsCreativeGame && !PlayerPreferences.AllowEnemiesCreative)`;
  `spawnMutants.Start` returns at once on Peaceful or NoEnemies (surface,
  caves, the boss-room babies), and the cave / worm spawns check it too.
- **Stamina and building** (2026-10-03, live with InfiniteEnergy off):
  sprint costs 3.5/s (`staminaCostPerSec`), regen 6/s from 0.4 s after the
  sprint (`timeToRecoverFromRun`), never above Energy; `running` = Run held
  and animator speed > 0.4 (no regen while it holds). Soda +50 stamina /
  +80 energy (carry 10); EnergyMix (coneflower + chicory) +30 / +100, Plus
  (+ aloe) +60 / +100 (carry 5). Creative blueprints: hold Build, one item
  per 0.065 s, nothing taken. The hole cutter cuts holes only in floors /
  roofs / rafts (`IHoleStructure`); any other building it touches is
  destroyed on placing (`FloorHoleArchitect.OnPlaced`).
- **The pause menu stops time on every single-player difficulty**
  (`HudGui.TogglePauseMenu`: `if (!BoltNetwork.isRunning) timeScale = 0`);
  only the inventory skips Hard / Hard Survival
  (`PlayerInventory.PauseTimeInInventory`, also `targetFrameRate = 60`
  while open, 0.05 s after opening). So the bomb boost works on Hard.
- **The knockback's second phase also pushes**: after the 0.5 s loop,
  while layer 2 is `explode`-tagged, `AddForce(-forward * 8)` continues for
  0.25 s of game time (`playerHitReactions.enableExplodeCamera`) - but
  `playerAnimatorControl.Update` zeroes the horizontal velocity every frame
  in that state, which is what ends the boost.
- **Zipline exit**: `ExitZipLine` over 10 m/s starts `PreserveExitVelocity`
  (1 s of game time, `_doingExitVelocity`); during it
  `FirstPersonCharacter.FixedUpdate` applies the input's velocity change
  with `ForceMode.Acceleration` instead of `VelocityChange` (ground and air)
  - the controller's braking is ~1/60 as strong. The ride: gravity off,
  +10 m/s² along the line, cap 50, lets go within 1.5 m of ground / under
  0.8 m/s after 1.3 s / on Take or Jump; the exit restarts `startJumpTimer`.
- **`doCave` timings**: crawl / climb entrances send `InACave` 1.5 s in
  (1 s + 0.5 s), swim entrances 3 s in (2.5 s + 0.5 s); the player's
  colliders are triggers (no collision) for the whole entry, and the player
  is parented to the entrance. (*Cave state force load* above said 2.5 s +
  0.5 s - that is the swim case.)
- **`PlayerStats.Explosion`**: ignored during `isExplode` (2.2 s), an
  endgame cutscene, the enter-cave animation, an `explode` state, or views
  below World; 25 damage (x3 from the player's own explosive with realistic
  player damage), minus armour; must survive it to be knocked back.
- **Red elevator split, keyless**: `ElevatorSystem.Goto` sends
  `openDoorRoutine` when `_playKeycardAnim` and (`!_sequence ||
  _sequence.IsActor`), then waits 5 s; the keyless live ride waited 5 s with
  no animation - whether `endGameCutScene` rose (the ASL's split) is not
  checked. `_useCount` counts rides.

## How to extend this file

0. **Decompiled C#** (2026-10-03, the overnight sweep) - for reading whole
   behaviours, faster than `ilscan body`:
   `dotnet tool install ilspycmd --tool-path <scratch>/ilspy`, then
   `ilspycmd "<Managed>/Assembly-CSharp.dll" -r "<Managed>" -p -o <scratch>/src`
   (~3,700 files, a few minutes) and grep it. Never commit the output. Live
   UnityEvent wiring (who a trigger / button / sequence calls) is not in the
   code: read `<event>.m_PersistentCalls.m_Calls[i].m_Target` /
   `.m_MethodName` over the bridge.

1. **In-game dump (`F11`)** — reflection metadata: type names, field names and
   types, live values. The explorer's "Dump filtered" gives full method
   signatures for a subset. Lands in `<game root>/ForestOverlayDumps/`.

2. **`tools/ILScan`** — reads `Assembly-CSharp.dll` with Mono.Cecil offline and
   sees real IL, which the dump cannot:

   ```bash
   dotnet build tools/ILScan/ILScan.csproj -c Release
   dotnet tools/ILScan/bin/Release/net8.0/ilscan.dll writes "UnityEngine.Cursor"
   dotnet tools/ILScan/bin/Release/net8.0/ilscan.dll refs  "IsMouseLocked"
   dotnet tools/ILScan/bin/Release/net8.0/ilscan.dll body  "VirtualCursor::LateUpdate"
   dotnet tools/ILScan/bin/Release/net8.0/ilscan.dll type  "TheForest.Items.Item"
   ```

   `writes` is the one to reach for when the question is "what keeps changing
   this every frame". `strings` finds string literals — the only way to see
   `SendMessage("name")` / `StartCoroutine("name")` callers, which `refs`
   cannot:

   ```bash
   dotnet tools/ILScan/bin/Release/net8.0/ilscan.dll strings "pickupTimmyRoutine"
   ```
