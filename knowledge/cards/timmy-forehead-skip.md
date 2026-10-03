---
id: timmy-forehead-skip
title: Timmy forehead skip (skipping part of the Timmy cutscene)
aliases: timmy forehead skip, forehead skip, timmy scar skip, timmy cutscene skip, bed skip, timmy bed skip, artifact cutscene skip, low fps timmy, 18 fps skip
tags: endgame, tech, cutscene, fps
confidence: runner
checked: 2026-10-03
sources: the speedrun community's Discord (DiamondMango, Joyful, 2026-06-21), relayed by the author 2026-10-03 - the author believes a video of it exists
related: endgame-splits, lab-skip
code: PlayerPickupTimmyAction.pickupTimmyRoutine
---

# Timmy forehead skip

A runner-found skip at the end of the game: when you find Timmy dead in
the artifact, interacting with him while aiming at **his forehead scar
(just above the eyebrow)** at a **low frame rate (~15-18 fps)** skips the
**bed part** of the Timmy cutscene, and the game carries on working
[runner]. Nothing about it has been researched in the code or tested yet -
how it works is not known.

## How runners do it

As described in the runners' Discord (2026-06-21) [runner]:
- Cap the frame rate low - "like 15 fps", "best is 18" - e.g. with RivaTuner.
- Aim at the middle of Timmy's scar, just above the eyebrow (a monitor
  crosshair helps), and interact.
- It skips the bed part of the cutscene; the character still plays the
  cutscene.
- Afterwards the player is **invisible** and the spear (held items) cannot
  be seen.
- The runners themselves found it by word of mouth and do not know why it
  works.

## Evidence

- Runners' reports only (above). Not reproduced in game; the code has not
  been read for it yet.
- The Timmy pickup is the scripted routine
  `PlayerPickupTimmyAction.pickupTimmyRoutine` (it also starts the
  "Finding Timmy" split - see `endgame-splits`); where in it a low frame
  rate or the aim point could matter is not known.

## Open questions

- Why the aim point and a low frame rate matter, and which part of the
  routine is skipped - a code read of the pickup routine plus the move done
  in game at a capped fps.
- Whether the invisible player / hidden items after it last until a reload,
  and whether anything later in the run depends on the skipped part.
- How much time it saves.
