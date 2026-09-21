# ForestOverlay — project context

A BepInEx plugin for **The Forest** that displays speedrun-relevant information
(velocity, timer, inventory counts) and includes a runtime type explorer for
discovering the game's internals.

Read this file first when picking the project back up.

---

## Hard runtime facts (confirmed, do not re-derive)

| Fact | Value | Why it matters |
|---|---|---|
| Unity version | **5.6.5** | Predates engine module splitting |
| Scripting runtime | Mono, **CLR 2.0.50727** | Means **.NET Framework 3.5** |
| Target framework | **net35** | Anything newer fails to load with `ReflectionTypeLoadException: The classes in the module cannot be loaded` |
| Unity assemblies | Single monolithic `UnityEngine.dll` | There are **no** `UnityEngine.*Module.dll` files — do not reference them |
| BepInEx | 5.4.23.5 installed, built against 5.4.21 | |
| Input system | **Rewired** | Plain `Input.*` reads won't reflect game bindings |

### net35 consequences
- No `Array.Empty<T>()`, no `ValueTuple`, no `string` interpolation helpers beyond basics.
- LINQ technically works but is avoided in hot paths (closure classes are an
  extra type-load risk on old Mono, and allocations feed GC spikes).
- `Logger` in a `BaseUnityPlugin` is `BepInEx.Logging.ManualLogSource`.

---

## Build

BepInEx packages are **not** on nuget.org — they live on BepInEx's own feed,
which `nuget.config` already points at. Without it you get `NU1101`.

```bash
# Local build against the real game install (preferred)
dotnet build -c Release -p:ForestManagedPath="G:\SteamLibrary\steamapps\common\The Forest\TheForest_Data\Managed"

# Or set FOREST_MANAGED_PATH once as an environment variable and just:
dotnet build -c Release
```

If no local install is found, the project silently falls back to BepInEx's
stubbed `UnityEngine 5.6.1` package. That is how CI builds with no game files.
The build prints which source it used.

**`Assembly-CSharp.dll` is deliberately never referenced.** All game types are
reached by reflection (see `src/GameBridge.cs`). This keeps the build legal to
run in CI, and makes a game update degrade to a logged warning rather than a
compile break.

### Deploy
```powershell
./scripts/deploy.ps1          # builds and copies the DLL into BepInEx/plugins
```

---

## Architecture

The plugin is a **module host**. `Plugin.cs` does lifecycle and composition
only; every feature is a self-contained `OverlayModule`.

| Path | Responsibility |
|---|---|
| `src/Plugin.cs` | BepInEx lifecycle, HUD frame, module registration |
| `src/Core/` | Module contract and host, hotkeys, HUD builder, cursor, practice marker |
| `src/Game/` | **All reflection into The Forest.** Game-specific names live here and nowhere else |
| `src/Data/` | File-backed content (practice locations) |
| `src/Modules/` | One file per feature |
| `src/TypeExplorer.cs` | In-game class/field browser (wrapped by `ExplorerModule`) |
| `src/GameDumper.cs` | Writes analysis files to `<game root>/ForestOverlayDumps/` |
| `tools/ILScan/` | Dev-time offline IL query tool. Never shipped |
| `locations/` | Community-contributed practice spots, synced by `deploy.ps1` |

### Adding a feature

1. Add a class in `src/Modules/` deriving from `OverlayModule`.
2. Override only what you need: `Tick`, `ContributeHud`, `RegisterHotkeys`,
   `DrawPanel`.
3. Add one line to `BuildModules()` in `Plugin.cs`.

Nothing else in the codebase needs to know it exists. Modules never reach for
globals or for each other - shared services arrive via `ModuleContext`.

Every module is individually try/caught at every lifecycle hook. A module that
throws is disabled and logged; the rest keep running.

### Rules for modules

- **Never allocate in `DrawPanel`/`OnGUI`.** Build strings in `Tick` (or on a
  throttle) and cache `GUIContent`. Long lists must be virtualised.
- **Declare `IsPracticeOnly`** if the module writes game state, and call
  `Ctx.Practice.Mark(...)` at each entry point that does.
- **Declare `WantsPlayerLock`** only if the panel genuinely needs the player
  held still. The lock writes `FirstPersonCharacter.Locked`, so it is
  state-altering and taking it marks the session as practice.

