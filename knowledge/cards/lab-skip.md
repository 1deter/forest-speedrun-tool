---
id: lab-skip
title: Lab skip (past the gold door without Timmy, Megan or the boss)
aliases: lab skip, gold door skip, megan skip, boss skip, timmy skip, invisible collision, invisible section, invisible floor, endgame collision, EndCollision, seven jumps, lab clip, artifact room skip
tags: endgame, tech, clip, route
confidence: code
checked: 2026-10-03
sources: game-notes "Speedrun tech and the endgame gate" (The endgame gate; Overnight sweep: Red elevator, area gates, the lab's collision); sxczurass's Creative Bombless Any% Guide (2025)
related: endgame-gate, elevator-skip, smash-clip, endgame-splits
code: activateKeypadDoor, TheForest.World.Area, AreaGate, SceneLoadTrigger, LoadSave.Activation
---

# Lab skip

The normal endgame route goes through the lab: Timmy, Megan, the boss,
then the **gold keycard** (242) opens the gold door to the corridor that
leads to the red elevator. The lab skip gets past the gold door another way
- over **invisible collision** that is always there, then a clip into the
elevator corridor - and since the red elevator **checks no keycard**, the
run can finish without ever picking up keycard 242. It skips Timmy, Megan
and the boss.

## Why invisible collision exists

The lab's collision lives in the **main scene**, not in the lab's own
streamed scene: `EndCollision`, on layer 25 (`Blocker`), which the player
collides with - the artifact room, the boss room, offices, corridors. It is
there whether or not the lab's visible sections are drawn. The sections
draw only when you enter them through their **area gates** (`AreaGate`,
`Area.OnEnter`). So a route that reaches places without passing their gates
walks on solid but invisible geometry - the runners' "invisible section"
[code + live].

## The route (runners' description)

Rocks, ledges and **seven jumps on invisible collision**, then a smash
clip into the elevator corridor (see `smash-clip`) [runner].

## The area order (route order, from the lab's gates)

... GlassOffice_C -> ArtifactRoom -> CorridorBasic -> Meetingrooms -> Lab
Corridor -> BossRoom (Megan), and ArtifactRoom -> **(gold door)**
BrokenCorridor -> EndgameCaves -> HellCorridor (red elevator) ->
ControlRoom.

The keycard is checked **only** at the gold door
(`ArtifactRoom/ElevatorCardReader/Trigger`, `activateKeypadDoor`, item 242),
which unlocks `LabDoor_Door (4)` between the ArtifactRoom and the
BrokenCorridor. Get past that door any other way and nothing later asks for
it.

## Things to know

- **The lab must be loaded at all** - that needs the vault door opened with
  keycard 210 (see `endgame-gate`). The lab skip skips the *gold* keycard,
  not the vault keycard.
- **With the lab unloaded** (after leaving it backwards, a teleport out, a
  first death) the main-scene collision has an **open doorway** where the
  gold door stands (only a side wall remains) - but the doors, the elevator
  and the end buttons all live in the lab's streamed scene, so there is
  nothing to use beyond it [live].
- Once the vault door has opened, re-entering reloads the lab **fresh** -
  doors locked again.

## Evidence

Collision and area gates read live over the bridge; the gold door and the
elevator chain from code and live tests (`elevator-skip`). The jump route
itself is the runners' and has not been retraced here.
