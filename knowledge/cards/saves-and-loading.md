---
id: saves-and-loading
title: Saving and loading - what a save keeps and what respawns
aliases: save, saving, load, loading, save load, save file, continue, shelter save, save anywhere, respawning items, pickups respawn, keycard respawn, item duplication, save slot, load twice, overlook save, endgame save
tags: loading, route, items
confidence: code
checked: 2026-10-03
sources: game-notes "Saving and loading" (incl. World pickups are not in the save), "Speedrun tech and the endgame gate" (The endgame gate; Dead ends: saving mid-air)
related: endgame-gate, keycards-and-pickups, deaths-and-revives, forestoverlay
code: PlayerStats.OnSaveSlotSelectedRoutine, LevelSerializer.Checkpoint, LevelSerializer.Resume, LoadSave.Awake, LoadSave.Activation, PickUp.ClearOut
---

# Saving and loading

You save at shelters only (never in the overlook), and a load reloads the game scene twice before restoring every saved object. What lies in the world as a pickup - the keycard included - is not in the save at all, so it comes back on every load.

## Saving

- You save at **shelters** (and similar save points) - there is no
  save-anywhere; saving mid-air is not possible [code].
- **Saving is refused in the overlook area** (the endgame's top).
- A save first drops a held glider, closes the inventory / pause view,
  force-unloads streamed content, then writes the level
  (`LevelSerializer.Checkpoint`) and undoes the unload.
- Saves are per slot (Slot 1-5); the previous save is kept as a `prev`
  file.

## Loading

The title screen's Continue loads the game scene, reads the slot, then
**loads the game scene a second time** with the save data and restores
every saved object - a save load loads the game scene twice. The game's
"activation sequence" then runs (this is where an endgame save makes the lab
loadable - see `endgame-gate`).

## What respawns on load

**World pickups are not in the save.** Items lying in the world as pickups
(the keycard in Cave 6, `C6_Props/C6_secretRoom02/Keycard`, and pickups
like it) have no save identity, so **every save load puts them back** -
picked up or not [live, logged in game]. What you carry is saved; the world
copy returns anyway.

## Death and loading

Reloading the save on death (ForestOverlay's reload on death - its Deaths tab - or
manually) is the game's own load of the same save; the author rules the
tool's version allowed in normal runs. See `deaths-and-revives`.

## Evidence

The save and load paths read from the decompiled code; the keycard's
missing save identity logged in game; the double scene load seen in load
timing logs.
