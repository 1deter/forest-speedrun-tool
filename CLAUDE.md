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
| Savestates, segment start states | `Modules/SavestateModule`, `Game/SavestateBridge`, `Game/PickupKeeper`, `Data/SavestateFile`; restart flow in `Modules/PracticeModule` (`Restart`; `Teleport` is Go) |
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

**Released: v0.22.3** (2026-09-23). The author runs it via the in-game updater.
**174 tests.**

Working: module host with tabbed UI, rebindable hotkeys, HUD, velocity,
per-item inventory, 100% checklist + nature guide + To Do list, type explorer,
dumps, unified practice spots/segments with an in-game editor and zone
preview, segment-driven timed runs with checkpoints, ghosts, live deltas and
run lines, full player-state capture, **separate endgame split events**,
**quick-load on death (no menu)**, **practice revive**, cave-aware teleports,
**savestates** (capture / restore in place / restore with load, no save slot
used), **segment start states**, debug views (freecam / colliders / triggers /
wireframe, with size and name filters), game input blocked while the window
is open, a 30 s perf log line, self-installing updates, offline IL scanner.

### Key concepts

- **Spots and segments are one thing.** Every Practice entry is somewhere to
  teleport; tick "Timed segment" and it gains start/end triggers and
  checkpoints. There is no separate "anchor". **F7 restarts the *current*
  spot** (the last one teleported to or captured on), not the editor's
  selection. **Go only teleports** — never restores a start state
  (author, v0.22.0: "buttons shouldn't have double-purposes"). Restoring
  is *Restart*: F7, the Runs tab's Restart, a death revive, and the
  *Restart* button on the editor's Start state row.
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
  reload, marks practice). A current spot **with a start state** revives
  even with practice mode off, and the restart restores the state the
  segment's way — a load-mode state means a full load per death (author,
  2026-09-23: "if the runner wants to practice with a save state then it
  should reload entirely on death"). Otherwise **quick-load** (toggle, on) — every death.
  The first-death capture and the boss-fight wake-up each have their own
  toggle, both on (no current route uses the capture; the boss toggle is the
  author's call, 2026-09-23). Never permadeath or multiplayer.
  **Skips the title screen** by default (v0.21.0, author 2026-09-23: "faster
  load with no compromise"): the death is skipped and the slot loads from in
  game via `LevelSerializer.Resume()` — the call `LoadSave.Awake` makes after
  the menu, one scene load fewer (5.2 s vs ~7 s). Toggle `QuickLoadSkipMenu`;
  falls back to the menu path if Resume fails. The author rules quick-load
  **allowed in normal runs**: it is the game's own load of the same save, so
  the game loaded is identical.
- **Savestates** (Savestates tab, practice-only; game-notes *Saving and
  loading* has the IL). The game's own level serialization
  (`LevelSerializer.SerializeLevel`) written to
  `config/ForestOverlay/savestates/*.fosave` — never a save slot or Steam
  Cloud. Capture mirrors the game's save routine. Two restores:
  - **in place** (~0.1 s, no load): `LoadNow`, plus what `LoadNow` does not
    do for a full-level save — delete objects not in the save (walls built
    since), clear their build-mission HUD line, stash held items — and
    `Game/PickupKeeper` puts taken world pickups back (they are destroyed
    and not in the save, so the keeper hides them instead once armed).
  - **with a load** (~5 s): `LoadSavedLevel` — the second half of the
    game's own load. Full reset, proven.
  The file header lists the world pickups at capture and whether streaming
  was unloaded; `Data/SavestateFile` is pure and tested.
- **Segment start states** (Practice editor, *Start state* row): a
  savestate at `savestates/segments/<safe segment id>.fosave`, restored on
  every restart (F7 / Restart / death revive — not Go) before the usual
  teleport, which then skips its cave guess: the restore sets the cave
  state from the file's `cave` flag (`GameBridge.ForceCaveState`).
  **A state from another save restores into your player** (v0.22.2): in
  place, the live player's identifiers take the saved ids first
  (`SavestateBridge.AdoptPlayer`) — without that, `LoadNow` built a second
  player beside the first (v0.22.0). **Refused across Creative and
  survival** (the mode is not in the save; author's suggestion). A refused
  or failed restart still teleports, says why under the Start state
  buttons, and — with the window closed (F7, a death) — in an on-screen
  notice (`Core/Notice`, `Ctx.Notice`). **In place by default;
  `restore = load` on the segment** for the full reset (author 2026-09-23:
  fastest by default, the validated method as the alternative). Capturing
  moves the spawn to where you stand and makes the segment current. No
  start state = the restart keeps the game as it is (routes need that).
  Each restart logs `Restart '<id>': restoring its start state ...` or
  `... no start state - teleport only.`
  **A new start state is a new route** (v0.22.0, author's call): capture
  writes `startstate = <FNV hash of the data>` into the segment block and
  saves the segment straight away; the hash is folded into
  `RouteFingerprint()` only when set, so older fingerprints are
  unchanged, and old times retire as when a zone moves. Capture and
  Delete ask for a second click when attempts would be retired
  (`AttemptStore.CountOnRoute`, header-only read). A restart whose file
  does not match the hash logs a warning — the hook for sharing. Start
  states captured before v0.22.0 carry no hash until recaptured.
- **Runs** record position at 30 Hz and ~60 named player-state channels at
  5 Hz (read only when a sample is due), discovered by reflection so a game
  update adds stats for free. Attempts persist per segment id and carry a
  **route fingerprint**, so moving a zone retires old times instead of
  letting them compete.

### Confirmed in game vs awaiting a check

Confirmed by the author: game-input block and freecam hold (v0.17.0), pitch
kept across teleport / window close (v0.17.1), every endgame split incl.
vault / gold door / red elevator (v0.18.x), quick-load and practice revive
(v0.19.2), cave teleport both ways (v0.19.1), Inventory tab item names
(v0.19.4), **savestates** in a cave in Creative (v0.20.1: in place removes
built walls, puts back the keycard / camcorder / sticks, empties hands and
inventory, no duplicates; with a load everything back in 5.0 s vs
6.95–7.45 s by stopwatch for a menu load), **quick-load without the menu**
(v0.21.0), the self-updater end to end, **segment start states on F7**
both ways and the start-state text on its own line (v0.21.1; log of
2026-09-23 03:44: every restart logged `restoring its start state`, in
place 132–501 ms with `168 -> 168`, with a load ~11 s).

**Awaiting an in-game check** — ask before building on these:
- **Retiring times on a new start state** (v0.22.0): capture on a segment
  with attempts asks for a second click and names the count; afterwards the
  Runs tab shows them as "from another route". Log line:
  `Savestate: start state of '<id>' is now <hash> - route <fp>.`
- **Cross-save restores** (v0.22.3). v0.22.2 fixed the second player but
  not the second inventory: the inventory's item views
  (`Spear_Upgraded_Inv`, `CraftedBomb1`...) sit outside the `player`
  hierarchy with per-game ids, so they were deleted and the save's set was
  rebuilt beside the live one (log: 15 remapped, 1 unmatched, 140 deleted).
  v0.22.3 adopts ids world-wide once the player is foreign. Expect ONE
  inventory holding the saved items; the log line reads
  `Savestate: from another save: N id(s) adopted, M left (K on the
  player)`, naming player misses. Creative vs survival is refused, under
  the buttons / on screen (confirmed working in v0.22.2 — but the message
  ran off the right edge; now wraps).
- **Updates tab**: Check again after a Download says "downloaded - restart
  to install" instead of offering the same version again (v0.22.3).
- **Text everywhere** now wraps via `UiText` (Practice, Deaths, Runs,
  Savestates, Debug views, Updates, the notice) — look for anything still
  clipped.
- Practice text placement — **confirmed** (v0.22.2): capture messages sit
  under the buttons and the saved-state line.
- Confirmed in v0.22.1: the restore out of a cave sets the surface state
  (`| cave: surface state set`); Go only teleports and Restart restores;
  the cross-save refusal logged as designed. Confirmed in v0.22.0: death
  at a start-state spot with practice mode off restores the state.
- **Whether a timed run still arms after an F7 restore** — runs do not log
  arming, so the v0.21.1 log could not show it.
- **"GATHER LOGS 0/4"** after an in-place restore (v0.20.2 clears the build
  mission before deleting a ghost).
- **Savestates outside the easy case:** a busy surface area. Oddity seen
  once after an in-place restore: the first stick picked up went to the
  inventory instead of the hand. No duplicates. **AI is now a known gap:
  an in-place restore does not bring killed enemies back** (author,
  2026-09-23) — see Next up.
- **Boss-fight quick-load toggle** (v0.19.4). The author sees it; a
  boss-fight death is rare to hit.
- `end-shutdown`, `timmy-goodbye`, `raft-out-of-world` never seen in a log.

### Open threads

- **Another runner's slowdown with ghost lines / recording.** Not reproducible
  on the author's machine (4080 Super / 7800X3D). Blind fixes shipped in
  v0.17.1 (see git log). The author will get that runner's `LogOutput.log`;
  read its `Perf (30 s):` lines (`GL ms/frame`, `verts/frame`, `GC x`, `heap`
  vs `overlay` KB/s) before changing anything.
- **Background performance (maks).** Possible slowdown just from having the
  tool loaded, no panel open. Waiting on maks's `LogOutput.log` and
  follow-up; read its `Perf (30 s):` and `Slow tick:` lines first.
- **Author's own perf:** overlay tick ≤ 0.02 ms, GC 0–1 per 30 s in play;
  big frames are loads and game streaming. The one-off `Slow tick:`
  lines for `collectibles` / `inventory` (15–100 ms) come while the player
  and nature guide bind during a load — not repeating, left alone.
  `Slow tick: 'savestates' ~100 ms` after a load-based restore is the
  deliberate full GC behind the heap figure.
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
checked with the author. This is all dev/alpha: nothing is used in real runs
until the admins rule, and a few runners act as QA.

1. **Savestates, finishing phase 1.** Built (see *Key concepts*): capture,
   both restores, segment start states, no-menu quick-load. Remaining, in
   order:
   - ~~F7 fix~~ confirmed; ~~retiring times on a new start state~~ built
     in v0.22.0 (awaiting a check).
   - **In-place restore does not revive killed enemies** (author,
     2026-09-23). Enemies are spawned and pooled by the game's spawn
     managers, most likely outside `UniqueIdentifier`, so `LoadNow` never
     sees them. Find from IL what owns a live enemy and what a scene load
     re-creates (the load restore is the reference: does it bring them
     back?), then do what the pickup keeper does for pickups.
   - **Sharing**: start states are already named by segment id and the
     segment now names its state's hash; nothing bundles a segment file
     with its `.fosave` yet.
   - A busy surface area; the stick oddity.
   - Author's idea, still open: reload the slot **in place** on death (no
     load at all). The Savestates tab's *Reload slot save in place* button
     is exactly that and worked in the author's test (teleport, inventory
     reset) — but world pickups only come back once the keeper is armed.
2. **The game's load memory leak.** Each save loaded without restarting the
   game makes it worse: stutters and lower performance, loading certainly,
   gameplay probably (runner Cheesecake404: "loading definitely"; gameplay
   is a guess). Runners reset constantly and every quick-load / load restore
   is another load. **Measuring has started:** a load-based savestate
   restore logs `Loads this session: N, Mono heap after GC X MB` (390 MB
   after the first load in the author's session). **First series (v0.21.1,
   four load restores in a row after one quick-load):** 540 → 664 → 796 →
   924 MB, and 10.7 → 11.0 → 11.6 → 12.1 s to in game — about **128 MB
   kept and +0.5 s per load**. In-place restores went from ~135 ms to
   200–500 ms after the quick-load. Next: log the same line
   for **every** load (quick-load, menu load) — hook the game's own load
   completion rather than the Savestates module — plus a one-off
   `Resources.FindObjectsOfTypeAll(Object)` count, so a session of repeated
   loads shows what grows. From IL: a menu load **loads the game scene
   twice** (the no-menu paths already cut one), and `LevelLoader` only
   unloads assets when its time-scale argument is 0. Suspects to check:
   objects surviving the scene change (the `DontDestroyOnLoad` `LevelLoader`
   — does it destroy itself?), static `EventRegistry` subscriptions from
   destroyed objects.
3. **Freecam keeps the game's lighting.** With freecam on the game goes
   darker **everywhere** (sky and distance too), normal again the instant
   freecam is off (author). Freecam is a new `Camera` from `CopyFrom`,
   which copies camera settings but **not** the image-effect components on
   the game's camera (tonemapping, scattering, colour grading…) — the likely
   cause, not yet checked. Dump the main camera's components first; fix by
   moving the game's own camera, or copying its effect components.
4. **LiveSplit split file import** (`.lss`/`.lsl`) — needed to replace
   LiveSplit rather than sit beside it. Plus HUD/layout customisation. The
   author's autosplitter is the reference for what runners split on — see
   memory `autosplitter-repo` (github.com/1deter/auto-splitters).
5. **forest.deter.cloud — shared runs and a web viewer.** Local-first,
   export always; the cloud holds players' best runs so they can be compared
   without clogging the GitHub repo. *(runner)*
   - Already true locally: runs are segment-based (a start → end "stage" such
     as plane spawn → cave 5, not free-form), attempts save per segment id in
     the config folder, and segments carry a category. Cloud comparison keys
     on the segment id + route fingerprint, which is why ids never embed a
     SteamID or timestamp. Start states make shared segments start from the
     same world — another reason to fold them into the fingerprint.
   - Web panel: everyone's runs vs your own, with data visualisation — look
     at how Momentum Mod does replays and comparison for the model.
   - 3D terrain is tractable above ground (Unity `Terrain` heightmap); caves
     are mesh geometry under the terrain, so a full map needs a visit pass
     plus a "dump loaded geometry" button. Wants a scrub bar and annotations.
6. **The author's list of 2026-09-23** (bugs first):
   - **Bug: closing the overlay window while the ESC menu is open hides
     the cursor**, so the pause menu cannot be used until reopened. Likely
     cause: on close the plugin releases `Menu` (`Game/GameInput`) and
     unlocks the view (`UnLockView` → `Input.LockMouse()`), both of which
     the pause menu still needs. Fix by leaving both alone when the pause
     menu is open.
   - **Revive after a fall plays a stagger / get-up animation.** Remove it
     on a practice revive. Find from IL what starts it (the fall trigger,
     `hitFallDown`, the animator) — `Fell` itself is skipped by the prefix.
   - **100%: passengers.** The tab shows the passenger To Do task but not
     which passengers were found or how many. Find where the game tracks
     each passenger (IL) and list them like the nature guide.
   - **Logs in the inventory** *(runner sxczurass, clarified 2026-09-23)*:
     picked-up tree logs go into the inventory with a counter like any
     item, up to a cap (runner wants 5; author wants it configurable —
     slider or text box). **Not** held in the arms and not infinite
     stacking in the hands. Rendering them in the inventory is optional
     (the full inventory is cramped). A gameplay mod, not practice
     tooling — label it honestly. Research the log pickup / carry /
     build-ingredient paths in IL first.
   - **"CANNOT CARRY ANY MORE LIGHTERS"** appears bottom-left after a
     start-state restore; harmless (author). Probably the inventory
     restore re-adding an item already held. QoL, later.
   - Idea (author): a **god mode** toggle for practice, as the other answer
     to deaths when there is no start state — the game's console has
     `_godmode` (`DebugConsole`, invokable by reflection).
7. **TAS** — exploratory only. Builds on savestates and the recorder.
8. Runs tab layout (deferred; it still builds strings in `DrawTab`, against
   the module rules), Timmy-drawing sub-pieces
   (`DrawingsInventoryItemView._ids`), freeform zone shapes.

Shipped from the old list: practice QoL (v0.17.0–0.17.1), separate endgame
splits (v0.18.0–0.18.2), deaths and caves (v0.19.0–0.19.1), nature guide
(v0.15.0), savestates phase 0 → 1 and no-menu quick-load (v0.20.0–0.21.1),
start states in the route fingerprint and death at a start-state spot (v0.22.0).

### How a session goes

The author tests in game and reports back with the `LogOutput.log` path; they
answer design questions quickly and mid-turn. After each change that builds
and passes tests: bump the version (csproj `Version`/`AssemblyVersion`/
`FileVersion` **and** `Plugin.PluginVersion`), commit, push, tag, then watch
the asset URL with a background `Monitor` and say when it is attached — never
`api.github.com`. Do not deploy into the game folder. Mark decisions made with
the author in this file, with who decided.
