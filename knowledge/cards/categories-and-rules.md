---
id: categories-and-rules
title: Speedrun categories and their rules (speedrun.com)
aliases: categories, category, rules, leaderboard, leaderboards, speedrun.com, src, any%, any percent, any% no explosive glitch, no explosives glitch, neg, bombless, any% bombs, glitchless, any% glitchless, coop, co-op, co-op any%, coop glitchless, inbounds, inbounds%, oob, out of bounds, vr%, vr, bathrobe%, shark%, 100%, hundo, world record, wr, records, difficulty, peaceful, normal, hardmode, hard mode, creative category, timing rules, when does the timer start, when does the run end, banned, allowed, is it legal
tags: categories, rules, timing
confidence: runner
checked: 2026-10-03
sources: speedrun.com API v1, game w6j5341j (The Forest): categories with rules and subcategories, leaderboards top 3, read 2026-10-03; site/ForestSite/Categories.cs (how the tool seeds them); docs/run-mode.md "Categories"
related: bomb-boost, smash-clip, lab-skip, elevator-skip, endgame-gate, wall-and-log-boost, dev-console-and-creative, pausing-and-game-time, endgame-splits, forestoverlay
code: Categories.Seeds, Categories.BannedOf, RunCategory
---

# Speedrun categories and their rules

The Forest's leaderboards are on speedrun.com. Every full-game category is
timed in **real time** (RTA, milliseconds shown) and **needs a video**;
runs are verified by the moderators. Most categories share one start and
end - *time starts when player movement occurs*, *time ends when the "E"
interact disappears from the screen, starting the cutscene* (the end
button) - and the same three bans: **no developer mode** (the game's own
console), **no third party software** ("cheat engine etc."), **no co-op**
outside the co-op boards. What separates the categories is which tech is
banned and which difficulty you play.

Everything below is speedrun.com's text as of 2026-10-03, quoted closely.
The rules are short, so many questions ("is X allowed in Y?") are **not
answered by the written rules** - where this card maps a rule onto a piece
of tech, that mapping is marked `[inferred]`, and the moderators have the
final word.

## The categories

| Category | Subcategories (difficulty) | Rules beyond the shared ones |
|---|---|---|
| **Any%** | Creative only | none - bombs, clips and out-of-bounds are all allowed |
| **Any% (No Explosive Glitch)** | Peaceful, Normal, Hardmode, Creative, "Any% Bombs" | no explosives glitch |
| **Any% Glitchless** | Peaceful, Normal, Hardmode, Creative | no OOB; no clipping through walls, "including if you stay in bounds i.e.: entering Sahara" |
| **Inbounds%** | Creative only | the player must stay in bounds (no out-of-bounds) |
| **Co-op Any%** | Peaceful, Normal, Hardmode, Creative | up to 5 players; time starts when the first player moves, but you may wait for the last player to load |
| **Coop Glitchless any%** | Peaceful, Normal, Hardmode, Creative | the glitchless bans + the co-op rules (this one's text does not repeat the third-party software line) |
| **VR%** | Creative, Normal | must use a VR headset (any that works with the game) |
| **Bathrobe%** | none (any difficulty) | time starts at player control, ends when the bathrobe is equipped |
| **Shark%** | none (any difficulty) | time starts at player control, ends when the shark ragdolls |
| **100%** | none | Normal or Hardmode; crash the plane; all story items, all unique items, the flashlight and the cooking pot; the survival book / nature guide completed; the passenger manifest completed; the to-do list completed ("pending bugginess, but required at this point") |

Things worth knowing about the list:
- **"Any%" on its own is Creative-only.** There is no unrestricted any% on
  Peaceful / Normal / Hardmode; the survival difficulties are run under
  *No Explosive Glitch* or *Glitchless*.
- **"Any% Bombs"** is listed as a subcategory of *Any% (No Explosive
  Glitch)* but has no runs, and the category's rules ban the explosives
  glitch - read it as an empty board, not as permission. The tool keeps it
  as a draft with the game left open (it names no difficulty).
- **100%** has no runs on the board.
- **Bathrobe% and Shark%** start at *player control*, not at movement, and
  are the only ones that let the runner pick any difficulty in one board.

## What the bans mean in game

The rules name the bans in runners' words; this is how they map onto the
tech the other cards explain. All of it is `[inferred]` from the rule text
- speedrun.com does not define the terms.

- **"Explosives glitch"** - the **bomb boost**: an explosion's knockback
  piled up while the pause menu is open (`bomb-boost`). A bomb used for
  anything else (killing, the knockback alone without pausing) is not
  obviously "the glitch"; ask the moderators before relying on it
  [inferred].
- **"OOB"** (out of bounds) - leaving the playable world, including
  walking on the lab's invisible collision outside its drawn sections
  (`lab-skip`) [inferred].
- **"Clipping through walls"** - the smash clip / axe clip / panel clip
  (`smash-clip`), the wall-boost squeeze through thin rock such as the
  keycard cave clip (`wall-and-log-boost`), clipping out of the red
  elevator (`elevator-skip`), the cliff-climb snap past walls
  (`position-snaps`). Glitchless bans these **even when you stay in
  bounds** - "Sahara" is the runners' name for a place reached that way.
  Where exactly "Sahara" is has not been confirmed here [inferred].
- **"Developer mode"** - the game's built-in developer console (F1:
  `goto`, `itemhack`, ...; `dev-console-and-creative`).
