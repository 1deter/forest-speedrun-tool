---
id: bomb-boost
title: Bomb boost
aliases: bb, bomb boosting, pause boost, menu boost, explosion boost, explosive boost, dynamite boost, bomb jump, esc boost, pause buffer boost, knockback boost
tags: movement, tech, explosion, physics, fps
confidence: live
checked: 2026-10-03
sources: game-notes "Speedrun tech and the endgame gate" (Bomb boost; Overnight sweep: Bomb boost, refined; measured with real pauses); sxczurass's bomb boost measurements by fps (QA, 2026-09-26); the author's QA post 2026-09-26; docs/run-mode.md "Banned moves: detection"
related: knockback-sources, player-physics, pausing-and-game-time, tunnelling-and-speed-cap, movement-tricks
code: playerHitReactions.enableExplodeCamera, PlayerStats.Explosion, PlayerStats.ExplosionPlayer, Explode.RunExplode, playerAnimatorControl.Update, HudGui.TogglePauseMenu
---

# Bomb boost

Stand near an explosion, open the pause menu the instant it knocks you back,
wait, close the menu - and you fly hundreds of metres. The reason is one
loop in the game's knockback code: it pushes the player **8 m/s backwards
once per rendered frame**, and it keeps doing that while the pause menu has
stopped physics. Every frame you sit in the menu adds another 8 m/s that
has nowhere to go; the first physics step after the menu closes applies all
of it at once. So the boost's size is **fps x time paused**, and when you
pause decides how long you keep that speed.

## How runners do it

1. Set up a small bomb (or another explosive) so it goes off within 15 m of
   you, facing **away** from where you want to fly - the push goes out of
   your back.
2. The instant the explosion hits you, press **Esc** (the pause menu).
3. Wait in the menu - longer = faster, up to what the landing and the path
   allow.
4. Close the menu. You launch along the direction your back was facing.
5. Uncapped / high fps makes each second in the menu worth more.

## Why it works

**The knockback.** An explosion within 15 m (`Explode.RunExplode` ->
`PlayerStats.Explosion(dist)`) does 25 damage, closes the inventory and puts
the player's damage FSM into its fall state, which starts the coroutine
`playerHitReactions.enableExplodeCamera`. Its core, verbatim from the game:

```csharp
LocalPlayer.FpCharacter.enabled = false;          // no speed cap, no air control
LocalPlayer.Rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
float timer = 0f;
while (timer < 0.5f)
{
    LocalPlayer.MainRotator.enabled = false;      // mouse look off
    LocalPlayer.CamRotator.enabled = false;
    timer += Time.deltaTime;
    LocalPlayer.Rigidbody.AddForce(LocalPlayer.Transform.forward * -8f,
                                   ForceMode.VelocityChange);
    yield return null;                            // = once per rendered frame
}
// then, while the "explode" animation plays: the same push for another 0.25 s
```

Three things in that loop make the boost:
- **`yield return null` = once per rendered frame**, not per physics step.
  At 240 fps it pushes 240 times a second; at 60 fps, 60 times.
- **The timer counts `Time.deltaTime`**, which is 0 while the game is
  paused. The pause menu (`HudGui.TogglePauseMenu(true)`) sets `timeScale`
  to 0: physics stops and `deltaTime` is 0, but **coroutines keep running**.
  So in the menu the timer never advances and the loop never ends - it adds
  8 m/s *every frame you are paused*.
- **`VelocityChange` forces queue up until the next physics step.** With
  physics stopped, none of the pushes are applied; they pile up. The first
  physics step after you close the menu applies all of them together.
  (`FirstPersonCharacter.Update` zeroes your horizontal velocity while the
  menu is open, but that only touches the current velocity - not the forces
  waiting for the physics step.)

**Why the speed then stops.** The free push lasts until the player's
animator (layer 2) enters the `explode`-tagged state, about **0.13-0.16 s of
game time** after the blast. From then on, every frame,
`playerAnimatorControl.Update` does:

```csharp
if (fullBodyState2.tagHash == explodeHash)
    LocalPlayer.Rigidbody.velocity = new Vector3(0f, LocalPlayer.Rigidbody.velocity.y, 0f);
```

- the **horizontal** speed is wiped every frame while that animation plays;
  the **vertical** speed is kept. That is why a boost that hits a slope can
  turn into a huge launch straight up.

**The direction** is your back at each pushing frame. Mouse look is
switched off during the knockback (both rotators disabled), so in practice
it is where your back faced at the blast.

**Why it cannot go through walls**: the knockback switches the player to
Continuous collision detection (CCD). The capsule is swept along its path,
so it stops at the first collider - live, 200 m/s and 1,500 m/s boosts into
the gold door both stopped. See `tunnelling-and-speed-cap`.

**Why the speed cap does not apply**: the cap (55 m/s) lives in
`FirstPersonCharacter`, which the knockback disables (`enabled = false`)
for its whole duration.

## Numbers

