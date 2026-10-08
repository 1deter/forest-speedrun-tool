---
id: knockback-sources
title: What knocks the player back (explosions, traps, thrown rocks)
aliases: knockback, explosion knockback, trap boost, rock trap boost, swinging rock trap, log trap, multi-thrower boost, multithrower, multi thrower, rock thrower boost, rock thrower, catapult, thrown rock, knockback direction, fat creepy charge, explosion damage, bomb damage, explosion radius
tags: tech, explosion, combat, traps
confidence: live
checked: 2026-10-08
sources: game-notes "Speedrun tech and the endgame gate" (Bomb boost; Overnight sweep: knockback sources and the melee correction); the author's confirmation of the trap boost (2026-10-04); game-notes "The multi-thrower and the Cave 6 body slide" (2026-10-08)
related: bomb-boost, player-physics, deaths-and-revives
code: PlayerStats.Explosion, playerHitReactions.lookAtExplosion, trapHit.registerTrapHit, thrownRockDamage, MultiThrowerProjectile, rockThrowerAnimEvents.throwRocks, playerEnterRockThrowerAction.doThrower, fatCreepyCharger, Explode.RunExplode, enemyWeaponMelee
---

# What knocks the player back

Every knockback that can be turned into a boost goes through one method,
`PlayerStats.Explosion(dist)`. If the distance is under 15 m it starts the
same knockback coroutine as a bomb - so **anything that calls it can be
pause-stacked exactly like a bomb boost** (see `bomb-boost`). The sources
differ only in how easy they are to set up.

## The gate: `PlayerStats.Explosion`

```csharp
if (isExplode || endGameCutScene || in the enter-cave animation
    || already in the explode animation || view below World) return;
if (dist < 15f)
{
    isExplode = true;  Invoke("resetExplosion", 2.2f);   // one per 2.2 s
    damage 25 (x3 from the player's own explosive with realistic damage), minus armour
    LocalPlayer.Inventory.Close();
    CheckDeath();
    if alive: swimming -> hit reaction only; else -> knockback (damage FSM "toHitFall")
}
```

What that means for a runner:
- **Within 15 m** of the source (the distance the source passes in; a trap
  passes -1, i.e. always in range).
- **25 damage** per knockback (armour absorbs some; 75 from your own bomb
  with *realistic player damage* on). You must survive it - a knockback
  that kills you does not boost.
- **One knockback per 2.2 s**: a second explosion inside that window is
  ignored entirely (no damage, no push).
- **No knockback while swimming**, during an endgame cutscene, or during a
  cave-entrance animation.
- It **closes the inventory** - which is why only the pause menu can stack
  the push.

## The direction: straight away from the source

Every source below sends a second message right after `Explosion`:
`lookAtExplosion(source position)`, which turns the player (yaw only) to
face the source - so the knockback, which pushes out of your back, goes
**straight away from the source**. Your facing before the hit does not
matter [live: a real bomb and a multi-thrower rock both turned the player
toward them; see `bomb-boost`]. The one exception in the code: on a rope
you are not turned [code].

## The sources

| Source | How it reaches the player | Notes |
|---|---|---|
| **Bombs and explosives** (`Explode.RunExplode`) | `Explosion(distance)` to everything within range | the standard; cheap, placeable, a small bomb trap is the runners' usual setup |
| **Large swinging rock trap** (`trapHit.registerTrapHit`) | the rock moving faster than **11 m/s** sends `Explosion(-1)` to whatever it hits, the player included | works for boosting (author, 2026-10-04: slower to build and less versatile than a small bomb trap) |
| **Thrown rocks** (`thrownRockDamage`) | a rock moving at **7.2 m/s or more** whose 2 m hit sphere reaches you | enemies' thrown rocks and the projectiles of the player-built **multi-thrower** - see below [live] |
| **Fat creepy's charge** (`fatCreepyCharger`) | its charge hitting the player | not a setup you control [code] |
| Co-op | the server's explosion event | |

