---
id: building-costs
title: Building costs - what every blueprint needs (logs, sticks, rocks, skins)
aliases: building cost, build cost, blueprint cost, how many logs, how many sticks, how many rocks, materials, ingredients, log cabin, small cabin, log cabin cost, shelter, leaf shelter, wall, log wall, defensive wall, custom wall, wall cost, logs per wall, treehouse, raft, houseboat, gazebo, bonfire, fire, drying rack, log sled, stick holder, log holder, couch, chair, wardrobe, bed, hang glider, effigy, sos, watchtower, tower, how much deer skin, deer fur, deerskin, rabbit fur, boar skin, everything once, craft every item
tags: building, crafting, numbers
confidence: code
checked: 2026-10-04
sources: the game's files - every Craft_Structure (blueprint) in resources.assets read offline 2026-10-04 (144 blueprints, `_requiredIngredients`); decompiled WallChunkArchitect.GetLogCost / SpawnStructure, WallDefensiveChunkArchitect, ZiplineArchitect, Craft_Structure; the item catalogue dump (ids to names)
related: crafting-and-building, crafting-recipes, wall-and-log-boost, zipline-boost, hundred-percent
code: Craft_Structure._requiredIngredients, WallChunkArchitect.GetLogCost, WallChunkArchitect.SpawnStructure, WallDefensiveChunkArchitect.GetLogCost, ZiplineArchitect, BuildingTypes
---

# Building costs

Every blueprint's cost is a fixed list stored on its ghost (the
`Craft_Structure` component's `_requiredIngredients`, item + amount). These
are the numbers read straight from the game's files for all 144 blueprints
(2026-10-04) [code]. The game applies no difficulty multiplier to them; in
Creative nothing is taken from the bag (`crafting-and-building`). The
**custom** buildings (walls, floors, roofs, foundations, stairs, fences,
ziplines, the crane, docks, rafts made by the custom tool) have no fixed
list - their log count is computed from the shape you draw (below).

Names are the ghosts' names in the game files, close to the book's names
(`LogCabin_Medium` is the small log cabin; `Ex_` = custom building). Which of
these appear in the survival book was not checked - leftovers named "OLD" are
left out [inferred: the book shows the rest].

## Fixed blueprints

Shelters and houses:
- **Log cabin**: 82 logs. **Small log cabin** (LogCabin_Medium): 13 logs.
- **Shelter**: 7 logs, 7 sticks, 6 rocks. **Leaf shelter** (temporary): 14 sticks, 26 leaves.
- **Tree house**: 35 logs. **Tree house chalet**: 29 logs, 18 sticks. **Tree platform**: 14 logs. Each has two ghost versions in the files (`_Anchor` and `_Rope`); the `_Rope` one adds 1 rope; what decides which one you get was not read.
- **Gazebo**: 21 logs, 60 sticks. **House boat**: 40 logs, 4 sticks, 4 ropes.
- **Raft**: 7 logs, 4 sticks, 4 ropes. **Large raft**: 57 logs, 32 sticks.
- **Platform**: 5 logs, 4 sticks. **Rock-side platform**: 6 logs. **Walkway**: 3 logs. **Staircase**: 5 logs.

Walls (the fixed ones):
- **Wall**, **wall with doorway**, **wall with window**: 5 logs each.
- **Defensive wall** (one fixed piece): 6 logs.
- **Defensive spikes**: 9 logs.
- None of the log walls takes sticks; the custom defensive wall's
  *reinforcement* is the one that takes sticks and rocks (counted from its
  shape) [code].

Storage and utility:
- Log holder 16 sticks (large 26). Stick holder 6 sticks (large 24). Rock holder 8 sticks (large 20).
- Log sled 21 sticks. Item stash 12 sticks, 6 logs. Arrow basket 25 sticks. Bone basket 21 sticks.
- Food holder small 7 logs, large 8 logs. Explosive holder 8 sticks, 1 log. Weapon rack 10 sticks. Armor mannequin 37 sticks. Skin rack 20 sticks.
- Drying rack 18 sticks (lite 5). Work bench 2 logs. Garden 2 logs. Water collector 4 sticks, 1 turtle shell. Tree sap collector 2 sticks, 1 pot, 1 rope.

Fire and light:
- Fire 2 sticks, 7 leaves. Fire rock pit 4 sticks, 7 rocks, 7 leaves. Fire stand 4 sticks, 3 rocks, 7 leaves. Bonfire 5 logs, 10 sticks, 20 leaves. Fireplace 2 logs, 68 rocks.
- Skull light 3 sticks, 1 skull, 5 tree sap. Ceiling skull light 1 rope, 1 skull, 1 tree sap.

Traps and defence:
- Rabbit trap 31 sticks. Fish trap 28 sticks, 2 ropes. Deadfall trap 3 logs, 3 sticks. Rope trap 5 logs, 2 sticks, 1 rope.
- Spiked wall trap 4 logs, 20 sticks, 2 rocks, 2 ropes. **Swinging rock trap** 6 logs, 10 rocks, 1 rope (the trap whose knockback runners stack, `knockback-sources`). Leaf pile trap 50 leaves.
- Trip wire explosive 1 stick, 2 cloth, 1 timed bomb. Trip wire molotov 1 stick, 3 cloth, 1 booze.
- Rock thrower 5 logs, 15 sticks, 1 rope.

