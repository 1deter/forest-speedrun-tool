# ForestOverlay

A speedrun and practice tool for **The Forest**, built as a
[BepInEx](https://github.com/BepInEx/BepInEx) plugin.

> **Status: early but usable.** Actively developed; expect rough edges.

## Features

**Information only** â€” reads game state, never writes it:

- **Velocity** â€” horizontal and total speed, per-axis, position
- **Per-item inventory** â€” live counts, pin the items you care about to the HUD
- **100% tracking** â€” the unique-item checklist, the nature guide (grouped by
  book page) and the in-game To Do List
- **Timed practice runs** â€” splits, ghost deltas against your best, run lines
- **Type explorer & dumps** â€” browse the game's classes and live field values

**Practice only** â€” writes game state, and flags the session when used:

- **Practice spots** â€” teleport anywhere you have saved
- **Segments** â€” a spot with a start, an end and checkpoints, timed and
  compared against your previous attempts
- **Debug views** â€” freecam, collider and trigger volumes, wireframe

## Using it

**`F2` opens the window.** Everything lives in a tab there.

| Key | Action |
|---|---|
| `F2` | Open the ForestOverlay window |
| `F5` | Show / hide all overlay UI |
| `F6` / `F7` | Save spot here / return to it |
| `F9` | Practice mode on / off |
| `F10` | Type explorer |
| `F11` | Write dump files |
| `F12` | Manual split / finish |
| `[` | Abort run |
| `Keypad *` | Freecam |

All keys are rebindable in the **Settings** tab, or in
`BepInEx/config/com.deter.forestoverlay.cfg`. Most per-tab keys are unbound by
default â€” one key for the window is usually enough.

### Making a timed segment

1. **Practice** tab â†’ stand where the run starts â†’ **New**
2. Tick **Timed segment**, then set the **End** trigger where it should finish
3. Optionally **Add checkpoint here** along the route â†’ **Save**
4. **Runs** tab â†’ tick **Practice mode** â†’ back to Practice â†’ **Go**

You are now armed. Cross the start zone and the clock begins; checkpoints
split, the end zone finishes and saves the attempt. Run again to race your own
ghost.

Zones are drawn in the world while editing â€” green start, amber checkpoints,
red end.

## Install

1. Install BepInEx 5.4.x (x64) into your Forest folder and run the game once.
2. Download `ForestOverlay.dll` from [Releases](../../releases).
3. Drop it into `<game>\BepInEx\plugins\`.

The plugin checks for updates on startup and tells you when one exists.
It cannot yet install them itself.

## Contributing data

Practice spots, segments and the 100% checklist are **plain text files** in
`BepInEx/config/ForestOverlay/`, so a set can be shared as a file and reviewed
as a diff. Everything is also editable in game â€” the files exist for sharing,
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
`Assembly-CSharp.dll` is never referenced â€” all game types are reached by
reflection.

## Run legality

Information-only features read state and never write it, roughly as an
autosplitter does. Teleports, position restore and the player lock write state
and are **practice only**; using one sets a sticky `PRACTICE` marker on the HUD
for the rest of the session.

The Forest's speedrun moderators have **not** yet ruled on this tool. Don't
assume any of it is permitted in submitted runs until they have.

## Development

[`CLAUDE.md`](CLAUDE.md) â€” architecture, conventions and the current task list.
[`docs/game-notes.md`](docs/game-notes.md) â€” confirmed game internals.

`tools/ILScan` is an offline IL query tool over the game assembly; it answers
behavioural questions ("what writes this every frame?") that the in-game
reflection dump cannot.
