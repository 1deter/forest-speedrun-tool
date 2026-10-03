---
id: wall-and-log-boost
title: Wall boost, log boost and structure clips (depenetration)
aliases: wall boost, log boost, custom wall boost, climbing wall boost, defensive wall boost, structure boost, build boost, log clip, wall clip, keycard cave clip, depenetration, pushed out, placed wall under player, building on yourself
tags: tech, physics, building, clip
confidence: live
checked: 2026-10-03
sources: game-notes "Speedrun tech and the endgame gate" (Axe / wall clips and log boosts; Overnight sweep: Depenetration, Movers in the world, the runners' words mapped); docs/run-mode.md "Banned moves: detection" (lifts and clips); sxczurass's Creative Bombless Any% Guide (2025)
related: player-physics, smash-clip, endgame-gate, tunnelling-and-speed-cap
code: FirstPersonCharacter.FixedUpdate, Craft_Structure.Build
---

# Wall boost, log boost and structure clips

When a solid object appears overlapping the player, Unity's physics
(PhysX) pushes the player's capsule out along the shortest way, in a single
physics step, with no speed limit. Build a wall or drop logs where you
stand, and the shortest way out is often **up** - onto the top of it. That
is the "custom wall ... boost yourself to the top" in the runners' guide,
and the log boost. When the shortest way out is through thin rock behind
you, the same push is a **clip**.

## How runners do it

- **Wall boost**: build a custom (or climbing) wall so that it overlaps
  you; finish it and you are pushed up onto it [runner].
- **Log boost**: logs placed / squeezed into the player lift them [runner].
- **Keycard cave clip** (true any%): a log or stone wall squeezing the
  player into thin rock pushes them through it [runner, the author's
  description].

## Why it works

- The player's rigidbody has **`maxDepenetrationVelocity` = 1e32** -
  effectively unlimited. Any overlap is resolved fully in one step.
- PhysX resolves an overlap by moving the body along the **minimum
  translation**: the shortest distance that separates the two shapes. For a
  wall built into your legs that is usually straight up by the overlap's
  depth; for a wall pressing you into a thin rock it can be through the
  rock.
- No game script moves the player here - it is pure physics.

**It is a lift, not a launch** [live]: a box moved 0.3 / 1 / 2 m into the
player's feet lifted the player out by **exactly that depth**, in one step,
with **zero velocity left over**. You end up standing on top; you do not
fly. Any extra height has to come from a jump after it.

## Numbers

- Lift = the overlap depth, instantly (one physics step, 1/60 s).
- Player capsule: 4.7 m tall standing, 3 m crouched, so the deepest lift
  from one overlap is about the capsule's height [inferred].

## Why it goes wrong

- **Pushed sideways or down instead of up**: the minimum translation
  pointed elsewhere - the structure overlapped you more from the side than
  from below.
- **Nothing happens**: the structure does not overlap the capsule when it
  becomes solid, or it is a trigger / not collidable with the player (the
  game turns collision off between some pairs - structures on rafts, for
  example).
- **Moving platforms**: some world objects are movers - the yacht's hull
  bobs on a kinematic body (~0.1 m), and pushes a player standing on it with
  no velocity. That is not a boost, just the platform [live].

## Evidence

- Live (bridge, 2026-10-03): boxes moved into the player at three depths;
  a leaf hut's collider moved 1.2 m into the feet lifted the player 1.2 m.
- ForestOverlay's run mode reports a rise with no velocity to explain it
  while touching a player-built structure (`BuildingHealth` up the
  parents), and a capsule entering a solid within 1.5 s of touching one.

## Open questions

- The runners' own wall boost and keycard cave clip have not been done with
  real input over the bridge yet; which structures and placements work best
  is their knowledge.
