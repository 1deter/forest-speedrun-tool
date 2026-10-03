---
id: swimming
title: Swimming, diving and water jumps
aliases: swim, swimming speed, swim fast, diving, dive speed, water jump, jump in water, swim jump, wall swim, shore swim, drowning, underwater, swim cave
tags: movement, water, physics
confidence: code
checked: 2026-10-03
sources: game-notes "Speedrun tech and the endgame gate" (Overnight sweep: Water; Deaths)
related: movement-tricks, player-physics, deaths-and-revives, knockback-sources
code: FirstPersonCharacter.HandleSwimmingSpeed, FirstPersonCharacter.Update, FirstPersonCharacter.FixedUpdate, PlayerStats.DeathInWater
---

# Swimming, diving and water jumps

## Speed

- **Surface swimming is capped at 3 m/s** (`maxSwimVelocity`), even when
  sprinting - the target speed would be 3.75 x 2.2 = 8.25 m/s, but the cap
  cuts it.
- **The cap is not applied while touching a wall at your side, or with your
  head under water.** So sprint-swimming **along a shore or wall** could be
  up to ~2.5x faster than open water [code; untested - the author: "might
  be gimmicky"].
- **Diving** has its own cap of 6.5-7 m/s - more than double surface
  swimming [code].

## Jumping in water

`FirstPersonCharacter.Update` has two swim jumps:
- **Small water jump**: at the surface, 6.3 m/s up - a ~0.3 m hop, once a
  second. Too small to notice in play [live].
- **Wall-side swim jump**: touching a wall at your side (or a low mesh
  contact) gives **1.5x the land jump** with no cooldown [code; untested].

Both need `!Diving`. `Diving` (a head sensor 0.25 m under the surface)
only clears once the sensor is back above the surface - so after sinking (a
fall into water, a teleport) **no jump works until you surface** [live].

## Other water rules

- **No inventory under water.**
- **No knockback while swimming**: an explosion gives only the hit reaction
  - no bomb boosts from water (see `knockback-sources`).
- **Landing in water** is judged by the fall-damage rule like any landing;
  live, a drop into the big lake was judged at 0 (and swimming) - no damage.
- **Death in water** (drowning, or killed while swimming) goes to
  `DeathInWater` -> `KillMeFast` after 7 s: the game-over camera, **no
  cave warp even on the first death** in single player (see
  `deaths-and-revives`).

## Evidence

Water jump tested live with the game's real Jump input (2026-10-03); caps
and the wall rule read from the decompiled code.
