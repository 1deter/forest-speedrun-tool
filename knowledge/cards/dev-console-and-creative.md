---
id: dev-console-and-creative
title: The game's developer console, Creative mode and cheats
aliases: dev console, developer console, debug console, console commands, cheats, godmode, god mode, creative, creative mode, peaceful, infinite energy, no survival, itemhack, goto, speedyrun, timescale command, capsulemode, f1 console
tags: tool, modes, cheats
confidence: code
checked: 2026-10-03
sources: game-notes "The game ships a debug console - 256 methods", "Cheats and Creative", "Speedrun tech and the endgame gate" (The game's own teleports)
related: endgame-gate, deaths-and-revives, forestoverlay
code: TheForest.DebugConsole, Cheats, TheForest.Player.GameMode_Creative, LocalPlayer.Goto
---

# The developer console, Creative and cheats

The retail game ships a full developer console (256 commands: god mode, teleports, items, even runtime C#), and Creative mode is a game that turns three cheat switches on by itself (god mode, infinite energy, no survival). Neither can load the endgame lab without the vault door.

## The developer console

The retail game ships a full developer console (`TheForest.DebugConsole`,
256 commands; F1 opens it when enabled). Commands are methods named
`_<command>`. Useful ones: `godmode`, `invisible`, `capsulemode` (the
closest thing to a hitbox view), `speedyrun`, `timescale`, `goto` /
`GotoPosition`, `additem` / `spawnitem` / `removeitem`, `setDrawDistance`,
`itemhack` (an item filter that makes `Owns` true - the only way around the
keycard checks), `eval` (runtime C#). The console UI is gated by
`CheatsAllowedSet`.

**The console's teleport** (`goto <target>`) does what the game's own
teleport does: cave state by terrain height, velocity zero, position - and
within 150 m of the endgame box it fires the box's crossing (sets the
endgame flag). It **never loads the lab without the vault door**
(`endgame-gate`).

## Creative mode

A Creative game turns on `GodMode`, `InfiniteEnergy` and `NoSurvival` by
itself (`GameMode_Creative`; it puts them back when you leave). So in
Creative: no death from damage, no stamina limit, no hunger / thirst.
Creative has its own speedrun categories (sxczurass's Creative Bombless
Any% guide is one).

## The cheat switches

`Cheats` keeps them as statics: `GodMode`, `InfiniteEnergy`, `NoSurvival`,
`UnlimitedHairspray`, `DebugConsole`, `Creative`, `PermaDeath`,
`NoEnemiesInternal`, ... ForestOverlay's run mode reads them for its
integrity report (and knows which ones Creative sets itself).

## Evidence

Read from the decompiled code; Creative's switches read live on a Creative
save (Peaceful).
