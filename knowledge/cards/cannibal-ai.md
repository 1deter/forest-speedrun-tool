---
id: cannibal-ai
title: Cannibal AI (sight, noise, spawns, when they attack)
aliases: cannibals, cannibal ai, enemy ai, mutants, mutant ai, stealth, how cannibals see you, detection, vision range, crouch stealth, hide in bushes, noise, running noise, cannibal spawns, families, skinny cannibals, regular cannibals, pale cannibals, creepies, stalking, aggression, fear, max attackers, enemy speed cap, hitByEnemy, setNearEnemyVelocity
tags: ai, enemies, stealth, combat
confidence: code
checked: 2026-10-03
sources: decompiled visRangeSetup, mutantSearchFunctions (toLook, toTrack, raycastTargets, checkValidTarget, updateSearchParams), playerTargetFunctions, playerNoiseDetection, mutantSoundDetect, mutantController (setupFamilies, updateSpawns, setDayConditions, doDawnSetupFamilies), mutantSpawnManager (setAmountDay*), mutantDayCycle, mutantAiManager, pmCombatReplace (doStalkRoutine), mutantScriptSetup, FirstPersonCharacter (ClampVelocity, EnableCrouch, doClampVelocity), PlayerClimbRopeAction; docs/fsm/mutant-*.txt and player-noiseDetectFSM.txt; bridge 2026-10-03 on a new Normal game (live sight ranges, crouch masks, run noise, which FSMs exist on a live cannibal); game-notes "Cannibal kinds and families"
related: player-physics, megan-boss, caves-and-loading, deaths-and-revives, dev-console-and-creative
code: visRangeSetup.updateVisParams, mutantSearchFunctions.toLook, mutantSearchFunctions.raycastTargets, mutantSearchFunctions.checkValidTarget, playerNoiseDetection.setNoiseRange, mutantSoundDetect.OnTriggerEnter, mutantController.updateSpawns, mutantController.setDayConditions, mutantSpawnManager.setMutantSpawnAmounts, mutantDayCycle.setDayConditions, pmCombatReplace.doStalkRoutine, FirstPersonCharacter.ClampVelocity
---

# Cannibal AI (sight, noise, spawns, when they attack)

The one idea that explains most of it: **how far a cannibal can see you is
a number computed on *you*, not on the cannibal.** Every 0.65 s the player
works out a sight range from light, crouching, bushes, nearby trees,
running, a lit weapon / lighter and the Stealth stat. A cannibal that has a
clear line to you only counts the sighting if you are closer than that
number. Noise is separate: running sends a pulse to every cannibal within
a radius. Walking normally makes no noise at all.

## What actually runs (C#, not only PlayMaker)

Each cannibal (`mutant_male_BASE` / `mutant_female_BASE`) carries four
PlayMaker FSMs: `global_brainFSM` (mood), `action_combatFSM`,
`action_encounterFSM`, `action_sleepingFSM` [live]. The FSMs still send
events to `global_motorFSM`, `global_visionFSM`, `action_searchFSM` and
`action_inTreeFSM`, but **those FSMs do not exist on a live cannibal**
(`mutantScriptSetup.pmMotor` reads `null`); the events go nowhere [live].
Their work is done in C#:

| Job | Where |
|---|---|
| Sight | `mutantSearchFunctions` (toLook / toTrack), the player's `visRangeSetup` |
| Hearing | `playerNoiseDetection` (player), `mutantSoundDetect` (cannibal) |
| Moving, paths | `mutantAI` (A* pathfinding) |
| Combat moves | `pmCombatReplace` coroutines. The combat FSM is a dispatcher: 29 of its states just start one (`baseStalk` -> `doStalkRoutine`, `runToAttack` -> `runToAttackRoutine`, `checkAngle` -> `doCloseAttackRoutine`, ...) |
| Searching | `pmSearchReplace` |
| Weights / tuning | `mutantAiManager` (stalk, combat, search weight sets) |

The combat FSM is off while a cannibal sleeps and switched on by the brain
when it wakes [live: off on a sleeping one, on and running on an awake one].

## Sight

### Your sight range (`visRangeSetup.updateVisParams`, every 0.65 s)

```
range = (100 - crouch - bush + running) x trees x 1.05 x stealth x light
        + litWeapon + lighter            clamped to 4..100 m
```