- **"Third party software"** - cheat tools, trainers, memory editors.
  Whether a plugin like **ForestOverlay** counts is the moderators' call
  and **has not been ruled** (2026-10-03); the tool's *run mode* exists so
  they can judge it concretely (`forestoverlay`).

Not banned by any written rule (so allowed in every category that does
not name them) [inferred]: the zipline boost (`zipline-boost`), diagonal
running and coyote time (`movement-tricks`), the fall damage slide cancel
(`fall-damage`), the cave force load (`cave-force-load`), pausing itself
(`pausing-and-game-time`). Some of these may still count as "glitches" to
a moderator for *Glitchless* - the rule names only OOB and wall clips.

## Difficulty matters

- **Creative** turns on god mode, infinite energy and no survival
  (`dev-console-and-creative`), and runs it under a different set of
  boards everywhere. Creative is the only difficulty with an unrestricted
  Any%.
- **Hardmode** does not stop time in the inventory - only the pause menu
  stops time on every single-player difficulty (`pausing-and-game-time`).
- **Peaceful** has **no enemies at all**: `Cheats.NoEnemies` is true in a
  non-Creative Peaceful game, and every mutant spawner (`spawnMutants.Start`)
  returns at once - surface, caves and Megan's boss-room babies alike.
  Creative has enemies only with its "Allow enemies" option on
  (`PlayerPreferences.AllowEnemiesCreative`) [code].
- **Normal / Hardmode** have cannibals (`cannibal-ai`); on day 0 in
  daylight a family sleeps for 250 s after it spawns.

## Timing in practice

- **Start**: the first movement. The LiveSplit autosplitter's "Moving"
  start fires when the player's speed passes 0.15 m/s; ForestOverlay's
  `moving` event is the same (`endgame-splits`).
- **End**: the "E" prompt disappearing as the end cutscene starts - the
  autosplitter's *Game end* split (`doEndPlaneCrashRoutine` /
  `doShutDownRoutine`), on the frame the shared cutscene flag turns on
  (`endgame-splits`).
- Real time only: load times count, and so does any time spent paused
  (a bomb boost's seconds in the menu are on the clock).

## Records (speedrun.com, 2026-10-03)

First place per board, to show the scale; the boards change, so check
speedrun.com for the current ones.

| Board | Time | Runner(s) |
|---|---|---|
| Any% - Creative | 3:31.000 | Cheesecake404 |
| Any% (No Explosive Glitch) - Peaceful | 5:16.017 | sxczurass |
| Any% (No Explosive Glitch) - Normal | 5:11.633 | sxczurass |
| Any% (No Explosive Glitch) - Hardmode | 5:10.800 | sxczurass |
| Any% (No Explosive Glitch) - Creative | 5:48.433 | yirequ |
| Any% Glitchless - Peaceful | 18:59.966 | yirequ |
| Any% Glitchless - Normal | 18:54.266 | yirequ |
| Any% Glitchless - Hardmode | 18:54.300 | yirequ |
| Any% Glitchless - Creative | 15:09.733 | yirequ |
| Inbounds% - Creative | 9:04.617 | sxczurass |
| Co-op Any% - Peaceful / Normal / Hardmode / Creative | 6:07.333 / 5:45.083 / 5:45.600 / 5:08.550 | yirequ, sxczurass |
| Coop Glitchless any% - Peaceful | 15:59.867 | yirequ, sxczurass |
| Coop Glitchless any% - Normal | 16:50.433 | yirequ, Swaggyswaggster |
| Coop Glitchless any% - Hardmode | 17:12.967 | yirequ, buntstift |
| Coop Glitchless any% - Creative | 13:55.100 | sxczurass, yirequ, Swaggyswaggster, buntstift |
| VR% - Creative / Normal | 4:31.133 / 6:32.767 | Cheesecake404 |
| Bathrobe% | 32.900 | Cheesecake404 |
| Shark% | 20.383 | Cheesecake404 |
| 100% | no runs | |

What the gaps say [inferred]: bombs are worth ~1:40 in Creative (3:31
vs 5:48); the clips and out-of-bounds routes are worth ~10-14 minutes
(No Explosive Glitch ~5:10 vs Glitchless ~18:54); the survival
difficulties are faster than Creative under *No Explosive Glitch* (5:10
vs 5:48), so Creative's route there is a different one, not just the same
route with god mode.

## How ForestOverlay uses them

The tool's website copies speedrun.com's categories once a day into
draft *run categories* (one per category x subcategory) that the
moderators edit and publish on its /admin page: which overlay features a
run may use, the banned moves its detector flags, the anti-splice codes.
Nothing is published yet (2026-10-03) - publishing is the moderators' job
(`forestoverlay`; docs/run-mode.md *Categories*).

## Open questions

- Where "Sahara" is and how runners get there.
- Whether a bomb used without the pause menu counts as "the explosives
  glitch" under *No Explosive Glitch*.
- What "Any% Bombs" was meant to be.
- Whether the moderators allow ForestOverlay in runs (not asked yet).
