---
id: player-physics
title: Player physics and movement numbers
aliases: physics, gravity, jump height, run speed, sprint speed, walk speed, speed cap, max speed, terminal velocity, fall speed, physics tick, fixed timestep, collision detection, discrete, continuous, ccd, capsule, hitbox, player collider, depenetration
tags: physics, movement, numbers
confidence: live
checked: 2026-10-03
sources: game-notes "Player", "Speedrun tech and the endgame gate" (Overnight sweep: Player physics numbers, Depenetration, Tunnelling, Looking down moves the player's colliders), "Crouch"
related: movement-tricks, fall-damage, bomb-boost, tunnelling-and-speed-cap, wall-and-log-boost, smash-clip
code: FirstPersonCharacter.FixedUpdate, FirstPersonCharacter.DetermineVelocityChange, FirstPersonCharacter.HandleLanded, playerAnimatorControl.Update
---

# Player physics and movement numbers

The player is a Unity rigidbody with a capsule collider, moved by the
`FirstPersonCharacter` script once per physics step. Almost every movement
trick in the game comes down to a handful of numbers on this page: the
gravity, the 60 Hz physics step, the 55 m/s speed cap (and when it is *not*
applied), and the fact that the player's collision is "Discrete" except
during an explosion knockback.

## The numbers

| What | Value | Notes |
|---|---|---|
| Physics step | 60 Hz (`fixedDeltaTime` 0.0167 s) | independent of your fps |
| Gravity | **26 m/s²** in total | Unity's `Physics.gravity` is -16, and `FirstPersonCharacter.FixedUpdate` adds its own extra `gravity * mass` push of 10 |
| Jump | 12.65 m/s straight up, ~3.1 m high, ~1 s in the air | `sqrt(2 * jumpHeight 8 * gravity 10)` |
| Walk | 6.5 m/s | |
| Run (sprint) | 13.5 m/s, up to x1.3 with athleticism | |
| Crouch | 4.5 m/s; run-crouch x1.75 | |
| Swim | 3.75 m/s (surface swimming capped at 3 m/s - see `swimming`) | |
| Hang glider | capped at 40 m/s | |
| Zipline | +10 m/s² along the line, capped at 50 m/s | |
| **Speed cap** | `maximumVelocity` **55 m/s** | applied each physics step only while grounded or jumping, and only while the character controller is in control (not locked, not knocked back) |
| Falling | levels off at **55.43 m/s** | live: from y 1,500 the speed held there for 16 s (drag 0, mass 7); it is the 55 cap plus one step of gravity - see below |
| Max speed change per step | `maxVelocityChange` 4 m/s | how fast ground movement can accelerate or brake |
| Input vector | clamped to length **1.1**, not 1 | W+A / W+D is 10% faster than W alone - see `movement-tricks` |

[live] for gravity, jump, the fall cap, the speed cap's effect and the
diagonal; the rest read from the code and the player's live fields.

## Collision

- **Discrete collision detection** in normal play: each physics step the
  capsule is placed at its new position and overlaps are resolved. A body
  moving fast enough can skip over a thin collider between two steps
  ("tunnelling"). With the 55 m/s cap that does not happen in normal play;
  see `tunnelling-and-speed-cap`.
- **Continuous (CCD) during an explosion knockback**: the knockback
  coroutine switches the rigidbody to `Continuous` for its whole duration
  and back to `Discrete` at the end. That is why a bomb boost stops at the
  first tree or wall in its path instead of passing through it.
- **No interpolation** on the player's rigidbody.
- **Depenetration is unlimited** (`maxDepenetrationVelocity` = 1e32). When
  a solid ends up overlapping the player (a wall built into you, a log, a
  door that closes on you), PhysX pushes the capsule out along the shortest
  way in one step. Live: a box moved 0.3 / 1 / 2 m into the feet lifted the
  player out by exactly that depth, **with no velocity left over** - a
  lift, not a launch. This is the core of wall boosts, log boosts and most
  clips (see `wall-and-log-boost`, `smash-clip`).

## The player's colliders

- **Body capsule**: 4.7 m tall standing, **3 m crouched**
  (`EnableCrouch` / `DisableCrouch` resize it).
- **Head sphere**: radius 0.6 m, follows the head.
- **Looking down moves both forward**: every frame `playerAnimatorControl`
  sets the body capsule's and the head sphere's centre z to
  `Clamp(normCamX, 0, 0.4)` - looking straight down shifts them **0.4 m
  forward** [live]. Relevant for clips against walls and panels.
- **During an axe ground smash** the head sphere follows the head bone
  forward to **1.63 m** and down from 1.76 to 0.97 m over ~30 frames [live]
  - see `smash-clip`.

## Where the speed cap is applied - and where it is not

`FirstPersonCharacter.FixedUpdate` runs its movement code only inside
`if (!MovementLocked && (!Locked || (Grounded && !prevGrounded)))`. Inside
it the speed is clamped to `maximumVelocity` (55 on the player; the
script's default of 25 is overridden) in two places:
- `ClampVelocity()` while **grounded or swimming**;
- `HandleJumpSpeed()` while **in the air** (`jumping` is set by
  `HandleStartJumping` the moment you leave the ground, by a jump or by
  walking off a ledge).

```csharp
float magnitude = rb.velocity.magnitude;
if (magnitude > maximumVelocity)
    rb.velocity = rb.velocity.normalized * maximumVelocity;
```

**That is also why a fall levels off at 55.43 m/s**: the clamp sets 55 at
the start of the physics step, then the step's gravity adds
26 m/s² x 1/60 s = 0.43 m/s before the speed is read again
(55 + 0.43 = 55.43, exactly what was measured) [inferred from the code +
the live number]. So "terminal velocity" in this game is the same cap as
the ground speed cap, not air drag.

The cap does nothing when:
- the controller is **disabled** - the explosion knockback does
  `LocalPlayer.FpCharacter.enabled = false` for its whole duration, which is
  why a bomb boost can reach thousands of m/s;
- the player is **locked** or **movement-locked** (menus, cutscenes,
  rides, keypad walk-ups) - the movement block is skipped entirely;
- the player is **diving** (its own 6.5-7 m/s cap instead) or on a ride
  (zipline 50, glider 40, their own rules).

The code also has lower caps of 3 m/s (`hitByEnemy`) and 5.5 m/s
(`setNearEnemyVelocity`, 0.65 s). Enemies do not turn them on: the 3 m/s
one is set only after a rope climb while your body overlaps another co-op
player, and nothing in the code starts the 5.5 m/s one - see `cannibal-ai`
[code].

## Ground and air

- **On the ground**, extra speed is braked hard: up to 4 m/s removed per
  physics step plus friction. Live: 40 m/s -> 6.3 m/s in 0.1 s.
- **In the air** it lasts about 5x longer: air control (`HandleJumpSpeed`,
  fading with `clampAirTouch`) brought 40 m/s down to 6.5 m/s over ~0.6 s.
  A real jump also zeroes all input for 0.2 s (`clampInput`), so the first
  0.2 s of a jump loses nothing at all. This is why runners jump right
  after a zipline exit or a boost - see `movement-tricks`.
- **Grounded** is set by any collision with a contact below the capsule's
  lower sphere (or 3+ contacts in its lower 0.8 m). **There is no slope
  angle check** except for capsule colliders (steeper than 45° does not
  ground) and surfaces marked slippery. Above 65°
  (`extremeAngleGroundedLimit`) friction drops to 0 and you slide - but you
  still count as grounded and can jump [live].

## Frame rate

The physics runs at 60 Hz whatever your fps, but some game logic runs once
per *rendered* frame (coroutines, `Update`). Where those push the player -
the explosion knockback's 8 m/s per frame - a higher fps means more pushes.
That is the whole reason fps matters for the bomb boost. Animation-driven
actions (the axe smash) also sample once per frame.

## Evidence

All numbers read from `FirstPersonCharacter`'s fields on the live player and
checked with bridge tests on 2026-10-03 (falls from y 1,500, jumps,
forced speeds on ground and in air, boxes moved into the player).
