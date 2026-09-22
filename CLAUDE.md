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
| `src/Core/` | Module contract and host, hotkeys, HUD builder, cursor, practice marker, perf monitor, update checker, updater installer |
| `src/Game/` | **All reflection and Harmony patches into The Forest.** Game names live here and nowhere else |
| `src/Data/` | Pure data + file formats (segments, triggers, runs, line buffer, volume filter, checklists, release JSON, page grouping, shipped data) |
| `src/Modules/` | One file per feature |
| `patcher/` | `ForestOverlay.Updater` preloader patcher. Embedded in the plugin, never shipped alone |
| `tools/ILScan/` | Offline IL query tool. Dev-time only, never shipped |
| `locations/`, `collectibles/` | Shipped data, embedded in the DLL and written out on startup (`Data/ShippedData.cs`) |

Modules never reach for globals or each other — shared services arrive via
`ModuleContext`; `Host.Find<T>()` covers the rare genuine collaboration.
Every module is individually try/caught at every hook: one that throws is
disabled and logged, the rest keep running.

Where things live:

| Feature | Files |
|---|---|
| Window, tabs, player lock, cursor, **game input block** | `Core/ModuleHost`, `Modules/MainWindowModule`, `Core/CursorController`, `Game/GameInput` |
| Practice spots / segments, teleport, cave switch | `Modules/PracticeModule`, `Data/Segments`, `Data/SegmentLibrary`, `Game/GameBridge` (look angles, `SyncCaveState`) |
| Timed runs, ghosts, lines | `Modules/PracticeRunModule`, `Data/RunRecorder` (`RunCompare`), `Data/LineBuffer`, `Game/DebugDraw` (`RunLineBehaviour`) |
| Endgame split events | `Game/GameEvents` (Harmony postfixes + `endGameCutScene` poll) |
| Quick-load / practice revive | `Modules/DeathModule` (Deaths tab), `Game/DeathHooks` (Harmony prefixes) |
| Debug views, freecam, volume filters | `Modules/DebugViewModule`, `Game/DebugDraw`, `Data/VolumeFilter` |
| Perf log line | `Core/PerfMonitor` (fed by `ModuleHost`, `Plugin.OnGUI`, `DrawTarget`) |
| Updates | `Core/UpdateChecker`, `Modules/UpdateModule`, `Data/ReleaseJson`, `Core/UpdaterInstaller`, `patcher/` |

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

Tabs: Practice, Runs, Deaths, Debug views, Inventory, 100%, Settings,
Updates. The type explorer keeps its own window (`F10`) — it needs the
space and is a dev tool, not runner-facing.

While the window is open the player is held (`LockView`) and the game's key
map is switched to `Menu` (`Game/GameInput`), so clicks never reach the game.
Settings shows *Game input: blocked* when that is working.

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
| *(unbound)* | info box only; each tab; clear blood overlay |

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
- **An attached DLL can still 404 for a while.** On v0.19.1 the API listed the
  asset as `uploaded` while the runner's download got GitHub's 9-byte
  `Not Found` (Unity 5.6's `UnityWebRequest` does not flag a 404), even though
  a curl from here already got 200. Since v0.19.2 the updater treats a 404 or
  a tiny non-DLL body as "not downloadable yet" and retries every 30 s. Anyone
  on 0.19.1 or older who hits it just clicks Download again later.
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
   `strings` finds `SendMessage("name")` callers, which `refs` cannot see.

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

12. **`OnRenderObject` runs once per camera**, reflections and UI included.
    GL overlays check `DrawTarget.ShouldDraw()` so a long run line is drawn
    into the view only. The `Perf (30 s):` log line counts passes drawn and
    skipped.

13. **Search `strings` for every method of an action, not just its entry
    point.** Cutscenes are started by `SendMessage("routine")`, which `refs`
    cannot see. The red elevator sends `openDoorRoutine` directly, bypassing
    the `openKeypadDoor` that was hooked — found only from a real run's log.

14. **Labels from memory are guesses too.** game-notes had "Approaching
    Megan" and "Megan into artifact" swapped (inherited from an earlier
    session). A real endgame run caught it. Anything a runner will see by
    name — split labels especially — gets confirmed against a log.

15. **Unity 5.6's `UnityWebRequest` does not treat a 404 as an error.** Check
    `responseCode` yourself; a 404 body arrives as ordinary data.

