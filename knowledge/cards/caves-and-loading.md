---
id: caves-and-loading
title: Caves - cave state, streaming and the cave mouths
aliases: caves, cave state, in a cave, cave streaming, cave loading, cave props, loading caves, cave 4 trigger, caves not loaded, invisible cave, black wall, cave mouth black, cave entrance, cave exit, terrain collision, cave lighting, cave numbers, sinkhole
tags: caves, loading, route
confidence: live
checked: 2026-10-03
sources: game-notes "Caves" (incl. the black walls), "The autosplitter's other splits" (CaveOptimizer), "Speedrun tech and the endgame gate" (the runners' words mapped); sxczurass's Creative Bombless Any% Guide (2025)
related: cave-force-load, deaths-and-revives, endgame-splits, saves-and-loading
code: PlayerStats.InACave, PlayerStats.NotInACave, CaveTriggers.Update, CaveDoor, activateCave, CaveOptimizer.Update, caveEntranceManager, LocalPlayer.Goto, LocalPlayer.GotoCave
---

# Caves

## Cave state

The caves sit **under the terrain**. Entering one, the game sends the
message `InACave` to the player (`CaveTriggers`, `CaveDoor`, the crawl /
climb entrances); leaving, `NotInACave`. `PlayerStats.InACave` turns on:
- cave lighting (`Clock.IsCave`) and cave audio;
- the in-cave flag (`LocalPlayer.IsInCaves`);
- **`IgnoreCollisionWithTerrain(true)`** - the terrain stops being solid
  for the player, because the caves are below it.

So cave state on the surface means you fall through the terrain - the
basis of the cave force load (`cave-force-load`). `NotInACave` turns all of
it back (ocean on, terrain solid).

A save made in a cave restores the same way: loading sends `InACave` when
the saved flag is set.

## Which cave you are in, and cave streaming

`ActiveAreaInfo._currentCave` names the cave (Cave 1-10, the hell cave,
snow cave, the underwater caves). It changes only through cave entrances /
triggers, the snow cave helper and the first death - **not** through a
teleport. `CaveOptimizer.Update` streams cave props by it:
- with a cave set: only that cave's props (2 scenes);
- **in cave state with no cave set: every cave's props** (16 scenes) [live].

The runners' note "a trigger loads the rest of the caves" (Cave 4) - you
must pass it or the cave stays unloaded - fits this: the triggers set the
current cave and what streams in [runner + code].

The endgame lab counts as "not in a cave" for `_currentCave`, with
`IsInCaves` true.

## The black walls in cave mouths

Each of the 22 entrances has black backings that hide a cave's inside from
the surface. Walking through a mouth switches **every** entrance's black
on or off at once. `InACave` and teleports never touch them - so after a
teleport into a cave from the surface the mouths can stay black, and after
one out the caves can look see-through [live].

## Teleports and caves

The game's own teleport (`LocalPlayer.Goto`, used by the dev console)
decides cave state by height: a target more than 6 m under the terrain (3 m
if already in a cave) is "in a cave" -> `InACave`, otherwise `NotInACave` -
then zeroes velocity and sets the position.

## The first death

Your first death warps you into a cave (always `Cave2DeadPlace`) in cave
state, with a current cave set (`SetCurrentCave(1)`) - see
`deaths-and-revives`.

## Evidence

Messages and receivers from the decompiled code; streaming counts, the
black walls and the teleport behaviour checked live over the bridge.
