---
id: zipline-boost
title: Ziplines and the zipline boost
aliases: zipline, zip line, zipline boost, zipline exit, zipline speed, zipline jump, zip boost, rope slide
tags: movement, tech, building, rides
confidence: code
checked: 2026-10-03
sources: game-notes "Caves - Rides: zipline, sled, hang glider, cliff climb", "Speedrun tech and the endgame gate" (Overnight sweep: Rides, Air keeps speed, the runners' words mapped)
related: movement-tricks, player-physics, fall-damage, pausing-and-game-time
code: playerZipLineAction.StickToZipLine, playerZipLineAction.ExitZipLine, playerZipLineAction.PreserveExitVelocity, FirstPersonCharacter.FixedUpdate, activateZipLine.GrabZiplineUpdate
---

# Ziplines and the zipline boost

A zipline accelerates you at **10 m/s² along the line, up to 50 m/s**, with
gravity off. Let go with Jump or Take and you keep that speed - and for
**1 second after letting go the game's own movement braking is about 60x
weaker** than normal. That window, plus staying in the air (where speed
decays far slower than on the ground), is the zipline boost.

## How runners do it

Build a zipline toward where you are going, ride it to full speed, let go
(Jump) before the end, and stay in the air as long as possible - a jump on
landing keeps the speed going [runner + code].

## The ride (code)

- **Grab**: Take within the start gate's trigger
  (`activateZipLine.GrabZiplineUpdate` -> `EnterZipLine`); you are snapped
  2.5 m under the line. The weapon is stowed. No inventory and no pause menu
  on a zipline (the inventory component is disabled).
- **Each physics step** (`StickToZipLine`): gravity off, your position
  projected onto the line (x 0, y -2.5 in the line's space), then
  `AddForce(line.forward * 10, Acceleration)` - **+10 m/s² along the line**,
  capped at **50 m/s**. The slope of the line does not change the
  acceleration (gravity is off); it only sets the direction.
- Reaching 50 m/s from rest takes 5 s and ~125 m of line [inferred:
  calculated].
- **It lets go by itself** when: within 1.5 m of the ground below
  (`CheckCloseToGround`), moving under 0.8 m/s after the first 1.3 s, or the
  line is gone. **You let go** with **Take or Jump**.

## The exit (code)

```csharp
public void ExitZipLine()
{
    if (velocity.magnitude > 10f)
        StartCoroutine(PreserveExitVelocity(line.forward));   // 1 s window
    FpCharacter.StartCoroutine("startJumpTimer");             // air time restarts
    ... controller, mouse look, gravity back on
}

IEnumerator PreserveExitVelocity(Vector3 dir)
{
    _forceDir = dir;  float t = 0f, force = 1f;
    FpCharacter._doingExitVelocity = true;
    while (t < 1f) { force = Lerp(force, 0, t); _forceDir *= force; t += deltaTime; yield WaitForFixedUpdate; }
    FpCharacter._doingExitVelocity = false;
}
```

While `_doingExitVelocity` is true, `FirstPersonCharacter.FixedUpdate`
applies your movement input's velocity change with
**`ForceMode.Acceleration` instead of `ForceMode.VelocityChange`** - on the
ground and in the air. A velocity change of up to 4 m/s per step becomes an
acceleration of 4 m/s², i.e. 1/60th of the effect. Since that same
velocity-change vector is what normally *brakes* extra speed, **for 1 s
after the exit the controller barely slows you down** [code]. It also adds a
small push along the line's direction that fades out within a few steps.

The speed cap (55 m/s) is above the zipline's 50, so it does not cut the
exit speed.

## After the window

Once the 1 s is over, normal rules apply (see `movement-tricks`):
- on the ground, extra speed is braked hard (40 -> 6 m/s in 0.1 s);
- in the air it lasts ~5x longer (~0.6 s from 40 m/s down to walking pace),
  and a jump zeroes input for 0.2 s so the first 0.2 s of a jump loses
  nothing.

So the best exit leaves you in the air with as much of the 1 s window and
fall time ahead of you as possible.

## Fall damage

The exit restarts the air-time timer (`startJumpTimer`), so the 3.8 s
"fell too long" counts from the exit. Letting go high above the ground can
still be a damaging landing (see `fall-damage`).

## Evidence

Code: decompiled `playerZipLineAction` and `FirstPersonCharacter.FixedUpdate`.
Live: ziplines built and ridden over the bridge (Slot 1, Creative, up to
470 m) - the ride mechanics confirmed; the exit window's effect on braking
is read from code, not measured.

## Open questions

- Measured exit distances for different exit heights and line angles.
- Placing ziplines precisely (the big schematic, a short window) - research
  planned.
