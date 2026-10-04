---
id: movement-tricks
title: Movement - diagonal running, air speed, jumps, slopes
aliases: jumping, jump speed, jump while running, is jumping faster, faster in the air, ground friction, friction, diagonal running, run diagonally, strafe running, straferun, air strafing, keep speed, speed in air, coyote time, coyote jump, jump buffer, jump cooldown, bunny hop, bhop, slope jump, slide, sliding, steep slope, wall jump, jump climb, sprint, stamina, run speed
tags: movement, physics, numbers
confidence: live
checked: 2026-10-04
sources: game-notes "Speedrun tech and the endgame gate" (Overnight sweep: Player physics numbers, Diagonal running is 10% faster, Air keeps speed ~5x longer, Jump and grounding rules), "Crouch"
related: player-physics, zipline-boost, bomb-boost, fall-damage, swimming
code: FirstPersonCharacter.HandleFrictionParams, FirstPersonCharacter.ApplyGroundingForce, FirstPersonCharacter.DetermineVelocityChange, FirstPersonCharacter.HandleJumpSpeed, FirstPersonCharacter.Update, FirstPersonCharacter.clampInput, FirstPersonCharacter.OnCollisionStay
---

# Movement tricks

The everyday movement rules, and the small ones runners exploit.

## Diagonal running is 10% faster

`DetermineVelocityChange` clamps your movement input to a length of
**1.1**, not 1:

```csharp
vector = Vector3.ClampMagnitude(vector, 1.1f);
```

W alone has length 1.0; W+A or W+D has length 1.41, clamped to 1.1. So
diagonal input moves you **10% faster** than straight. Live, walking: strafe
6.23 m/s, diagonal 6.91 m/s on the same floor. This is the runners' "always
run diagonally" - hold W+A or W+D and turn your view 45° to face where you
are going.

## Speeds

Walk 6.5, run 13.5 (x up to 1.3 with athleticism), crouch 4.5 (run-crouch
x1.75), swim 3.75 (capped at 3 on the surface - see `swimming`). All times
1.1 diagonally. Running needs stamina above 0.

## Air keeps speed, the ground does not

Extra speed (above your run speed - from a boost, a zipline, a slope):
- **On the ground** it is braked hard: up to 4 m/s removed per physics step
  plus friction. Live: 40 -> 6.3 m/s in 0.1 s.
- **In the air** it lasts about **5x longer**: `HandleJumpSpeed`'s air
  control fades with `clampAirTouch` (which itself decays each frame). Live:
  40 -> 6.5 m/s over ~0.6 s.
- **A jump zeroes all input for 0.2 s** (`clampInput`) - so the first 0.2 s
  of a jump loses **nothing**.

That is why runners jump right after a zipline exit or a boost, and keep
jumping on landing: every moment on the ground costs a lot of speed.

## Jumping while running (the runners' bunny hop)

Runners keep jumping on open ground: being in the air is "essentially
optimal for speed", a bit faster than running on the ground because there
is no ground friction [runner, the bot's feedback 2026-10-04]. What the
code says about it [code]:
- **The ground brakes you a little all the time.** While you move on the
  ground the player's physics material has friction 0.2
  (`HandleFrictionParams`), and `ApplyGroundingForce` pushes you down with
  a force that grows with your speed, which adds to that friction. Each
  physics step the movement code sets your speed back to the target (input
  x run speed, up to 4 m/s per step), and friction takes a little off
  again [inferred]. Measured walking speeds sit about **4% under the target**: strafe
  6.23 m/s for 6.5, diagonal 6.91 for 7.15 [live].
- **The air has no friction.** Off the ground friction is 0, and the air
  steering (`HandleJumpSpeed`) only pulls your velocity toward the same
  target - it can bring you up to the target speed but never past it, and
  nothing brakes you below it.
- **The first 0.2 s of a jump keeps your take-off speed exactly** (all
  input is zeroed, `clampInput`).

So a jump cannot make you faster than your run speed on flat ground, but
in the air you travel at the full target instead of a few percent under
it, and any extra speed (a slope, a boost, a zipline exit) lasts far
longer (above) [inferred from the code + the measured ground gap]. How
much a jump chain gains over running on flat ground has **not been
measured** - the landings (a moment of ground braking each time, the 0.2 s
before the next jump is accepted) take some back. The C# gives a jump no stamina cost (only hunger: 2
calories a second while airborne, `JumpingCaloriesPerSecond`), and holding
Run in the air still drains the sprint bar [code]; whether the jump
animation drains stamina through an animation event (`staminaDrain`) was
not checked [inferred].

Diagonal + jumping stack: the 1.1 input clamp applies in the air too
(`DetermineVelocityChange` feeds both).

## Jump rules

A jump needs `allowJump`, `CanJump`, no ceiling blocking a crouched stand,
not on a rope / sled / climbing / diving / locked / in the inventory, and
**`Grounded` or within 0.21 s of the last grounded physics step**:

```csharp
... && (Grounded || (Time.time < fauxGroundedTimer && !blockFauxJump)) && GetJumpInput() ...
// fauxGroundedTimer = Time.time + 0.21f  on each grounded step
```

- **Coyote time: 0.21 s** - you can still jump that long after walking off
  an edge. Using it sets `blockFauxJump` for 0.5 s.
- `allowJump` turns off 0.25 s after leaving the ground.
- A jump blocks the next press for 0.2 s.
- Jump = 12.65 m/s up, ~3.1 m high, ~1 s in the air (gravity 26 m/s²).

## Slopes and grounding

- `Grounded` is set by **any** collision with a contact below the capsule's
  lower sphere (or 3+ contacts in its lower 0.8 m). **There is no slope
  angle check** - except against capsule colliders (normals steeper than 45°
  do not ground) and surfaces marked slippery (`getWalkableSurface`: the jump
  there is 1/9 as high).
- **Above 65°** (`extremeAngleGroundedLimit`) friction drops to 0 and you
  slide - but you stay grounded and can still jump [live: sliding down an
  ~80° terrain face read `Grounded` and `allowJump` true].
- **Steep terrain holds you**: on a 55-80° terrain slope the game keeps the
  player at ~3 m/s - no speed builds while you keep contact [live].
- **Jump-climbing**: spamming jump against the sinkhole's steep west wall
  (a mesh) gained no height over walking - both stopped at the same point
  [live]. An open terrain face is untested.

## Crouch

Toggle or hold, per your settings (`UseCrouchToggle`). Crouching shrinks
the capsule from 4.7 to 3 m. The capsule is not resized during an axe
ground smash - the basis of the smash clip (see `smash-clip`).

## Evidence

Live over the bridge (2026-10-03): strafe vs diagonal speeds, forced speeds
on ground and in air, the slope slide, jump spam; the jump gate and clamp
read from the decompiled `FirstPersonCharacter`.
