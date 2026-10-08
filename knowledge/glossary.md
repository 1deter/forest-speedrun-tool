# Glossary - runner words to cards

Always in the bot's prompt. One line per term: what runners mean, and the
card that explains it. Keep each meaning to one line; the card has the rest.

| Term | Meaning | Card |
|---|---|---|
| bb, bomb boost, pause boost, menu boost | explosion knockback stacked while the pause menu is open | bomb-boost |
| trap boost, rock trap boost | the large swinging rock trap's knockback, stacked like a bomb boost | knockback-sources |
| knockback | `PlayerStats.Explosion`'s push: 8 m/s per frame for ~0.16 s, straight away from the source | knockback-sources |
| multi-thrower, multithrower, rock thrower, catapult | the player-built thrower; its rocks knock you back like a bomb | knockback-sources |
| last usable frame | the end of the knockback's free push window (~0.13-0.16 s of game time) | bomb-boost |
| slide cancel, fall damage cancel, body slide | landing judged on a slow last collision, so no fall damage | fall-damage |
| fall damage, fall damage formula, how fall damage works | damage only if the last collision-enter speed is over 28 m/s: 0.9 x v^2 / 27.5, 1000 (death) over 3.8 s in the air, needs over 0.75 s of air time | fall-damage |
| fell too long, 3.8 s | over 3.8 s in the air = 1000 damage | fall-damage |
| hard landing, landing stun | the 1 s freeze after a damaging landing | fall-damage |
| force load, cave force load | cave state left on at the surface by a cut-short cave entry | cave-force-load |
| cave state, InACave | terrain not solid, cave lighting, cave streaming | caves-and-loading |
| smash clip, axe clip, panel clip, plane clip | crouch + jump + axe ground smash + uncrouch through a thin wall | smash-clip |
| ground smash, axe smash | the axe attack into the ground while looking down | smash-clip |
| elevator skip, elevator clip, elevator boost | clipping out of the red elevator's locked car early | elevator-skip |
| red elevator | Hell Corridor elevator to the overlook; checks no keycard | elevator-skip |
| cannibals, enemy ai, stealth, detection | sight range computed on the player (light, crouch, bushes, trees, running, lighter); running is heard at ~59 m, walking is silent | cannibal-ai |
| stalking, stalker | a passive cannibal watching you: attacks within 8 m, rolls within 24 m | cannibal-ai |
| families, cannibal spawns | spawner groups by day (day 0: 6 skinny); the 4 nearest spawn points are skipped | cannibal-ai |
| megan, boss fight, megan ai | the endgame boss: within 35 m she attacks every cycle (picked by distance); beyond 35 m she rolls, and births babies; her dodges throw her 30-55 m back, past 35 m | megan-boss |
| boss babies, baby spawn | Megan's births: only from her roll beyond 35 m (her dodges get her there); one birth drops 6 babies; none while 3+ spawners exist, and a dead baby frees its spawner | megan-boss |
| lab skip, gold door skip, megan skip | past the gold door over invisible collision + a clip, skipping Timmy / Megan / boss | lab-skip |
| timmy forehead skip, forehead skip, bed skip | runner-reported: aim at Timmy's scar at ~15-18 fps to skip the bed part of his cutscene (not researched) | timmy-forehead-skip |
| invisible section, invisible collision | the lab's collision (`EndCollision`), always present, drawn only through area gates | lab-skip |
| true any% | any% including the keycard cave wall clip | endgame-gate |
| keycard skip, keycardless | beating the game without keycard 210 - not possible in single player | endgame-gate |
| vault door, keycard 210 | the endgame entrance; opening it is what lets the lab load | endgame-gate |
| gold keycard, keycard 242 | opens the gold door only | keycards-and-pickups |
| spam 1 | equip right after the keycard pickup to cut its animation | keycards-and-pickups |
| wall boost, custom wall boost, log boost | a structure built into the player pushes them out on top (depenetration) | wall-and-log-boost |
| keycard cave clip | a built wall squeezing the player through thin rock | wall-and-log-boost |
| depenetration | physics pushing an overlapping player out the shortest way | wall-and-log-boost |
| zipline boost | keeping a zipline's exit speed (1 s of weak braking + air time) | zipline-boost |
| diagonal running, run diagonally | input clamped to 1.1, so W+A / W+D is 10% faster | movement-tricks |
| coyote time | a jump still works 0.21 s after leaving the ground | movement-tricks |
| jumping, bhop, jump chain | the air has no friction and steering only pulls toward run speed: a few % faster than the ground, gain not measured | movement-tricks |
| cliff climb clip, climbing axe clip | the climbing axe's 5 m ray ignores most walls, snapping you past them | position-snaps |
| rope pull | grabbing a rope from up to 6.1 m pulls you to it | position-snaps |
| tunnelling, speed clip | passing a wall by speed; blocked by the 55 m/s cap and the knockback's CCD | tunnelling-and-speed-cap |
| speed cap, max speed | 55 m/s on the ground and in the air (falls level off at 55.43) | player-physics |
| CCD, continuous collision | swept collision, on only during a knockback | player-physics |
| first death warp, cave warp | the first death always wakes you in Cave2DeadPlace | deaths-and-revives |
| last stand, adrenaline | an enemy hit that would kill leaves 1 health above the grey zone | deaths-and-revives |
| pause menu vs inventory | the pause menu stops time on every SP difficulty; the inventory only on Normal / Peaceful / Creative | pausing-and-game-time |
| hitch | a long frame counts in full as game time (up to 9 s) | pausing-and-game-time |
| autosplitter, asl, splits | LiveSplit's autosplitter; endgame splits on the shared cutscene flag | endgame-splits |
| plane meal | the run's start split: the hold-to-interact flag at the start | endgame-splits |
| respawning pickups | world pickups (the keycard) come back on every save load | saves-and-loading |
| dev console, itemhack, goto | the game's built-in developer console | dev-console-and-creative |
| Creative | god mode + infinite energy + no survival, set by the mode | dev-console-and-creative |
| categories, glitchless, inbounds%, peaceful | speedrun.com's boards: rules, difficulties, timing, records | categories-and-rules |
| top runners, wr, records, leaderboard, top 3, co-op records, itsslack | every board's top 3 on speedrun.com (nothing below 3rd), first-place counts solo and co-op, each runner's places; Cheesecake404, sxczurass, yirequ hold most | top-runners |
| 100%, hundo, all items, passenger manifest | the 100% rules and the full item list, nature guide, passengers, to-do list | hundred-percent |
| bomb boost on Normal, bombs in a category, is the bomb boost allowed | Normal is run under No Explosive Glitch (bans the bomb boost) or Glitchless; unrestricted Any% only for Creative; moderators decide edge cases | categories-and-rules |
| explosives glitch, OOB | the bomb boost; out of bounds - banned by board | categories-and-rules |
| route, K4 skip, Sahara | the runs step by step; Sahara = the cave to the vault door | routes |
| stamina, energy, sodas, stamina mix, shift reset | sprint 3.5/s, regen 6/s (not while sprint held), capped by energy | crafting-and-building |
| building, blueprint, hole cutter | Creative: hold Build, an item per 0.065 s, free | crafting-and-building |
| build cost, how many logs, log cabin, log wall, deer skin for everything | every blueprint's ingredients; custom wall / zipline formulas; material totals | building-costs |
| recipes, crafting, weapon upgrades, warmsuit, waterskin | every inventory recipe and upgrade from the recipe database | crafting-recipes |
| ForestOverlay, the tool, savestates, Quick load, run mode | this project's speedrun plugin | forestoverlay |
| fps, uncapped fps | matters wherever the game pushes once per rendered frame (bomb boost) or samples per frame (smash clip); physics stays 60 Hz | player-physics |