16. **The runner's `LogOutput.log` is the test harness.** There is no game
    here to run. The author tests in game and gives the path
    `G:\SteamLibrary\steamapps\common\The Forest\BepInEx\LogOutput.log` —
    read it directly. So every new mechanism logs one line when it acts
    (`Game event:`, `Death (...)`, `Teleport to ...`, `Perf (30 s):`), and
    that line is what gets asked for.

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
interop, not legality. Harmony is HarmonyX 2.7, via `BepInEx.Core`; no extra
package. The one place that **replaces** game behaviour is `Game/DeathHooks`
(prefixes on `PlayerStats.CheckDeath` / `Fell` that skip the original only when
quick-load or revive acts). Patches take `__instance`, `__args` and
`__originalMethod`; key lookups on `Type::Method` strings, not `MethodBase`
identity.

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

**Released: v0.19.3** (2026-09-23). The author runs it via the in-game updater.
**148 tests.**

Working: module host with tabbed UI, rebindable hotkeys, HUD, velocity,
per-item inventory, 100% checklist + nature guide + To Do list, type explorer,
dumps, unified practice spots/segments with an in-game editor and zone
preview, segment-driven timed runs with checkpoints, ghosts, live deltas and
run lines, full player-state capture, **separate endgame split events**,
**quick-load on death**, **practice revive**, cave-aware teleports, debug views
(freecam / colliders / triggers / wireframe, with size and name filters), game
input blocked while the window is open, a 30 s perf log line, self-installing
updates, offline IL scanner.

### Key concepts

- **Spots and segments are one thing.** Every Practice entry is somewhere to
  teleport; tick "Timed segment" and it gains start/end triggers and
  checkpoints. There is no separate "anchor".
- **Triggers** (`zone`, `box`, `item`, `event`, `manual`) are the spine —
  splits, segment bounds and eventually autosplits are all "a trigger fired".
  Item triggers can be **relative** (`+3` = three more than at the start).
  `event` triggers fire from `Game/GameEvents`; the segment editor steps
  through the known names with `<` `>`.
