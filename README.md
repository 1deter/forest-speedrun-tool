# ForestOverlay

A speedrun information overlay for **The Forest**, built as a
[BepInEx](https://github.com/BepInEx/BepInEx) plugin.

Shows velocity, a run timer and live inventory counts in game, plus practice
tools and a runtime type explorer for discovering the game's internals without
decompiling.

> **Status: early.** Working, but not a finished tool.

## Features

**Information only** (reads game state, never writes it):

- **Speed / velocity** - horizontal speed and total, plus per-axis and position
- **Run timer** with manual start/stop/reset and splits
- **Per-item inventory** - full breakdown with live counts; pin the items you
  care about and they stay on the HUD
- **Type explorer** - browse every class in the game with live field values,
  filterable, in game
- **Dump system** - writes the type index, scene hierarchy and a player
  snapshot to files for offline analysis

**Practice only** (writes game state - see [Run legality](#run-legality)):

- **Position save / restore**
- **Teleport library** - practice spots loaded from text files, grouped and
  searchable, and extensible without touching code

Using any practice tool sets a sticky `PRACTICE` marker on the HUD for the rest
of the session.

## Hotkeys

| Key | Action |
|---|---|
| `F3` | Practice panel (teleports, capture) |
| `F4` | Inventory panel |
| `F5` | Toggle HUD |
| `F6` / `F7` | Save / restore position |
| `F8` / `F9` | Start-stop / reset timer |
| `F10` | Type explorer |
| `F11` | Write dump files |
| `F12` | Split |

`F12` is Steam's screenshot key by default - rebind one of them if you use the
Steam overlay.

## Install

1. Install BepInEx 5.4.x (x64) into your Forest folder and run the game once.
2. Download `ForestOverlay.dll` from [Releases](../../releases), or build it
   yourself below.
3. Drop it into `<game>\BepInEx\plugins\`.

## Build

```bash
dotnet build -c Release -p:ForestManagedPath="<path>\TheForest_Data\Managed"
```

Or `./scripts/deploy.ps1` to build, install, and sync location files in one
step.

The project builds without the game installed (CI does exactly that), falling
back to a stubbed UnityEngine assembly. No game files are included in this
repository, and `Assembly-CSharp.dll` is deliberately never referenced - all
game types are reached by reflection.

## Contributing practice spots

Practice locations are plain text, one spot per line:

```
category | name | x | y | z | yaw | notes
```

Every `.txt` file in [`locations/`](locations/) is loaded and merged, so adding
a set needs no code change and no rebuild. Capture spots in game with `F3` ->
**Add here**, then copy the lines you want to share into a file there and open
a pull request. See [`locations/README.md`](locations/README.md).

## Run legality

Information-only features read game state and never write it, which is roughly
what an autosplitter does. Position restore and teleports write state and are
**practice only**.

The Forest's speedrun moderators have **not** yet ruled on this tool. Don't
assume any of it is permitted in submitted runs until they have.

## Development

See [`CLAUDE.md`](CLAUDE.md) for architecture and how to add a module, and
[`docs/game-notes.md`](docs/game-notes.md) for the confirmed game internals
reference.

`tools/ILScan` is an offline IL query tool over the game assembly - it answers
behavioural questions ("what writes this every frame?") that the in-game
reflection dump cannot.
