---
id: megan-boss
title: Megan's boss fight (how her AI works)
aliases: megan, megan fight, spear strat, spin attack, spin chance, megan boss, boss fight, final boss, girl boss, girl mutant, spider megan, megan ai, megan attacks, megan babies, boss babies, baby spawn, megan health, kill megan, bombing megan, megan stagger, girlMutant
tags: endgame, boss, ai, combat
confidence: code
checked: 2026-10-03
sources: docs/fsm/megan-action_combatFSM.txt (exported live 2026-10-03 from the ruben-megan savestate, Normal); decompiled girlMutantAiManager, creepyAnimatorControl, creepyAnimEvents, EnemyHealth, enemyWeaponMelee, spawnMutants, AiSettings; PlayMaker.dll Fsm.ProcessEvent (IL); bridge 2026-10-03 (40 s of her FSM states, one explosion and one hit); game-notes "Savestates during an endgame cutscene" (Megan after a load / Quick load)
related: endgame-splits, deaths-and-revives, lab-skip, knockback-sources
code: girlMutantAiManager.setAiParams, girlMutantAiManager.setInitialWeightParams, girlMutantAiManager.setupHealthParams, creepyAnimatorControl.activateGirlMutant, creepyAnimEvents.birthLeft, EnemyHealth.Explosion, EnemyHealth.HitReal, EnemyHealth.HitFire, enemyWeaponMelee
---

# Megan's boss fight (how her AI works)

Megan's fight is run by a PlayMaker state machine (`action_combatFSM` on
`girl_base`) plus a small C# script that retunes its numbers once a second.
The one idea that explains most of the fight: **the distance between you and
her decides everything.** Within 35 m she goes straight into an attack every
cycle, and the attack is picked by distance alone. Only when you stay
**beyond 35 m** does she reach the weighted "what next" roll, and that roll
is where the baby births come from. Nothing in the FSM picks an attack at
random from a moveset. The random parts are the dodge roll, the counter on
a hit and the action roll beyond 35 m.

## The fight loop

After the transformation cutscene, `creepyAnimatorControl.activateGirlMutant`
sends `begin`, turns her AI on, sets her animation speed to **1.2x** and
marks you as fighting the boss (`IsFightingBoss`, which is why a death here
wakes you up instead of warping you, see deaths-and-revives) [code].

One cycle, in the FSM's own state names [code; the order seen live over
40 s on the bridge]:

1. `randomIdle` -> `setToPlayer` (0.2 s, targets the closest player) ->
   `worldCheck` -> `moveToPlayer`.
2. `moveToPlayer`: she walks at you. **The check runs every frame: the
   moment you are within 35 m she switches to attacking.** If you are still
   farther away after 1-2 s (random), she goes to `chooseAction` (below).
3. Attack path: `checkTurns` (turns first if you are more than 135° behind
   her) -> `chanceToDodge` -> `chooseAttack`.
4. After the attack: a cooldown (0.8-1.2 s), then back to step 1.

`chooseAttack` picks **by distance to her target**, nothing else:

| Distance | State | What it is |
|---|---|---|
| under 8 m | `doCloseStomp` | stomp, 2 s + 1 s cooldown |
| 8-13 m | `doCloseAttack` | close swing (one of two), 1.2 s; ~5% chance of the spin attack instead |
| 13-27 m | `doMidAttack` | mid-range attack (one of five), up to 1.3 s; ~5% spin |
| 27-38 m | `doLongAttack` | long attack (one of three), 1.5 s |
| 38-50 m | `doJumpAttack` | the leap (charge) attack, 2 s |
| over 50 m | `runToPlayer 2` | runs at you for up to 7-10 s, leaps once you are within 35 m (50/50 a second run + an arm smash) |

The spin attack (`longSpinAttack`, 4 s) is rolled at the start of a close
attack (8-13 m), a mid attack (13-27 m) and a counter (below): weight 0.03
against 0.6. PlayMaker's `SendRandomEvent` divides by the total
(`ActionHelpers.GetRandomWeightedIndex`, IL), so one roll is **0.03 / 0.63
= 4.76%**, not 3%. The stomp, long, leap and run attacks never roll it. Over
n rolls the chance of at least one spin is 1 - 0.9524^n: 21.6% for 5, 38.6%
for 10, 47.0% for 13, 62.3% for 20 [code; arithmetic]. With two or more
players within 25 m of her, the weight becomes 1 (1 / 1.6 = 62.5% a roll)
[code].

A runner measured "3% on the dot" per attack (QA, 2026-10-03). One
explanation that fits [inferred, not tested]: only about 63% of her attacks
roll at all, and 0.63 x 4.76% = 3.0%. Counting only close / mid / counter
attacks should give 4.76%.

## The dodge

`chanceToDodge` runs before every attack chosen from step 3:

- **If you hit her within the last 1.3 s** (`gettingHit`), that is checked
  first and the roll is 1 against 0.4: **71% she attacks, 29% she walks
  back** - from the very start of the fight, the 15 s lock does not apply
  to this roll [code].
- Otherwise the dodge weight is **0 for the first 15 s** after she activates
  (`setInitialWeightParams`), then 0.25 against 1: **20%** per cycle [code].
- A "dodge" is never a leap away: within 15 m it is a walk back (2 s), half
  of those the dodge animation (2.5 s); farther than 15 m she attacks
  anyway. A walk back that does not turn into the dodge animation ends in an
  attack [code].

## When you hit her: the counter

