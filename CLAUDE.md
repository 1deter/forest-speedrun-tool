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

## Tests

```bash
dotnet test tests/ForestOverlay.Tests/ForestOverlay.Tests.csproj
```

The plugin targets net35 and cannot be referenced from a modern test runner,
so the files under test are **linked into** the test project and compiled
against a tiny `UnityEngine` shim (`tests/.../UnityShim.cs`). No game files and
no Unity needed; CI runs them on every push.

The shim only implements `Vector3`, `Vector2` and `Mathf` - arithmetic with one
unambiguous definition each. **If it ever needs `Quaternion`, `Transform` or
anything with Unity-specific semantics, that is a signal the logic under test
is not pure and should be refactored - not that the shim should grow.**

BepInEx's UnityEngine stub is deliberately *not* used here: its method bodies
are empty, so `Vector3.Distance` would return 0 rather than compute, which is
worse than no test.

Only genuinely pure files belong in the linked set. Anything touching
MonoBehaviour, reflection into the game, or the filesystem does not.

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

All keys are **rebindable** - in game via the settings panel (`F2`), or by
editing `BepInEx/config/com.deter.forestoverlay.cfg`. Both write the same
BepInEx `ConfigEntry`.

| Default | Action |
|---|---|
| *(F1 left free)* | the game's own dev console uses it when enabled |
| `F2` | Settings / keybinds |
| `F3` | Practice panel (anchor, teleports) |
| `F4` | Inventory panel |
| `F5` | Toggle HUD |
| `F6` | Set anchor here |
| `F7` | Return to anchor (also restarts a practice run) |
| `F8` | Practice runs panel |
| `F9` | Practice mode on / off |
| `F10` | Type explorer |
| `F11` | Write dumps |
| `F12` | Finish practice run |
| `Insert` | Debug views panel |
| `End` | Updates panel |
| `Keypad *` | Toggle freecam |
| `[` | Abort practice run |

Modules register their own keys with a stable id, so the settings panel, the
config file and the startup log line are all generated from one table and
cannot drift from the handlers. Adding a key is one `map.Add(...)` call.

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

Working: module host, rebindable hotkeys, HUD, velocity, per-item inventory,
type explorer, dumps, anchor-based practice teleports with a community
location library, practice runs with ghost deltas and persisted attempts,
debug views (freecam / colliders / triggers / wireframe), update checking,
offline IL scanner.

`TimerModule` is written but **deliberately not registered** - its manual
start/stop/split clashed with the practice run keys and has no purpose until
automatic, configuration-driven splits are designed. The file is kept so that
work has somewhere to land.

### Practice runs

Being placed at the anchor **arms** a run; the clock starts when you actually
move (start radius 0.5m), `F12` finishes. Practice mode is **off by default**
and toggled with `F9`. The delta reads "at the point you are standing, the
reference run had taken N seconds". Attempts persist per anchor under
`BepInEx/config/ForestOverlay/runs/<anchor>/`, one plain text file each, so a
folder is a shareable track.

### Updates

`Core/UpdateChecker.cs` queries the GitHub releases API on startup and stages a
download beside the plugin as `ForestOverlay.dll.pending`.

**It cannot apply the update itself** - Windows will not let a loaded assembly
be overwritten, and ours is loaded by definition. Applying it needs code that
runs *before* plugins load, i.e. a BepInEx **preloader patcher** in
`BepInEx/patchers/`. That piece is not written yet; until it is, the staged
file sits there and a restart does nothing with it.

UnityWebRequest is used rather than `HttpWebRequest` because Unity 5.6's Mono
predates TLS 1.2 and GitHub requires it; UnityWebRequest uses the OS stack. It
is reached by reflection so the CI stub build still compiles.

### Segments and triggers (the spine)

Almost every remaining feature needed the same missing concept: **a named
thing that happens**. Splits, segment start/end, checkpoints and leaderboard
keys are all "a trigger fired", so it is defined once in `Data/Segments.cs`
rather than reinvented per feature.

Trigger kinds: `zone` (sphere), `item` (inventory comparison), `event` (a
named in-process game event), `manual`.

