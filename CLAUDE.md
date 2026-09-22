# ForestOverlay — project context

A BepInEx plugin for **The Forest**: speedrun information, practice tooling,
and a segment/run system with ghosts and deltas.

Read this first when picking the project back up.
Game internals live in [`docs/game-notes.md`](docs/game-notes.md) — everything
there is confirmed from a dump or from IL, never guessed.

---

## Hard runtime facts (do not re-derive)

| Fact | Value | Why it matters |
|---|---|---|
| Unity | **5.6.5** | Predates engine module splitting |
| Runtime | Mono, CLR 2.0.50727 | Means **.NET Framework 3.5** |
| Target | **net35** | Anything newer fails to load (`ReflectionTypeLoadException`) |
| Unity assemblies | One monolithic `UnityEngine.dll` | There are **no** `UnityEngine.*Module.dll` files |
| BepInEx | 5.4.23.5 installed, built against 5.4.21 | |
| Input | **Rewired** | Plain `Input.*` won't reflect game bindings — hence F-keys |

**net35 consequences:** no `Array.Empty<T>()`, no `ValueTuple`. LINQ works but is
avoided in hot paths (closure classes are a type-load risk on old Mono).
`Logger` is `BepInEx.Logging.ManualLogSource`.

**`Assembly-CSharp.dll` is never referenced.** All game types are reached by
reflection, so CI builds with no game files and a game update degrades to a
logged warning instead of a compile break.

---

## Commands

```bash
# Build against the real install (FOREST_MANAGED_PATH is User-scope; shells
# spawned by tooling do NOT inherit it - read it explicitly and pass it)
dotnet build -c Release -p:ForestManagedPath="<path>\TheForest_Data\Managed"

dotnet test tests/ForestOverlay.Tests/ForestOverlay.Tests.csproj
```

```powershell
./scripts/deploy.ps1 -GameRoot $env:FOREST_ROOT   # build + copy the DLL into the game
```

Deploy fails with "user-mapped section open" if the game is running. **Do not
deploy into the author's install unasked** — it now updates through the real
release path (see *Releases and updates*), and a hand-copied DLL hides whether
that path works.

BepInEx packages are on BepInEx's own feed (`nuget.config`), not nuget.org.
With no local install the build falls back to BepInEx's stubbed UnityEngine —
that is how CI works. The build prints which source it used.

### Tests

Files under test are **linked into** the test project and compiled against a
tiny `UnityEngine` shim (`tests/.../UnityShim.cs`). BepInEx's stub is not used:
its method bodies are empty, so `Vector3.Distance` would return 0.

The shim implements `Vector3`, `Vector2`, `Mathf` only. **If it ever needs
`Quaternion` or `Transform`, that means the code under test is not pure and
should be refactored — not that the shim should grow.** Only pure files can be
linked; anything touching MonoBehaviour or reflection cannot. The one
filesystem exception is `patcher/PendingSwap.cs`, tested against real temp
folders because it is the code that can break an install.

---

## Architecture

`Plugin.cs` does lifecycle and composition only. Every feature is an
`OverlayModule`; adding one is a class in `src/Modules/` plus one line in
`BuildModules()`.

| Path | Responsibility |
|---|---|
| `src/Core/` | Module contract and host, hotkeys, HUD builder, cursor, practice marker, update checker, updater installer |
| `src/Game/` | **All reflection into The Forest.** Game names live here and nowhere else |
| `src/Data/` | Pure data + file formats (segments, triggers, runs, checklists, release JSON, page grouping, shipped data) |
| `src/Modules/` | One file per feature |
| `patcher/` | `ForestOverlay.Updater` preloader patcher. Embedded in the plugin, never shipped alone |
| `tools/ILScan/` | Offline IL query tool. Dev-time only, never shipped |
| `locations/`, `collectibles/` | Shipped data, embedded in the DLL and written out on startup (`Data/ShippedData.cs`) |

Modules never reach for globals or each other — shared services arrive via
`ModuleContext`; `Host.Find<T>()` covers the rare genuine collaboration.
Every module is individually try/caught at every hook: one that throws is
disabled and logged, the rest keep running.

### Rules for modules

- **Never allocate in `DrawTab`/`OnGUI`.** Build strings in `Tick` (throttled)
  and cache `GUIContent`. Long lists must be virtualised.
- **Declare `IsPracticeOnly`** if it writes game state, and call
  `Ctx.Practice.Mark(...)` at each entry point that does.
- **Lay panels out vertically**, not packed across a row at fixed offsets —
  that clips on narrow widths.
- **If it can fail invisibly, show why on screen.** A dead toggle, an empty
  search and a timer that never starts were all reported as "nothing happens".

### UI