**Not a knockback source: melee.** `enemyWeaponMelee` sends `Explosion` to
*trees* (creepy male, the boss), not to the player. A cannibal's hit is a
normal hit reaction [code].

## The multi-thrower

The player-built rock thrower (catapult): you sit in it, it fires up to
**3 items at once** at the spot you aim at. It takes rocks, skulls,
molotovs, timed bombs and dynamite [code].

**How a shot flies** [code]: each item is launched on a ballistic arc that
lands **exactly 2.2 m from the aim point** (a random direction around it -
never on it) after a random **2.4-2.6 s**. It comes down at about 20 m/s
[arithmetic: a level target, the game's gravity 16 m/s²].

**What a rock does to you** [live]: each rock carries an invisible hit
sphere of **2 m** radius, switched on **0.75 s after the launch** (during
the first 0.75 s of its flight it hits nothing). If that sphere reaches
your body while the rock moves at **7.2 m/s or more**, you get the full
knockback - 25 damage, turned to face the rock, pushed straight away from
it - exactly like a bomb. By the code it can be pause-stacked the same way
(`bomb-boost`) [code - not tried after a rock hit]. A slower rock does nothing, even if it bounces off you.

Why 7.2 m/s: the game's check is `checkVel >= 12`, where `checkVel` is
the distance the rock moved in the last physics step x 100. The physics
step is 1/60 s, so `checkVel` = speed x 1.667, and 12 means 7.2 m/s.

```csharp
// thrownRockDamage.FixedUpdate
checkVel = (currPos - lastPos).magnitude * 100f;
// thrownRockDamage.OnTriggerEnter, the player's hit collider
if (checkVel < 12f) return;
other.SendMessageUpwards("Explosion", 8);          // 8 m: well inside 15
other.SendMessage("lookAtExplosion", transform.position);
```

**A timed bomb** loaded into it is thrown as the bomb itself, without the
rock's hit sphere: it arms on the launch and explodes on its own 3 s fuse,
i.e. shortly after landing, knocking you back like any bomb [code + the
prefab read live]. Dynamite has its own thrower prefab too; how it behaves
was not checked.

**What that means for a boost** [code]:
- A landing rock is always fast enough, so a shot that lands within ~2 m
  of you knocks you back. Three rocks still give only **one** knockback
  (the 2.2 s lock).
- You fire from the seat (Fire1) and can leave it at once (Take); the
  shot needs 2.4-2.6 s to land, so the timing is fixed by the flight -
  which makes the pause timing (`bomb-boost`: pause within a few
  hundredths of a second) harder to read than with a bomb you trigger.
- The direction is away from the rock at the moment it hits, and rocks
  come down from above: the more nearly overhead the rock is when it
  reaches you, the less predictable the direction [inferred].

## Choosing a source for a boost

All of them give the same physics: 8 m/s per rendered frame for the
knockback's ~0.16 s free window, piled up while paused. So what matters is:
- **timing control** - you must pause within a few hundredths of a second
  of the hit; a bomb you place and trigger is the most predictable;
- **direction** - the push goes straight away from the source (you are
  turned to face it on the hit), so place the bomb / trap / landing spot on
  the far side from your target;
- **build cost** - a small bomb trap is quick; the swinging rock trap
  takes longer.

## Evidence

The thresholds and the gate are read from the decompiled code
(`PlayerStats.Explosion`, `trapHit`, `thrownRockDamage`). Bombs are tested
live (`bomb-boost`); the trap boost is confirmed in game by the author.
The multi-thrower (2026-10-08, v0.24.256): its projectile thrown at the
player at 15, 8 and 6 m/s (`checkVel` 25, 13.3 and 10 - knockback, knockback,
nothing), a real shot from a thrower aimed at the player's feet (knockback
on landing, 25 damage), and the turn to face the source with both rocks
and a real timed bomb. The fat creepy is code only.

## Open questions

- How runners use the multi-thrower for a boost (if they do): where they
  stand, which ammo, and how they time the pause - not described in the
  guides we have.
- Whether a rock older than 8 s can still knock you back (its speed check
  stops updating then) [code: not tested].