**Triggers are edge-based, and the first evaluation primes rather than
fires.** That is deliberate and unit-tested: teleporting *into* a start zone
must not start the run before you have moved.

Segments live in `BepInEx/config/ForestOverlay/segments/*.txt` as `key = value`
blocks under `[segment]` headers - a block format rather than the pipe format
locations use, because a segment has a variable number of checkpoints.

`Segment` is pure data with **no `GUIContent`**: it is linked into the test
project, which has no Unity. Label caching belongs to the panel that draws it.
`TriggerParser` is likewise split out of `SegmentLibrary` so the parsing can be
tested without dragging in BepInEx and the filesystem.

### Spots, not anchors

There is **no separate "anchor" concept**. An earlier version had a manually
set anchor *and* a teleport library, which overlapped confusingly: if you can
save a spot, setting a nameless anchor as well is redundant.

So the spot you last teleported to **is** where the next attempt starts from,
`F6` saves where you stand as a real named spot (and selects it), and `F7`
returns to the selected one. A spot is also what a segment grows out of -
attach start/end triggers to one and it becomes timed and splittable.

**Everything must eventually be editable in the GUI.** Runners should never
have to open a config file; the text formats exist so sets can be shared and
diffed, not as the primary interface.

### Segment id convention

Lowercase kebab-case, dot-separated from broad to narrow:

    route.plane-to-cave5
    cave5.sinkhole-drop
    practice.rope-skip

Momentum keys zones off map plus stage index, KSF off `map_stage`. Neither
translates here because The Forest is one continuous world with no map names,
so the first token names the *route or area* instead.

Ids are the comparison key, so **renaming one orphans every time recorded
against it**. Choose before sharing a set.

### Player state capture

`Game/PlayerStateReader.cs` discovers every numeric and boolean field on
`PlayerStats` by reflection and records them as **named channels** - Health,
Stamina, Energy, Fullness, Thirst, BodyTemp, Armor, Cold, PedometerSteps and
~50 more. Hand-picking fields would decide today what matters and leave
everything else unbackfillable.

Two tracks at different rates, on purpose: position at 30 Hz so the line is
smooth, state at 5 Hz because stats do not change meaningfully per frame and
~60 channels at 30 Hz would inflate a run by an order of magnitude.

`TryStateAt` is a **step** lookup, not interpolated - several channels are
booleans and interpolating those would invent states that never happened.

`RunRecorder` stays free of reflection so it can be linked into the tests; the
module feeds it the channel array.

### Decisions taken 2026-09-22

- **Leaderboards are comparative, not competitive.** Lines and ghosts for
  practice, no verified ranking - so client-submitted times need no
  anti-cheat story.
- **The in-game timer aims to replace LiveSplit**, not complement it. That
  makes reading existing LiveSplit split files and HUD/layout customisation
  real requirements.
- **Record everything about the player per sample**, not a chosen subset, and
  let the runner filter later. Samples are cheap; re-recording history is not.
- **Web viewer wants real 3D terrain** as a heavier secondary option, because
  2D maps fall apart in caves. Plus annotations for concept lines, and a
  scrub bar for replay - Momentum-style.
- Flower/plant coordinate display is **out of scope by the author's own
  call**: it pushes what the category should allow.

### Next up

1. **GUI editor for spots and segments** - create, edit, and attach triggers
   without touching a file. This is the blocker on segments being testable at
   all, and the author has been clear that config-file editing is not an
   acceptable interface for runners.
2. **Wire segments into practice runs** - select a segment, auto start/stop on
   its triggers, split on checkpoints. The data layer is done and tested; the
   module still uses the spot flow.
3. **Separated endgame splits** via Harmony `Postfix` on the individual action
   classes (see game-notes). This is the thing an external autosplitter
   cannot do.
4. **Nature guide + todo panel** - `SurvivalBookBestiary` / 
   `SerializableSurvivalBookTodo`, both read-only.
5. **LiveSplit split file import** (`.lss` / `.lsl`).
6. **Preloader patcher** to apply staged updates.
7. **Web viewer** - local-first, export always; cloud later.