**`F2` opens one window; everything is a tab.** A hotkey per panel does not
scale. Per-feature keys still exist and are rebindable, but they open the
window on that tab and are **unbound by default**.

The type explorer keeps its own window (`F10`) — it needs the space and is a
dev tool, not runner-facing.

| Default | Action |
|---|---|
| `F2` | Open the ForestOverlay window |
| `F5` | Show / hide **all** overlay UI |
| `F6` | Save spot here |
| `F7` | Return to current spot |
| `F9` | Practice mode on / off |
| `F10` | Type explorer |
| `F11` | Write dumps |
| `F12` | Manual split / finish |
| `[` | Abort run |
| `Keypad *` | Freecam |
| *(unbound)* | info box only; each tab; `\` free-timer split |

`F1` is deliberately free — the game's own dev console uses it.
All keys are rebindable in **Settings**, or in
`BepInEx/config/com.deter.forestoverlay.cfg`.

### Releases and updates

**The plugin DLL is the whole install.** It carries the shipped data files
and the update patcher as embedded resources and writes both out on startup.

```
tag vX.Y.Z -> CI builds + tests -> GitHub Release with ForestOverlay.dll
  -> in game: startup check, Updates tab -> Download
  -> ForestOverlay.dll.pending beside the plugin
  -> next launch: ForestOverlay.Updater (BepInEx/patchers) runs before plugins,
     moves .pending into place, keeps the old DLL as .bak
```

- **Version lives in two places** — `ForestOverlay.csproj` and
  `Plugin.PluginVersion`. They must match; the updater compares against the
  latter.
- **A tag publishes before its DLL is attached.** Wait for the asset, not the
  release, before telling anyone to update. The plugin reads a release with no
  DLL as "still being published" and re-checks every minute.
- **Never poll `api.github.com` to watch a release.** Anonymous API calls are
  limited to 60 an hour *per IP*, shared with the author's own game — polling
  once locked their in-game update check out for an hour. Poll the asset
  instead; downloads are not API calls:
  `curl -s -o /dev/null -w '%{http_code}' -L https://github.com/1deter/forest-speedrun-tool/releases/download/vX.Y.Z/ForestOverlay.dll`
  (200 = attached).
- **Rollback:** close the game, delete `ForestOverlay.dll`, rename
  `ForestOverlay.dll.bak` to `ForestOverlay.dll`. A download that is not the
  ForestOverlay assembly is renamed `.rejected` and never installed.
- **The patcher updates less reliably than the plugin** — it is loaded while
  the game runs, so `Core/UpdaterInstaller` swaps it by renaming the loaded
  copy aside. Keep `patcher/` small and its behaviour stable.
- **Manual test without a release:** save any ForestOverlay.dll as
  `BepInEx/plugins/ForestOverlay.dll.pending` and launch.
- **Confirmed end to end in game (v0.16.2 -> v0.16.3):** check, Download,
  restart, installed, `.bak` kept — and the plugin replaced the loaded patcher
  by renaming it aside.

---

## Gotchas learned the hard way

1. **The game re-asserts state every frame — use its flags, don't fight it.**
   `timeScale` is overwritten by `InventoryItemView.Update`. The cursor is
   overwritten by `VirtualCursor.LateUpdate`, which *warps the pointer to
   screen centre*, so no later write can fix it. Both are solved by setting
   the game's own flag (`Input.IsMouseLocked`, `FirstPersonCharacter.LockView`).
   "Win the frame" is not a strategy; find the flag.

2. **`OnGUI` runs several times per frame.** Never allocate in it.

3. **A throwing `Awake` silently kills the plugin** while BepInEx still logs
   "loaded". Every lifecycle method is individually try/caught.

4. **Don't trust assumed names.** Everything in `src/Game/` was confirmed from
   a dump or IL. `docs/game-notes.md` once contained a guess that was wrong
   (`VirtualCursor` filed as "gamepad-related"), and that guess cost a release.

5. **The F11 dump only sees reflection metadata.** For behavioural questions —
   what writes this field, what runs every frame, which method to hook — use
   `tools/ILScan`, which reads real IL offline:
   ```bash
   dotnet tools/ILScan/bin/Release/net8.0/ilscan.dll writes "UnityEngine.Cursor"
   ```

6. **Cached component references go stale across a save load.** Unity's
   fake-null makes them look merely absent. Re-resolve, and prefer the game's
   statics (`LocalPlayer.Inventory`) over `FindObjectOfType`.

7. **Edge semantics matter.** A start zone fires on *crossing* (you spawn
   inside it); checkpoints and ends fire on *entry*. Getting this wrong made
   the clock never start.

