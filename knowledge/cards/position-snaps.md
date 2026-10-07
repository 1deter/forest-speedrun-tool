---
id: position-snaps
title: Actions that move the player (cliff climb through walls, rope pull, keypad walk-up)
aliases: cliff climb clip, climbing axe clip, climb through wall, rope grab, rope pull, rope teleport, keypad walk up, keypad pull, snap, teleport glitch, raft carry, sit glitch, skinning teleport, position writers
tags: tech, clip, rides
confidence: code
checked: 2026-10-03
sources: game-notes "Speedrun tech and the endgame gate" (Overnight sweep: Position writers); "Caves - Rope climb entrances", "Rides"
related: smash-clip, wall-and-log-boost, elevator-skip, endgame-gate
code: activateCliffClimb.scanForCliff, PlayerClimbCliffAction.enterClimbCliff, PlayerClimbRopeAction.enterClimbRope, playerOpenKeypadDoorAction.openKeypadDoor, DynamicFloor.UpdatePlayerPosition, playerAnimatorControl.updateCliffClimb
---

# Actions that move the player

About 30 scripts set the player's position directly instead of moving the
body through physics. A direct set is a **teleport**: it passes through
anything between the start and the destination that the action's own start
check cannot see. All of them were read (2026-10-03, decompiled + live);
these are the ones that matter.

## Cliff climb (climbing axe) - goes through walls [live]

`activateCliffClimb.scanForCliff` casts **5 m** from the camera with layer
mask 67117056 = **ReflectBig (13) + Terrain (26) only**. Take with the
climbing axe (item 138) snaps the player to `hit.point - forward`
(`enterClimbCliff`).

Anything on another layer between the camera and a climbable surface is
**invisible to that ray**. Live: a 0.3 m wall (a Default-layer cube) 1.9 m
in front of a climbable rock 3.8 m away - Take put the player **0.5 m past
the wall's far face**, climbing.

- On the surface any ReflectBig rock counts (2,264 `Collision` meshes); in
  caves the hit must be tagged `climbWall`.
- Which real walls qualify (player-built walls, doors, the lab) is not
  checked - a wall's layer decides it.
- **No route uses it.** The Any% No Explosive Glitch Creative route (`routes`) has
  no cliff climb [runner: a speedrunner, knowledge-testing feedback 2026-10-05, "climbing
  axe is never used in runs as far as I'm aware"]. This section is how the tool
  works, not a technique runners practise.
- The climb ends itself after 2 s when the surface is under 30° from flat or
  the forward ray misses (12 m) - a steep wall is needed for a long climb.

## Rope grab - a pull of up to ~6 m [code]

`enterClimbRope`: grabbable from up to **6.1 m** (camera to the rope's
bottom trigger). If you are more than 2.5 m below the attach point, your
position is set **to** it. The wall check casts from the trigger to the
camera on Default, ReflectBig, Cave, Wall, Prop, Blocker, Terrain - only
small props, trees and animals let it through.

## Keypad walk-up - no line-of-sight check [code]

`openKeypadDoor` parents the player to the door's `playerPos` from anywhere
within **4.75 m** of the keypad while the grabber touches its trigger,
idle or walking. A walk-up from behind or below a wall is plausible but
untested - and the pull goes to the door's front, where a runner usually
already is. The Take prompt needs an idle or walk animation state (not
jumping / falling / landing) - see `endgame-gate`.

## Others (no gain found)

- **Rafts, houseboats, cranes** (`DynamicFloor.UpdatePlayerPosition`):
  carry you by writing your position every physics step (no collision)
  while you are on them.
- **Bench / chair**: getting up puts you back where you sat.
- **Skinning**: moves you to 1.3 m (3 m for one animal type) from the
  animal, then back toward the start - stays near.
- **Zipline grab**: within 2.5 m, snapped 2.5 m under the line.
- **Rock thrower**: held at its seat, released there.
- **Cave crawl entrances**: parented to the entrance and moved by the
  animation - see `cave-force-load`.
- **Crane climb**: x / z held on the rope.
- **Cutscenes** place you at fixed markers - no destination you choose.
- Dead code: `CaveTriggers.CaveDoorRoutine` (would mirror the player
  through a door) is never started.

## Evidence

Every script that writes the player's position or parent was listed from
the decompiled code (97 methods, ~30 really move the player); the cliff
climb through a wall was reproduced live.
