---
id: pausing-and-game-time
title: Pausing, the inventory and game time
aliases: pause, pause menu, esc menu, inventory pause, timescale, time scale, game time, real time, frame cap, 60 fps inventory, hitch, stutter, lag spike, load hitch, maximumDeltaTime, adrenaline, adrenaline rush, crafting animation, timer while paused
tags: timing, physics, fps
confidence: code
checked: 2026-10-03
sources: game-notes "timeScale", "The ESC menu and the player lock", "Speedrun tech and the endgame gate" (The game's timers and pauses), "Deaths" (the last stand); gotcha 48
related: bomb-boost, fall-damage, deaths-and-revives, player-physics
code: HudGui.TogglePauseMenu, PlayerInventory.PauseTimeInInventory, PlayerInventory.Close, FirstPersonCharacter.LockView, PlayerStats.hitFromEnemy
---

# Pausing, the inventory and game time

## What stops time

| | Pause menu (Esc) | Inventory |
|---|---|---|
| Stops game time (`timeScale` 0) | **single player, every difficulty** (not multiplayer) | single player **Normal / Peaceful / Creative only** - not Hard, Hard Survival, multiplayer or VR |
| When | at once | 0.05 s after it opens (0.25 s in some cases) |
| Frame rate | unchanged | **capped at 60 fps** while open (not VR) |
| Player | `LockView`: kinematic if grounded, no gravity, locked | |
| Can open during a knockback | **yes** (the bomb boost) | no - refused while root motion is on (knockback) or jumping |
| On a zipline | no | no (the inventory component is disabled) |
| Under water | | no |

Code: `HudGui.TogglePauseMenu` (`if (!BoltNetwork.isRunning) Time.timeScale
= 0f;`) and `PlayerInventory.PauseTimeInInventory` (`if (!BoltNetwork.isRunning
&& !IsHardMode && !IsHardSurvivalMode && !VR) Time.timeScale = 0f;` then
`Application.targetFrameRate = 60`).

Opening the pause menu also closes build mode (a blueprint in your hands is
put away).

## What keeps running while paused

`timeScale` 0 stops physics and makes `Time.deltaTime` 0, but:
- **Coroutines and `Update` still run every frame** - which is the whole
  bomb boost: the knockback loop keeps adding a push each frame, and its
  timer (counting `deltaTime`) never advances (`bomb-boost`).
- **Real-time timers keep counting**: the **adrenaline rush cooldown**
  (120 s, measured with `realtimeSinceStartup`) and the **crafting /
  upgrade animation** keep going in the menu.

## Game-time timers (stop while paused)

- Fall air time (`jumpingTimer`) and the 0.35 s fall-damage arm
  (`Invoke`) - pausing mid-fall adds nothing (`fall-damage`).
- The knockback's 0.5 s and the explosion cooldown (2.2 s).

## A long frame counts in full

Unity caps one frame's `deltaTime` at `maximumDeltaTime`, which is **9 s**
in this game. So a stutter or load hitch counts its whole length as game
time: a 2 s hitch mid-fall adds 2 s of air time and can turn a survivable
landing into the 3.8 s "fell too long" death [code]. Time the event, not
the freeze, when judging anything across a hitch.

## The adrenaline rush (last stand)

A hit from an enemy that would kill you while your health is above the grey
zone (10) is clamped to leave you just over 1 health, and the adrenaline
rush starts: it gives back half the missing stamina when health drops into
the grey zone. Only the next hit can kill. Cooldown 120 s of **real** time
[code + live].

## Evidence

Read from the decompiled code (both pause paths, the inventory's rules,
`maximumDeltaTime`); the pause menu's effect on the knockback measured live.