8. **Test against real payloads, not remembered ones.** GitHub's API
   pretty-prints (`"name": "x"`); the asset lookup matched only the compact
   form, so no update ever downloaded while the version check looked
   healthy. `tests/.../ReleaseJsonTests.cs` holds a trimmed real response.

9. **Never round-trip text through Windows PowerShell 5.1**
   (`Get-Content | Set-Content`). It reads BOM-less UTF-8 as cp1252 and turns
   every `—` into `â€”`. Edit with the editor tools, Python with an explicit
   encoding, or `sed`. Check: `git grep -n -I -P 'â€|Ã|Â' -- ':!CLAUDE.md'`.

10. **Anything that only reaches a machine via `deploy.ps1` is missing for
    runners.** The 100% list did exactly that. Ship data inside the DLL.

11. **`Resources.FindObjectsOfTypeAll` walks every loaded object.** Calling it
    on a 1 Hz refresh was a visible once-a-second stutter. Find a component
    once, keep it, re-search only when it goes fake-null, and rate-limit the
    search (there is nothing to find at the main menu). `ModuleHost` logs any
    module Tick over 5 ms as `Slow tick: '<id>'` — check the log for it before
    guessing at a hitch.

---

## Project intent

### Current phase: explore the capability envelope

**Legality enforcement is explicitly not the priority.** The speedrun.com
moderators have not been asked; the plan is to hand them a working tool so they
can judge concretely. Do not gate, disable or refuse a feature because it
*might* be ruled illegal.

Keep the honest **labelling** though — `IsPracticeOnly`, the sticky HUD marker,
the info-only/state-altering split. It costs nothing and makes that
conversation concrete. Once there is a ruling, circle back and enforce it.

Flower/plant coordinate display is **out of scope by the author's own call**.

Prefer read-only Harmony `Postfix` observers — for update resilience and plugin
interop, not legality.

### Conventions

- **Data, not code.** Locations, segments and the 100% checklist are text files
  so they can be shared, diffed and edited by non-programmers. Anything
  admin-decided or community-contributed belongs in a file.
- **Everything must be editable in the GUI.** The text formats exist for
  sharing, not as the interface. A data-driven feature without an editor is
  not finished.
- **Segment ids** are author-namespaced, dot-separated:
  `deter/route.plane-to-cave5`. They are the comparison key — **no SteamID and
  no timestamp**, or two people running the same route could never be compared.
  Renaming one orphans every time recorded against it.
- **Leaderboards are comparative, not competitive** — lines and ghosts, no
  verified ranking, so client-submitted times need no anti-cheat story.
- **The in-game timer aims to replace LiveSplit**, not sit beside it.

---

## Current status

Working: module host with tabbed UI, rebindable hotkeys, HUD, velocity,
per-item inventory, 100% checklist + nature guide + To Do list, type explorer, dumps,
unified practice spots/segments with an in-game editor and zone preview,
segment-driven timed runs with checkpoints, ghosts, live deltas and run lines,
full player-state capture, debug views (freecam / colliders / triggers /
wireframe), self-installing updates (download in game, applied by a preloader
patcher on restart), offline IL scanner. 125 tests.

### Key concepts

- **Spots and segments are one thing.** Every Practice entry is somewhere to
  teleport; tick "Timed segment" and it gains start/end triggers and
  checkpoints. There is no separate "anchor".
- **Triggers** (`zone`, `box`, `item`, `event`, `manual`) are the spine —
  splits, segment bounds and eventually autosplits are all "a trigger fired".
  Item triggers can be **relative** (`+3` = three more than at the start).
- **Runs** record position at 30 Hz and ~60 named player-state channels at
  5 Hz, discovered by reflection so a game update adds stats for free.
  Attempts persist per segment id and carry a **route fingerprint**, so moving
  a zone retires old times instead of letting them compete.

### Open threads

- **Nature guide page names are unverified.** Pages are derived from the tick
  marks' hierarchy (`Data/PageGrouping.cs`) and named after the page
  GameObjects, which may read as "Page 3" rather than "Birds". The runner who
  asked for it (maks) should send a `natureguide_*.txt` from the 100% tab's
  **Write dumps**; use it to check the grouping and name the pages.
- **The runner's own install** predates the download fix. Anything below
  v0.16.2 cannot download updates, so they need one manual install of a
  current release; after that updates are automatic.

### Next up

Ordered by what runners feel soonest for the effort. Items marked *(runner)*
came from runners' own requests (2026-09-22 idea dump), often in few words;
the interpretation given here was checked with the author.

