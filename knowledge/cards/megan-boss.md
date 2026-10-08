---
id: megan-boss
title: Megan's boss fight (how her AI works)
aliases: megan, megan fight, spear strat, spin attack, spin chance, megan boss, boss fight, final boss, girl boss, girl mutant, spider megan, megan ai, megan attacks, megan babies, boss babies, baby spawn, megan health, kill megan, bombing megan, megan stagger, girlMutant, megan dodge, megan damage, empty health bar, health bar empty
tags: endgame, boss, ai, combat
confidence: live
checked: 2026-10-08
sources: docs/fsm/megan-action_combatFSM.txt (exported 2026-10-03, ruben-megan, Normal); decompiled girlMutantAiManager, creepyAnimatorControl, creepyAnimEvents, EnemyHealth, enemyWeaponMelee, spawnMutants, mutantTypeSetup, PlayerStats; PlayMaker.dll; bridge 2026-10-03 and 2026-10-08 (T-0044, game-notes "Megan's boss AI"); game-notes "Deaths"
related: endgame-splits, deaths-and-revives, lab-skip, knockback-sources
code: girlMutantAiManager.setAiParams, girlMutantAiManager.setInitialWeightParams, girlMutantAiManager.setupHealthParams, creepyAnimatorControl.activateGirlMutant, creepyAnimEvents.birthLeft, EnemyHealth.Explosion, EnemyHealth.HitReal, EnemyHealth.HitFire, enemyWeaponMelee, mutantTypeSetup.removeFromSpawn, spawnMutants.updateSpawnConditions, PlayerStats.hitFromEnemy, PlayerStats.RechargeHealth
---

# Megan's boss fight (how her AI works)

Megan's fight is run by a PlayMaker state machine (`action_combatFSM` on
`girl_base`) plus a small C# script that retunes its numbers twice a
second. The one idea that explains most of the fight: **the distance
between you and her decides everything.** Within 35 m she goes straight
into an attack every cycle, and the attack is picked by distance alone.
Only when you are **beyond 35 m** does she reach the weighted "what next"
roll, and that roll is where the baby births come from. Her own dodges
throw her 30-55 m back, so she gets beyond 35 m even when you stand still
[live]. Nothing in the FSM picks an attack at random from a moveset. The
random parts are the dodge roll, the spin roll and the action roll beyond
35 m.

## The fight loop

After the transformation cutscene, `creepyAnimatorControl.activateGirlMutant`
sends `begin`, turns her AI on, sets her animation speed to **1.2x** and
marks you as fighting the boss (`IsFightingBoss`, which is why a death here
wakes you up instead of warping you, see deaths-and-revives) [code].

**The first two seconds**: her first move is a short run "home" (her
leash, below, still measures from (0, 0, 0)); her first attack comes about
2.2 s after `begin` [code + live].

One cycle, in the FSM's own state names [live: the transition log]:

1. `randomIdle` -> `setToPlayer` (0.2 s, targets the closest player) ->
   `worldCheck` -> `moveToPlayer`.
2. `moveToPlayer`: she walks at you. **The check runs every frame: the
   moment you are within 35 m she switches to attacking.** If you are still
   farther away after 1-2 s (random; live 1.0-2.0 s over 50), she goes to
   `chooseAction` (below).
3. Attack path: `checkTurns` (turns first if you are more than 135° behind
   her) -> `chanceToDodge` -> `chooseAttack`.
4. After the attack: a cooldown (up to 0.8 s), then back to step 1.

`chooseAttack` picks **by distance to her target**, nothing else [live: 130
picks, each in its band within the timing of the poll]:

| Distance | State | What it is |
|---|---|---|
| under 8 m | `doCloseStomp` | stomp, 2 s + 1 s cooldown |
| 8-13 m | `doCloseAttack` | close swing (one of two), 1.2 s; ~5% chance of the spin attack instead |
| 13-27 m | `doMidAttack` | mid-range attack (one of five), up to 1.3 s; ~5% spin |
| 27-38 m | `doLongAttack` | long attack (one of three), 1.5 s |
| 38-50 m | `doJumpAttack` | the leap (charge) attack, 2 s |
| over 50 m | `runToPlayer 2` | runs at you for up to 7-10 s, leaps once you are within 35 m (50/50 a second run + an arm smash; live 14 of 26) |

The spin attack (`longSpinAttack`, 4 s) is rolled at the start of a close
attack (8-13 m), a mid attack (13-27 m) and a counter (below): weight 0.03
against 0.6 (0.03 read live). PlayMaker's `SendRandomEvent` divides by the
total (`ActionHelpers.GetRandomWeightedIndex`, IL), so one roll is **0.03 /
0.63 = 4.76%**, not 3%. The stomp, long, leap and run attacks never roll
it. Over n rolls the chance of at least one spin is 1 - 0.9524^n: 21.6% for
5, 38.6% for 10, 47.0% for 13, 62.3% for 20 [code; arithmetic]. Live: 10
spins in 161 rolls (6.2%), within chance of 4.76% [live]. With two or
more players within 25 m of her, the weight becomes 1 (1 / 1.6 = 62.5% a
roll) [code].

