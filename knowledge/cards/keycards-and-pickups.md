---
id: keycards-and-pickups
title: Keycards and pickups (incl. "spam 1 after the keycard")
aliases: keycard, keycards, vault keycard, gold keycard, keycard 210, keycard 242, maintenance keycard, keycard pickup, spam 1, spam one, pickup animation cancel, book after pickup, keycard location, cave 6 keycard, item ids, owns, fallback items
tags: items, endgame, route
confidence: code
checked: 2026-10-03
sources: game-notes "Speedrun tech and the endgame gate" (The endgame gate; the runners' words mapped; Keypad prompts), "Endgame splits", "Saving and loading" (World pickups)
related: endgame-gate, lab-skip, elevator-skip, saves-and-loading
code: PlayerInventory.Owns, activateKeypadDoor.Update, playerOpenKeypadDoorAction.openKeypadDoor, PickUp.ClearOut
---

# Keycards and pickups

## The keycards

| Item | Id | Opens | Where |
|---|---|---|---|
| Keycard (vault) | **210** | the vault door into the endgame (and loads the lab) | Cave 6, `C6_Props/C6_secretRoom02/Keycard` - the only one in the world |
| Keycard 2 (gold) | **242** | the gold door between the artifact room and the broken corridor | in the lab (its exact spot is not in the knowledge base yet) |

- **Neither has fallback items**: `Owns(id)` is true only if you carry that
  exact item (the only override is the dev console's `itemhack` filter).
- **The red elevator checks no keycard** - only the gold door needs 242
  (`elevator-skip`, `lab-skip`).
- The keypad prompt shows within **4.75 m**, not on a rope, and only in an
  idle / walk animation (not jumping, falling or landing) - "jump from the
  lowest point so the button shows up instantly" (`endgame-gate`).
- One keycard 210 opens the lab for **everyone** in co-op (the door's
  opening runs on every player).
- The keycard is a world pickup: **it respawns on every save load**
  (`saves-and-loading`).

## "Spam 1 after the keycard pickup"

The runners' guide says to spam **1** (the first quick-equip slot) right
after picking up the keycard. The mapping from the code: equipping an item
**cancels the pickup's animation before the survival book opens** - so you
skip that sequence [runner + inferred; not traced in the code line by line].

## Item ids

Ids come from the game's item database (`ItemDatabase.ItemIdByName`).
Examples: Lighter 48, Plane axe 80, Climbing axe 138, Keycard 210,
Keycard 2 242.

## Evidence

Owns / fallback rules and the keypad gate read from the decompiled code;
the keycard path and its respawn logged in game; the "spam 1" mapping is
from the runners' guide and has not been confirmed in code.

## Open questions

- Confirm what equipping interrupts after a keycard pickup (the pickup
  routine and the book) - a code read + an `anim watch`.