1. **Practice quality-of-life** — small, and runners are practising now.
   - **Freecam holds the player still.** It currently moves the view but the
     body still takes movement input. And trigger/collider/wireframe drawing
     is centred on the player, so it should follow the freecam camera
     instead. *(runner)*
   - **Filter large debug volumes.** Collider *and* trigger views are swamped
     by huge volumes (area and cave-load boxes) that hide the small ones that
     matter. Add a size cap and/or an exclude list so important hitboxes read
     clearly. *(runner)*
   - **Clicks in the overlay reach the game** — clicking a button while
     holding the plane axe swings it. Lead (IL, see game-notes *Input
     states*): every game overlay calls
     `TheForest.Utils.Input.SetState(InputState.X, true)` on open and `false`
     on close — the pause menu uses `Menu`, the dev console `Chat` — which
     switches the Rewired maps. Do the same while the window is open. It is a
     flag, not a per-frame fight (gotcha 1). Verify in game that a click no
     longer swings. *(author)*
   - **Look direction is wrong after closing the window.** Yaw/pitch (and
     roll) saved with a spot do not end up applied once the overlay is closed,
     e.g. after Go / `F7` from the Practice tab. A fix was attempted
     (`GameBridge.ApplyLook` + `resetOriginalRotation`, game-notes *Camera*)
     and it still fails, so **reproduce in game before changing code** —
     suspects: the unlock path (`UnLockView`) running after the rebase was
     consumed, and the rebase adopting a rotation that is then recomposed.
     Roll is not captured or reset at all; a tilt left over from a fall or a
     ragdoll would persist. *(author)*
   - **Replay / practice-run performance.** Runners report slowdown while
     ghost lines play or a run records. Suspects: `RunRecorder` sampling
     (30 Hz position, 5 Hz reflection over ~80 state channels) and line
     drawing. **Measure first** — `ModuleHost` logs `Slow tick:` over 5 ms.
     *(runner)*
2. **Separated endgame splits** — Harmony `Postfix` on each action class
   (`PlayerPickupTimmyAction`, `PlayerGirlPickupAction`, etc; table in
   game-notes). The shared `endGameCutScene` flag is why the author's
   autosplitter could not separate them, and the call sites carry the identity
   the flag does not. **The clearest thing a plugin can do that an external
   autosplitter cannot.** Wire these to the `event` trigger kind, which parses
   and saves but currently never fires.
3. **Deaths and caves.**
   - **Quick-load on death** — skip the death animation and load straight
     back into the save instead of waiting to quit to menu. The author rules
     this **allowed in normal runs** (not practice-only): it goes through the
     game's own load, so it saves real time without altering game state. Use
     the same load path the menu uses so the result is identical. *(runner)*
   - **Practice-only death recovery** — for risky tricks (sinkhole jump,
     cave 5 drop): respawn at the practice spot without a full reload, and
     clear the blood overlay that builds up after repeated fall damage and
     never fades. Writes state → `IsPracticeOnly`. *(runner)*
   - **Teleporting into a cave loads the cave.** Cave geometry streams in on
     entry, so a teleport to an unloaded cave lands in nothing. Find the
     game's cave load trigger with ILScan and invoke it before the teleport.
     *(runner)*
4. **Savestates** via the game's own `LoadSave`/`LevelSerializer`, so AI,
   health and inventory are restored rather than reconstructed badly. This is
   the foundation for several runner requests:
   - restarting a segment respawns dropped/used world items (e.g. the
     keycard, item 210) — or a plain restart when nothing needs respawning;
   - resetting to the **exact** start state: built walls and structures
     removed, picked-up items back in place (probably needs a fast reload);
   - some segments need game state *preserved* across a restart instead —
     make it a per-segment choice. *(runner)*
5. **LiveSplit split file import** (`.lss`/`.lsl`) — needed to replace
   LiveSplit rather than sit beside it. Plus HUD/layout customisation.
6. **forest.deter.cloud — shared runs and a web viewer.** Local-first,
   export always; the cloud holds players' best runs so they can be compared
   without clogging the GitHub repo. *(runner)*
   - Already true locally: runs are segment-based (a start → end "stage" such
     as plane spawn → cave 5, not free-form), attempts save per segment id in
     the config folder, and segments carry a category. Cloud comparison keys
     on the segment id + route fingerprint, which is why ids never embed a
     SteamID or timestamp.
   - Web panel: everyone's runs vs your own, with data visualisation — look
     at how Momentum Mod does replays and comparison for the model.
   - 3D terrain is tractable above ground (Unity `Terrain` heightmap); caves
     are mesh geometry that streams in on entry, so a full map needs a visit
     pass plus a "dump loaded geometry" button. Wants a scrub bar and
     annotations.
7. **TAS** — exploratory only. Builds on savestates and the recorder.
8. Runs tab layout (deferred), Timmy-drawing sub-pieces
   (`DrawingsInventoryItemView._ids`), freeform zone shapes.

Nature guide in the 100% tab was also requested and **shipped in v0.15.0**.