A runner measured "3% on the dot" per attack (QA, 2026-10-03). It fits if
only ~63% of her attacks roll at all: 0.63 x 4.76% = 3.0% [inferred].

## The dodge

`chanceToDodge` runs before every attack chosen from step 3:

- The dodge weight is **0 for the first 15 s** after she activates
  (`setInitialWeightParams`), then 0.25 against 1: **20%** per cycle
  [live: 0 of 6 rolls in the first 15 s, 17 of 97 after (17.5%)].
- A special roll for "hit in the last 1.3 s" (`gettingHit`, 29% walk
  back) never comes up: after a hit her next dodge roll is at least 1.6 s
  away [code; live: 45 hits, none used it].
- **A dodge throws her far back.** Within 15 m she walks back (`doWalkback`,
  2 s); half of those turn into the dodge animation (`doDodge2`, 2.5 s).
  Both move her a long way: live the dodge animation moved her 28-55 m
  (5 dodges), a walk back up to 35 m. She typically ends 35-65 m from a
  player who stood still, so her next move is a leap, a run or the action
  roll (births) [live]. Beyond 15 m she attacks instead [code].

## When you hit her: the counter

Every melee or projectile hit (`EnemyHealth.HitReal`) sends `gotHit`. After
0.2 s she counter-attacks: within 8 m a counter swing (one of three, 1.2 s),
farther away straight into `chooseAttack` [live: 45 hits, every one went
`gotHit` -> `counterAttack` after 0.2 s; 38 of them beyond 8 m went on to
`chooseAttack`]. **Hitting her does not interrupt her. It starts her next
attack.**

`counterAttack` rolls the spin itself (4.76%) **at any distance**, before
the 8 m check; beyond 8 m it then goes into `chooseAttack`, which rolls
again in the close / mid bands (8-27 m) [code: FSM export]. So each hit
is **one roll from under 8 m or beyond 27 m, two rolls from 8-27 m**
(1 - 0.9524^2 = 9.3% for that hit) [code; arithmetic].

**The spear fight's spin risk.** 10 spears solo means at least 10 rolls
from the counters alone: 38.6% for at least one spin if every throw lands
from beyond 27 m (or within 8 m), 62.3% if every throw lands from 8-27 m
(20 rolls) [arithmetic]. Her own close / mid attacks between your throws
add more rolls; how many depends on how long the fight takes, which is not
measured (Open questions) - so these are floors, not the whole risk.

## Explosions and the stagger

- An explosion takes a **flat 30 health** whatever the distance (normal
  cannibals take 65) [live: 13 explosions at 0-60 m, each -30].
- **Two explosions within 0.1 s count once**: each explosion blocks the
  next for 0.1 s (`explodeBlock`) [code + live: two explosions two frames
  apart, 40 -> 10 -> 10].
- Each explosion has a **25% chance to stagger her for 10 s** (she stops in
  `hitstagger`), unless she is already staggered; explosions during a
  stagger neither re-roll nor extend it. Otherwise she flinches for 1 s
  (`hitExplode`) [code + live: 2 staggers in 9 rolls, the stagger lasted
  10.0 s].
- Explosions do nothing before she has transformed [live: two on the
  seated Megan, 370 stayed 370]; nor do hits [code].

## Health and damage

- **370 health** (read live, Normal). No difficulty scaling of her health
  was found in the code [code].
- **Multiplayer** (`setupHealthParams`, when she activates): `Health +
  123 x n`, n = every player within 350 m of her, you included: 2 players =
  616, 3 = 739, 4+ = 800 (cap) [code].
