---
id: endgame-splits
title: Splits, the autosplitter and timing
aliases: splits, autosplitter, auto splitter, asl, livesplit, split timing, when does it split, endgame cutscene, vault door split, timmy split, megan split, game end split, load removal, timer start, plane meal, cave splits, clothing splits, passenger splits, in-game timer, igt, rta
tags: timing, endgame, splits, tool
confidence: live
checked: 2026-10-03
sources: game-notes "Endgame splits - the separate triggers", "The autosplitter's other splits"; the LiveSplit ASL (1deter/auto-splitters); CLAUDE.md "Key concepts" (Endgame events)
related: endgame-gate, elevator-skip, forestoverlay
code: playerAnimatorControl.endGameCutScene, playerOpenKeypadDoorAction.openDoorRoutine, PlayerPickupTimmyAction.pickupTimmyRoutine, PlayerGirlTransformAction.doGirlTransformRoutine, PlayerGirlPickupAction.girlToMachineRoutine, PlayerEndCrashAction.doEndPlaneCrashRoutine, LocalPlayer.ActiveAreaInfo
---

# Splits, the autosplitter and timing

The LiveSplit autosplitter splits every endgame event on one shared flag, `endGameCutScene`, the frame it turns on; the other splits (caves, clothing, passengers, the start) read their own fields. When a split fires is therefore when the game's cutscene routine sets that flag - for keypad doors, after the walk-up, not on the button press. ForestOverlay's timer splits on the same frames.

## The endgame splits: one shared flag

The LiveSplit autosplitter (ASL) reads one bool,
`playerAnimatorControl.endGameCutScene`, which **every** endgame cutscene
sets. Its endgame split list: Vault Door, Finding Timmy, Approaching Megan,
Putting Megan in Artifact, Gold Keycard (Automatic Door), Gold Keycard (Red
Elevator), Game End. It splits on the flag's **rising edge** - the frame it
turns true.

When each one fires (confirmed against two real endgame runs):

| Split | The game's routine | Flag set |
|---|---|---|
| Vault door | `openDoorRoutine` (keycard 210) | after the walk-up to the keypad |
| Gold keycard: automatic door | same, keycard 242 | after the walk-up |
| Gold keycard: red elevator | `openDoorRoutine`, sent directly by the elevator | when the ride starts its 5 s keycard sequence (whether it fires on a ride **without** keycard 242 is not confirmed - see `elevator-skip`) |
| Finding Timmy | `pickupTimmyRoutine` | at the start (before its first frame wait) |
| Approaching Megan (she transforms) | `doGirlTransformRoutine` | after its first frame |
| Putting Megan in the artifact | `girlToMachineRoutine` | after its first frame |
| Game end | `doEndPlaneCrashRoutine` / `doShutDownRoutine` | after its first frame |

So a keypad door splits when the walk-up ends, not when you press Take.

## The other autosplitter splits

| Split | What it reads |
|---|---|
| Cave enter / exit (per cave) | `ActiveAreaInfo._currentCave` - changes only through cave entrances / triggers, the snow cave helper, the first death; **not** on a teleport. The endgame lab counts as "not in a cave" |
| Clothing | worn clothing ids, once per item per run |
| Passengers | the found-passenger **count** |
| Plane meal start | the "hold to interact" input flag rising while the game finishes loading |
| Moving | the player's speed over 0.15 m/s |

## The ForestOverlay timer

ForestOverlay's in-game timer fires its endgame events on the **same rising
edge**, so its split times are frame-identical to the autosplitter's; a
Harmony hook only tells it *which* cutscene it was (so it can split them
separately). It can also import a LiveSplit `.lss` and compare against it.

## Evidence

Each routine and where it sets the flag read from the code; confirmed
against the author's two endgame runs (2026-09-22) and the ASL's source.
`end-shutdown`, the Timmy goodbye and the raft out of the world have never
been seen in a real log.
