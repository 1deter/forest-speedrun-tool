---
id: forestoverlay
title: ForestOverlay - the speedrun tool (what it does)
aliases: forestoverlay, forest overlay, the tool, the mod, the plugin, overlay, practice tool, savestates, save states, quick load, full load, practice spots, segments, ghosts, run lines, splits table, run mode, run code, receipts, forest.deter.cloud, website, bepinex, install, update, f2, f7
tags: tool
confidence: live
checked: 2026-10-03
sources: docs/areas/plugin.md "UI"; docs/areas/plugin-concepts.md; docs/decisions.md "Run mode"; docs/run-mode.md; docs/savestates.md; CHANGELOG.md
related: endgame-splits, deaths-and-revives, saves-and-loading
code:
---

# ForestOverlay

A BepInEx plugin for The Forest: speedrun information, practice tools, and a
timer with segments, ghosts and comparisons. Free and open source
(github.com/1deter/forest-speedrun-tool), made by d.eter (`top-runners`); runs and spots are shared on
forest.deter.cloud.

## Install and update

Install BepInEx 5 into the game, drop `ForestOverlay.dll` into
`BepInEx/plugins`. After that the tool updates itself: the Updates tab
downloads a new version, and it installs on the next launch (the old one is
kept as `.bak`). All of a runner's data lives in `BepInEx/config/` -
moving or replacing the BepInEx folder takes it along.

## Keys (all rebindable in Settings)

| Key | Action |
|---|---|
| F2 | the ForestOverlay window (everything is a tab) |
| F5 | show / hide all overlay UI |
| F6 | save a practice spot here |
| F7 | restart the current spot (restores its start state, if it has one) |
| F9 | practice mode on / off |
| F12 | manual split / finish |
| `[` | abort run |
| Keypad * | freecam |

F1 is left free (the game's own console).

## Main features

- **Practice spots and segments**: every entry is somewhere to teleport
  (Go); tick "Timed segment" for start / end triggers and ordered
  checkpoints. F7 restarts the *current* spot.
- **Savestates** (start states on spots): **Quick load** restores in place
  (fast, the default); **Full load** reloads the scene. Practice only.
- **Timed runs** with live deltas, ghosts and run lines, a LiveSplit-style
  splits table (every column toggleable), comparisons against your PB, best
  segments, other runners or a LiveSplit `.lss` file. Endgame splits are
  frame-identical to the LiveSplit autosplitter (`endgame-splits`).
- **Reload on death** (what a death does when no spot applies; allowed in
  normal runs - the game's own load of the same save).
- **Views** tab: freecam, colliders, triggers, wireframe. Developer-only
  and experimental tools sit in the last tab, **Developer**.
- **Sharing**: a spot with its start state and attempts as one `.foseg`
  file; community packs fetched from the repo.
- **Run mode**: a run spot's Restart starts a run with practice features
  locked, an on-screen anti-splice code, a receipt for every attempt and an
  integrity report uploaded to forest.deter.cloud; detected moves (bomb
  boost, cave force load, fall damage cancel, clips, lifts) are listed on
  the attempt page for verifiers - never an automatic reject. Categories are
  set by the moderators.

Anything that changes the game is labelled practice / "ON NOW" on the HUD.
Whether a feature is allowed in a real run is the speedrun.com moderators'
call.

## Evidence

The project's own documentation (CLAUDE.md, docs/, CHANGELOG.md).
