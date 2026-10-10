---
id: categories-and-rules
title: Speedrun categories and their rules (speedrun.com)
aliases: categories, category, rules, speedrun.com, src, any%, any percent, any% no explosive glitch, no explosives glitch, neg, bombless, any% bombs, glitchless, any% glitchless, coop, co-op, co-op any%, coop glitchless, inbounds, inbounds%, oob, out of bounds, vr%, vr, bathrobe%, shark%, 100%, hundo, difficulty, peaceful, normal, hardmode, hard mode, creative category, timing rules, when does the timer start, when does the run end, banned, allowed, is it legal
tags: categories, rules, timing
confidence: runner
checked: 2026-10-03
sources: speedrun.com API v1, game w6j5341j (The Forest): categories with rules and subcategories, leaderboards top 3, read 2026-10-03; site/ForestSite/Categories.cs (how the tool seeds them); docs/run-mode.md "Categories"
related: top-runners, hundred-percent, routes, bomb-boost, smash-clip, lab-skip, elevator-skip, endgame-gate, wall-and-log-boost, dev-console-and-creative, pausing-and-game-time, endgame-splits, forestoverlay
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
- **100%** has no runs on the board; its full item list and book parts
  are in `hundred-percent`.
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
  bounds** - "Sahara" is the runners' name for the cave section that leads
  to the vault door (yirequ's Glitchless guide has a chapter of that name;
  `routes`): the Glitchless route walks into it, and clipping in is banned
  [runner].
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

The **fall damage slide cancel is allowed in Glitchless** [runner: a speedrunner in the
knowledge-testing feedback, 2026-10-05: slides to avoid fall damage "are more of a physics
quirk than a specific bug" and were ruled allowed in glitchless; not on the rules page].
The written Glitchless rules name only OOB and clipping through walls.

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
  start fires when the player's speed passes 0.15 m/s (`endgame-splits`);
  ForestOverlay starts on the first input instead (`first-input`: any key,
  button or movement but Esc and the camera), which the autosplitter
  cannot read.
- **End**: the "E" prompt disappearing as the end cutscene starts - the
  autosplitter's *Game end* split (`doEndPlaneCrashRoutine` /
  `doShutDownRoutine`), on the frame the shared cutscene flag turns on
  (`endgame-splits`).
- Real time only: load times count, and so does any time spent paused
  (a bomb boost's seconds in the menu are on the clock).

## Where the times and holders are

The top 3 of every board, who holds the most of them, what the gaps
between categories say, and the moderators: `top-runners`. The full 100%
item list: `hundred-percent`.

## How ForestOverlay uses them

The tool's website copies speedrun.com's categories once a day into
draft *run categories* (one per category x subcategory) that the
moderators edit and publish on its /admin page: which overlay features a
run may use, the banned moves its detector flags, the anti-splice codes.
Nothing is published yet (2026-10-03) - publishing is the moderators' job
(`forestoverlay`; docs/run-mode.md *Categories*).

## Open questions

- Whether a bomb used without the pause menu counts as "the explosives
  glitch" under *No Explosive Glitch*.
- What "Any% Bombs" was meant to be.
- Whether the moderators allow ForestOverlay in runs (not asked yet).
