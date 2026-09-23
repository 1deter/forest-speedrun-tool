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
| Savestates, segment start states | `Modules/SavestateModule`, `Game/SavestateBridge` (incl. cross-save `AdoptPlayer`), `Game/PickupKeeper`, `Data/SavestateFile`; restart flow in `Modules/PracticeModule` (`Restart`; `Teleport` is Go); retire warning via `Data/AttemptStore.CountOnRoute` |
| Practice spots / segments, teleport, cave switch | `Modules/PracticeModule`, `Data/Segments`, `Data/SegmentLibrary`, `Game/GameBridge` (look angles, `SyncCaveState`) |
| Timed runs, ghosts, lines | `Modules/PracticeRunModule`, `Data/RunRecorder` (`RunCompare`), `Data/LineBuffer`, `Game/DebugDraw` (`RunLineBehaviour`) |
| Endgame split events | `Game/GameEvents` (Harmony postfixes + `endGameCutScene` poll) |
| Quick-load / practice revive | `Modules/DeathModule` (Deaths tab), `Game/DeathHooks` (Harmony prefixes; `HandleLanded` prefix/postfix for the fall revive) |
| Debug views, freecam, volume filters | `Modules/DebugViewModule`, `Game/DebugDraw`, `Data/VolumeFilter` |
| Perf log line | `Core/PerfMonitor` (fed by `ModuleHost`, `Plugin.OnGUI`, `DrawTarget`) |
| Updates, changelog | `Core/UpdateChecker`, `Modules/UpdateModule`, `Data/ReleaseJson` (`ExtractNotes`), `Core/UpdaterInstaller`, `patcher/`, `CHANGELOG.md` |
| Load leak diagnostics | `Game/LoadWatcher` (every load), `Game/MemoryCensus` (static roots holding destroyed objects, Unity objects by type), run from `Modules/SavestateModule` |
| Timed run split order | `Data/SplitSequence` (pure, tested) |

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
    ~120 MB a load: the old world was held by a live pathfinding thread
    (`AstarPath` skipped its own cleanup). Threads, live
    `DontDestroyOnLoad` objects' fields and generic-type statics are
    invisible to it. When a census comes up flat, read the teardown code
    (`OnDestroy`) of anything that runs threads or holds big graphs.

25. **Singleton guards in `OnDestroy` skip cleanup on a reload.** A
    same-scene reload awakes the new scene before destroying the old, so
    `if (active != this) return;` returns for the old instance. Harmless
    when the rest only nulls `Instance`; a leak when it guards real cleanup
    (`AstarPath`, gotcha 24). All 19 guarded `OnDestroy`s in the game were
    checked (game-notes *The load leak*).

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

**Released: v0.23.1** (2026-09-23). The author runs it via the in-game updater.
**191 tests.**

### Pick up here (handoff of 2026-09-23)

The author is testing **v0.23.1** in game. Ask for the `LogOutput.log` path
(`G:\SteamLibrary\steamapps\common\The Forest\BepInEx\LogOutput.log`) and
read it before they relaunch. What to look for, in order:

1. **The load-leak fix** (Next up 1). The author repeats ~20 load restores
   in a row. Expect on **every** reload of the game scene:
   `Pathfinding: the previous world's AstarPath was destroyed while the new
   one was active - running the cleanup the game skips (n this session).`
   and the `Memory census N after load N (game scene reloaded, pathfinding
   cleanups n)` heap figure **no longer climbing ~120 MB a load** (v0.23.0
   baseline: 261 -> 2741 MB over 21 loads, 4.8 -> 14.8 s each).
   - Line present, heap flat: the leak is fixed. Record it (game-notes *The
     load leak*, here), then finish item 1's tail below.
   - Line never appears: the ordering theory is wrong - `AstarPath` was
     already inactive or destroyed first. Look at `OnDestroy` order again.
   - Line present, heap still grows (less): the census stays on; look for
     what remains (live DontDestroyOnLoad objects and threads are what a
     static walk cannot see).
   - Also check nothing broke: enemies still path (they chase), and no
     `PathfindingCleanup: ... threw` warning.
2. **The keycard checkpoint** (v0.22.7): the runner's case, a checkpoint
   `item 210 >= 1`, re-tested with a quick reload after picking the keycard
   up. Log lines: `Run '<id>': checkpoint n/m at mm:ss`, or `... end reached
   with checkpoint n (...) outstanding; holding x, y at the start`.
3. In-place restore slowdown (optional): *Memory census now*, ~20 in-place
   restores standing still, *Memory census now* again - the Unity-object
   growth between the two is per-restore, if any.

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
**memory census on every load**, the **pathfinding leak fix**, offline IL
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
  `Diagnostics.MemoryCensusOnLoad`, on - a hitch of ~0.4-0.8 s after a
  load). *Memory census now* runs it on demand. `Game/PathfindingCleanup`
  (switch `Fixes.PathfindingCleanupOnReload`, on) is the leak fix - memory
  only, no gameplay effect, so not practice-only.
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
v0.23.0 census ran after every load without trouble (0.4-0.8 s).

**Awaiting an in-game check** — ask before building on these:
- **The pathfinding leak fix** (v0.23.1) - see *Pick up here*.
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
- **Retiring times on a new start state** (v0.22.0): capture on a segment
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

- **A renamed plugin never updates** *(runner)*: a browser saved the DLL as
  `ForestOverlay(1).dll`; the download is staged as
  `ForestOverlay(1).dll.pending`, which the patcher does not install, so
  the same update is offered every launch. Workaround told to the author:
  rename it to `ForestOverlay.dll`. Fix is Next up 2.
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
  (fixed in v0.23.1, if confirmed) slowed everything on long sessions.
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

