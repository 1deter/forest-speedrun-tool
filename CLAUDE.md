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
| Variable text in panels, on-screen notice | `Core/UiText` (wraps, returns height), `Core/Notice` (`Ctx.Notice`, drawn by `Plugin.OnGUI`) |
| Savestates, segment start states | `Modules/SavestateModule`, `Game/SavestateBridge` (incl. cross-save `AdoptPlayer`), `Game/PickupKeeper`, `Game/PanelKeeper` (cave panels), `Game/BookPages` + `Data/BookPageState` (book page), `Data/SavestateFile`; restart flow in `Modules/PracticeModule` (`Restart`; `Teleport` is Go); retire warning via `Data/AttemptStore.CountOnRoute` |
| Practice spots / segments, teleport, cave switch | `Modules/PracticeModule`, `Data/Segments`, `Data/SegmentLibrary`, `Game/GameBridge` (look angles, `SyncCaveState`) |
| Timed runs, ghosts, lines | `Modules/PracticeRunModule`, `Data/RunRecorder` (`RunCompare`), `Data/LineBuffer`, `Game/DebugDraw` (`RunLineBehaviour`) |
| Endgame split events | `Game/GameEvents` (Harmony postfixes + `endGameCutScene` poll) |
| Quick-load / practice revive | `Modules/DeathModule` (Deaths tab), `Game/DeathHooks` (Harmony prefixes; `HandleLanded` prefix/postfix for the fall revive) |
| Debug views, freecam, volume filters | `Modules/DebugViewModule`, `Game/DebugDraw`, `Data/VolumeFilter` |
| Perf log line | `Core/PerfMonitor` (fed by `ModuleHost`, `Plugin.OnGUI`, `DrawTarget`) |
| Updates, changelog | `Core/UpdateChecker` (incl. `TidyPluginFolder`), `Modules/UpdateModule`, `Data/ReleaseJson` (`ExtractNotes`), `Data/UpdateStaging` (staging under any file name), `Core/UpdaterInstaller`, `patcher/`, `CHANGELOG.md` |
| Load leak diagnostics and fix | `Game/LoadWatcher` (every load), `Game/MemoryCensus` (static + DontDestroyOnLoad roots, sizes, threads, Unity objects by type), `Game/LeakedThreads` (stops the two threads a load leaves), `Game/StaleSubscribers` (drops dead event subscribers), run from `Modules/SavestateModule` |
| Timed run split order | `Data/SplitSequence` (pure, tested) |
| **Live test bridge** (dev) | `Modules/BridgeModule` (file polling, queue, commands), `Game/ObjectProbe` (generic reflection: find / inspect / get / set / call), `Data/BridgeCommand` (parsing, tested), `scripts/bridge.sh` (this end) |

### Rules for modules

- **Never allocate in `DrawTab`/`OnGUI`.** Build strings in `Tick` (throttled)
  and cache `GUIContent`. Long lists must be virtualised.
- **Declare `IsPracticeOnly`** if it writes game state, and call
  `Ctx.Practice.Mark(...)` at each entry point that does.
- **Lay panels out vertically**, not packed across a row at fixed offsets —
  that clips on narrow widths.
- **If it can fail invisibly, show why on screen.** A dead toggle, an empty
  search and a timer that never starts were all reported as "nothing happens".
- **Variable text goes through `Core/UiText.Draw`** — status lines, errors,
  descriptions, anything whose length is not fixed. It wraps to the width
  given, takes the height it needs and returns it; the caller adds that to
  `y`. A fixed `GUI.Label(new Rect(x, y, w, 20), text)` is only for short
  constant text and virtualised list rows. The author reported clipped text
  three times in one session (cut in half when wrapping in 20 px, cut off
  on the right when not, running under the list) — this rule is the fix.
  A message goes **where the click was** (under its button); with no panel
  open it goes to `Ctx.Notice` (upper middle, a few seconds).

### UI

**`F2` opens one window; everything is a tab.** A hotkey per panel does not
scale. Per-feature keys still exist and are rebindable, but they open the
window on that tab and are **unbound by default**.

Tabs: Practice, Savestates, Runs, Deaths, Debug views, Inventory, 100%, Settings,
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
| `F7` | Restart the current spot (restores its start state, if it has one) |
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

### The live test bridge (v0.24.13)

Dynamic analysis: the running game answers questions from here. Off by
default; the author ticks **Settings -> Test bridge** (or
`[Diagnostics] TestBridge = true`, persists). The plugin polls
`BepInEx/config/ForestOverlay/bridge/in.txt`, runs one line a frame on
the main thread (a waiting command holds the queue, so a batch reads as
a script), appends replies to `out.txt` (`> #n cmd`, result lines,
`< #n ok (t)` / `< #n error: ...`) and logs one `Bridge #n: ...` line
per command. From here:

```bash
scripts/bridge.sh 'status' 'find _Dummy 100'
scripts/bridge.sh -t 300 'restore my-state' 'wait 2' 'find mutant_ 80'
scripts/bridge.sh -f commands.txt
```

`help` lists the commands. Targets: `#<handle>` (printed by every
listing; the instance id), `player`, `camera`, `static:<Type>`, or a
GameObject name/path. Paths: on a GameObject the first step is a
component (`GameObject` = itself, `Comp[1]` = the second), then fields /
properties, `[n]` indexes lists. `find` / `type` (radius, `all` =
inactive too, `max=N`, nearest first), `roots`, `types`, `members`
(live `ilscan type`), `inspect`, `fields`, `get`, `set` / `call` /
`destroy` (mark practice; an `IEnumerator` method is started as a
coroutine), and the overlay's own actions: `savestates`, `capture`,
`restore <name> [load]` (wait until done), `spots`, `go`, `restart`
(waits until idle), `tp`, `dump`, plus `wait` / `waitidle`.
The author does what needs hands (combat, chopping) while a session
drives the rest. A bad path is an error line, never an exception.
`in.txt` still present after a call = the game is not reading (not
running, bridge off).

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
- **Every release has a `CHANGELOG.md` section** (author, 2026-09-23: "for
  all future updates"). `## vX.Y.Z - date`, a few short runner-facing
  bullets. CI copies the tag's section into the GitHub release body
  (`body_path`) and **fails the release if the section is missing**; the
  plugin's Updates tab reads the body from the release JSON it already
  fetches (`ReleaseJson.ExtractNotes`) and shows it - "What's new in vX"
  for an update, "(installed)" once it is the running version. Write it in
  plain words; hard-wrapped lines are joined in game.
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
  ForestOverlay assembly is renamed `.rejected` and never installed; if
  that leaves no `ForestOverlay.dll`, the patcher puts the `.bak` back.
- **Any plugin file name (v0.23.7).** The download is always staged as
  `ForestOverlay.dll.pending` beside the running plugin
  (`Data/UpdateStaging`); a plugin running as e.g. `ForestOverlay(1).dll`
  renames itself to `ForestOverlay.dll.bak` at download time (a loaded
  DLL can be renamed on Windows), so the restart leaves one
  `ForestOverlay.dll`. At startup `UpdateChecker.TidyPluginFolder`
  deletes a stale `<other name>.dll.pending` and renames any second
  ForestOverlay assembly in the folder to `.old`. Installs older than
  v0.23.7 under another name need one manual rename - their code stages
  the wrong name.
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

16. **The runner's `LogOutput.log` is the test harness** - and since
    v0.24.13 the **live test bridge** is the other one (see *The live
    test bridge*): ask the running game directly instead of guessing
    from IL. There is no game
    here to run. The author tests in game and gives the path
    `G:\SteamLibrary\steamapps\common\The Forest\BepInEx\LogOutput.log` —
    read it directly. So every new mechanism logs one line when it acts
    (`Game event:`, `Death (...)`, `Teleport to ...`, `Perf (30 s):`), and
    that line is what gets asked for. Log what a hotkey **acted on**, not
    just that it ran: F7 restarted a different spot than the one the
    author had just set up, and only a `Restart '<id>'` line would have
    shown it.

17. **A library method that "does X" may only do X in one mode.**
    UnitySerializer's `LoadNow` deletes objects missing from the save — but
    only for a partial save (`rootObject` set), never for a full level, so
    walls survived the first in-place restore. Read the whole method body
    (`ilscan body`) before building on what its name promises.

18. **Static or instance: check before binding.** `ItemDatabase.ItemById`
    is static; binding it with instance flags found nothing, and every held
    item read `item <id>` for months. `ilscan type` marks static methods
    (since 2026-09-23); pass `BindingFlags.Static` when it says so.

19. **Tooling: multi-line edits go through a script file.** Long
    `python - <<'EOF'` heredocs in the Bash tool failed with quoting
    errors; write the script to the scratchpad and run it. Inside a
    triple-quoted Python replacement, `\n` meant for C# becomes a real
    newline — use the Edit tool for C# string literals with escapes.
    Use raw strings (`r'''...'''`) in those scripts so C# escapes survive.

20. **A restore can bring back a flag without its effects.** The serializer
    restored `IsInCaves = false` while the cave's side effects (no terrain
    collision, cave streaming) stayed, and `GotoCave` does nothing when the
    flag already agrees — so a death in a cave restored "outside" and fell
    through the world. When restoring state, send the game's own message
    for the state you want (`InACave` / `NotInACave`), don't test the flag.

