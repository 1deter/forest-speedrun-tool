---
id: cave-force-load
title: Cave state force load
aliases: cave force load, force load, cave load, forced cave state, cave state glitch, fall through terrain, through the map, smash in the air at a cave, cave entrance cancel, enter cave cancel, InACave, cave clip
tags: caves, tech, glitch
confidence: code
checked: 2026-10-03
sources: game-notes "Caves", "Speedrun tech and the endgame gate" (Cave state force load, with the 2026-10-03 correction); docs/run-mode.md "Banned moves: detection" (cave state force load); survey of every crawl / swim entrance (2026-10-03)
related: caves-and-loading, smash-clip, player-physics
code: playerEnterCaveAction.doCave, PlayerStats.InACave, PlayerStats.NotInACave, activateCave, CaveTriggers
---

# Cave state force load

Crawl and swim cave entrances play an animation that carries you under the
terrain, and somewhere in the middle the game switches you into **cave
state** (terrain collision off, cave lighting, cave streaming). The switch
is on a **timer**, not tied to the animation. So if the animation is cut
short, the timer has still put you in cave state - and the entrance lets
go of you **wherever the animation had got to**, which can be standing at
the cave mouth on the surface, with the terrain no longer solid under you.

## How runners do it

At a crawl entrance, interrupt the entry animation with an axe **smash in
the air** (the runners' description), so you are released at the mouth in
cave state instead of being carried down [runner]. With terrain collision
off you can drop through the terrain from there.

## Why it works

Taking an entrance (`activateCave` -> `enterCave`) starts
`playerEnterCaveAction.doCave`. In order (decompiled, condensed):

```csharp
FpCharacter.enabled = false;  mouse look off;  velocity = 0;
playerCollider.isTrigger = true;  playerHeadCollider.isTrigger = true;   // no collision
player.parent = entrance;  localPosition = 0;                          // the animation carries you
Animator "enterCaveInt" = 1;                                           // start the crawl animation
yield 1 s  (swim entrances: 2.5 s);
show "loading caves";  yield 0.5 s;
SendMessage("InACave");                       // <- on a TIMER, whatever the animation is doing
do { yield; } while (layer 0 and layer 2 are NOT in an "enterCave"-tagged state);
while (layer 0 or layer 2 IS in an "enterCave"-tagged state)
    hold the player (kinematic, root motion, rotation = entrance's);
// the frame the animation stops:
colliders solid again;  player.parent = null;  controller and mouse look back on;
```

What `InACave` does (`PlayerStats.InACave`): cave lighting
(`Clock.IsCave`), the in-cave flag, cave audio, and
**`IgnoreCollisionWithTerrain(true)`** - caves are under the terrain, so the
terrain must stop being solid. It is meant to arrive while the animation has
already carried you down.

**The let-go is the end of the animation, not a destination.** A normal
entry's root motion ends 7.5 m (Cave 2) to 300 m under the terrain - the
survey of every crawl and swim entrance. If the animation stops early, the
player is released at whatever point the root motion reached - and cave
state is already on.

**Live (2026-10-03):** stopping the animation 1.7 s in (an
`Animator.Rebind`, standing in for the runners' smash) released the player
standing at the **Cave 1 mouth, 1.9 m above the terrain, in cave state** -
the runners' description. With terrain collision ignored, the next step off
the mouth falls through the terrain [live].

## Timings

| Entrance | `InACave` sent after |
|---|---|
| Crawl / climb | **1.5 s** (1 s + 0.5 s) |
| Swim | **3 s** (2.5 s + 0.5 s) |

So the animation must still be going at those marks (otherwise you were
never put in cave state), and must stop before it has carried you under.

## Getting out of cave state

Cave state ends when the game sends `NotInACave`: walking out through a
cave's exit trigger (`CaveTriggers`, `CaveDoor`), an exit animation, a
respawn, or a teleport whose target is above the terrain
(`LocalPlayer.Goto` compares the terrain height with the target's).

## Why it goes wrong

- **Interrupted too late**: the root motion has already carried you down;
  a normal entry.
- **Interrupted too early, or the animation never starts**: the coroutine
  only lets go after it has *seen* an `enterCave`-tagged state and then
  seen it end. If no such state is playing when it starts looking (after
  the 1.5 s mark), it sends `InACave` and then keeps waiting - the player
  stays parented to the entrance, colliders off, controller disabled - until
  an `enterCave` state plays [inferred from the code order; not tested].
  So the useful window is an animation that is still playing at the 1.5 s
  mark and stops before it has carried you under.

## Evidence

- Code: decompiled `playerEnterCaveAction.doCave` (above),
  `PlayerStats.InACave`.
- Live: the cut-short entry at Cave 1; the survey of normal let-go depths.
- ForestOverlay's run mode reports an entry that let go of the player in
  cave state no more than 3 m under the terrain; normal entries are silent.

## Open questions

- Exactly how the runners' smash stops the entry animation (which animator
  layer it holds, which frame window works) - needs the move done for real
  with `anim watch`.
