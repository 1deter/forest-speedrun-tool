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

`ItemDatabase`: static `_instance`, `Items` (`Item[]`), `ItemById(int)`.
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

| Split | Method (event name) | Flag set |
|---|---|---|
| Vault door, gold keycard doors, red elevator | `playerOpenKeypadDoorAction.openKeypadDoor` (`keycard-door`, `keycard-door-<itemId>`) | after the walk-up, in its `lockPlayerParams` |
| Finding Timmy | `PlayerPickupTimmyAction.pickupTimmyRoutine` (`timmy-pickup`) | before first yield |
| Approaching Megan | `PlayerGirlPickupAction.girlToMachineRoutine` (`megan-to-machine`) | after first yield |
| Megan into artifact | `PlayerGirlTransformAction.doGirlTransformRoutine` (`megan-transform`) | after first yield |
| Game end | `PlayerEndCrashAction.doEndPlaneCrashRoutine` / `doShutDownRoutine` (`end-crash` / `end-shutdown`, both `game-end`) | after first yield |
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

**Keypad doors all share one action.** `activateKeypadDoor.DoActorAnimation`
sends `setKeycardId(_keycardId)`, `setShortSequence(shortSequence)`,
`setDoorAnimator` and then `openKeypadDoor(playerPos)`. So the vault, the
automatic door and the red elevator differ only by keycard item id,
`shortSequence` and the door object. The event log line carries all three
(`door '<path>', keycard <id>`). **Which door is which is not yet confirmed**
— one endgame run's log settles it; then give each its own event name.

Also present: `TheForest.Tools.TfEvent+Endgame` with static `Completed`,
`FireDetected`, `Shutdown2ndArtifact`.

---|---|
| Finding Timmy | `PlayerPickupTimmyAction` (`lockPlayerParams`, `pickupTimmyRoutine`) |
| Goodbye Timmy | `PlayerGoodbyeTimmyAction.goodbyeTimmyRoutine` |
| Approaching Megan | `PlayerGirlPickupAction.girlToMachineRoutine` |
| Megan into artifact | `PlayerGirlTransformAction.doGirlTransformRoutine` |
| Keycard door | `playerOpenKeypadDoorAction.lockPlayerParams` |
| Game end | `PlayerEndCrashAction.doEndPlaneCrashRoutine` / `doShutDownRoutine` |
| Raft / out of world | `RaftPush.outOfWorldRoutine` |

**The shared flag carries no identity; the call site does.** This is the
clearest case of something a plugin can do that an external autosplitter
cannot.

Also present: `TheForest.Tools.TfEvent+Endgame` with static `Completed`,
`FireDetected`, `Shutdown2ndArtifact`. Still unmapped: Vault Door and the Red
Elevator — `ElevatorManager` / `ElevatorGlobalState` are where to look.

---

## Survival book (100%)

`TheForest.Player.SerializableSurvivalBookTodo` — one `TodoTask` field per
objective (`_son`, `_camp`, `_cave1..10`, `SinkHoleTodoTask`,
`PassengersTodoTask`, …). Each inherits `ACondition`, which carries `_id` and
**`_done`**. Discover them **by shape** (any field whose type has `_done`)
rather than by name, so a game update adds objectives for free.

The older `SurvivalBookTodo` also exists and adds `FindTimmyTodoTask` /
`FindMeganTodoTask` — check which is live.

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
