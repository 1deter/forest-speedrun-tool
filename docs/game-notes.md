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

## Things still unknown

- What re-locks the mouse cursor each frame (no obvious `CursorManager` type;
  `TheForest.UI.VirtualCursor` appears to be gamepad-related)
- `InventoryItem` field layout (item id + quantity)
- Which method to hook for run start/end autosplitting
- Where `Time.timeScale` is re-asserted

To extend this file: press `F11` in game, then inspect the generated files in
`<game root>/ForestOverlayDumps/`. The `types_detail_*` dump (via the explorer's
"Dump filtered" button) gives full method signatures for a filtered subset.