- **A thrown spear does 40** (plain and upgraded; `ArrowDamage.damage` on
  the spear's `Tip`, read live), no headshot bonus, no difficulty factor
  [code + live]. Throws to kill:
  10 solo (370), 16 with 2 players (616), 19 with 3 (739), 20 with 4+ (800)
  [arithmetic].
- **She enters the "hurt" weights below half health (185)**, but see the
  babies section for why that rarely matters [code].
- Her melee hits use the creepy damage: **28 per hit on Normal** [live: 100
  -> 72 -> 44 -> 16 with god mode off], 42 on Hard (`28 x
  creepyDamageRatio`, 1.5 on Hard). If **she** is poisoned, her hits are
  divided by 1.6 (17 / 26) [code].
- Fire on her: 2 damage per tick on Normal; on Hard fire against creepy
  enemies is cut to 0.35x, so 1 per tick [code].
- 370 / 30 = 13 explosions kill her from full health, with nothing else
  [arithmetic].

## When your health bar looks empty

Her hits go through the game's **last stand** (deaths-and-revives): a hit
that would kill you above 10 health leaves you on 1. Live, standing in
front of her: 100 -> 72 -> 44 -> 16 -> **1** -> dead on the next hit 2.2 s
later [live]. It re-arms: 12 s after you drop to 10 or less the game sets
you to 11, so a hit 12-14 s or more after the last one is clamped to 1
again; only a hit inside that window kills [code + live]. That is how an
empty-looking bar lasts "a few more hits".

## The babies and the "beyond 35 m" roll

`chooseAction` is a weighted random pick among actions. Its weights are set
by `girlMutantAiManager.setAiParams`, **twice a second** (two 1 s timers)
[code + live]:

| Weight | You beyond 35 m | Within 35 m, her health above half | Within 35 m, at or below half |
|---|---|---|---|
| give birth (babies) | 6 | 6 | 6 |
| "charge" attack | 0.5 | 0.5 | 1 |
| "long" attack | 5 | 4.5 | 2 |
| strafe | 0 | 1 | 0.6 |
| walk back | 0 | 0.6 | 0.2 |
| walk forward | 2 | 1 | 1.5 |
| idle | 0 | 1 | 1 |
| run forward | 0 | 0 | 0 |

Two things the weight names hide [code]:

- **"Charge" and "long" are the same choice.** Both send `toMainAttack`,
  which goes to `chooseAttack`, and that picks by distance (the table
  above). Together they are simply "attack now".
- **She reaches `chooseAction` only after 1-2 s of walking at you while
  you stay beyond 35 m**, and in that case the "beyond 35 m" column is the
  one that applies. So in practice: **44% give birth, 41% attack, 15% keep
  walking** [live: 70 rolls - 27 birth, 32 attack, 11 walk; 5 more with
  you beyond 105 m went wandering]. The two "within 35 m" columns can only
  apply in the half second or so after you close in, so the health switch
  barely changes the fight [inferred from the FSM + the update rate].

**Giving birth**: she walks back for 3 s, then the birth animation (4.5 s).
**One birth drops 6 `bossBabySpawner`s**, each spawning **one** baby
[live, twice]. While **more than 2 spawners exist** (`fsmBlockBabySpawn`),
a birth roll is cancelled and re-rolled at once [live]. A spawner goes when
its baby dies, so **kill 4 of the 6 and she can give birth again** [code +
live]. Boss babies die by themselves 300 s after they spawn [live]. In the
boss room they **do not** die with Megan (three alive 36 s after) [live].

**What this means for a fight**:

- **Within 35 m she never rolls for a birth**, but her dodges (20% of
  cycles after 15 s) throw her 30-55 m away: standing still, the first
  birth came 28.8 s in, after a dodge [live]. Following her back within
  35 m within 1-2 s keeps the births away [inferred].
- **Backing off beyond 35 m is what brings the babies**, nearly one roll
  in two, until the 6 babies of a birth are mostly dead.
- Bombs: 13 explosions, each a 1-in-4 chance of a free 10 s window; space
  them more than 0.1 s apart.

## Leashes and the overworld Megan

- If you get more than 310 m from her start position (in the boss room,
  where she is not `activeInWorld`), she runs back towards it for up to
  7 s, then checks again next cycle [code + live: 305 m no, 315 m yes].
- Beyond 105 m she stops chasing and wanders (`moveAroundWorld`, 90% random
  waypoints) [code + live].
- The **overworld** Megan (spawned by `spawnMutants.spawnGirl`, not the
  endgame) is a different setup: `activateGirlMutantInWorld` gives her **145
  health**, 1.25x size and 1.1x animation speed. Only there does her death
  kill her babies through `killAllBossBabies` [code].

## Evidence

- FSM export: `docs/fsm/megan-action_combatFSM.txt` (94 states), plus
  `megan-global_motorFSM.txt` and `megan-global_alertManagerFSM.txt`, from
  the transformed Megan in the `ruben-megan` savestate (Normal).
- PlayMaker checks an FSM's **global** transitions before the current
  state's own (`Fsm.ProcessEvent`, IL): the export's blank local
  transitions for `toWander`, `goToAttack` and `toMainAttack` never apply.
- Live counts [dev]: PlayMaker's own transition log over three fights
  (2026-10-08, god mode on, one fight with it off for her damage); every
  number is in game-notes "Megan's boss AI".

## Open questions

- How long a full fight takes with each strategy (within 35 m vs bombs).
- Melee weapon damage to her by weapon (the thrown spear's 40 is read).
- The artifact: her FSM has `toRepelArtifact` / `toAttractArtifact`
  branches (`repelArtifact`, `findArtifact`) that this card does not cover.
