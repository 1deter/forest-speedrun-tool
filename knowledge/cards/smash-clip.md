---
id: smash-clip
title: Axe ground smash clips (panel clip, plane clip, elevator clip)
aliases: consistent clips, clip consistency, smash angle, smash clip, axe clip, panel clip, plane clip, wall clip, door clip, ground smash, axe smash, crouch smash, uncrouch clip, elevator boost, lab skip clip, crouch jump smash, uncapped fps clip
tags: tech, clip, physics, fps, caves, endgame
confidence: inferred
checked: 2026-10-04
sources: game-notes "Speedrun tech and the endgame gate" (Axe / wall clips; Overnight sweep: Looking down moves the player's colliders, The axe ground smash and the panel / elevator clip, Depenetration, the runners' words mapped); sxczurass's Creative Bombless Any% Guide (2025); docs/run-mode.md "Banned moves: detection" (clips)
related: player-physics, wall-and-log-boost, elevator-skip, lab-skip, tunnelling-and-speed-cap
code: playerAnimatorControl.LateUpdate, treeHitTrigger, FirstPersonCharacter.ScaleCapsuleForCrouching, FirstPersonCharacter.DisableCrouch, FirstPersonCharacter.EnableCrouch, playerAnimatorControl.OnAnimatorMove, playerAnimatorControl.Update
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
capsule's centre past the wall's middle at a physics step, depenetration
resolves it to the far side. Physics steps stay at 60 Hz, so higher fps
does not add chances; it updates the collider layout more often between
steps (point 3 below), which could be why runners need uncapped fps.
**None of this combination has been reproduced live.**

## Making it more consistent: what the code pins down

The clip itself has not been reproduced, so there is no tested "optimal
version". What the code does fix are the angle, the uncrouch window and
how frame rate enters - three things a runner can stop worrying about or
aim for.

**1. The angle: any pitch that smashes already has the full forward
shift.** The smash is allowed only while the animator's `normCamX` is
above `axeSmashAngle` (`playerAnimatorControl.Update`): **0.43** on flat
ground, in caves and on structures, raised up to **0.63** when you face
downhill within 0.5 m of the terrain (`treeHitTrigger`) [code]. With the
axe out, `normCamX = pitch / 82 - 0.1` (pitch in degrees below level), so
on flat ground the smash needs about **43° down** once the plane intro is
over [code, computed]. The
colliders' forward offset is `Clamp(normCamX, 0, 0.4)`: full **0.4 m from
41° down** [code]. So looking further down than the smash needs adds no
reach. Moving the mouse up takes the offset off the **body capsule** only
(0.2 m left at ~25°, none at ~8°) - during a ground smash (and a jump
crouch) the head sphere is not given that offset; it follows the head
bone instead (`if (!doingGroundChop && !doingJumpCrouch)`) [code].

**2. The uncrouch is a window, not a frame.** Standing up eases the
`crouch` value from 10 to 0 with `SmoothDamp` (0.1 s), and the capsule
only starts to change once that value is under 1 - the size is a clamped
`Lerp` [code]. That works out to the capsule changing between about
**0.2 s and 0.46 s after you let go of crouch** [inferred: computed from
SmoothDamp's curve at a steady frame rate]. To keep the crouch-size body,
that stretch has to fall inside `doingGroundChop` (on ~0.2 s after the
swing, for ~1.5 s [live]) - so uncrouching anywhere from about the swing
to ~1 s after it should freeze the capsule; earlier than the swing and it
resizes normally [inferred]. The runners' "uncrouch as the axe hits the
ground" sits inside that window.

**3. Frame rate does not add physics checks.** The physics runs at a
fixed 60 Hz whatever your fps; the colliders' offsets are set once per
*rendered* frame (`LateUpdate`), and the physics only sees the layout that
is there at its next step [code]. So uncapped fps does **not** give
"more collision checks". What it does change: the collider layout is
updated several times between physics steps, so the layout the physics
sees is closer to where your mouse and the animation are at that instant;
below 60 fps several physics steps see the same layout [code]. Whether
that is why runners need uncapped fps is not known [inferred].

**What is left to the runner** (none of it measured): where you stand
against the wall, how hard the run-crouch jump presses you into it, and
the mouse-up movement during the smash. The open question below (a
frame-by-frame read of a successful clip) is what would turn this into a
real optimal version.

## What our tests tried [dev]

Our own test setup, not advice for runners: at the red elevator door, a
script sent the game's real inputs (crouch, look down, axe smash, uncrouch
0.25-0.55 s later, jump every 0.05 s, at the seam and at the corner) and
the clip never happened - the smash's head sphere pushed the player *back*
~1 m. For a runner this means only: the clip has not been reproduced in
our tests, and the runners' exact timing and angle are the missing part.

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
- Live (2026-10-03): the frozen resize, the head sphere's path,
  `doingGroundChop` timing, the look-down shift, depenetration at 0.07 m past
  mid-leaf.
- Our failed scripted attempts at the elevator door (above) [dev].
- ForestOverlay's run mode reports a capsule entering a solid through a
  front face within 1.5 s of a ground smash (or of touching a built
  structure).

## Open questions

- The exact frame-by-frame collider positions during a successful runner
  clip - needs the move done by hand with `anim watch` and per-frame reads [dev].
- Why fps matters: physics stays at 60 Hz, so it is not more collision
  checks; the idea that it is the fresher collider layout at each step is a
  guess.
- The body capsule's radius (not recorded) - with it, how far past
  mid-wall the centre can get from the 0.4 m look-down shift alone.
- A runner reports a temporary **~50 m/s** after a good elevator or panel
  clip, bigger the better the clip [runner, the knowledge bot's feedback
  2026-10-03]. Not measured: the live depenetration test (a box lifting the
  player, `wall-and-log-boost`) left zero velocity, but it was not a clip
  through a wall. Measuring it needs a real clip with the speed read each
  frame. A "big position change in one step = big speed" explanation does
  not fit that measurement (the lift moved the player up to 2 m in one step
  and left no speed) - why a clip would differ is not known.