- **Endgame events**, route order: `vault-door`, `timmy-pickup`,
  `megan-transform` (approaching Megan), `megan-pickup`, `megan-to-machine`
  (Megan into the artifact), `gold-door`, `red-elevator`, `game-end`
  (`end-crash` / `end-shutdown`). Also `endgame-cutscene` (every cutscene —
  the old autosplitter's behaviour), `keycard-door`, `keycard-door-<itemId>`,
  `timmy-goodbye`, `raft-out-of-world`. Each fires on the rising edge of
  `endGameCutScene`, so split times are **frame-identical to the LiveSplit
  autosplitter**; the Harmony postfix only says which cutscene it was.
- **Deaths** (Deaths tab): practice mode on + a current spot → **revive** at
  the spot on every death, capture included (health 100, blood cleared, no
  reload, marks practice). Otherwise **quick-load** (toggle, on) — every death,
  incl. the boss-fight wake-up; the first-death capture has its own toggle
  (on: no current route uses the capture). Never permadeath or multiplayer.
  The author rules quick-load **allowed in normal runs**: it loads through
  the title screen's own path, so the game loaded is identical.
- **Runs** record position at 30 Hz and ~60 named player-state channels at
  5 Hz (read only when a sample is due), discovered by reflection so a game
  update adds stats for free. Attempts persist per segment id and carry a
  **route fingerprint**, so moving a zone retires old times instead of
  letting them compete.

### Confirmed in game vs awaiting a check

Confirmed by the author: game-input block and freecam hold (v0.17.0), every
endgame split incl. vault / gold door / red elevator (v0.18.x), quick-load
and practice revive (v0.19.2), the self-updater end to end.

**Awaiting an in-game check** — ask before building on these:
- **Pitch after teleport / window close** (v0.17.1). The camera rotator keeps
  pitch only in its angles and our rebase used to zero them; fixed per
  game-notes *Camera*. The author reported the bug ("pitch always snaps
  straight") but has not confirmed the fix.
- **Cave teleport** (v0.19.1). Asked, no answer yet. The log line to look for
  is `Teleport to '<spot>': entered cave.` (v0.19.3+).
- **Boss-fight death quick-loads** instead of the game's wake-up in the boss
  room. Offered the author a choice; no answer yet. Easy to exclude in
  `DeathModule.Decide` (`DeathKind.BossWake`).
- `end-shutdown`, `timmy-goodbye`, `raft-out-of-world` never seen in a log.

### Open threads

- **Another runner's slowdown with ghost lines / recording.** Not reproducible
  on the author's machine (4080 Super / 7800X3D). Blind fixes shipped in
  v0.17.1 (see git log). The author will get that runner's `LogOutput.log`;
  read its `Perf (30 s):` lines (`GL ms/frame`, `verts/frame`, `GC x`, `heap`
  vs `overlay` KB/s) before changing anything.
- **Author's own perf (v0.19.2 log):** overlay tick ≤ 0.01 ms, overlay
  allocation ~10% of heap growth, GC 0–1 per 30 s in play; the big frames are
  loads and game streaming. Nothing to fix. A GC every ~7.5 s was seen once
  with cheats on (`developermodeon`, `speedyrun`) — not reproduced since.
- **Nature guide page names are unverified.** Pages are derived from the tick
  marks' hierarchy (`Data/PageGrouping.cs`) and named after the page
  GameObjects, which may read as "Page 3" rather than "Birds". The runner who
  asked for it (maks) should send a `natureguide_*.txt` from the 100% tab's
  **Write dumps**; use it to check the grouping and name the pages.
- **Installs older than v0.16.2 cannot download updates** — one manual install
  of a current release, then automatic. Installs older than v0.19.2 can hit
  the post-release 404 (see *Releases and updates*); clicking Download again
  a minute later works.

### Next up

Ordered by what runners feel soonest for the effort. Items marked *(runner)*
came from runners' own requests (2026-09-22 idea dump); the interpretation was
checked with the author.

1. **Savestates** via the game's own `LoadSave`/`LevelSerializer`, so AI,
   health and inventory are restored rather than reconstructed badly. The
   foundation for several runner requests:
   - restarting a segment respawns dropped/used world items (e.g. the
     keycard, item 210) — or a plain restart when nothing needs respawning;
   - resetting to the **exact** start state: built walls and structures
     removed, picked-up items back in place (probably needs a fast reload);
   - some segments need game state *preserved* across a restart instead —
     make it a per-segment choice. *(runner)*

   **Start with research, not code** — the author was asked and has not yet
   answered; propose a plan from ILScan findings first. Known starting
   points: the title-screen load path and `LoadSave.ShouldLoad`
   (game-notes *Deaths*), `LoadSave.Activation`, `PlayerStats.JustSave`
   (calls `Input.SetState`), `LevelSerializer` (progress callback on
   `LoadSave`), `GameSetup.Slot`. Quick-load already proves a save can be
   reloaded through the menu path from in game.
2. **LiveSplit split file import** (`.lss`/`.lsl`) — needed to replace
   LiveSplit rather than sit beside it. Plus HUD/layout customisation. The
   author's autosplitter is the reference for what runners split on — see
   memory `autosplitter-repo` (github.com/1deter/auto-splitters).
3. **forest.deter.cloud — shared runs and a web viewer.** Local-first,
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
     are mesh geometry under the terrain, so a full map needs a visit pass
     plus a "dump loaded geometry" button. Wants a scrub bar and annotations.
4. **TAS** — exploratory only. Builds on savestates and the recorder.
5. Runs tab layout (deferred; it still builds strings in `DrawTab`, against
   the module rules), Timmy-drawing sub-pieces
   (`DrawingsInventoryItemView._ids`), freeform zone shapes.

Shipped from the old list: practice QoL (v0.17.0–0.17.1), separate endgame
splits (v0.18.0–0.18.2), deaths and caves (v0.19.0–0.19.1), nature guide
(v0.15.0).

### How a session goes

The author tests in game and reports back with the `LogOutput.log` path; they
answer design questions quickly and mid-turn. After each change that builds
and passes tests: bump the version (csproj `Version`/`AssemblyVersion`/
`FileVersion` **and** `Plugin.PluginVersion`), commit, push, tag, then watch
the asset URL with a background `Monitor` and say when it is attached — never
`api.github.com`. Do not deploy into the game folder. Mark decisions made with
the author in this file, with who decided.
