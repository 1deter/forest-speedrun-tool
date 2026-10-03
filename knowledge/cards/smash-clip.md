---
id: smash-clip
title: Axe ground smash clips (panel clip, plane clip, elevator clip)
aliases: smash clip, axe clip, panel clip, plane clip, wall clip, door clip, ground smash, axe smash, crouch smash, uncrouch clip, elevator boost, lab skip clip, crouch jump smash, uncapped fps clip
tags: tech, clip, physics, fps, caves, endgame
confidence: inferred
checked: 2026-10-03
sources: game-notes "Speedrun tech and the endgame gate" (Axe / wall clips; Overnight sweep: Looking down moves the player's colliders, The axe ground smash and the panel / elevator clip, Depenetration, the runners' words mapped); sxczurass's Creative Bombless Any% Guide (2025); docs/run-mode.md "Banned moves: detection" (clips)
related: player-physics, wall-and-log-boost, elevator-skip, lab-skip, tunnelling-and-speed-cap
code: FirstPersonCharacter.ScaleCapsuleForCrouching, FirstPersonCharacter.DisableCrouch, FirstPersonCharacter.EnableCrouch, playerAnimatorControl.OnAnimatorMove, playerAnimatorControl.Update
---

# Axe ground smash clips

The runners' way through thin walls - cave wooden panels, the plane's
walls, the red elevator's door, the lab skip's last clip. The pieces the
game gives you are measured: during an axe ground smash the game **stops
resizing your crouch capsule** and **moves your head collider far forward
and down**, and standing up mid-smash leaves you **standing with a
crouch-sized body**. Unity's physics pushes an overlapping capsule out the
shortest way - and once your capsule's centre is a few centimetres past the
middle of a thin wall, the shortest way out is the far side. How the
runners' exact input gets the centre past the middle has **not** been
reproduced yet; this card separates what is measured from what is not.

## How runners do it

The recipe (sxczurass's guide; fruich's tutorial):
1. Crouch, face the wall / panel.
2. Shift + W (run-crouch) and jump.
3. At the top of the jump, **smash the axe into the ground** (attack while
   looking down).
4. As the axe hits the ground, **move the mouse up and uncrouch**.
5. Uncapped fps - the runners say it needs it.

## The measured pieces

**1. The smash stops the crouch resize.** The capsule is resized only
through this method, and it does nothing during a ground smash:

```csharp
public void ScaleCapsuleForCrouching(float alpha)
{
    if (!LocalPlayer.AnimControl.doingGroundChop)
    {
        capsule.height = Mathf.Lerp(originalHeight /*4.7*/, crouchHeight /*3*/, alpha);
        capsule.center = ...;
        HeadBlock.center = new Vector3(0f, Mathf.Lerp(1.76f, -0.1f, alpha), HeadBlock.center.z);
    }
}
```

Standing up runs `DisableCrouch`, a loop that eases `crouch` down to 0
calling `ScaleCapsuleForCrouching` each frame, then sets `crouching = false`.
If you stand up **while `doingGroundChop` is true**, every one of those
calls does nothing - the routine still finishes and marks you standing.
Result (live): you stand (`crouching` false) with the **body capsule still
crouch-size (3 m tall, centre -0.85) until your next crouch**, while the
head sphere snaps back to standing height when the smash ends - a 0.5 m gap
between body and head colliders. That is the "uncrouch when the axe hits
the ground" step.

**2. The smash moves your head collider.** During a ground smash
(`axeAttackGround1`, `doingGroundChop`) the head sphere (radius 0.6 m)
follows the head bone: centre **forward 0.4 -> 1.63 m and down 1.76 ->
0.97 m** over ~30 frames [live]. Pressed against the gold door, it pushed
the body *back* 0.24 m; facing a wall, a smash shoves the player ~1.5 m
back within 0.1 s [live].

**3. `doingGroundChop` timing.** Set in `playerAnimatorControl.OnAnimatorMove`
while the full-body layer plays `axeGround2` or `axeAttack`; comes on
~0.2 s after the swing event and stays true **~1.5 s per smash** [live].

**4. Looking down moves you forward.** Every frame the body capsule's and
head sphere's centres are set to z = `Clamp(normCamX, 0, 0.4)` - looking
straight down shifts both **0.4 m forward** [live]. Moving the mouse up
pulls them back.

**5. Depenetration finishes the job.** A capsule placed overlapping a thin
wall is pushed out along the shortest way, at unlimited depenetration
speed, in one step. Live at the red elevator's 0.1 m door leaf: a capsule
set **0.07 m past the leaf's middle** was pushed out the far side. So the
clip needs only a few centimetres past mid-wall.

## How the pieces might combine [inferred]

A small (crouch-size) body with its centre shifted forward by looking down,
pressed against the wall by the run-crouch jump, then the collider layout
changing in one frame (the mouse up pulling the 0.4 m offset back, the head
sphere snapping back from 1.63 m, the stand-up) - if any frame leaves the
capsule's centre past the wall's middle, depenetration resolves it to the
far side. Higher fps would give more frames - more chances - inside the
short window where the colliders move, which could be why runners need
uncapped fps. **None of this combination has been reproduced live.**

## What has been tried and failed

At the red elevator door, scripted with the game's real inputs (crouch,
look down, axe smash, uncrouch 0.25-0.55 s later, jump every 0.05 s, at the
seam and at the corner) the clip never happened: the smash's head sphere
pushes the player *back* ~1 m [live]. The runners' exact timing and angle
are the missing part.

## Where it is used

- **Cave wooden panels** ("panel clip") - skipping panels you would
  otherwise chop.
- **The plane** ("plane clip").
- **The red elevator** ("elevator boost / elevator skip") - out of the
  locked car early; see `elevator-skip`.
- **The lab skip's last clip** into the elevator corridor; see `lab-skip`.

## Evidence

- Code: `ScaleCapsuleForCrouching`, `DisableCrouch` (above),
  `playerAnimatorControl`'s collider offsets.
- Live (bridge, 2026-10-03): the frozen resize, the head sphere's path,
  `doingGroundChop` timing, the look-down shift, depenetration at 0.07 m past
  mid-leaf, the failed scripted attempts.
- ForestOverlay's run mode reports a capsule entering a solid through a
  front face within 1.5 s of a ground smash (or of touching a built
  structure).

## Open questions

- The exact frame-by-frame collider positions during a successful runner
  clip - needs the move done by hand with `anim watch` and per-frame reads.
- Why fps matters (the "more frames in the window" idea is a guess).
