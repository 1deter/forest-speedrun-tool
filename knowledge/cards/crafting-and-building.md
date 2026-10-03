---
id: crafting-and-building
title: Crafting, building and stamina items (what runners make, and how fast)
aliases: crafting, craft, building, build, blueprint, blueprints, ghost, place, survival book, book, custom wall, custom building, hole cutter, zipline placing, free standing zipline, spear, crafted spear, stamina mix, energy mix, energy mix plus, sodas, soda, stamina, energy, sprint, stamina bar, energy bar, shift reset, release shift, timed bomb, bomb recipe, fast building, hold to build, creative building, recipes, coneflower, chicory, flowers
tags: building, crafting, stamina, route
confidence: code
checked: 2026-10-03
sources: decompiled Craft_Structure.Update / AddIngredient, FloorHoleArchitect, PlayerStats, FirstPersonCharacter; live reads 2026-10-03 (ReceipeDatabase, ItemDatabase stat effects, sprint drain and regen measured with InfiniteEnergy off); src/Game/FastBuild.cs; the runners' guides (routes card)
related: routes, wall-and-log-boost, zipline-boost, bomb-boost, knockback-sources, dev-console-and-creative, categories-and-rules
code: Craft_Structure.Update, Craft_Structure.AddIngredient, FloorHoleArchitect.OnPlaced, PlayerStats.Update, FirstPersonCharacter.HandleRunningStaminaAndSpeed, ReceipeDatabase, ItemDatabase
---

# Crafting, building and stamina items

Runners build to move: a **custom wall** for a wall boost, a **zipline** for
speed, the **hole cutter** to boost off a wall they built earlier, a **bomb
trap** for a bomb boost. They craft to keep running: **stamina mixes** and
a **spear** in Glitchless. Building a structure is two steps - place its
blueprint (the ghost), then add every ingredient to it - and in Creative
the second step is free and fast. Stamina is a sprint bar capped by the
slower **energy** bar; every soda and mix raises both.

## Placing and filling a blueprint

1. Open the survival book, pick a blueprint, place the ghost.
2. Add the ingredients one at a time (`Craft_Structure.Update`): the last
   one added builds the real structure.
   - **Normal / Hard / Peaceful**: one ingredient per **press** of Build,
     and only ingredients you carry.
   - **Creative**: **hold** Build - one ingredient every **0.065 s**
     (`_nextAddItem = Time.time + 0.065f`), and none are taken from the bag
     (`AddIngredient`: `!Cheats.Creative` skips the removal). A structure
     needing N items takes about N x 0.065 s of holding [code].
   - ForestOverlay's *Fast building* (Inventory tab, a practice-only mod)
     gives the Creative hold in any mode; the items still come out of the
     bag.
3. The structure becomes solid **when it is built** - and if that happens
   overlapping you, physics pushes you out: the **wall boost**
   (`wall-and-log-boost`).

## What runners build

- **Custom wall** (custom building section). Placed and **extended** to a
  length, then built while standing in it so the push lands you on top
  (sxczurass's Cave 6 route; `routes`). How many logs a given length costs
  has not been read [inferred: the wall architects count logs per length].
- **Free-standing zipline** (the custom building section's last page). Its
  use prompt sits at one end - placed too far, you cannot grab it from
  where you arrive. Speed, exit and the boost: `zipline-boost`.
- **Hole cutter.** It cuts real holes only in **floors, roofs and rafts**
  (`IHoleStructure`): on placing, those are rebuilt with the hole. Any
  **other** building it touches - a wall included - is **destroyed** when
  it is placed (`FloorHoleArchitect.OnPlaced`: a 13,371,337-damage
  `LocalizedHit`, unless the blueprint sets `_preventHoleCutting`) [code].
  sxczurass places a custom wall on the way into Cave 6 and, coming back,
  uses the hole cutter on it "to boost yourself to the top" [runner]. How
  that turns into a boost has not been tested - the broken wall's pieces
  appearing in the player and pushing them out (a log boost,
  `wall-and-log-boost`) would fit [inferred].
- **Bomb trap / timed bomb** for a bomb boost (`bomb-boost`). Timed bomb
  recipe: 1 circuit board, 1 coins, 1 booze, 1 watch, 1 sticky tape [live];
  a head bomb adds a head.

## Crafted items (recipes, read live)

Crafting in the inventory is instant once the ingredients are on the mat.

| Item | Recipe | Notes |
|---|---|---|
| **Energy mix** ("stamina mix") | 1 coneflower + 1 chicory | +30 stamina, +100 energy; carry 5 |
| Energy mix + | 1 coneflower + 1 chicory + 1 aloe | +60 stamina, +100 energy; carry 5 |
| **Spear** | 2 sticks | thrown at Megan in Glitchless (`megan-boss`) |
| Upgraded spear | 1 spear + 3 bones + 2 cloth | |
| Crafted axe | 1 stick + 1 rock + 1 rope | |
| Medicine | 1 marigold + 1 aloe | |
| Rope | 7 cloth | |
| Timed bomb | circuit board, coins, booze, watch, sticky tape | |

Bought, not crafted: the **soda** - 100 coins at the lab's vending
machines; **+50 stamina, +80 energy**, carry 10 [live].

yirequ's Glitchless route crafts **5 energy mixes** (the carry limit) from
the flowers picked between Cave 5 and Cave 6, the fifth on a hotkey, and a
spear from two sticks - all in one stop after the second wall climb.

## Stamina and energy

Two bars: **stamina** (the sprint bar) and **energy** (the slower one under
it). Stamina can never be above energy (`PlayerStats`: it regenerates only
`if (Stamina < Energy)`, else `Stamina = Energy`) [code].

- **Sprinting** costs **3.5 stamina per second** (`staminaCostPerSec` 3.5
  on the player; x the run-stamina skill ratio, 1 here). Measured: 99.4 ->
  81.9 in 5 s of sprint, 70 m covered [live].
- **Regeneration**: **6 per second** while not sprinting, starting **0.4 s**
  after you stop (`timeToRecoverFromRun`). Measured: ~5-6 a second after a
  sprint, until it met the energy bar [live].
- Below **10.2** stamina the game stops your sprint for 0.9 s and then
  will not let you sprint (`doForceStopRun`, `CantRun`) [code].
- Energy drains slowly on its own in survival modes (~0.4 a second in that
  test on Normal) [live]; a soda or mix raises energy too, so it lifts the
  cap as well as the bar.

**Why the runners' "shift reset" works** (yirequ: release sprint before
every cave entrance, rope, door). The game counts you as **running**
whenever the Run button is held and your animator's speed is above 0.4
(`running = run && overallSpeed > 0.4`); while `running`, stamina does not
regenerate and the 0.4 s recovery timer keeps restarting [code]. Holding
sprint into an animation can therefore keep `running` on through it, so
the animation's seconds do not refill the bar; let go and they do. Whether
the animator's speed stays above 0.4 through each cutscene has not been
checked live [inferred].

## Open questions

- Log / item counts for the custom wall per length and for the zipline.
- The hole-cutter boost done for real (which structure, what pushes).
- The shift reset measured through a real cave entrance with sprint held
  and released.