21. **Ids are per game, not per scene.** `UniqueIdentifier` ids (GUIDs)
    differ between saves for the player **and** for objects outside him
    (the inventory's item views). Anything cross-save — shared start
    states, cloud runs — must map ids, not assume them
    (`SavestateBridge.AdoptPlayer`). Look at the restore log's
    `adopted / left (n on the player)` counts before guessing.

22. **A hook runs mid-method; the caller carries on.** The practice revive
    happens inside `PlayerStats.Hit`, called from
    `FirstPersonCharacter.HandleLanded` — which then played the stagger,
    froze input and scheduled a 1 s recovery. Read the **caller's** body
    past the hooked call (`ilscan body`), and when cancelling a sequence,
    apply its **end state** (the delayed routine's last lines), not just
    stop its start.

23. **Whose lock is it?** Opening the window over the ESC menu found the
    player already locked by the game; releasing it on close called
    `UnLockView` under the menu and hid the cursor. Before taking over a
    game state, note whether the game already held it, and hand back only
    what you took.

---

24. **A static walk cannot see every root.** The v0.23.0 census walked
    every static of the game and found nothing growing while the heap grew
    ~120 MB a load. Threads, live `DontDestroyOnLoad` objects' fields and
    generic-type statics are invisible to it, and counting objects hides
    one huge array (v0.23.2 adds sizes, DDOL roots and a thread count).
    The pathfinding theory it led to was wrong (gotcha 25); the thread
    count found two leaked threads a load (v0.23.3). When a census comes up
    flat, count threads, then read the teardown code (`OnDestroy`) of
    anything that starts one (`ilscan refs "System.Threading.Thread::.ctor"`).
    A worker parked on a wait handle that only a frame callback signals
    never sees its "stop" flag once the object is destroyed. And **ask
    what the title screen clears that a reload does not** - a menu trip
    freeing the memory pointed at `TitleScreen.Awake` ->
    `EventRegistry.Clear()` all along (v0.23.4).

25. **A theory built from IL alone is still a guess.** v0.23.1 shipped a
    fix on the belief that a same-scene reload wakes the new scene before
    destroying the old one, so `AstarPath.OnDestroy`'s
    `if (active != this) return;` skipped the cleanup. In game it never
    acted once in 21 reloads. Before shipping a fix for an ordering or
    lifecycle theory, **ship the log line that proves the theory first**
    (or with it), and read the whole lifecycle: `OnApplicationQuit` calling
    `OnDestroy` itself made the only hit a false positive at quit.

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

**Released: v0.24.25** (2026-09-24). The author runs it via the in-game
updater. **263 tests.**

### Pick up here (maks's test round of 2026-09-24 late evening, v0.24.25)

**State of the author's machine:** v0.24.25 installed. Runner maks's Megan
practice save (Normal, just short of Megan's trigger) is at
`C:\Users\deter\Downloads\Slot4`; swap it in for a slot temporarily
(author's permission, 2026-09-24) - the author's slot 4 is Peaceful, put
it back after.

**Naming (author, 2026-09-24; done in v0.24.27, UI only - config keys and
log lines unchanged):** **Quick load** = restore in place, **Full load** =
restore with a scene load ("save, load"); the death feature is **Reload
save on death** (was "Quick-load on death"). Plan (author): polish Quick
load to parity, then keep Full load as a separate option for full save
reloads (e.g. a death in a full practice run) - kept, not deleted, as the
escape hatch for states Quick load has no patch for.

**maks's results on v0.24.25** (via the author):
- **Confirmed:** cave panels (straightened), mid-air restore, the lighter,
  the book page, auto-restart, the Practice list, the keycard checkpoint
  in order, Megan held until she exists + fast-forward ("works good"),
  the endgame area after a load restore (v0.24.25), the red elevator put
  back **by a load restore** ("flawlessly").
- **New / open, roughly by weight:**
  1. **Enemies after a Full load** - found live (bridge): the load rolls
     the world families afresh at other spawners (16 cannibals 10 s after,
     the captured family gone, none near the player). v0.24.27 runs the
     captured-family rebuild after a Full load once the game's own setup
     has made its families (`EnemiesAfterLoad`, log `Savestate after the
     load: enemies - the game rolled n families ...`) - awaiting a check.
     In place in a cave: they stay dead (maks's log: `positions (by type,
     a v0.24.16 file): 0 of 5 placed`, once `no spawn controller`) - cave
     captures still use the old by-type path; open. (maks's "Creative"
     lines were other saves in the same session - loads 9-13; his enemy
     tests were Normal.)
  2. **Red elevator, in place:** v0.24.26 clears the overlook flag the
     ride leaves set (maks's log: `overlook yes` after, `no` at capture);
     the elevator's position / Sahara are still open. Full load is fine.
  3. **Full load drops the player before the geometry has loaded.**
     v0.24.26's hold never engaged in maks's log (no `held the player`
     line): `EndgameLoader`'s `ForceLoad` goes through the trigger's 0.5 s
     `_loadDelay`, so nothing was loading yet. v0.24.28 pins the player
     at the file's captured position until every scene loaded at capture
     is loaded again, at least 1 s, 30 s cap (log `held the player at the
     captured spot for x s ...`) - awaiting maks.
     Elevator note from the same log: an in-place restore after the ride
     read `same as at capture`, overlook `no` - the overlook flag is not
     the cause; the elevator's scene objects are. Needs a save near the
     red elevator (ask maks for his lab save) and a bridge session.
  4. **A restore should cancel a player animation** in progress (e.g. the
     plane axe's swing plays on through a reset).
  5. **Megan:** (I) the player can walk while `BossHold` waits for her -
     freeze him until the cutscene starts; (II) the spear held before the
     cutscene is not re-equipped after it, as the game would; (III)
     Megan's end-of-transformation audio sometimes plays late, during the
     fight - make it match a normal run, or mute it if that is impossible
     (maks).
  6. **Auto-restart:** improve the UI of the flashed time.
     (I) done in v0.24.26 (pinned while held) - **confirmed** (maks).
     (II) the spear: the cutscene's `HideAllEquiped` -> `MemorizeItem`
     stores the held weapon in `PlayerInventory._equipmentSlotsPrevious`
     and its `ShowAllEquiped` -> `EquipPreviousWeapon` re-equips it; not in
     the save, so a restored replay memorized empty hands. v0.24.28 writes
     it to the file (`heldbefore`, "slot:id") and sets it back every frame
     of the fast-forward (log `..., held before it: n of m slot(s) set
     back for its end`; capture line `held nothing (before that: Spear)`)
     - needs a **new capture**, awaiting maks. (III) audio: not started.
  7. **Placed pickups taken before the capture came back after a Full
     load** (maks: coins, taken before capturing; bridge: `PickUps/Cash`
     x3 and a `Tape_Roll` back, greeble cash stays gone). The game's own
     behaviour - placed pickups are not in the save, any load re-creates
     them. v0.24.27 removes, once the load has finished, placed pickups
     the capture did not list, only in scenes loaded at capture, never
     `(Clone)`s (`PickupKeeper.RemoveTakenAfterLoad`, log `pickups -
     removed n ...`) - awaiting a check.
- **QA tooling (author, 2026-09-24: "let's do all of them"),** after the
  animation-cancel fix, in this order:
  1. bridge `shot` - a screenshot Claude reads (sparingly; reduced size);
  2. keep previous sessions' `LogOutput.log` (timestamped copies on
     startup, last few kept) - the log is replaced every launch;
  3. a "something weird happened" key: `MARK:` line with time, position,
     spot and an optional typed note;
  4. one-click report: zip of current + previous logs and the savestate /
     segment files under test, on the desktop;
  5. a **QA tab** holding 3 and 4 too: each test list shipped in the
     plugin, items with Pass / Fail / Note, the proving log line, and
     auto-ticked where the plugin can see that line; results go into the
     report. Never let runners run bridge scripts (arbitrary calls).
- **Animation cancel** (maks: plane axe swing plays through a reset):
  v0.24.29 fires `playerAnimatorControl.resetAnimator` (resetTrigger) -
  cuts the swing (author) but shows the headless body for a frame
  (camera placement fine). Snapping only `upperBody.idle` did nothing.
  Author wants it exact ("get it right once"): v0.24.29's bridge `anim
  watch N` logs every layer / parameter change - run it during a swing,
  then reset only the layer(s) and parameters the swing uses.
- **Bridge `mark`** (v0.24.27, **confirmed** - seen through walls; author: "I don't have a compass"): `mark
  <target> | mark x y z | mark clear` puts a magenta beacon on a thing -
  use it instead of compass directions when asking the author to find
  something.
- **Red elevator** (runner): trigger it, then F7 / restore in place: the
  elevator leaves its shaft for the overlook area (a hole left behind, its
  button stays - and a second button at the overlook), parts of the Sahara
  load (half the textures), the endgame cave visuals vanish while
  collisions stay, and the player is put in a cave state. The area report
  says `same as at capture` - scenes are not the difference; the elevator
  ride's effects are scene objects (read `ElevatorSystem.Goto` and the
  elevator's move / activate calls with `ilscan body`, then the bridge on
  `HellCorridor/Elevator_01a`). Runners want **the exact state before the
  elevator**: Sahara only partly loaded (its triggers skipped out of
  bounds), elevator in place, the same textures.
- Then the **Fix list** (trees first).

**How these sessions run:** the author loads the
save and says so; from here: `tp 523 56.3 10 180` (20 m north of a
two-to-three-male regular family at spawner (522.9, 56.74, -10.7)),
`set static:Cheats GodMode true`, `set static:Cheats InfiniteEnergy true`,
`capture test-vX`; the author kills and walks off; `restore test-vX`,
then `find "_male(Clone)" 40`, the after-restore log line, and the FSM
states (`get #<_BASE> PlayMakerFSM[1|2].ActiveStateName`). **Updates
through the bridge:** `type OverlayPlugin all` (the plugin is `#-88`
`BepInEx_Manager`), `call #-88 OverlayPlugin._host._modules[1]._checker.Check`,
then `..._checker.Download "<plugin path>"` once `Message` says available
(the API lags the asset by a minute or two); the author restarts.

The author is on high effort for this work; say when medium is enough
again (memory `effort-level-switching`). The bridge made this session's
fixes fast: prefer a live read over an IL theory (gotcha 25).

**Enemies after an in-place restore - where it stands** (fix list 2;
game-notes *Seen live through the test bridge*, *Cannibal kinds and
families*, *Putting cannibals back*):
- **Confirmed:** bodies removed (v0.24.15), blood washed (v0.24.14), one
  plane wreck (v0.24.14), the families' setup re-run so enemies exist at
  all (v0.24.15); captured families rebuilt at their spawners with the
  right kind - "looked like the large family type" (author, v0.24.17);
  every captured cannibal placed to the cm with its health, no duplicate
  spawn (v0.24.18); **sleepers come back asleep on their spot and wake and
  fight when approached** (author, v0.24.22 - "walked around for a sec,
  then went into sleep mode"). v0.24.20 puts a cannibal captured in the
  air (the game stacks them, or captured at the spawner's height before
  dropping) on the ground under it; v0.24.21 clears the pooled
  `sleepBlocker` and marks sleeper families `sleepingSpawn`; v0.24.22
  calls `initWakeUp` at placement and sets sleepers back on their spot
  after the game's own call (game-notes *Who decides sleep*). Polish left:
  the ~1.5 s awake between spawn and placement.
- **How:** capture writes `families` (`FamilyRecord`: spawner position,
  kind lists, every Int32/Boolean/Single `spawnMutants` setting) and
  `enemies` (`EnemyRecord`: family, kind `<prefab>/<storeMutantType>[/s][/p][/L]`,
  position, yaw, health, `/s` asleep). ~1.5 s after an in-place restore on
  the surface `EnemyKeeper.Rebuild` runs the game's `startSetupFamilies`,
  builds each captured family (`Instantiate(spawnGo)`, settings, kind list
  + counters, `invokeSpawn` - whose first `checkSpawn` spawns it -
  `addToWorldSpawns`), 1.5 s later places members by kind with
  `fixMutantPosition`, despawns extras, and 1.2 s later puts the sleepers
  back to sleep (v0.24.22: `initWakeUp` at placement, set back on the spot
  4.5 s later; `switchToSleep` only for one still awake). v0.24.16 files (no
  `families`) fall back to a by-`enemyType` move; cave captures still use
  the old path (setup re-run + by-type move).
- **Open:** does `updateSpawns` add a random family when the captured
  count is below its target (watch `roots mutantSpawner` a minute after)?
  weapons (clubs / sticks) are whatever the spawn gives - record the
  prop if the author wants it; cave captures; load restores; the AI state
  beyond asleep (awake ones come back searching).

**Confirmed working (author, v0.24.11):** hits after an in-place restore
(`kept 18 held-item object(s)`; chopping bushes / trees works); the book
page, in place and with a load; quitting to the title screen clears the
run line; auto-restart and ordered checkpoints in real runs; the
held-item log (`re-equipped by the game`).

**Decided (author, 2026-09-24):** in a Creative game with "Allow enemies"
off, **respect the game's state** - the runner chose that save; do not
spawn cave or world enemies there (as v0.24.10 does).

**Fix list, in order** (before Next up 5):

1. **In-place restore leaves chopped trees and bushes** (author): a
   chopped tree stays a stump, its logs and the sticks from a cut stick
   bush stay on the ground (`Log x5`, `Stick x3` in the "not at capture"
   line), the bush does not come back. A load restores all of it; coins /
   cash pickups respawn fine in place. Tree state is outside `LoadNow`'s
   reach for a full-level save - start from what the save holds:
   `MassDestructionSaveManager` (`mass_v016` in the savestate data),
   `GlobalDataSaver`, `TreeHealth` / the LOD tree grid (`TreeLodGrid`,
   `LOD_Trees`, `CutDown`), and what their `OnDeserialized` does on a load
   vs in place (`ilscan body`, gotcha 17). Logs / sticks lying about could
   go the way of limbs (`PickupKeeper.RemoveNew` by item) once the trees
   come back - not before, or a route loses logs it cut before capture.
2. **Enemies do not come back in place in a Normal game** (author).
   **Found live (bridge):** worse - the restore's family setup dies
   partway and leaves **no cannibal anywhere** for minutes; v0.24.14
   re-runs it (awaiting a check). Still open: the captured positions
   (below). The bridge showed what to record: `activeCannibals`, each one's
   `enemyType.Type`, `EnemyHealth.Health`, `mutantTypeSetup.spawner` and
   that spawner's `spawnMutants` settings. Old notes: the
   restore logs `enemies: families restarted by the game (leaving the cave
   state)` (the surface `NotInACave` -> `startSetupFamilies`), and the
   v0.24.12 log shows families spawning after some restores
   (`mutant_male(Clone)0010 ... mutant_female(Clone)0011`, 8-12 of them,
   plus `mutantSpawner(Clone) x6`), yet the killed ones do not come back
   where they were; a load brings them back. **Author (2026-09-24):
   enemies should respawn, ideally in the same position and state as at
   capture** ("I know that's difficult"). So: record each live cannibal
   at capture (prefab / type, position, rotation, family / spawner, health,
   AI state if cheap) into a new header line (outside the start-state
   hash), and after an in-place restore despawn what is there and spawn
   those - read how `mutantSpawnManager` / `spawnMutants` / the family
   setup instantiate one (`SpawnMutantsSerializerManager` is in the save:
   read what it stores and whether a load uses it for positions). Use the
   test bridge to list live `mutant_*` objects and their components first.
3. **Bodies stay** - **fixed and confirmed (v0.24.15)**
   (`*_Dummy(Clone)` roots without a save id; bridge-confirmed shape in
   game-notes). Old notes (author, v0.24.12: limbs are cleared, bodies not).
   **Found (v0.24.12 log):** a dead body is a scene-root
   **`mutant_male_Dummy(Clone)`** (x1, then x2 over restores - they pile
   up; expect `mutant_female_Dummy(Clone)` etc. too). v0.24.10's
   ragdoll-clone rule (`clsragdollify.vargamragdoll` names) removed 0.
   Next: confirm with the bridge what component a Dummy carries (the
   `*Dummy*` types, e.g. `CoopMutantDummy` is multiplayer - find the SP
   one) and destroy root objects named `*_Dummy(Clone)` without a save id
   after an in-place restore (in `ClearCorpses`). Other roots in that
   line are the game's managers (`_mutantSetup`, `DeadSpots`,
   `cannibalVillages`, `mutantWorldPosition`) - never touch those.
   Limbs / heads: **confirmed cleared** (`removed 2 limb / head
   pickup(s)`).
4. **An extra `Axe Plane`** - **fixed and confirmed (v0.24.14)**: each in-place restore added another plane wreck
   (`Hull(Clone)`, game-notes *The plane wreck*). Old notes: The "not
   at capture" line counted `Axe Plane x2, x3, x4, x5` over consecutive
   restores (back to x2 after a load). Something drops or spawns a plane
   axe pickup per restore - `StashHands` / the game's re-equip, or the
   serializer bringing back a pickup. Find those objects (position vs the
   player) before they pile up.
5. **Blood on the player** - **fixed and confirmed (v0.24.14)**:
   the game's wash `PlayerStats.GotCleanReal()` after every in-place
   restore (confirmed by hand through the bridge). Old notes (author,
   2026-09-24): blood from killing cannibals remains on the player's
   body / arms. Not the `BleedBehavior` screen overlay (Deaths tab) - the
   player model's bloodiness; find what sets it (`ilscan` for the player's
   blood / "bloody" material or property block, e.g. `PlayerStats` blood
   amount, a `BloodyPlayer`-like component, a wash in water) and reset it
   after an in-place restore the way washing does.
6. **Phantom stick** (author, once, during practice runs after in-place
   restores): picked up a stick, it vanished, nothing in the inventory, no
   "picked up" text. Suspects: a `PickupKeeper` copy (disabled instead of
   destroyed, then re-enabled) or a greeble instance despawned by the
   streaming reload under the hand. Watch for it; the stick oddity in
   *Awaiting* is probably the same.
7. **Pickups move** (old fix 4): the "not at capture" lines show few
   sticks / rocks (`Stick x3`, `Rock x1`) - most of the list is logs,
   cloth, boards and plane axes. Re-test after 1 and 4; greeble IL notes
   are in game-notes *Greebles* (positions are seeded; type pick depends
   on regrowth time).
8. **Cave panels** (old fix 5): no `nearest cave panel` line yet - it
   needs a capture within 30 m of a panel, a few hits, an in-place
   restore.

Shipped in v0.24.12, **confirmed (author)**: a restart voids the running
timer at once (`OnRestartStarting`; log `Run '<id>': aborted -
restarting the spot.`); limb / head removal; the body-candidates line;
the title screen no longer runs the nature guide's object scan every 5 s
(it logged `Slow tick: 'collectibles'` ~12 ms there).

**Live testing through the running game** - **built in v0.24.13**
(author approved 2026-09-24; design as proposed: a file bridge,
practice-only, off by default, one log line per command, plain-text
replies, every command guarded).

**Then, still Next up 3, before Next up 5** (author, 2026-09-24):
- **Stats-only start state** (runner): a spot option restoring only thirst,
  hunger, stamina, energy (and health?) - an instant revive with no
  restore freeze. Author: "get them done before 5".
- **Time of day without cycling through the night**: the author thinks
  the cycle is the game's own resync after a load; try only if a clean
  way exists (read what sets the time after `LoadNow` / a load).
- **Sharing** - author: "whichever you think fits best with my future
  website" (it will visualise runs, character info etc.); "we'll refactor
  if I don't like it". Design it as the website's export format: one
  self-describing file per segment (segment definition + its start state
  `.fosave` + optionally its attempts with samples), plain text / JSON-like
  so a web page can read it; Export and Import buttons in the Practice
  editor; import never overwrites an existing id silently.
- The lab / hellcave fix waits for the author's log (pending).

**Still awaiting an in-game check** (the author did not get to them):
(the old list) the Megan cutscene fast-forward (v0.24.3), mid-air restore (v0.23.9 - the
log's `fall ended (1.2 s in the air, 0 m/s)` after every load restore's
teleport is the loaded player's own air time, harmless), the panels, renamed-plugin updates (v0.23.7), the keycard checkpoint, the
in-place restore timing (~155 ms on this heap - fine), and maks: the
Practice list (v0.23.8) - he still runs v0.23.1 and should update.

Then continue with **Next up**, in order. The author wants Next up finished
before QoL/UX work; the runner feedback below is deferred unless critical
(judge it, and say so).

### What works

Module host with tabbed UI, rebindable hotkeys, HUD, velocity, per-item
inventory, 100% checklist + nature guide + To Do list, type explorer, dumps,
unified practice spots/segments with an in-game editor and zone preview,
segment-driven timed runs with **ordered checkpoints**, ghosts, live deltas
and run lines, full player-state capture, **separate endgame split events**,
**quick-load on death (no menu)**, **practice revive** (no hard-landing
aftermath), cave-aware teleports, **savestates** (capture / restore in place
/ restore with load, no save slot used, **across saves**), **segment start
states** (in the route fingerprint), debug views (freecam / colliders /
triggers / wireframe, with size and name filters), game input blocked while
the window is open, an on-screen notice, a 30 s perf log line,
self-installing updates **with a changelog in the Updates tab**, a
**memory census on every load**, offline IL
scanner.

### Key concepts

- **Spots and segments are one thing.** Every Practice entry is somewhere to
  teleport; tick "Timed segment" and it gains start/end triggers and
  checkpoints. There is no separate "anchor". **F7 restarts the *current*
  spot** (the last one teleported to or captured on), not the editor's
  selection.
- **One button, one job** (author, v0.22.0: "buttons shouldn't have
  double-purposes"). **Go only teleports**, start state or not. Restoring
  is **Restart**: F7, the Runs tab's Restart, a death revive, and the
  *Restart* button on the editor's Start state row.
- **Triggers** (`zone`, `box`, `item`, `event`, `manual`) are the spine —
  splits, segment bounds and eventually autosplits are all "a trigger fired".
  Item triggers can be **relative** (`+3` = three more than at the start).
  `event` triggers fire from `Game/GameEvents`; the segment editor steps
  through the known names with `<` `>`.
- **Split order** (`Data/SplitSequence`, v0.22.7, tested): the start fires
  on *crossing*; checkpoints fire **in order**, and **the end only once all
  have fired** (reached early, it says which checkpoint is missing; F12
  skips one). A checkpoint that becomes current because the previous one
  fired **fires at once if it already holds** (keycard in the bag, standing
  in its zone). The one prime kept: a *zone* that is current when the clock
  starts must be entered (a loop route's end is its start zone).
- **Endgame events**, route order: `vault-door`, `timmy-pickup`,
  `megan-transform` (approaching Megan), `megan-pickup`, `megan-to-machine`
  (Megan into the artifact), `gold-door`, `red-elevator`, `game-end`
  (`end-crash` / `end-shutdown`). Also `endgame-cutscene` (every cutscene —
  the old autosplitter's behaviour), `keycard-door`, `keycard-door-<itemId>`,
  `timmy-goodbye`, `raft-out-of-world`. Each fires on the rising edge of
  `endGameCutScene`, so split times are **frame-identical to the LiveSplit
  autosplitter**; the Harmony postfix only says which cutscene it was.
- **Deaths** (Deaths tab), decided in `DeathModule.Decide`:
  1. a current spot **with a start state** → revive and restore it, the
     segment's way, **even with practice mode off** (author: "if the runner
     wants to practice with a save state then it should reload entirely on
     death"; a load-mode state = a full load per death);
  2. practice mode on + a current spot → **revive** at the spot (health
     100, blood cleared, no reload, marks practice);
  3. otherwise **quick-load** (toggle, on) — every death, the first-death
     capture and the boss-fight wake-up each with their own toggle (both on;
     the boss toggle is the author's call). Never permadeath or multiplayer.
  Quick-load **skips the title screen** by default (`QuickLoadSkipMenu`,
  author: "faster load with no compromise") via `LevelSerializer.Resume()`,
  falling back to the menu path. The author rules quick-load **allowed in
  normal runs**: it is the game's own load of the same save.
  A revive from a fatal hard landing cancels the landing's aftermath (a
  postfix on `FirstPersonCharacter.HandleLanded`; game-notes *Deaths*).
- **Savestates** (Savestates tab, practice-only; game-notes *Saving and
  loading* has the IL). The game's own level serialization
  (`LevelSerializer.SerializeLevel`) written to
  `config/ForestOverlay/savestates/*.fosave` — never a save slot or Steam
  Cloud. Capture mirrors the game's save routine. Two restores:
  - **in place** (~0.15 s on a fresh heap, no load): `LoadNow`, plus what
    `LoadNow` does not do for a full-level save — delete objects not in the
    save (walls built since; **never weapon-upgrade receivers**, v0.22.7),
    clear their build-mission HUD line, stash held items — and
    `Game/PickupKeeper` puts taken world pickups back. Afterwards the
    **cave state is sent outright from the file's `cave` flag**
    (`GameBridge.ForceCaveState`): the serializer restores the flag without
    its effects.
  - **with a load** (~5 s): `LoadSavedLevel` — the second half of the
    game's own load. Full reset, proven.
  **From another save** (sharing): every game gives its objects its own
  `UniqueIdentifier` ids, so an in-place restore first **adopts** the saved
  ids — every live identifier the save lacks takes the id of the saved
  object with the same name, prefab class and parent id, shallowest first,
  unique matches only; identical siblings pair in order
  (`SavestateBridge.AdoptPlayer`; unmatched ones are named as `other
  misses:`). **Refused across Creative and survival** (the mode is not in
  the save) unless *Allow restoring across Creative and survival (testing)*
  is on (`AllowCrossModeRestore`, off).
  The file header lists the world pickups at capture and whether streaming
  was unloaded; `Data/SavestateFile` is pure and tested.
  Messages sit under the button group that produced them; a Practice
  restart's go at the top of the tab.
- **Segment start states** (Practice editor, *Start state* row: Capture /
  Delete / Restart): a savestate at
  `savestates/segments/<safe segment id>.fosave`, restored on every restart
  before the usual teleport (which then skips its terrain-based cave guess).
  **In place by default; `restore = load` on the segment** for the full
  reset (author: fastest by default, the validated method as the
  alternative). Capturing moves the spawn to where you stand, makes the
  segment current and **saves the segment**. No start state = the restart
  keeps the game as it is (routes need that). **A new start state is a new
  route** (author's call): capture writes `startstate = <FNV hash of the
  data>`, folded into `RouteFingerprint()` only when set, so old times
  retire as when a zone moves; Capture and Delete ask for a second click
  when attempts would be retired (`AttemptStore.CountOnRoute`). A restart
  whose file does not match the hash logs a warning. Each restart logs
  `Restart '<id>': ...`; a refused or failed one still teleports and says
  why under the buttons, or — window closed — in `Ctx.Notice`.
- **Runs** record position at 30 Hz and ~60 named player-state channels at
  5 Hz (read only when a sample is due), discovered by reflection so a game
  update adds stats for free. Attempts persist per segment id and carry a
  **route fingerprint**, so moving a zone retires old times instead of
  letting them compete. Lines are cleared when the current entry is a plain
  spot or another segment.
- **Loads and memory** (Savestates tab, *Memory* section): `Game/LoadWatcher`
  sees every load by `Scene.FinishGameLoad`; 1.5 s later `Game/MemoryCensus`
  logs the heap, destroyed Unity objects still reachable from statics (per
  root, with growth) and Unity objects by type (switch
  `Diagnostics.MemoryCensusAfterEveryLoad`, **off** since v0.23.6 - a
  hitch of ~0.6-1.0 s after a load, noticed by the author; the old key
  `MemoryCensusOnLoad` is orphaned). *Memory census now* runs it on demand.
  `Game/LeakedThreads` (switch `Fixes.StopLeakedThreadsOnLoad`, on) stops
  the two threads each load leaves running; `Game/StaleSubscribers`
  (switch `Fixes.PruneDeadSubscribersOnLoad`, on) drops the old world's
  event subscribers the game keeps until the title screen - memory only,
  no gameplay effect, so not practice-only.
- **Changelog** (author, 2026-09-23, "all future updates"): `CHANGELOG.md`,
  one runner-facing section per release. CI puts the tag's section in the
  GitHub release and fails without one; the Updates tab shows the latest
  release's notes ("What's new in vX", "(installed)").
- **Tabs know when they are showing**: `OverlayModule.TabShowing` (the main
  window open on this tab). `PanelOpen` is only for a module's OWN window
  and is never set for a tab - the Inventory tab refreshed behind it and
  opened empty (fixed v0.22.7).

### Confirmed in game vs awaiting a check

Confirmed by the author: game-input block and freecam hold (v0.17.0), pitch
kept across teleport / window close (v0.17.1), every endgame split incl.
vault / gold door / red elevator (v0.18.x), quick-load and practice revive
(v0.19.2), cave teleport both ways (v0.19.1), Inventory tab item names
(v0.19.4), savestates in a cave in Creative (v0.20.1), quick-load without
the menu (v0.21.0), the self-updater end to end, segment start states on F7
both ways (v0.21.1); death at a start-state spot with practice mode off
restores it; a restore out of a cave sets the surface state; Go only
teleports and Restart restores; cross-save restores in place give **one
player and one inventory**; the ESC menu keeps its cursor when the window
closes over it; the fall revive has no stagger and **jump comes back at
once** (v0.22.6, author); text wraps and sits under its buttons; the
v0.23.0 census ran after every load without trouble (0.4-0.8 s); **the
load leak fixed** (v0.23.3-0.23.5: threads flat, heap flat, loads ~5 s;
building, chopping and killing across reloads fine); the menu route (exit
to title -> Continue) flat too, 10 trips (v0.23.7).

**Awaiting an in-game check** — ask before building on these:
- ~~The Practice list's unsaved reminder~~ **confirmed** (runner, 2026-09-24).
- **The Practice list never sticks** (v0.23.8): switch with unsaved
  edits, "(unsaved)" on the row, "Save (n)" saves them all.
- **No blood / no stagger** (v0.24.7): in Creative and survival - fall
  hard with no stagger on (moving and jumping at once), take damage with
  no blood on (no red overlay).
- **Auto-restart** (v0.24.6): tick it in the Runs tab, finish a timed
  spot - the time flashes and the spot restarts; with a load-mode start
  state too.
- **Enemies respawn after an in-place restore** (v0.24.5): kill a few,
  restore in place - they are back (elsewhere is expected). Also check
  nothing doubles up and caves still spawn. `Savestates bound. ...
  enemies:True`.
- **Lab / hellcave area report** (v0.24.4): the runner's red-elevator case -
  capture in the lab, trigger the overlook, restore both ways; read the
  `Savestate areas ...` lines (Next up 3).
- **Megan cutscene savestate** (v0.24.3): capture ~2 s before the end of
  Megan's transformation, restore both ways. Capture line: `during
  cutscene 'megan-transform' at x s`; after the restore: `cutscene
  'megan-transform' fast-forwarded to x s (captured at x s) in y s real
  time`, or why not (`no cutscene began within 20 s` - then the cutscene
  does not restart after a restore and the approach needs rethinking).
  Maks: same stand-up spot every time?
- **Cave panels kept by savestates** (v0.24.2): capture in a cave, axe
  clip a panel a few times (or break it), restore in place - the line
  says `panels: 1 healed` (or `1 rebuilt`) and the panel is whole.
  `PanelKeeper: hooked.` at startup.
- **The lighter kept by in-place restores** (v0.24.1): capture with the
  lighter out and lit, restore in place. Capture line: `held Lighter`;
  restore: `held at capture: Lighter (held)` or `(re-equipped)`, and no
  "CANNOT CARRY" message. Is it lit? `hands put away in N ms` on the
  restore line shows the wait. `Savestates bound. ... held:True`.
- **The book page kept by savestates** (v0.24.0): capture on a page,
  flip to another, restore both ways - the capture line's `book:` names
  the page, the restore line says it was switched back. `BookPages
  bound. ...` must be all True.
- **No landing damage after a mid-air restore** (v0.23.9): F7 or a
  Savestates-tab restore while falling; the restore line ends `| fall
  ended (...)`.
- **v0.23.6's census off by default** - no hitch after a load.
- **Checkpoints in order** (v0.22.7) - the keycard case, see *Pick up here*.
- **Changelog in the Updates tab** (v0.23.0): "What's new in v0.23.1
  (installed)" after updating.
- **Run lines cleared** on a plain spot / another segment and the
  **Inventory tab** filled on first open (v0.22.7).
- **Weapon-upgrade receivers kept** on a cross-save restore (v0.22.7): the
  restore line says `kept N weapon-upgrade receiver(s)`; the adoption line
  lists `other misses:` - read it to see why they did not adopt.
- **Updates tab**: Check again after a Download says "downloaded - restart
  to install" instead of offering the same version (v0.22.3).
- ~~Retiring times on a new start state~~ **confirmed** (runner, 2026-09-24:
  double click, names the count). Old note (v0.22.0): capture on a segment
  with attempts asks for a second click and names the count.
- **Whether a timed run still arms after an F7 restore** — runs do not log
  arming; add a log line if it is ever in doubt.
- **"GATHER LOGS 0/4"** after an in-place restore (v0.20.2).
- **Savestates outside the easy case:** a busy surface area; the stick
  oddity (first stick picked up after an in-place restore went to the
  inventory, not the hand; seen once).
- **Boss-fight quick-load toggle** (v0.19.4) — a boss-fight death is rare.
- `end-shutdown`, `timmy-goodbye`, `raft-out-of-world` never seen in a log.

### Open threads

- **A renamed plugin never updated** *(runner)* - fixed in v0.23.7 for
  every install from v0.23.7 on. A runner still on an older version under
  another name (`ForestOverlay(1).dll`) must rename it to
  `ForestOverlay.dll` once, game closed; tell them if they report being
  offered the same update every launch.
- **Spots stop switching** *(runner maks, seen ~3 times)* - **fixed in
  v0.23.8**, awaiting maks. The Practice
  tab stays stuck on one spot ("logboosts") and clicking another does
  nothing, until a new spot is created and deleted. A core-flow bug, not
  QoL. Almost certainly the **unsaved-changes guard**
  (`PracticeModule.DrawList`: with `_dirty` set, a row click only sets
  `_status = "Unsaved changes - Save or Reload first."`). The message is
  easy to miss at the top and does not change on a second click, so it
  looks like nothing happens; *Delete* writes the file and clears
  `_dirty`, hence the workaround. Anything that calls `Touch()` sets it -
  a slider nudged, *Restore with a load* ticked. Suspect too: slider
  defaults (`SphereFields` / `BoxFields` show 3 m / DefaultRadius for a
  zero size and write it back, so merely *viewing* such a zone dirties
  it). Fix (v0.23.8, author approved): selecting always works (edits
  stay in memory on the `Segment`); `_unsaved` lists edited entries, the
  row says "(unsaved)", the button "Save (n)", and Save writes every
  unsaved entry (refusing, with the entry selected, if one is invalid);
  leaving an unsaved entry says so in the status line. Every write goes
  through `WriteFile`, which clears that file's entries. Sliders write
  only when dragged. Log: `Practice: selected '<id>'[, '<id>' left
  unsaved] (n unsaved).` and `Practice: saved n unsaved entries to ...`. Selection is
  not logged, so maks's log (v0.23.1) could not show it; the author
  recalls maks often forgot to save, which fits.
- **Game stopped responding** (runner, v0.22.6, third log of 2026-09-23):
  about 30 in-place restores of a sinkhole start state, each after a fall
  death + revive, then the first **load** restore started **from a death**
  (`Restart ... with a load` right after `Death: revived from a fall`), and
  the log ends. Every earlier load restore in these logs came from F7, not a
  death. Unknown whether it is the death path or the degraded session (a
  leaked heap). Needs the Unity log (`TheForest_Data/output_log.txt`,
  replaced each launch) if it recurs.
- **Another runner's slowdown with ghost lines / recording.** Not reproducible
  on the author's machine (4080 Super / 7800X3D). Read that runner's
  `Perf (30 s):` lines before changing anything - and note the load leak
  (fixed in v0.23.5) slowed everything on long sessions.
- **Background performance (maks).** Waiting on maks's `LogOutput.log`; read
  `Perf (30 s):` and `Slow tick:` first.
- **Author's own perf:** overlay tick ≤ 0.02 ms, GC 0–1 per 30 s in play.
  One-off `Slow tick:` lines for `collectibles` / `inventory` (15–100 ms)
  while the player binds during a load, and `deaths` (~150–490 ms) while a
  quick-load or load restore starts, are the load itself — left alone.
- **Nature guide page names are unverified** (`Data/PageGrouping.cs`); maks
  should send a `natureguide_*.txt` from the 100% tab's **Write dumps**.
- **Installs older than v0.16.2 cannot download updates**; older than
  v0.19.2 can hit the post-release 404 (click Download again later).
- `gh` is not installed on this machine: release pages cannot be edited from
  here (v0.22.7's page has only GitHub's generated notes). CI writes every
  later release's notes from `CHANGELOG.md`.

### Next up

Ordered by what runners feel soonest for the effort. Items marked *(runner)*
came from runners' own requests; the interpretation was checked with the
author. This is all dev/alpha: nothing is used in real runs until the admins
rule, and a few runners act as QA. The author: "work through the current
list so we can move onto expanding more features".

1. ~~Finish the load leak~~ **done** (v0.23.3-0.23.6). Left: in-place
   restore timing on a fixed heap (Pick up here 4).
2. ~~Updater: any plugin file name~~ **done** (v0.23.7). Left: the in-game
   check at the next release (Pick up here 2).
3. **Savestates, remaining** (with the runner feedback that belongs here):
   - ~~**Falling state carries over**~~ **done** (v0.23.9, awaiting a
     check) *(runner)*: restoring while in mid-air kept the fall and dealt
     landing damage. `GameBridge.EndFall` (before and after every in-place
     restore, and after every teleport) zeroes the body's velocity and
     `FirstPersonCharacter.prevVelocity` / `prevVelocityXZ` /
     `jumpingTimer`; the game's own `HandleLanded` then lands softly
     (damage needs `prevVelocity > 28` and air time `> 0.75 s`; game-notes
     *Deaths*). Log: `... | fall ended (x s in the air, y m/s)` on the
     restore line, or on `Teleport to '<name>': ...`.
   - ~~**The survival book's page**~~ **done** (v0.24.0, awaiting a check)
     *(runner)*: an in-place restore did not keep the page, a load restore
     reset it. **Author's call (2026-09-23): a savestate keeps the page it
     was captured on, in place or with a load; a quick-load keeps the
     game's default.** The page is which page objects are active
     (game-notes *The open page*): `Game/BookPages` captures it into the
     file's `book` header (`Data/BookPageState`, tested; not part of the
     start-state hash) and applies it after an in-place restore and once in
     game after a load (`AfterLoad`). Log: `..., book: showing 'x' (of n)`
     on capture; `| book: showing 'x' (of n) (k page object(s) switched)`
     on an in-place restore; `Savestate after the load: book: ...`.
     Files from before v0.24.0 leave the book as it is.
   - **Lab + hellcave not restored, even with a load** *(runner)* -
     **2026-09-24: the load half is fixed in v0.24.25 (awaiting the
     runner), the red-elevator in-place half is open; see *Pick up here*.**
     Old notes: after the
     red elevator loaded the overlook area, the last lab section (collision
     loaded, invisible) must stay as it was at capture - runners do it
     "blind". Streaming / area state outside the serializer. **v0.24.4
     ships the diagnostic first (gotcha 25):** IL cannot show which object
     holds it - `LocalPlayer.SetInOverlookArea` has no code callers (scene
     objects / PlayMaker), no C# loader names the lab. `Game/AreaReport`
     logs `Savestate areas at capture: caves / endgame / overlook | scenes:
     ... | streamed: <each Scene.SceneLoaders entry> loaded / unloaded /
     loaded-inactive` and, 2 s after every restore, `Savestate areas after
     the restore: <now> || at capture: <then>` (stored as the `areas`
     header). **Next step: get that log from the runner's case**, then
     restore what differs. If scenes and loaders match but the lab still
     differs, the state lives in scene objects: dump the lab's active
     GameObjects / colliders at capture and after (F11 or a new report).
     Note: capture is refused inside the overlook area (the game's own
     save rule, `SavestateBridge`).
   - ~~**In-place restore does not revive killed enemies**~~ **done**
     (v0.24.5, awaiting a check; game-notes *Enemies across an in-place
     restore*) (author). After every in-place restore the game's own enemy
     restart runs (`mutantController.restartEnemiesFromPauseMenu` ->
     `setupFamilies`), switch `Savestates.RespawnEnemiesInPlace` (on,
     checkbox in the Savestates tab). Enemies come back where the game
     spawns them, as after a load - **not the captured positions**; if a
     route needs those, the next step is recording each cannibal's
     position/type at capture and placing the respawns. Log: `| enemies:
     respawned (the game's enemy restart)` on the restore line.
   - ~~**The lighter is put away by an in-place restore**~~ **done**
     (v0.24.1, awaiting a check; game-notes *Held items across an in-place
     restore*) *(runner maks)*:
     captured with the lighter out and lit, every in-place restore leaves
     it away, so it has to be taken out again each reset (cave 6,
     sinkhole; load restores are fine). **Our own doing:** the restore
     calls `StashHands()` -> `PlayerInventory.StashLeftHand()`
     (`SavestateBridge`), and the lighter is a left-hand item. Record what
     each hand held at capture and re-equip it after the restore (lit if
     it was lit). The harmless **"CANNOT CARRY ANY MORE LIGHTERS"**
     message (author) is probably the same path - `LogControler` has
     `_lighterItemId` and an `OnDeserialized` routine; check its IL too.
   - ~~**A savestate taken during the Megan cutscene**~~ **done**
     (v0.24.3, awaiting a check; game-notes *Savestates during an endgame
     cutscene*: the capture notes the cutscene and game seconds into it,
     the restore fast-forwards its replay there) *(runner maks)*:
     capturing while Megan transforms into the boss and restoring (in
     place or with a load) starts the cutscene over from its beginning;
     cutscene progress is outside the serializer. Maks practises the boss
     kill from the exact spot the player stands up, in a tight window, and
     wants the **last 2-3 s of the cutscene** kept as a reference point,
     "always same variables". Do not refuse the capture (author). Aim:
     restore to the captured moment. Approach to check in IL first: note
     how far into the cutscene the capture was (its clock / animator
     normalized time), restore as now (cutscene from the start), then
     **fast-forward** it to that point (the game's own time scale flag -
     gotcha 1 - or the animators' speed) and return to normal speed a few
     seconds early. Replaying the game's own script keeps it identical
     every time; resuming a coroutine mid-way is not possible.
   - ~~**Cave panels keep their damage**~~ **done** (v0.24.2, awaiting a
     check; game-notes *Cave wooden panels*) *(runner maks)*: the wooden
     panels in caves (`BreakWoodSimple.Health`) lose health with every axe
     clip and eventually break; an in-place restore did not put it back.
     `Game/PanelKeeper` writes every panel's health to the `panels`
     header, sets it back after both restores, and (armed, in place) keeps
     a copy of a panel before it breaks and puts it back. Log: `..., n
     cave panels` on capture; `| panels: n healed, m rebuilt[, k broken
     and not kept]` on the restore line; `PanelKeeper: panel <pos> broke;
     kept for a restore`.
   - **Sharing**: nothing bundles a segment file with its `.fosave` yet.
   - Optional *(runner)*: time of day restored without cycling through the
     night; a **stats-only start state** (thirst, hunger, stamina, energy -
     an instant revive with no restore freeze).
   - Author's idea, still open: reload the slot **in place** on death (the
     Savestates tab's *Reload slot save in place* does exactly that).
4. **Practice QoL the runners asked for** (author, 2026-09-23):
   - ~~**Auto-restart at the end of a timed spot**~~ **done** (v0.24.6,
     awaiting a check: `Runs.AutoRestartAtEnd`, off; checkbox on its own
     line in the Runs tab; `FinishRun` shows the time as a 1.2 s notice and
     `ReturnToSpot` runs 0.4 s later unless the run was aborted, the spot
     changed or a new run started; log `Run '<id>': finished in m:ss` and
     `Run '<id>': auto-restart.`) *(runner maks)*: a
     tickable option; the moment the end condition fires, the time shows
     briefly (~0.4 s, "like those games") and the spot restarts at once,
     exactly as F7 would (start state if it has one). The attempt is saved
     first, like any finished run. **One global setting** (author), and
     it acts for load-mode start states too (~5 s) - runners untick it if
     they do not want that (author).
   - ~~**No blood** and **no stagger** toggles~~ **done** (v0.24.7,
     awaiting a check: `Deaths.NoBlood` / `Deaths.NoStagger`, off,
     checkboxes in the Deaths tab; no blood clears `BleedBehavior` every
     tick; no stagger reuses the fall revive's cancel in the
     `HandleLanded` postfix whenever `jumpLand` went false -> true in that
     call (the hard-landing branch); log `No stagger: hard landing
     cancelled.`, `Deaths: practice toggles on - ...`) (author): *no blood* keeps
     the blood overlay cleared all the time while ticked (`BleedBehavior`,
     game-notes *Deaths*); *no stagger* skips the hard-landing stagger and
     its animations (what the fall revive already undoes in
     `HandleLanded`). Each on its own, off by default, practice-only;
     survival keeps its own feel. **This is also the answer for Creative**,
     where the player never dies: there is no "death" to detect there, so
     the toggles do it instead (author). The death revive keeps doing both
     on death, as now.
5. **Performance: can patches make the game itself faster?** (author,
   2026-09-23, after the leak fix). The leak hunt showed the game doing
   avoidable work - dead subscribers were called on every publish, worker
   threads never ended - so there may be more. Measure first, change
   second:
   - **Baseline**: the `Perf (30 s):` line (fps, worst frame, frames over
     50 ms, GC count, heap KB/s). The author's v0.23.4 log at idle: ~175
     fps, **heap +1091 KB/s** with the overlay at +5 KB/s - the game
     allocates ~1 MB/s, and Unity 5.6's Boehm GC is non-generational, so
     every collection walks the whole heap (why a leaked heap made
     everything slower). GC count and worst frame are the numbers to move.
   - **Find hot spots offline**: `ilscan` over `Update` / `LateUpdate` /
     `FixedUpdate` / `OnGUI` bodies for per-frame `FindObjectsOfType`,
     `GameObject.Find`, `GetComponent(s)`, `SendMessage`, string building,
     `UniLinq`, `new List`/closures; `strings` for per-frame `SendMessage`.
     Candidates already seen: `MecanimEventManager.globalLastStates` and
     `TreeWindSfxManager` lists growing, `WorkScheduler.ProcessArea`,
     `AdvancedTerrainGrass.GrassManager`, enemy AI updates.
   - **Measure in game**: a debug toggle (Debug views tab) that wraps a
     named list of game methods in Harmony prefix/postfix `Stopwatch`
     timing and logs the top N by ms per 30 s - the log is the test
     harness (gotcha 16). Never leave timing patches on by default.
   - **Rules**: only behaviour-preserving patches (cache a lookup, skip a
     no-op, pool an allocation); each with its own switch and one log line
     when it acts; before/after `Perf` lines from the author. A patch that
     changes game timing or outcomes is a gameplay change - label it
     honestly (`IsPracticeOnly` / the run-legality split) like everything
     else; the admins have not ruled.
6. **The author's list of 2026-09-23:**
   - **100%: passengers.** The tab shows the passenger To Do task but not
     which passengers were found or how many. Find where the game tracks
     each passenger (IL) and list them like the nature guide. (Note the
     `PassengerManifest` objects on the player — three of them.)
   - **Logs in the inventory** *(runner sxczurass, clarified with the
     author)*: picked-up tree logs go into the inventory with a counter
     like any item, up to a cap (runner wants 5; author wants it
     configurable — slider or text box, editable in the GUI). **Not** held
     in the arms, not infinite stacking in the hands. Rendering them in the
     inventory is optional. A gameplay mod, not practice tooling — label it
     honestly. **IL starting points:** item `Log` is id 78; carrying is
     `TheForest.Items.Special.LogControler` (`PlayerInventory.Logs`):
     `_logs`, `_logsHeld` (the shoulder models), `Lift()`,
     `PutDown(fake, drop, equipPrevious, preSpawned)`, `RemoveLog`,
     `UpdateLogCount`, `Amount`, `HasLogs`, and `_infiniteLogHack` — set by
     the game's own console command `DebugConsole._loghack on|off`. Still to
     map: what calls `Lift` on a pickup, how building takes logs
     (`Craft_Structure` ingredients vs `LogControler`), and dropping.
   - Idea (author): a **god mode** toggle for practice, the other answer to
     deaths without a start state — the game's console has `_godmode`
     (`DebugConsole`, invokable by reflection).
7. **Freecam keeps the game's lighting.** With freecam on the game goes
   darker everywhere, normal the instant it is off (author). Freecam is a new
   `Camera` from `CopyFrom`, which does not copy the image-effect components
   on the game's camera — the likely cause, unchecked. Dump the main
   camera's components first.
8. **LiveSplit split file import** (`.lss`/`.lsl`) — needed to replace
   LiveSplit rather than sit beside it. Plus HUD/layout customisation. The
   author's autosplitter is the reference (memory `autosplitter-repo`).
9. **forest.deter.cloud — shared runs and a web viewer** *(runner)*.
   Local-first, export always; comparison keys on segment id + route
   fingerprint (now including the start state). Web panel: everyone's runs
   vs yours (look at Momentum Mod); 3D terrain from the `Terrain` heightmap,
   caves need a geometry dump; scrub bar and annotations.
10. **TAS** — exploratory only. Builds on savestates and the recorder.
11. Timmy-drawing sub-pieces (`DrawingsInventoryItemView._ids`), freeform
   zone shapes.

### Deferred runner feedback (voice call, 2026-09-23)

Collected by the author testing v0.22.6 with a runner. **Deferred** until
Next up is done (author: finish the list, then QoL/UX), unless critical.
Already done: checkpoints bypassed, stale run lines, Inventory tab empty on
first open (v0.22.7), the renamed DLL (v0.23.7). The savestate items are
in Next up 3.

Deaths / UX:
- **Revive is confusing**, worse with practice mode on and another spot
  selected. Wants one clear choice of what a death does: quick-load, restore
  the start state (in place / load), revive, or reload the whole save.
- The no-blood / no-stagger toggles moved to Next up 4 (author decided).

Runs:
- Checkpoint **boxes should rotate**; new ones could face the look direction.
- Hide zones individually, or show only the next one.
- Runs tab: when each time was set, more detail; the HUD should show the
  **previous** time, not only the best.
- **Runs continue at the main menu** - abort / invalidate automatically.
- Ghost: a custom model; buildings in the replay (a ghost of what was built).
- **Checkpoint savestates** ("saveloc", like KSF surf): reload from the last
  checkpoint of a mapped route. Problem: capturing on the fly without a
  hitch.

Settings / HUD:
- **Settings do not persist** (run lines, practice mode etc. re-toggled
  every launch) - persist all of them.
- More control over the top-left HUD; remove duplicated clutter (UX pass).

Debug views:
- More detailed colliders (hitboxes); a better collider filter - items share
  generic names.
- Investigate colliders that change between attempts and make no-fall-damage
  tech inconsistent (cave drop, rebreather cave stalagmite drop, keycard cave
  body slide, wall climbs - landing on bodies a certain way makes wall climbs
  consistent).

Shipped: practice QoL (v0.17.0–0.17.1), separate endgame splits
(v0.18.0–0.18.2), deaths and caves (v0.19.0–0.19.1), nature guide (v0.15.0),
savestates phase 0 → 1 and no-menu quick-load (v0.20.0–0.21.1), start
states, cross-save restores and UI standards (v0.22.0–0.22.6), and the
session of 2026-09-23 afternoon (v0.22.7–0.23.1): ordered checkpoints
(`Data/SplitSequence`), stale run lines, Inventory tab refresh, upgrade
receivers kept, no string building in any `DrawTab`, messages under their
buttons everywhere, `TabShowing`, the changelog (repo, release, Updates
tab), the load watcher and memory census; v0.23.1-0.23.6 **the load leak
fixed** (pathfinding ruled out, two leaked threads stopped, dead event
subscribers pruned, census off by default); v0.23.7 updates under any
plugin file name; v0.23.8-0.24.7 (2026-09-24) the Practice list fix,
savestate completeness (fall, book page, held items, cave panels,
cutscene moment, enemies, the area report) and Next up 4 (auto-restart,
no blood / no stagger); v0.24.13-0.24.25 (2026-09-24, with the test
bridge) the live test bridge, cannibals rebuilt as captured and asleep
on their spot (confirmed), panels straightened, no stagger after a
mid-air restore, Megan held until she exists + 25x fast-forward, the
endgame area loaded after an out-of-bounds load restore.

### How a session goes

The author tests in game and reports back with the `LogOutput.log` path; they
answer design questions quickly and mid-turn, and often send several
messages while a turn runs. After each change that builds and passes tests:
bump the version (csproj `Version`/`AssemblyVersion`/`FileVersion` **and**
`Plugin.PluginVersion`), **add its `CHANGELOG.md` section** (CI fails the
release without one), commit, push, tag, then poll the asset URL in the
background and say when it is attached — never `api.github.com`. Docs-only
changes need no version or tag. Do not deploy into the game folder. Mark
decisions made with the author in this file, with who decided. The log is
replaced on every game launch — read it before the author starts the game
again. Before a handoff, rewrite *Pick up here*.

Build with the path read explicitly (the env var is User-scope):
`dotnet build -c Release -p:ForestManagedPath="G:\SteamLibrary\steamapps\common\The Forest\TheForest_Data\Managed"`.

Editing tip: for multi-line changes, write a Python script **with the Write
tool** to the scratchpad, using a small `edit(path, [(old, new), ...])`
helper that asserts each `old` occurs once and **preserves the file's BOM
and line endings** (several `.cs` files are CRLF, others LF; a mismatch
makes `old` not match). Run it with `python <file>`. Heredocs in the Bash
tool break on quoting (a long Python heredoc failed again this session); a
`git commit -F - <<'EOF'` message is fine.
