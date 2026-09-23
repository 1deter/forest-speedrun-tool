# ForestOverlay

A speedrun and practice tool for **The Forest**, built as a
[BepInEx](https://github.com/BepInEx/BepInEx) plugin.

> **Status: early but usable.** Actively developed; expect rough edges.

## Features

**Information only** — reads game state, never writes it:

- **Velocity** — horizontal and total speed, per-axis, position
- **Per-item inventory** — live counts, pin the items you care about to the HUD
- **100% tracking** — the unique-item checklist, the nature guide (grouped by
  book page) and the in-game To Do List
- **Timed practice runs** — checkpoints in order, ghost deltas against your
  best, run lines
- **Separate endgame splits** — each endgame cutscene (keycard doors, Timmy,
  Megan, red elevator, game end) is its own `event` trigger, splitting on the same frame
  as the LiveSplit autosplitter
- **Type explorer & dumps** — browse the game's classes and live field values

**Run helper** — changes what happens, not your save:

- **Quick-load on death** — any death loads your save straight away with the
  game's own load, skipping the title screen (toggles in the **Deaths** tab;
  the first-death capture and the boss fight have their own). The author
  rules this allowed in normal runs.
- **Load slowdown fix** — every quick-load (or any load
  that reloads the game over itself) kept about 120 MB more memory, so
  loads got slower and slower until you went back to the title screen. The
  game's event system keeps the old world's listeners until the title
  screen; the overlay removes them after every load, and stops two
  background threads each load leaves running. Memory only; both can be
  switched off in the **Savestates** tab.

**Practice only** — writes game state, and flags the session when used:

- **Practice spots** — teleport anywhere you have saved
- **Segments** — a spot with a start, an end and checkpoints, timed and
  compared against your previous attempts
- **Savestates** *(experimental)* — capture the world to a file (no save slot
  is touched) and restore it instantly in place, or with a ~5 s load for the
  game's full reset. Built walls go, taken items come back
- **Segment start states** — give a segment a savestate and every restart
  (`F7`, or *Restart* in the editor) puts the world back as it was before
  teleporting you to the start - *Go* only ever teleports.
  A new start state retires the segment's old times (it asks first), and
  dying at such a spot restores it too
- **Death recovery** — a death in practice mode puts you back at your spot
  with full health and no blood overlay
- **Debug views** — freecam (the body is held still), collider and trigger
  volumes with a size cap and a name filter, wireframe

## Using it

**`F2` opens the window.** Everything lives in a tab there.

| Key | Action |
|---|---|
| `F2` | Open the ForestOverlay window |
| `F5` | Show / hide all overlay UI |
| `F6` / `F7` | Save spot here / restart it (restores its start state) |
| `F9` | Practice mode on / off |
| `F10` | Type explorer |
| `F11` | Write dump files |
| `F12` | Manual split / finish |
| `[` | Abort run |
| `Keypad *` | Freecam |

All keys are rebindable in the **Settings** tab, or in
`BepInEx/config/com.deter.forestoverlay.cfg`. Most per-tab keys are unbound by
default — one key for the window is usually enough.

### Making a timed segment

1. **Practice** tab → stand where the run starts → **New**
2. Tick **Timed segment**, then set the **End** trigger where it should finish
3. Optionally **Add checkpoint here** along the route → **Save**
4. **Runs** tab → tick **Practice mode** → back to Practice → **Go**

You are now armed. Cross the start zone and the clock begins; checkpoints
split, the end zone finishes and saves the attempt. Run again to race your own
ghost.

Zones are drawn in the world while editing — green start, amber checkpoints,
red end.

## Install

1. Install BepInEx 5.4.x (x64) into your Forest folder and run the game once.
2. Download `ForestOverlay.dll` from [Releases](../../releases).
3. Drop it into `<game>\BepInEx\plugins\`.

That one file is the whole install. On first launch it writes the 100%
checklist and shared spots into `BepInEx/config/ForestOverlay/`, and a small
update installer into `BepInEx/patchers/`.

### Updates

The plugin checks for a new release on startup and opens the **Updates** tab
when there is one, with what's new in it ([`CHANGELOG.md`](CHANGELOG.md)).
Click **Download**, then restart the game — the update is installed before
the plugin loads, and the previous version is kept as `ForestOverlay.dll.bak`.

Keep the file named `ForestOverlay.dll`: a browser that saves it as
`ForestOverlay(1).dll` stops updates from installing (a fix is planned).

To roll back, close the game, delete `ForestOverlay.dll` and rename
`ForestOverlay.dll.bak` to `ForestOverlay.dll`.

Versions before **v0.16.2** cannot download updates; install a current release
by hand once and it is automatic from then on.

## Contributing data

Practice spots, segments and the 100% checklist are **plain text files** in
`BepInEx/config/ForestOverlay/`, so a set can be shared as a file and reviewed
as a diff. Everything is also editable in game — the files exist for sharing,
not as the interface.

Shared spots live in [`locations/`](locations/) and the 100% list in
[`collectibles/`](collectibles/). Both are embedded in the DLL and written to
the config folder on startup, so the plugin file is the whole install.

Segment ids are the comparison key, so they are author-namespaced and stable:
`deter/route.plane-to-cave5`. Renaming one orphans every time recorded against
it.

## Build

```bash
dotnet build -c Release -p:ForestManagedPath="<path>\TheForest_Data\Managed"
dotnet test tests/ForestOverlay.Tests/ForestOverlay.Tests.csproj
```

Or `./scripts/deploy.ps1` to build and install in one step.

Builds without the game installed (CI does exactly that) using a stubbed
UnityEngine assembly. No game files are in this repository, and
`Assembly-CSharp.dll` is never referenced — all game types are reached by
reflection.

## Run legality

Information-only features read state and never write it, roughly as an
autosplitter does. Teleports, position restore and the player lock write state
and are **practice only**; using one sets a sticky `PRACTICE` marker on the HUD
for the rest of the session.

The Forest's speedrun moderators have **not** yet ruled on this tool. Don't
assume any of it is permitted in submitted runs until they have.

## Development

[`CLAUDE.md`](CLAUDE.md) — architecture, conventions and the current task list.
[`docs/game-notes.md`](docs/game-notes.md) — confirmed game internals.

`tools/ILScan` is an offline IL query tool over the game assembly; it answers
behavioural questions ("what writes this every frame?") that the in-game
reflection dump cannot.
