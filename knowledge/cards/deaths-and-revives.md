---
id: deaths-and-revives
title: Deaths - the first-death cave warp, death in water, the last stand
aliases: death, dying, first death, death warp, cave warp, wake up in cave, captured, drag away, cave 2 dead place, death skip, second death, game over, permadeath, last stand, adrenaline, grey zone, drowning, death in water, boss fight death, empty health bar, health recharge, rechargehealth, reload save on death, quick load
tags: deaths, route, caves
confidence: live
checked: 2026-10-08
sources: game-notes "Deaths", "Speedrun tech and the endgame gate" (First death; Overnight sweep: Deaths)
related: pausing-and-game-time, fall-damage, swimming, caves-and-loading, forestoverlay
code: PlayerStats.KillPlayer, PlayerStats.CheckDeath, PlayerStats.DeathInWater, PlayerStats.KillMeFast, PlayerStats.hitFromEnemy, PlayerStats.RechargeHealth, PlayerStats.Fell, DeadSpotController, PlayerStats.EndgameWakeUp
---

# Deaths

In single player your first death is not a game over: you wake up in a cave - always the same spot, `Cave2DeadPlace` - unless you died in water or in the boss fight. The second death ends the game. An enemy hit that would kill you while you are above the grey zone leaves you on just over 1 health instead (the last stand).

## The first death: always the same cave

In single player, your **first** death (outside the boss fight) is not a
game over: you are "captured" and wake up in a cave. `PlayerStats.KillPlayer`
with `DeadTimes == 1`:
- in the endgame: the lab is unloaded (`LoadEndgame.ForceUnload`) and the
  overlook / endgame areas exited;
- the player is warped to a "random" `DeadSpotController.DeadSpots` entry,
  put in cave state (`SetCurrentCave(1)`, `InACave`), `WakeInCave`;
- starvation set to 0, thirst to 0.35, `TimeOfDay` to 1.

**All seven DeadSpots are the same place**: `Cave2DeadPlace` at
(-692.29, 110.44, 1110.9). So the first-death warp is **always there**
[live]. A first death outside a cave plays the drag-away cutscene first,
then the same warp.

## Exceptions

- **Death in water** (drowning, or killed while swimming) goes to
  `DeathInWater` -> `KillMeFast` after 7 s - the game-over camera, **no
  warp, even on the first death** [code].
- **The boss fight**: dying while fighting the boss in the endgame runs
  `EndgameWakeUp` instead of the warp.
- **Second death** (`DeadTimes > 1`): the dead camera, then `GameOver` after
  6 s (back to the title screen). With permadeath on, the save is deleted.
- **God mode** (`Cheats.GodMode`, on in Creative): `CheckDeath` returns
  early - no death from damage at all.

## The last stand

`PlayerStats.hitFromEnemy`: while your health is **above the grey zone
(10)**, an enemy hit that would kill you is clamped to leave you **just over
1 health**, and the adrenaline rush starts. Only the *next* hit can kill.
An "empty" health bar that survives cannibal hits is this [live]. Fall
damage and explosions do not go through this path (fall damage calls `Hit`
directly).

**It re-arms.** While your health is 10 or less the game checks every 2 s
and, 12 s later, sets it to 11 (`RechargeHealth`; not while sprinting).
At 11 you are above the grey zone again, so the next hit that would kill
you leaves you on 1 again. With an empty-looking bar you survive every
enemy hit that comes 12-14 s or more after the last one; only a hit
inside that window kills [code + live: 1 health, 12-14 s later 11,
another 28-damage hit -> 1, alive]. This is how "my bar was empty but I
died only a few hits later" happens in the Megan fight (megan-boss).

## Fall deaths

- A landing over 28 m/s does `0.9 x v² / 27.5` damage; at the max fall
  speed that is 100 - lethal from full health.
- Over 3.8 s in the air = 1000 damage.
- Scripted fall triggers send `Fell` (-200 health).
See `fall-damage` (including how the slide cancel avoids all of it).

## ForestOverlay: Reload save on death

The tool's Deaths tab (*When I die*: Automatic by default, or *Reload the
save*) reloads your save on
every death instead of the game's death sequence, skipping the title screen
(the game's own `LevelSerializer.Resume`). The author rules it allowed in
normal runs: it is the game's own load of the same save. With practice mode
and a spot set, it can revive you at the spot instead (practice only).

## Evidence

The DeadSpots read live (all seven entries the same object); the death
paths read from the decompiled `PlayerStats`; the last stand reproduced over
the bridge (50 health - 80 damage -> 1.02); its re-arm (1 -> 11 after
12-14 s, then another lethal hit -> 1) and Megan's hits (28 each, 100 -> 1
-> dead) over the bridge 2026-10-08 (T-0044).