Measured live (2026-10-03, real pause menu, ~235 fps, in the air with
nothing to hit):
- Speed after closing the menu = **8 m/s x frames paused** (+ the frames
  pushed before the pause).
- That speed is held for **~10 physics steps (~0.163 s of game time)**, then
  the horizontal part is zeroed.
- Distance = 8 m/s x 0.163 s = **~1.3 m per paused frame**, i.e.
  **distance ≈ 1.3 m x fps x seconds paused**.
  - 0.5 s / 1 s / 2 s paused (123 / 237 / 475 frames) -> 170 / 308 / 652 m.
- Knockback alone (no pause): peaks at ~95-264 m/s, 25-40 m of travel.
- 1 s paused at ~200 fps: 1,564 m/s after the menu closed [live].

**Pausing late costs a lot.** The ~0.163 s window counts from the blast in
game time. For the same 1 s pause:

| Pause after the blast | Window left | Distance |
|---|---|---|
| 0 s | 0.163 s | 317 m |
| 0.05 s | 0.11 s | 239 m |
| 0.10 s | 0.04 s | 91 m |
| 0.15 s | (gone) | 51 m |

If the explode animation has already started when you pause, the piled-up
push is applied for **one physics step** only and then wiped: a hop of
8 x frames paused / 60 m = **0.13 m per paused frame** (0.5 s at 240 fps =
16 m) - the "late regime".

sxczurass's table (distances at 180 / 210 / 240 fps for 1-5 s paused) sits
at 0.8-0.9 of the in-air figure (240 fps, 1 s: 276 m vs 312 m) - what a
pause ~0.02-0.03 s after the blast gives, i.e. a human reaction time.

## The optimal version

1. **Pause the instant the bomb goes off.** Every 0.05 s of delay costs
   about a third of the distance.
2. **Max fps.** Distance scales linearly with frames paused, so 240 fps
   gives 4x what 60 fps does for the same wait.
3. **A clear, level runway behind you** (or be in the air - a jump just
   before the blast). The boost covers its distance in only ~0.16 s; if the
   capsule touches anything inside that window, CCD stops it there or
   deflects it.
4. **Pause length = the distance you need.** The distance is linear in time
   paused only while nothing is hit; a longer pause is a faster boost that
   is *more* likely to meet a slope or object within its 0.16 s.
5. Face exactly away from the target at the blast; you cannot steer during
   the knockback.

## Why it goes wrong

- **"Flying off course" / sideways drift**: the capsule touched something
  inside the window. Live, on the ground where sxczurass tested, 1 s and 2 s
  boosts (~1,870 / ~3,700 m/s) both met the same terrain rise ~115 m out;
  the CCD contact turned the velocity sideways and up (1,870 m/s ->
  (315, 198, -633)): only ~170 / ~250 m of horizontal travel instead of
  ~310 / ~620 m, and a launch of 540 m / 1,100 m straight up (the explode
  state only wipes horizontal speed).
- **Long pauses lose more than they should** (sxczurass's 3-5 s rows): a
  faster boost covers more ground in the same 0.16 s, so it is more likely
  to meet a slope or object.
- **Hitting a tree or object**: CCD stops you at it (a tree 23 m out ended
  one live test).
- **Paused too late**: the late regime - a small hop.
- **The inventory does not work instead of the pause menu**: it refuses to
  open while the knockback's root motion is on (or while jumping).
- **A second explosion within 2.2 s is ignored** (`isExplode`).
- **Swimming**: a swimming player only gets the hit reaction, no knockback.
- **Multiplayer**: the pause menu does not stop time there
  (`HudGui.TogglePauseMenu` sets `timeScale = 0` only when
  `!BoltNetwork.isRunning`), so nothing piles up. In single player it stops
  time on **every difficulty**, Hard included [code]. (The *inventory*'s
  pause is different: Normal / Peaceful / Creative only - and it cannot be
  opened during the knockback anyway.)

## Other things that start the same knockback

Anything that calls `Explosion` on the player with a distance under 15 m
starts the same coroutine, so the same pause stacking works: bombs and
other explosives, the **large swinging rock trap** (confirmed by the author:
works, but slower to build and less versatile than a small bomb trap),
thrown rocks over 12 m/s (enemies' and the player-built multi-thrower's),
the fat creepy's charge. **Not** melee hits. See `knockback-sources`.

## Evidence

- The code above: decompiled `playerHitReactions.enableExplodeCamera` and
  `playerAnimatorControl.Update`.
- Live tests over the test bridge (2026-10-03, v0.24.225-227): real pause
  menu through the game's own Esc input, explosions at the player, speeds
  and positions read every physics step; air and ground runs; the pause
  delay sweep; CCD against the gold door.
- ForestOverlay's run mode detects the boost from the mechanism itself
  (pushes made while game time is stopped) - every real boost reported,
  plain knockbacks silent.

## Open questions

- A boost visualiser (the predicted path swept with the player's capsule)
  is designed, not built.
