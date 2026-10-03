# Knowledge base for the game-knowledge bot

What the Discord bot ([`docs/knowledge-bot.md`](../docs/knowledge-bot.md))
explains to runners. Written by Claude sessions from the game's code, the
live test bridge and the runners' own guides; reviewed here, in git.

- `cards/*.md` - one topic per card (format below). The bot's main source.
- `glossary.md` - runner words -> cards. Always in the bot's prompt.
- `eval/questions.md` - test questions with the facts a good answer needs.

The bot also searches `docs/game-notes.md`, `docs/fsm/*.txt` and the
decompiled game code (private, on the server only) for what no card covers.

## Writing a card

**Audience: a runner who wants to understand the mechanic completely** -
how to do it, why it works at the code level, the numbers, what the best
version looks like, why it fails. Plain words, full sentences, numbers with
units. Quote the game's code where it is the explanation (the author allows
it freely). Never guess: anything not tested or read is marked as such, and
an open question stays an open question.

```markdown
---
id: bomb-boost
title: Bomb boost
aliases: bb, bomb boosting, pause boost, explosion boost
tags: movement, tech, explosion
confidence: live
checked: 2026-10-03
sources: game-notes "Speedrun tech and the endgame gate"; ...
related: knockback-sources, player-physics, pausing-and-game-time
code: playerHitReactions.enableExplodeCamera, PlayerStats.Explosion
---

# Bomb boost

One-paragraph summary: what it is and the one idea that explains it.

## How runners do it
## Why it works
## Numbers
## The optimal version
## Why it goes wrong
## Evidence
## Open questions
```

Front matter, one `key: value` per line, lists comma-separated:

| Key | Meaning |
|---|---|
| `id` | file name without `.md`; lowercase, hyphens |
| `title` | the name runners would recognise |
| `aliases` | every other name runners use - slang, abbreviations, misspellings. Search lives on these |
| `tags` | loose grouping (movement, tech, endgame, caves, physics, timing, tool) |
| `confidence` | the card's weakest core claim: `live` (reproduced in game over the bridge), `code` (read from the game's code, not reproduced), `runner` (runners' reports, not reproduced), `inferred` |
| `checked` | date the content was last checked against the game |
| `sources` | where each fact came from: docs sections, guides, QA messages |
| `related` | other card ids |
| `code` | `Type.Method` names the bot can open for deeper follow-ups |

Inside the body, mark claims whose confidence differs from the card's with
`[live]`, `[code]`, `[runner]` or `[inferred]` at the end of the sentence or
bullet. Sections that do not apply are left out; a card may add its own.

**When a card changes the knowledge** (a test, a correction), update
`docs/game-notes.md` too - it stays the reference for sessions; cards are
the explained version for runners.

## Cards

Written 2026-10-03 (23): player-physics, movement-tricks, bomb-boost,
knockback-sources, fall-damage, cave-force-load, smash-clip,
wall-and-log-boost, zipline-boost, swimming, position-snaps,
tunnelling-and-speed-cap, elevator-skip, lab-skip, endgame-gate,
keycards-and-pickups, endgame-splits, caves-and-loading,
saves-and-loading, deaths-and-revives, pausing-and-game-time,
dev-console-and-creative, forestoverlay (+ timmy-forehead-skip, a
runner report only, 2026-10-03; megan-boss, 2026-10-03, from her FSMs; cannibal-ai, 2026-10-03, from the C# AI + live reads). Each was checked against the
decompiled code while written (corrections made on the way: the pause menu
stops time on Hard too; the fall cap is the speed cap; the zipline exit
weakens braking).

Research queue (seed - open questions from the cards; the bot's queue adds
to it):
- Does the "Gold Keycard (Red Elevator)" split fire on a keyless ride?
  (`elevator-skip`)
- The smash clip, the cave force load's smash, the Cave 6 body slide, the
  wall boost and the keycard cave clip done with real input + `anim watch`
  (the author's hands-on list).
- What equipping cuts after a keycard pickup ("spam 1").
- Where keycard 242 lies.
- The wall-side swim jump (1.5x) and wall-side swim speed.
- Zipline exit distances by height and angle.
- Megan: her hits' damage live, a timed fight within 35 m vs bombs, births
  before the block, what kills the boss-room babies, weapon damage to her
  (`megan-boss`).
- Cannibals: weapon swing noise range, the bush bonus above 60 fps, the
  Stealth stat's sources, cave sight / noise live (`cannibal-ai`).

Planned next - each needs research, not only writing:
- `categories-and-rules` - speedrun.com's categories and what each allows.
- `routes` - the any% / Creative routes step by step (from the guides).
- `crafting-and-building` - what runners build (bomb traps, walls,
  ziplines, gliders) and how long it takes.
