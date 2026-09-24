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

**Which page an entry is on is not recorded.** The plugin derives it from
where `_tickGo` sits in the hierarchy (`Data/PageGrouping.cs`); the
100% tab's **Write dumps** writes `natureguide_*.txt` with each tick's path.

`SurvivalBookBestiary` exists (one component per page, `FoundEnemyInfo[]`,
names from the `EnemyType` enum on `_availableConditionStorage`) but **is not
part of the 100% requirement** and the game carries two of them, so it renders
twice. Not used.

---

## Deaths

All in `PlayerStats` (IL):

| Method | What it does |
|---|---|
| `CheckDeath` | Returns under `Cheats.GodMode`. `Health <= 0` and not `Dead`: swimming → `DeathInWater`, else `Dead = true` → `FallDownDead`. Called from `Hit`, `Explosion` |
| `Fell` | `Health -= 200`; if `<= 0`, `Dead = true` → `KillPlayer`. No IL callers — sent by name, from fall triggers |
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

## The plane wreck across an in-place restore (bridge + IL, 2026-09-24)

`PlaneCrashController.OnDeserialized` does `Invoke("setupCrashedPlane",
0.3)`; `loadCrashPlane` instantiates `savedHullPrefab` at `savePos` into
`spawnedHullPrefab` - it never destroys the previous one. A load starts
from none; every in-place restore added one more root `Hull(Clone)` (three
after two restores), each with its own copy of every wreck pickup - the
growing `Axe Plane xN` in the "not at capture" line. The roots have no
save identifier. v0.24.14 destroys every root named like the current
`spawnedHullPrefab` except that one, 1.5 s after an in-place restore.

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
is unknown. Not yet confirmed in game.

## The ESC menu and the player lock

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

## Saving and loading

The game uses **UnitySerializer** (`LevelSerializer`, `LevelLoader`,
`UniqueIdentifier` / `PrefabIdentifier` / `EmptyObjectIdentifier`). A
`JSONLevelSerializer` twin exists; the game uses the binary one. All IL.

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
call (without the spawn) before deleting one.

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

Creative is chosen before the game loads and is not in the save data, so
restores are refused across Creative and survival (author's suggestion).

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

## How to extend this file

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