1. **Finish the load leak.** *(see Pick up here)* Data: runner logs of
   2026-09-23 (~103 MB kept per load restore, 6.4 -> 15.2 s, a menu trip
   gave most of it back); the author's v0.23.0 census (21 load restores:
   +122 MB a load while statics and Unity objects stayed flat); IL:
   `AstarPath.OnDestroy` skips all cleanup unless it is `active`, and on a
   same-scene reload the new world's pathfinder already is (game-notes
   *The load leak*). **v0.23.1 fixes that; confirm it.** Then the tail:
   - fix our own small holders, left in as known positives for the census:
     `PickupKeeper.TakenList` (prune destroyed entries when a load
     finishes - only an in-place restore prunes it today) and
     `DeathHooks._lastStats` (clear it once a death is handled). The
     v0.23.0 census did not list either among the top holders - they are
     small;
   - decide with the author whether `MemoryCensusOnLoad` stays on by
     default (it costs ~0.5 s after every load) - probably off once the
     leak is confirmed fixed, with *Memory census now* kept;
   - in-place restores: 841 -> 1157 ms over 21 with the heap flat - re-check
     on a fixed heap (a big heap slows every GC).
2. **Updater: any plugin file name** *(runner)*. The plugin stages
   `<its own file name>.pending`; the patcher only installs
   `ForestOverlay.dll.pending`, so `ForestOverlay(1).dll` never updates.
   Small and blocks runners from getting every other fix - do it early.
   The patcher updates less reliably than the plugin (see *Releases and
   updates*), so prefer fixing it in the plugin: e.g. stage as
   `ForestOverlay.dll.pending` and, if its own file is named differently,
   rename itself aside the way `Core/UpdaterInstaller` swaps the patcher.
   `patcher/PendingSwap.cs` is tested against real temp folders - extend
   those tests.
3. **Savestates, remaining** (with the runner feedback that belongs here):
   - **Falling state carries over** *(runner)*: restoring while in mid-air
     keeps the fall and deals landing damage. Zero the rigidbody velocity
     and the fall state on restore (find the fall-damage state in
     `FirstPersonCharacter` IL - `HandleLanded` is where it lands).
   - **The survival book's page** *(runner)*: an in-place restore does not
     keep the page, a load restore resets it. Savestates should keep it; a
     **quick-load** (death) should reset it to the game's default opening
     page. Find where the book keeps its page (`survivalBookController`,
     `SurvivalBook`).
   - **Lab + hellcave not restored, even with a load** *(runner)*: after the
     red elevator loaded the overlook area, the last lab section (collision
     loaded, invisible) must stay as it was at capture - runners do it
     "blind". Streaming / area state outside the serializer; start from
     `ElevatorSystem`, `SceneLoaders`, `Area`.
   - **In-place restore does not revive killed enemies** (author). Enemies
     are spawned and pooled by the game's spawn managers, most likely outside
     `UniqueIdentifier`. Find from IL what owns a live enemy and what a
     scene load re-creates, then do what `PickupKeeper` does for pickups.
   - **"CANNOT CARRY ANY MORE LIGHTERS"** after a start-state restore —
     harmless (author). `LogControler` has `_lighterItemId` and an
     `OnDeserialized` routine; check its IL first.
   - **Sharing**: nothing bundles a segment file with its `.fosave` yet.
   - Optional *(runner)*: time of day restored without cycling through the
     night; a **stats-only start state** (thirst, hunger, stamina, energy -
     an instant revive with no restore freeze).
   - Author's idea, still open: reload the slot **in place** on death (the
     Savestates tab's *Reload slot save in place* does exactly that).
4. **The author's list of 2026-09-23:**
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
5. **Freecam keeps the game's lighting.** With freecam on the game goes
   darker everywhere, normal the instant it is off (author). Freecam is a new
   `Camera` from `CopyFrom`, which does not copy the image-effect components
   on the game's camera — the likely cause, unchecked. Dump the main
   camera's components first.
6. **LiveSplit split file import** (`.lss`/`.lsl`) — needed to replace
   LiveSplit rather than sit beside it. Plus HUD/layout customisation. The
   author's autosplitter is the reference (memory `autosplitter-repo`).
7. **forest.deter.cloud — shared runs and a web viewer** *(runner)*.
   Local-first, export always; comparison keys on segment id + route
   fingerprint (now including the start state). Web panel: everyone's runs
   vs yours (look at Momentum Mod); 3D terrain from the `Terrain` heightmap,
   caves need a geometry dump; scrub bar and annotations.
8. **TAS** — exploratory only. Builds on savestates and the recorder.
9. Timmy-drawing sub-pieces (`DrawingsInventoryItemView._ids`), freeform
   zone shapes.

### Deferred runner feedback (voice call, 2026-09-23)

Collected by the author testing v0.22.6 with a runner. **Deferred** until
Next up is done (author: finish the list, then QoL/UX), unless critical.
Already done: checkpoints bypassed, stale run lines, Inventory tab empty on
first open (v0.22.7). The savestate items and the renamed DLL are in Next
up 2-3.

Deaths / UX:
- **Revive is confusing**, worse with practice mode on and another spot
  selected. Wants one clear choice of what a death does: quick-load, restore
  the start state (in place / load), revive, or reload the whole save.

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
tab), the load watcher and memory census, the pathfinding leak fix.

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