### Hotkeys

`F3` practice · `F4` inventory · `F5` HUD · `F6`/`F7` save/restore position ·
`F8`/`F9` timer start-stop/reset · `F10` explorer · `F11` dumps · `F12` split

The table is declarative (`Core/HotkeyMap.cs`) and the startup log line is
generated from it, so the list can never drift from the handlers.

Note: `F12` is Steam's screenshot key by default. Rebind one of them if you
use the Steam overlay.

## Gotchas learned the hard way

1. **The game re-asserts state every frame.** `Time.timeScale = 0` does nothing —
   the game overwrites it. Same for the cursor. Either win late in the frame
   (`OnGUI` runs after `LateUpdate`) or, better, use the game's own flags
   (`FirstPersonCharacter.Locked` / `.MovementLocked`).
2. **`OnGUI` runs several times per frame.** Never allocate in it. Building a
   `GUIContent` per list row per pass caused a GC spike roughly once a second.
   The type list is virtualized and all labels are cached — keep it that way.
3. **A throwing `Awake` silently kills the plugin.** An early `Harmony.PatchAll()`
   failure meant the overlay never rendered while still logging "loaded". Every
   lifecycle method is individually try/caught for this reason.
4. **Don't trust assumed class names.** Everything in `src/Game/` was confirmed
   from a dump or from IL. If something isn't in `docs/game-notes.md`, go and
   look rather than guessing - `docs/game-notes.md` already contains one note
   that was a guess and was wrong (`VirtualCursor` was filed as
   "gamepad-related"; it is in fact the cursor owner, and that wrong guess is
   what cost v0.4.0 its cursor fix).

5. **The F11 dump only sees reflection metadata.** When the question is
   *behavioural* - what writes this field, what runs every frame, which method
   to hook - use `tools/ILScan`, which reads the real IL offline:

   ```bash
   dotnet tools/ILScan/bin/Release/net8.0/ilscan.dll writes "UnityEngine.Cursor"
   ```

---

## Project intent

### Current phase: explore the capability envelope

**As of 2026-09-21, legality enforcement is explicitly NOT the priority.** The
speedrun.com moderators have not been asked yet, and the plan is to hand them a
working tool so they can judge concretely what should be allowed. Guessing at
their ruling and pre-emptively restricting the tool would defeat that.

So, for now:

- Build the feature and find out what is possible. Do not gate, disable or
  refuse to implement something because it *might* be ruled illegal.
- Do not add new enforcement machinery, confirmation gates or lockouts.
- **Do** keep labelling things honestly - `IsPracticeOnly`, the sticky HUD
  marker and the info-only/state-altering split in the docs all stay. They cost
  nothing, and they are what makes the eventual conversation with the
  moderators concrete rather than hand-wavy.

The distinction below is therefore **descriptive, not a restriction**:

- **Info-only** (velocity, timer, item counts): reads state, never writes.
  Plausibly legal for verified runs, like an autosplitter.
- **State-altering** (teleport, position restore, player lock): writes to the
  game.

Once there is a ruling, circle back and enforce it properly - that is when the
labels become load-bearing. Until then they are just accurate reporting.

### Harmony patches

Prefer read-only `Postfix` observers. This is still worth following, but for
engineering reasons rather than legality ones: a `Prefix` that skips or
replaces game logic is far more likely to break on a game update or interact
badly with other plugins.

## Current status

Working: injection, module host, HUD, velocity (horizontal + total), timer with
splits, per-item inventory with pinnable HUD counters, type explorer, dump
system, position save/restore, file-backed teleport library, sticky practice
marker, offline IL scanner.

Recently solved (see `docs/game-notes.md`):
- `InventoryItem` field layout - `_itemId` / `_amount`
- Cursor re-lock - `VirtualCursor.LateUpdate` gated on `Input.IsMouseLocked`
- `timeScale` re-assertion - `InventoryItemView.Update`

Next up:
- Autosplit triggers. Lead: `TheForest.Tools.TfEvent+Endgame.Completed`. Needs
  a read-only Harmony `Postfix` to stay info-only, and a confirmed run-start
  trigger, which is still unidentified.
- Seed `locations/` with verified spots - the format and UI are done, the data
  is empty.
- Ask the speedrun.com moderators for a ruling on the info-only feature set.
  That conversation has still not happened.