Furniture and decorations:
- Bed 17 sticks, 1 rope, 4 rabbit skins. Chair 30 sticks, 2 deer skins. **Couch** 5 logs, 12 sticks, 7 deer skins. Bone chair 31 bones. Table 5 logs. Small table 3 logs.
- **Wardrobe** 14 logs, 16 sticks, 22 bones, 2 deer skins, 1 rabbit skin (the logs are stored as 13 + 1).
- Bone chandelier 26 bones, 5 cloth, 8 skulls. Bone frame 4 bones, 1 rabbit skin. Wood frame 4 sticks, 1 rabbit skin.
- Deer skin rug 1 deer skin. Rabbit skin rug 1 rabbit skin. Skull decoration 1 stick, 1 skull. Trophy 1 stick. Wall weapon holder 2 sticks. Ground weapon holder 1 log. Wall plant pot 25 sticks. Birdhouse 4 sticks, 1 log. Target 4 sticks, 1 log. Stick marker 2 sticks, 2 rocks, 1 cloth. Cage 13 sticks.
- **Hang glider** 32 sticks, 8 rabbit skins, 8 ropes, 6 bones, 2 arms, 1 head.

Effigies and the big builds:
- Small effigy 4 sticks, 3 rocks, 3 heads, 3 arms, 2 legs. Large effigy 10 sticks, 6 rocks, 9 heads, 9 arms, 4 legs. Rain effigy 4 sticks, 3 rocks, 7 arms. Head effigy 1 stick, 3 rocks, 1 head. Custom effigy 2 sticks, 3 rocks.
- Timmy / Mom effigies 38 sticks, 1 cloth (sitting: +1 log). Dog 20 sticks, 1 log, 1 cloth. TV 3 logs, 8 sticks. Car 47 logs, 121 sticks.
- Church 82 logs, 5 sticks. Coffin 11 logs, 1 stick. Cross 34 logs, 48 sticks, 18 rocks. **Tower** 300 logs, 22 sticks (the most expensive). SOS sign 34 rocks.
- Crane (custom) 11 logs, 6 ropes. Climbing rope 1 rope. Custom raft oar 4 sticks. Custom platform 3 logs.

## Custom buildings: computed from the shape

**Custom wall** (`WallChunkArchitect.GetLogCost`) - each straight piece
(chunk) between two points you place [code]:
- A **short** piece (no longer than 3.5 log widths) is built of **upright
  logs**, one per log that fits: cost = the number of logs drawn.
- A **longer** piece is built of **horizontal logs stretched to its
  length**: cost = its **height in logs, 5 by default**, however long it is
  (`SpawnStructure` scales each log to the piece's length). A long wall
  therefore costs 5 logs per piece; the placer cuts a long edge into pieces
  no longer than a log's length times a per-blueprint factor
  (`_maxSegmentHorizontalScale`, a value on the prefab not read yet).
- The custom **defensive wall**, the small wall and the stick / bone fences
  and rock path cost one log (or stick / bone / rock) **per log or piece
  it draws** (`GetLogCost` = the number of pieces), the gate its two halves' logs.

**Zipline** (`ZiplineArchitect`) - the ghost stores **10 logs**; the
**rope** count is added when you place it: one rope per piece of the
line's model (`_ziplineRoot`'s children) [code]. A longer line likely has
more pieces and so costs more rope [inferred - the piece length was not
read]. The tree-to-tree zipline stores 2 ropes, 2 sticks.

Floors, roofs, foundations, stairs, docks, bridges and the
custom raft likewise store 0 and get their logs from the drawn shape (each
architect's own count) - those formulas have not been written up here.

## How much of a material for "everything once"

Adding every fixed blueprint once and every inventory recipe once
(`crafting-recipes`), leaving out the computed custom buildings [code]:
- **Deer skin: 22** - buildings 12 (couch 7, chair 2, wardrobe 2, deer
  skin rug 1) + crafts 10 (warm suit 6, waterskin 2, spear bag 2).
- **Rabbit skin: 27** - hang glider 8, bed 4, wardrobe / bone frame / wood
  frame / rabbit rug 1 each; crafts: quiver 3, rabbit fur boots 3, pouch 2,
  stick bag 1, small rock bag 1, warm suit 1.
- **Boar skin: 7** (crafts only: warm suit 4, spear bag 2, rock bag 1).
  Raccoon skin 1 (warm suit). Lizard skin 1 (stealth armor).
- **Bones: 98** (bone chair 31, chandelier 26, wardrobe 22, hang glider 6,
  bone frame 4; crafts: bone armor 6, upgraded spear 3).
- **Skulls: 12**, **tree sap: 16** (repair tool 10, skull light 5,
  ceiling skull light 1), **heads 15**, **arms 21**, **legs 6**.
- **Rope: 57** (buildings 31, crafts 26) - and each crafted rope is 7 cloth.
The tree house / platform rope versions, the "partial" fire pits and the
sitting effigies are counted as their own blueprints here; drop them for
"each kind once". Upgrades (feathers, teeth, booze, berries on weapons)
are not counted.

## Open questions

- Which blueprints the survival book actually offers (all but the "OLD"
  leftovers is assumed).
- The custom wall's maximum piece length, the zipline's rope piece length,
  and the floor / roof / foundation formulas.
