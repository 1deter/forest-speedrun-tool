# The Forest — internals reference

Everything here was confirmed from a live `F11` dump on 2026-09-21
(game build as installed via Steam, ~7,300 types in `Assembly-CSharp`).
Nothing in this file is guessed.

---

## Player object

Root GameObject is named **`player`**, tagged `Player`.
Components of interest sit directly on it.

### `FirstPersonCharacter` — movement controller

| Field | Type | Observed value | Notes |
|---|---|---|---|
| `walkSpeed` | float | 6.5 | |
| `runSpeed` | float | 13.5 | |
| `strafeSpeed` | float | 6 | |
| `crouchSpeed` | float | 4.5 | |
| `swimmingSpeed` | float | 3.75 | |
| `maximumVelocity` | float | 55 | hard cap |
| `maxVelocityChange` | float | 4 | acceleration limit |
| `gravity` | float | 10 | |
| `jumpHeight` | float | 8 | |
| `staminaCostPerSec` | float | 3.5 | |
| **`Locked`** | bool | | **game's own full input lock** |
| **`MovementLocked`** | bool | | movement-only lock |
| `Grounded` | bool (backing field) | | `<Grounded>k__BackingField` |
| `Sitting`, `Diving`, `run`, `running`, `jumping` | bool | | state flags |
| `StandingOnDynamicObject`, `standingOnRaft`, `SailingRaft`, `PushingSled` | bool | | |
| `rb` | Rigidbody | | the player's rigidbody |
| `Stats` | PlayerStats | | |
| `setup` | playerScriptSetup | | |
| `targets` | playerTargetFunctions | | |

Velocity is best read from `rb.velocity` (already what the overlay does).

---

## Inventory

### `TheForest.Items.Inventory.PlayerInventory`
67 fields / 34 properties / 164 methods. Lives on the player subtree.

| Field | Type | Notes |
|---|---|---|
| `_possessedItems` | `List<InventoryItem>` | the actual held items |
| `_possessedItemsCount` | int | **currently displayed on the HUD** |
| `_possessedItemCache` | `Dictionary<int, InventoryItem>` | keyed by item id |
| `_itemDatabase` | `ItemDatabase` | for resolving ids to names |
| `_itemViews` | `InventoryItemView[]` | UI views |
| `_equipmentSlots` | `InventoryItemView[]` | equipped items |
| `_currentView` | `PlayerViews` enum | observed `World` |

Known item ids seen as fields:
`_leafItemId 34`, `_seedItemId 103`, `_sapItemId 104`, `_defaultWeaponItemId 80`

### Related types
- `TheForest.Items.Inventory.InventoryItem` — 4 fields / 1 prop / 4 methods.
  **Field layout not yet captured** — needed for per-item counts.
- `TheForest.Items.ItemDatabase` — `ScriptableObject`, 5 fields / 10 methods.
- `TheForest.Items.Inventory.InventoryItemView` — 30 fields / 63 methods.

---

## Other notable types

| Type | Shape | Likely use |
|---|---|---|
| `PlayerStats` | MB, 163 fields / 196 methods | health, stamina, hunger, thirst |
| `TheForest.Utils.LocalPlayer` | MB, 132 fields | static-style access point to player subsystems |
| `TheForest.Utils.Scene` | MB, 59 fields | scene-wide references |
| `HudGui` | MB, 213 fields / 66 methods | the game's own HUD |
| `TheForest.Items.Core.ItemStorage` | MB | on player |
| `TheForest.Player.Clothing.PlayerClothing` | MB | |
| `TheForest.Items.Special.*Controler` | MB | lighter, map, compass, walkman etc. (note the single-L spelling) |

Special-item controllers follow the pattern
`TheForest.Items.Special.<Name>Controler` — e.g. `LighterControler`,
`MapControler`, `CompassControler`, `FlashLightControler`.

---

## Cursor locking - SOLVED 2026-09-21

`TheForest.UI.VirtualCursor.LateUpdate` is the cursor owner. (An earlier note
in this file guessed it was gamepad-related. It is not.) Its first branch is:

```
if (TheForest.Utils.Input.IsMouseLocked) {
    if (Cursor.lockState != Locked) Cursor.lockState = Locked;
    if (Cursor.visible)             Cursor.visible   = false;
}
```

`Cursor.lockState = Locked` **warps the pointer to screen centre**, which is
why v0.4.0's "win the frame in OnGUI" approach produced a cursor that was
visible but pinned in place and flickering: OnGUI could restore visibility
after LateUpdate, but could not un-warp a pointer that had already been
recentred that frame.

The switch is `TheForest.Utils.Input.IsMouseLocked` (backing field
`<IsMouseLocked>k__BackingField`, helpers `LockMouse()` / `UnLockMouse()`,
both confirmed to be plain one-line flag setters with no side effects). With
it false, `VirtualCursor` takes its other branch and sets
`lockState = None; visible = true` itself, every frame. That is what the ESC
menu does.

Implemented in `src/Core/CursorController.cs`. The flag is asserted from
`Update()`, not `LateUpdate()`, because Unity runs every `Update` before any
`LateUpdate` - ordering between two `LateUpdate`s is undefined.

## timeScale re-assertion - SOLVED 2026-09-21

`Time.timeScale` is written from 22 places. The one that beats an external
write every frame is **`TheForest.Items.Inventory.InventoryItemView.Update`**.
Others worth knowing: `HudGui.TogglePauseMenu` (the ESC menu),
`PlayerInventory.PauseTimeInInventory` / `.Close`, `MenuMain.OnLoad` /
`.OnExitMenu`, and `LoadSave`.

This confirms the existing rule: do not try to freeze the game with
`timeScale`. Use `FirstPersonCharacter.Locked` / `.MovementLocked`.

## Autosplit candidates - lead, not yet confirmed

`TheForest.Tools.TfEvent+Endgame` holds static event objects:

| Field | Likely meaning |
|---|---|
| `Completed` | run end - the obvious split trigger |
| `FireDetected` | |
| `Shutdown2ndArtifact` | |

Also present: `EndGameStats` (MonoBehaviour), `PlayerStats.EndgameWakeUp`
(coroutine), `TheForest.Tools.PlayerInEndgameTester`.

Nothing here is wired up yet. Any autosplit hook must be a read-only Harmony
`Postfix` so it stays info-only.

## Things still unknown

- Which concrete method to patch for a run-start trigger
- Whether `TfEvent.Endgame.Completed` fires on every ending variant

## How to extend this file

Two complementary tools:

1. **In-game dump (`F11`)** - reflection metadata: type names, field names and
   types, live values. Use the explorer's "Dump filtered" button for
   `types_detail_*`, which gives full method signatures for a filtered subset.
   Dumps land in `<game root>/ForestOverlayDumps/`.

2. **`tools/ILScan`** - reads `Assembly-CSharp.dll` with Mono.Cecil offline and
   sees the actual IL, which the dump cannot. This is how the cursor and
   `timeScale` questions above were answered rather than guessed.

   ```bash
   dotnet build tools/ILScan/ILScan.csproj -c Release
   dotnet tools/ILScan/bin/Release/net8.0/ilscan.dll writes "UnityEngine.Cursor"
   dotnet tools/ILScan/bin/Release/net8.0/ilscan.dll refs  "IsMouseLocked"
   dotnet tools/ILScan/bin/Release/net8.0/ilscan.dll body  "VirtualCursor::LateUpdate"
   dotnet tools/ILScan/bin/Release/net8.0/ilscan.dll type  "TheForest.Items.Item"
   ```

   `writes` is the useful one when the question is "what keeps changing this
   every frame".
