---
id: knockback-sources
title: What knocks the player back (explosions, traps, thrown rocks)
aliases: knockback, explosion knockback, trap boost, rock trap boost, swinging rock trap, log trap, multi-thrower boost, rock thrower boost, thrown rock, fat creepy charge, explosion damage, bomb damage, explosion radius
tags: tech, explosion, combat, traps
confidence: code
checked: 2026-10-03
sources: game-notes "Speedrun tech and the endgame gate" (Bomb boost; Overnight sweep: knockback sources and the melee correction); the author's confirmation of the trap boost (2026-10-04)
related: bomb-boost, player-physics, deaths-and-revives
code: PlayerStats.Explosion, trapHit.registerTrapHit, thrownRockDamage, MultiThrowerProjectile, fatCreepyCharger, Explode.RunExplode, enemyWeaponMelee
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

## The sources

| Source | How it reaches the player | Notes |
|---|---|---|
| **Bombs and explosives** (`Explode.RunExplode`) | `Explosion(distance)` to everything within range | the standard; cheap, placeable, a small bomb trap is the runners' usual setup |
| **Large swinging rock trap** (`trapHit.registerTrapHit`) | the rock moving faster than **11 m/s** sends `Explosion(-1)` to whatever it hits, the player included | works for boosting (author, 2026-10-04: slower to build and less versatile than a small bomb trap) |
| **Thrown rocks** (`thrownRockDamage`) | a rock faster than **12 m/s** hitting the player | enemies' thrown rocks and the projectiles of the player-built **multi-thrower** (`MultiThrowerProjectile`) |
| **Fat creepy's charge** (`fatCreepyCharger`) | its charge hitting the player | not a setup you control |
| Co-op | the server's explosion event | |

**Not a knockback source: melee.** `enemyWeaponMelee` sends `Explosion` to
*trees* (creepy male, the boss), not to the player. A cannibal's hit is a
normal hit reaction [code].

## Choosing a source for a boost

All of them give the same physics: 8 m/s per rendered frame for the
knockback's ~0.16 s free window, piled up while paused. So what matters is:
- **timing control** - you must pause within a few hundredths of a second
  of the hit; a bomb you place and trigger is the most predictable;
- **direction** - the push goes out of your back; with a trap you face
  away from the rock's swing, with a bomb away from your target;
- **build cost** - a small bomb trap is quick; the swinging rock trap
  takes longer.

## Evidence

The thresholds and the gate are read from the decompiled code
(`PlayerStats.Explosion`, `trapHit`, `thrownRockDamage`). Bombs are tested
live (`bomb-boost`); the trap boost is confirmed in game by the author; the
thrower / thrown rocks / fat creepy are code only.