Every melee or projectile hit (`EnemyHealth.HitReal`) sends `gotHit`. After
0.2 s she counter-attacks: within 8 m a counter swing (one of three, 1.2 s),
farther away straight into `chooseAttack` [code; live: a hit at ~20 m went
`gotHit` -> `doMidAttack` within 0.4 s]. The ~5% spin roll applies here too.
**Hitting her does not interrupt her. It starts her next attack.**

## Explosions and the stagger

- An explosion takes a **flat 30 health** whatever the distance (normal
  cannibals take 65) [code + live: 370 -> 340].
- Each explosion has a **25% chance to stagger her for 10 s** (she stops in
  `hitstagger`), unless she is already staggered. Otherwise she flinches for
  about 1 s [code].
- Explosions do nothing before she has transformed [code].

## Health and damage

- **370 health** (read live, Normal). No difficulty scaling of her health
  was found in the code [code].
- **Multiplayer** (`setupHealthParams`, once, when she activates, only with
  2+ players in the game): `Health + Health / 3 x n`, where n counts **every**
  player within 350 m of her, you included, and `Health / 3` is integer
  division (123). So 2 players = 616, 3 = 739, 4 = 862 -> capped at 800
  [code].
- **A thrown spear does 40** (plain and upgraded; `ArrowDamage.damage` on
  the spear's `Tip`, read live). Spears get no headshot bonus (that path
  skips `spearType`) and no difficulty factor [code + live]. Throws to kill:
  10 solo (370), 16 with 2 players (616), 19 with 3 (739), 20 with 4+ (800)
  [arithmetic].
- **She enters the "hurt" weights below half health (185)**, but see the
  next section for why that rarely matters.
- Her melee hits use the creepy damage: **28 per hit on Normal, 42 on
  Hard** (`28 x creepyDamageRatio`, 1.5 on Hard). If **she** is poisoned,
  her hits are divided by 1.6 (17 / 26) [code; damage to the player not
  measured live].
- Fire on her: 2 damage per tick on Normal; on Hard fire against creepy
  enemies is cut to 0.35x, so 1 per tick [code].
- 370 / 30 = 13 explosions kill her from full health, with nothing else
  [inferred from the two numbers above].

## The babies and the "beyond 35 m" roll

`chooseAction` is a weighted random pick among actions. Its weights are set
once a second by `girlMutantAiManager.setAiParams` [code]:

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
  walking.** The two "within 35 m" columns can only apply in the second
  after you close in (the weights update once a second), so the health
  switch barely changes the fight [inferred from the FSM + the 1 s update].

**Giving birth**: she walks back for 3 s, then the birth animation (4.5 s).
Its animation events (`birthLeft` / `birthRight`) each drop a
`bossBabySpawner` that spawns **one** baby [code + live]. **She stops giving
birth for good once more than 2 spawners exist** (`fsmBlockBabySpawn`): the
spawners are never destroyed, so dead babies do not free a slot [code; live
the list stayed at 6 spawners with 5 babies alive]. A blocked roll is
re-rolled at once. Boss babies die by themselves 300 s after they spawn, and
in the boss room they died a moment after Megan [code; live, game-notes].

**What this means for a fight** [inferred from the above, not timed]:

- **Stay within 35 m and she never gives birth.** Every cycle is an attack
  (or a dodge), chosen by distance. The spear strat (throwing from within
  35 m) fits this: no births, and no leaps unless you stand 38-50 m away
  [inferred].
- **Backing off beyond 35 m is what brings the babies**, nearly one
  roll in two, until three or more spawners exist.
- Bombs: 13 explosions, each a 1-in-4 chance of a free 10 s window.

## Leashes and the overworld Megan

- If you get more than 310 m from her start position (in the boss room,
  where she is not `activeInWorld`), she walks home [code].
- Beyond 105 m she stops chasing and wanders (`moveAroundWorld`, 90% random
  waypoints) [code].
- The **overworld** Megan (spawned by `spawnMutants.spawnGirl`, not the
  endgame) is a different setup: `activateGirlMutantInWorld` gives her **145
  health**, 1.25x size and 1.1x animation speed. Only there does her death
  kill her babies through `killAllBossBabies` [code].

## Evidence

- FSM export: `docs/fsm/megan-action_combatFSM.txt` (94 states), plus
  `megan-global_motorFSM.txt` and `megan-global_alertManagerFSM.txt`.
  Exported from the transformed Megan in the `ruben-megan` savestate (Normal).
- PlayMaker checks an FSM's **global** transitions before the current
  state's own (`Fsm.ProcessEvent`, IL), so `toWander`, `goToAttack` and
  `toMainAttack` always lead to `randomIdle`, `checkTurns` and
  `chooseAttack`. The export's blank local transitions for those events
  never apply.
- Live, 40 s with god mode, standing ~7-20 m from her: `moveToPlayer` ->
  `doLongAttack` / `doCloseAttack` / `doCloseStomp` / `doDodge2` /
  `runToPlayer 2` -> `toArmSmashAttack`, always through `coolDown` and
  `setToPlayer`. `chooseAction` and births were never seen.

## Open questions

- Megan's attack damage to the player, measured live, and which of her
  attacks hit at what range.
- How long a full fight takes with each strategy (within 35 m vs bombs).
- How many births happen before the block (the code allows a birth while 2
  or fewer spawners exist; how many spawners one birth animation drops is
  read from live counts only).
- Which path kills the babies when the boss-room Megan dies (the code's
  `killAllBossBabies` runs only for the overworld one).
- Melee weapon damage to her by weapon (the thrown spear's 40 is read).