| Part | Value |
|---|---|
| `light` | 1 by day; falls from 1 to **0.5** over TimeOfDay 50-90, stays 0.5 until 310, then jumps back to 1 (the code's 270-310 ramp only reaches 0.5, so the clamp holds it there); **0.65 in caves**; never under 0.5 |
| `crouch` | 40 x (1.5 - light): **20 by day, 40 at night**, 34 in caves |
| `bush` | inside a bush (tag `SmallTree`): **50 crouched, 20 standing** |
| `running` | **+35** when the animator's speed is over 0.5. Plain walking is 0.34 and adds nothing [live] |
| `trees` | trees within 32 m: 0-2 trees = 0.8, 7+ trees = **0.4** (1 - n/12, clamped 0.4-0.8). Caves count as 9 trees (0.4) |
| `stealth` | 1 - Stealth / 75 (the Stealth stat from items; 0 on a new game [live]) |
| `litWeapon` | a burning weapon: **+70 x light** |
| `lighter` | lighter or flashlight held: **+50 m, +70 at night on the surface** |

Live, new Normal game, daylight, open ground (0 trees): standing still
**84 m** (100 x 0.8 x 1.05), crouched **67.2 m** (80 x 0.8 x 1.05)
[live]. From the formula [inferred: calculated]:

| Situation | Range |
|---|---|
| Day, open, running | 100 (113 clamped) |
| Night, open, standing | 42 |
| Night, open, crouched | 25 |
| Day, 7+ trees around, crouched | 25 |
| Day, crouched in a bush, 7+ trees | 4 (the floor) |
| Night, holding the lighter, standing | 42 + 70 = 112 -> 100 |

### Crouching also changes what blocks their eyes

Crouching sends nearby cannibals `setVisionLayersOn`: their sight ray then
also stops at layer 12 (`treeSmall`, small trees and bushes) [live: mask
104204288 -> 104208384 while crouched, back when standing]. Only the
cannibals in the scene's "close enemies" list get the message, at the
moment you crouch [code].

### How a cannibal looks (`mutantSearchFunctions.toLook`)

Every ~0.25 s while it has no target [code]:

1. One ray from its head to a point **1.2 m above the centre of your body
   collider**, only if you are within **100 m** and within **65° either side
   of where it faces**. Behind it, it cannot see you at all.
2. The ray must hit you (the first solid thing; a trigger is looked
   through).
3. `checkValidTarget`: the hit counts only if you are **closer than your
   sight range** above, and you are not knocked down (`targetDown`).
4. It locks on (`targetFound` -> combat / encounter / sleep FSMs) **at once
   if you are within 30 m**, or it is already "aware" of you; otherwise
   after **3 valid sightings**. The count does not reset when a look
   misses, only when a target is found, so 3 glimpses spread out still add
   up [code].

Once locked, `toTrack` re-checks every 0.2 s (sight then uses its own close
/ long ranges, 42 m / 90 m on the live cannibal, at least 12 / 30 by day,
8 / 25 at night or in caves). Losing you makes it **aware of you for 30 s**
(`playerAwareReset`): any valid sighting in that window re-locks at once
[code].

### Stealth code that does nothing

The player also sends cannibals a vision range (12 when crouching), a
lighter range, a mud range and a crouch range. They are stored on the
cannibal (`modifiedVisRange`, `modLighterRange`, `modMudRange`,
`modCrouchRange`) and **never read** anywhere in the code [code]. The crouch
one is even sent as `setcrouchRange` to a method named `setCrouchRange`,
so it never lands [code]. Only the formula above counts.

## Hearing

`playerNoiseDetection` sends a pulse every 0.5 s to every cannibal (and
bird, animal) within `soundRange`. The player's `noiseDetectFSM` sets the
range from the animator's speed [code + live]:

| Movement | Surface | Cave |
|---|---|---|
| Standing, walking (speed under 0.9) | **0 - silent** [live] | 0 |
| Speed 0.9-1.5 | the FSM's walk value x 1.4 (value not measured) | x 1.65 |
| Running (over 1.5) | **58.8 m** [live] (42 x 1.4) | 92.4 m (42 x 2.2) [inferred] |
| Weapon swing | its own range for 0.7 s (not measured) | |

Crouched, it stays 0 whatever the speed (the FSM's `crouchIdle` check sends it back to `trackNoise`, which resets the range) [code]. Items with a `SoundRangeDampFactor` effect scale all
of it (0.7 and up). A cannibal that hears a pulse sends its FSMs
`toPlayerNoise` / `toNoise` (go and look where it came from; a sleeping one
gets `toNoise`), then ignores noise for 5 s [code].

## Moods: stalking or attacking

The brain's `chooseMood`: no target seen -> searching; **aggression 5 or
more -> aggressive, under 5 -> passive (stalking)** [code, FSM]. The day
sets aggression (`mutantDayCycle`) [code]:

| Day | Aggression | Fear |
|---|---|---|
| 0, 1 | 1 | 4 |
| 2 | 2 | 4 |
| 3 | 3 | 2 |
| 4+ | 4 | 3 |

So by day count alone every cannibal starts as a stalker. Many events
switch one to aggressive (sets aggression to 10): its family switching to
combat (`mutantFamilyFunctions.switchToCombat`,
`mutantFollowerFunctions.switchToAttack`), its search finding you, another
cannibal as target, several combat routines [code]. Horde mode sets 10.

**A stalker** (`doStalkRoutine`) stands 3-9 s and watches [code]:

- you come within **8 m** -> it attacks;
- you are within **24 m** -> weighted roll: run / back away 1, attack 0.25
  (0.1 by day, 0.15 for skinnies), disengage, leader calls followers;
- you are beyond **53 m** -> it closes in (sneak / run / to a tree);
- the time runs out -> another roll: flank 1, rock, sneak forward, tree,
  run at you, leave the area.

At most **3 cannibals attack at once** (`maxAttackers = 3`, single player)
[code].

## Spawns

These are **families** (a spawner and its members), not single cannibals.
`setupFamilies` (new game, load, dawn) despawns every cannibal, then
`updateSpawns` builds families on the surface spawn points [code]:

- spawn points are sorted by distance from you and **the 4 nearest are
  skipped**: families start at the 5th nearest [code];
- a surface family spawns its members at once; a cave family when you are
  in the caves within 130 m (a sinkhole family 200 m) - game-notes;
- active cap: 15 by day / 16 at night on days 0-2, 18 at night on days
  3-5, 16 by day on day 5, 18 from day 10; caves 20. Days 6-9 have no
  branch and keep day 5's value [code];
- at dawn, cannibals more than **75 m** from you are despawned and new
  families spawn [code];
- on day 0 in daylight a family **sleeps for 250 s** after spawning, then
  wakes (any other time they spawn awake) - game-notes.

Families per day, Normal (Hard differs on days 0-2) [code]:

| Day | Day time | Night |
|---|---|---|
| 0 | 6 skinny | 6 skinny |
| 1 | 6 skinny | 6 skinny, 2 regular |
| 2 | 5 skinny, 2 regular | same |
| 3 | 3 skinny, 3 regular | same |
| 4 | 2 skinny, 6 regular | 1 skinny, 4 regular |
| 5 | 1 skinny, 6 regular | 1 skinny, 3 skinny pale, 4 regular |
| 6-9 | regulars, pale (large and skinny); creepies from night 7 | |
| 10-24 | random 0-1 skinny, 1-5 regular, 0-1 skinny pale, 1-2 pale, 1-2 creepy | |
| 22+ | skinned, painted, creepy, a few regular / skinny | |

The day-15 table (`setAmountDay15`, the only one with painted cannibals
before day 22) can never run: days 10-24 all take the day-10 branch first
[code]. Hard on day 0: 4 skinny + 2 regular.

## Enemy speed caps on the player (3 and 5.5 m/s)

`FirstPersonCharacter.ClampVelocity` has two lower caps. Despite their
names, **enemies do not turn them on** [code]:

- **3 m/s** (`hitByEnemy`): set only by the rope climb's end
  (`PlayerClimbRopeAction.restorePlayerCollisions`), for 1 s, while your
  body overlaps **another co-op player** (with drag 200); cleared by
  `playerHitReactions.disableControllerFreeze`. Never in single player.
- **5.5 m/s for 0.65 s** (`setNearEnemyVelocity`, coroutine
  `doClampVelocity`): nothing in the game's code starts it (no call, no
  `StartCoroutine` / `SendMessage` string). An animation event could still
  call it by name - not checked [code].

## Evidence

- Bridge, 2026-10-03, new Normal game, family near (522.9, 56.7, -10.7):
  player `visRangeSetup` read 84 standing / 67.2 crouched; a cannibal's
  `visLayerMask` 104212480 (spawn default, includes layer 13 `ReflectBig`)
  -> 104208384 crouched -> 104204288 standing; `soundRange` 0 walking
  (`overallSpeed` 0.34), 58.8 running (2.32); `pmMotor` null; combat FSM
  disabled on the sleeping cannibal, enabled on the awake one (brain
  `passive`); `closeVisRange` 42, `longVisRange` 90.
- Code: the files in *sources*.

## Open questions

- The weapon swing's noise range (the FSM export does not show the
  argument; measure `soundRange` during a swing).
- Whether the bush bonus is lost above 60 fps: `bushOffset` is set in
  physics (`OnTriggerStay`) and cleared in `LateUpdate`, so on a frame with
  no physics step it may read 0 when the range is computed [inferred].
- What the Stealth stat's sources are in practice (armour, mud) and their
  amounts.
- Cave sight and noise numbers live (only the surface was measured).
