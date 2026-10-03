---
id: tunnelling-and-speed-cap
title: Tunnelling - can you go fast enough to pass through walls?
aliases: tunnelling, tunneling, phase through wall, speed clip, high speed clip, go through door, ccd, continuous collision, discrete collision, speed cap, max speed, 55 m/s
tags: physics, clip
confidence: live
checked: 2026-10-03
sources: game-notes "Speedrun tech and the endgame gate" (Overnight sweep: Player physics numbers, Tunnelling, Bomb boost refined)
related: player-physics, bomb-boost, smash-clip
code: FirstPersonCharacter.ClampVelocity, FirstPersonCharacter.HandleJumpSpeed, playerHitReactions.enableExplodeCamera
---

# Tunnelling

With **Discrete** collision a fast body moves in jumps of speed x 1/60 s
per physics step; if a wall is thinner than one jump, the body can land on
the far side without ever overlapping it. The game closes this in normal
play two ways - the speed cap and CCD during knockbacks - and no way around
both was found.

## Measured (live, the gold door's 0.11 m leaves)

| Player mode | Speed | Result |
|---|---|---|
| Discrete, normal | 20 / 40 / 55 m/s | stopped |
| Discrete, speed cap off | **100 / 200 / 500 m/s** | **passed through** |
| Continuous (the knockback's mode) | 200 / 1,500 m/s | stopped |

## Why it does not happen in play

- **The 55 m/s cap** (`ClampVelocity` / `HandleJumpSpeed`) holds every
  normal movement - running, falling, slopes - at or below 55 m/s, which is
  0.92 m per physics step: too short to skip a door.
- **The explosion knockback** - the one way to go faster - switches the
  player to **Continuous** collision for its whole duration and back to
  Discrete only when it ends. A bomb boost at 1,500 m/s still stops at the
  first collider.

## The gap that would be needed

To tunnel you would need to be over ~60-100 m/s while **Discrete and not
capped** - e.g. the knockback ending (back to Discrete) while you are still
fast and the controller's cap does not apply (locked / movement-locked).
**No such state was found** [live search, 2026-10-03].

## Evidence

Bridge tests against the gold door with forced speeds in each mode; the
cap and the CCD switch read from the decompiled code.
