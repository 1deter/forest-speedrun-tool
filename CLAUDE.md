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

| File | Responsibility |
|---|---|
| `src/Plugin.cs` | Lifecycle, hotkeys, HUD rendering, cursor/player lock, practice save-restore |
| `src/GameBridge.cs` | All reflection into The Forest's own types. **Game-specific names live here and nowhere else.** |
| `src/TypeExplorer.cs` | In-game browser for the game's classes and live field values |
| `src/GameDumper.cs` | Writes analysis files to `<game root>/ForestOverlayDumps/` |

### Hotkeys
`F5` HUD · `F6`/`F7` save/load position · `F8`/`F9` timer · `F10` explorer · `F11` dumps

---

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
4. **Don't trust assumed class names.** Everything in `GameBridge` was confirmed
   from an F11 dump. If something isn't in `docs/game-notes.md`, dump it and look
   rather than guessing.

---

## Project intent

The run-legality distinction matters and shapes the architecture:

- **Info-only features** (velocity, timer, item counts) read state and never
  write it. These are plausibly legal for verified runs, like an autosplitter.
- **State-altering features** (position restore, player lock) write to the game
  and are practice-only.

Keep these separated. Harmony patches should be read-only `Postfix` observers
unless a feature is explicitly practice-only. The speedrun.com moderators have
not yet been asked for a ruling — that conversation is still pending.

---

## Current status

Working: injection, HUD, velocity, timer, type explorer, dump system,
inventory item count, position save/restore.

Next up:
- Per-item inventory breakdown — needs `InventoryItem`'s field layout
  (filter the explorer for `InventoryItem` and inspect it)
- Verify the cursor fix actually holds; if not, find what re-locks it
- Autosplit triggers via Harmony once a suitable method is identified
