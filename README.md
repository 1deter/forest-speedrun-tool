# ForestOverlay

A speedrun information overlay for **The Forest**, built as a
[BepInEx](https://github.com/BepInEx/BepInEx) plugin.

Displays velocity, a run timer and inventory counts in-game, and includes a
runtime type explorer for discovering the game's internals without decompiling.

> **Status: early.** Working proof of concept, not a finished tool.

## Features

- **Speed / velocity** readout (magnitude and per-axis)
- **Run timer** with manual start/stop/reset
- **Inventory item count**, read live from the game
- **Type explorer** — browse every class in the game with live field values,
  filterable, in-game
- **Dump system** — writes the game's type index, scene hierarchy and a live
  player snapshot to files for offline analysis
- **Practice position save/restore** *(practice only — writes game state)*

## Hotkeys

| Key | Action |
|---|---|
| `F5` | Toggle HUD |
| `F6` / `F7` | Save / restore position |
| `F8` / `F9` | Start-stop / reset timer |
| `F10` | Toggle type explorer |
| `F11` | Write dump files |

## Install

1. Install BepInEx 5.4.x (x64) into your Forest folder, run the game once.
2. Download `ForestOverlay.dll` from
   [Releases](../../releases) — or build it yourself, below.
3. Drop it into `<game>\BepInEx\plugins\`.

## Build

```bash
dotnet build -c Release -p:ForestManagedPath="<path>\TheForest_Data\Managed"
```

Or `./scripts/deploy.ps1` to build and install in one step.

The project builds without the game installed (CI does exactly that), falling
back to a stubbed UnityEngine assembly. No game files are included in this
repository.

## Run legality

Information-only features read game state and never write it. Position
save/restore writes state and is **practice only**. The Forest's speedrun
moderators have not yet ruled on this tool — don't assume any of it is
permitted in submitted runs until they have.

See [`CLAUDE.md`](CLAUDE.md) for development context and
[`docs/game-notes.md`](docs/game-notes.md) for the game internals reference.
