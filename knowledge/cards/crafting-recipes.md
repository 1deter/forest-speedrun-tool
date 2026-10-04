---
id: crafting-recipes
title: Crafting recipes - every inventory recipe and upgrade
aliases: recipes, recipe, crafting recipe, craft, crafting mat, how to craft, what do i need to craft, weapon upgrade, upgrades, upgrade weapon, poison arrows, fire arrows, bone arrows, warmsuit, warm suit, waterskin, spear bag, quiver, stick bag, rock bag, pouch, snowshoes, bone armor, stealth armor, lizard armor, rabbit fur boots, repair tool, bow, crafted bow, slingshot, molotov, head bomb, timed bomb, flintlock, medicine, energy mix, rope, crafted axe, crafted club, flashlight bow, flashlight attachment, map pieces, toy, assembled toy
tags: crafting, numbers
confidence: code
checked: 2026-10-04
sources: the game's files - ReceipeDatabase (globalgamemanagers.assets, 145 recipes, `_lastId` 263) read offline 2026-10-04; the item catalogue dump (ids to names); decompiled Receipe, ReceipeIngredient
related: crafting-and-building, building-costs, bomb-boost, megan-boss, hundred-percent
code: ReceipeDatabase, Receipe, ReceipeIngredient, WeaponStatUpgrade
---

# Crafting recipes

Every recipe the inventory's crafting mat knows, read from the game's
recipe database (`ReceipeDatabase`, 145 entries) on 2026-10-04 [code].
Three kinds: **Craft** (ingredients become a new item), **Upgrade** (an
item plus ingredients becomes a better / changed version of itself) and
**Extension** (the flashlight taped onto a weapon). Crafting is instant
once the ingredients are on the mat. A few recipes are marked hidden
(the flare gun's reload, the bows' arrow loading) - they run without
showing on the mat. Speedrun use: `crafting-and-building`.

## Crafts

Weapons and tools:
- **Crafted axe**: 1 stick, 1 rock, 1 rope.
- **Crafted club**: 1 skull, 1 stick.
- **Spear**: 2 sticks. **Upgraded spear**: 1 spear, 3 bones, 2 cloth.
- **Crafted bow**: 1 stick, 1 cloth, 1 rope.
- **Arrows** (5): 1 stick, 5 feathers. Bone arrows (5): 1 stick, 5 feathers, 5 bones.
- **Slingshot**: 1 stick, 1 cloth, 1 sticky tape.
- **Repair tool**: 2 sticks, 1 rock, 2 cloth, 10 tree sap.
- **Flintlock pistol**: the 8 flintlock parts (1 each).

Explosives:
- **Timed bomb**: 1 circuit board, 1 coins, 1 booze, 1 watch, 1 sticky tape.
- **Head bomb**: 1 head, 1 timed bomb.
- **Molotov**: 1 booze, 1 cloth.

Health and stamina:
- **Medicine**: 1 marigold, 1 aloe. **Medicine +**: 1 marigold, 1 aloe, 1 coneflower.
- **Energy mix**: 1 coneflower, 1 chicory. **Energy mix +**: 1 coneflower, 1 chicory, 1 aloe.

Clothing, armor and bags:
- **Warm suit**: 2 rope, 2 cloth, 1 rabbit skin, **6 deer skins**, 4 boar skins, 1 raccoon skin.
- **Waterskin**: 2 deer skins, 1 rope.
- **Spear bag**: 3 rope, 2 cloth, 2 deer skins, 2 boar skins.
- **Rock bag**: 3 rope, 1 cloth, 1 boar skin. **Small rock bag**: 1 rope, 1 rabbit skin.
- **Stick bag**: 2 rope, 3 cloth, 1 rabbit skin.
- **Quiver**: 1 rope, 3 rabbit skins. **Pouch**: 2 rabbit skins.
- **Rabbit fur boots**: 3 rabbit skins, 2 rope. **Snowshoes**: 5 sticks, 2 rope.
- **Bone armor**: 6 bones, 3 cloth. **Stealth (lizard skin) armor**: 1 lizard skin, 15 leaves.
- **Rope**: 7 cloth.

Flashlight extensions (the weapon stays): flashlight + 1 sticky tape onto
the crafted bow, the modern bow, the chainsaw or the flintlock.

## Upgrades

- **Feathers, teeth, booze** - each its own recipe, with 1 tree sap: 1
  feather, 1 tooth or 1 booze onto the plane axe, rusty axe, modern axe,
  crafted axe, club, crafted club, upgraded stick, tennis racket or
  machete (a plain stick takes them too and becomes the upgraded stick; the
  upgraded rock takes booze + sap). These recipes carry no stat entry of
  their own - what each adds is handled elsewhere and was not written up.
- **Poison** (`PoisonnedWeapon`): 4 twinberries, 4 snowberries, 1 jack
  mushroom or 1 amanita mushroom onto the four axes, the machete, the
  katana or the upgraded spear. Arrows the same (`PoisonnedAmmo`).
- **Cloth wrap** (`BurningWeapon`, "unique" - once per item; read as a
  weapon you can light [inferred from the name]): 1 cloth on the four axes, the club, the crafted club, a
  stick (it becomes the upgraded stick) or a rock (the upgraded rock).
- **Arrows**: 1 booze + 1 cloth (`BurningAmmo`, unique); bone arrows are
  made with 5 bones (`BoneAmmo`). The upgraded spear also takes 1 booze
  + 1 cloth.
- **Timed bomb**: + 1 tree sap (`StickyProjectile` - the sticky bomb).
- **Walkman**: + a cassette (one recipe per cassette, 1-5).
  **Camcorder**: + a tape (1-6).
- **Flashlight** / **walkie-talkie**: + 1 battery.
- **Map**: two map pieces make the map, then each further piece is added.
- **Toy**: the torso + any of the head / arm / leg, then each part added;
  the full toy is 1 torso, 1 head, 2 arms, 2 legs.
- Hidden: the flare gun + 1 flare, a bow + arrows (loading).

## Notes

- All amounts are per craft; no recipe gives more than one item except
  arrows (5).
- Totals across every recipe and blueprint (deer skin 22, ...):
  `building-costs`.
