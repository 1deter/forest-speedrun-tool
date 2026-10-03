---
id: endgame-gate
title: The endgame gate - can the game be beaten without the vault keycard?
aliases: vault door, vault keycard, keycard 210, keycard skip, true any%, keycardless, beat the game without keycard, endgame loading, endgame_streaming, lab loading, end buttons, finish game trigger, keycard bounty, sinkhole door, keypad door, keycard button
tags: endgame, route, keycards
confidence: code
checked: 2026-10-03
sources: game-notes "Speedrun tech and the endgame gate" (The endgame gate; The game's own teleports; Keypad prompts), "Loading the endgame area"
related: lab-skip, elevator-skip, endgame-splits, keycards-and-pickups, caves-and-loading
code: activateEndCrash.Update, SceneLoadTrigger.SetCanLoad, SceneLoadTrigger.ForceLoad, LoadSave.Activation, activateKeypadDoor.Update, PlayerInventory.Owns, TheForest.World.Area.OnEnter
---

# The endgame gate

Short answer: **in single player, no.** The buttons that end the game exist
only in the lab's streamed scene, and the game loads that scene only after
the vault door has opened with keycard 210 in *this* save - or from a save
made inside an already loaded lab. No item, Megan or flag check sits on the
end buttons themselves; the gate is the loading.

## Beating the game, in code

Take twice on `Sections/ControlRoom/endPlaneCrashPrefab1/TriggerFINISHGAME`
(the plane-crash ending) or `TriggerDEACTIVATE` (shutdown), at
(-446, 707.5, -1757) / (-430, 705.7, -1766). `activateEndCrash.Update`
checks only: in range, view `World`. No keycard, no Megan [code].

## Why they cannot be reached early

- Those triggers exist only in **`endgame_streaming`** (the lab scene).
- It loads only through `EndgameEntrance/LoadEndgame` (a
  `SceneLoadTrigger`): `ForceLoad` does nothing until `_canLoad` is set.
- `SetCanLoad` has **exactly two callers**:
  1. the **vault door's** `onDoorOpen` event;
  2. `LoadSave.Activation`, when the save says it has an active endgame area
     (`_isInEndgame` **and** an active area hash).
- The vault door (`activateKeypadDoor.Update`) needs `Owns(210)` within
  4.75 m. Keycard 210 (and the gold keycard 242) have **no fallback items**,
  and `Owns` only skips the check through an item filter set by the dev
  console's `itemhack` (Creative does not set it).
- The save path: `_isInEndgame` is set just by crossing the endgame box
  forwards (no keycard), but the **active area** only by `Area.OnEnter`,
  whose callers are the lab's own area gates (all 23 inside
  `endgame_streaming`), the boss fight's wake-up, a save that already has
  the hash, and the dev console. The two areas in the main scene are only
  entered by gates inside the lab.

So a save made in the vault entrance without the door opened does **not**
load the lab on Continue.

There is **one keycard 210** in the world: `C6_Props/C6_secretRoom02/Keycard`
(Cave 6), a world pickup that respawns on every save load.

## "True any%" and the keycard cave clip

Runners' true any% includes a **wall clip in the keycard cave**, using a
structure they build (a log or stone wall squeezing them into thin rock)
[runner, the author's description 2026-10-03]. Keycard 210 is still needed
for the vault door. See `wall-and-log-boost` for the depenetration behind
it.

## Not covered

- **Multiplayer**: a client of a host who opened the vault door - the door
  opening runs on every player, so one keycard opens the lab for all.
- Anything physical that reaches the buttons with the lab loaded (that is
  the lab skip, which still needs the vault door).

## Teleports

The game's own teleports (the dev console's `goto`) never load the lab
without the door either: `Goto(Transform)` within 150 m on the lab side
invokes the endgame box's crossing (`EnterEndgame` + `ForceLoad`), which
still needs `_canLoad` [code].

## The keypad prompt

`activateKeypadDoor.Update` shows the Take prompt only within **4.75 m**,
not on a rope, and with the base animator layer in an **idle- or
walk-tagged state** - not jumping, falling or landing. Hence the runners'
"jump from the lowest point so the keycard button shows up instantly": land
early and the prompt is there [code].

## Evidence

Code read in full for every caller named above (decompiled), the save
fields checked on real saves, the console's teleport read from code.
