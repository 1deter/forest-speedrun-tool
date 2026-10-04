# Older handoffs

Pick up here entries replaced at later handoffs, kept for reference (moved out of CLAUDE.md 2026-10-04). Facts in them live in their own homes (game-notes, cards, run-mode.md); this is the narrative.

## Before that (2026-10-03, v0.24.234 released)

**Latest session: three cards - `categories-and-rules`, `routes`,
`crafting-and-building`** (no plugin code, no release). speedrun.com's API
(game `w6j5341j`): rules, subcategories, top 3, moderators. Unrestricted
Any% is Creative only; "explosives glitch" = the bomb boost; Glitchless
bans OOB + wall clips; "Sahara" = the runners' cave section to the vault
door. Peaceful = no enemies at all. `routes` from three guides'
transcripts (`yt-dlp --write-auto-subs`; the guides page needs the
built-in browser - Cloudflare). Stamina live (InfiniteEnergy off): sprint
3.5/s, regen 6/s after 0.4 s, capped by Energy; recipes and soda / mix
effects read from `ReceipeDatabase` / `ItemDatabase`; the hole cutter
destroys walls it touches (game-notes). The bot's glossary goes into
search in parts now (`Corpus` AddParts; it hit the 6 KB chunk limit).
9 eval questions (58). The planned cards are done; next is the bot's 👎
queue and the research queue (knowledge/README.md). Slot 1's game was a
Normal game with GodMode / InfiniteEnergy on (left as found).

**Before that: the `cannibal-ai` card** (no plugin code, no release).
A new Normal game over the bridge (Slot 1 is Peaceful Creative - no
cannibals; `GameSetup.Game` stays Creative across `OnNewNormalGame`, so
`call static:TheForest.Utils.GameSetup SetGameType Standard` first; skip
the plane intro with `press Jump`). Finding: the cannibals' motor / vision
/ search FSMs **do not exist** - that AI is C# (`mutantSearchFunctions`,
`mutantAI`, `pmSearchReplace`; the combat FSM dispatches to
`pmCombatReplace`), so nothing was left to export. Sight range is computed
on the player (`visRangeSetup`: 84 m standing / 67.2 crouched, day, open,
live); walking is silent, running is heard at 58.8 m (live); the player's
"stealth ranges" sent to cannibals are never read; the 3 / 5.5 m/s "enemy"
caps are not enemies (co-op rope overlap / no caller) - research queue item
closed. Card `knowledge/cards/cannibal-ai.md`, game-notes *Cannibal AI*,
docs/fsm/README corrected, 4 eval questions (49). Left: the card's open
questions (knowledge/README.md research queue).

**Before that: the `megan-boss` card** (her FSMs exported live from the
`ruben-megan` savestate; game-notes *Megan's boss AI*).

**Before that: the knowledge bot's eval run + tuning** (no plugin code,
no release; docs/knowledge-bot.md *Build order* 3). Eval 41% -> **87%**
(+ the new questions ~88%), all on Flash-Lite: Flash's free daily quota
runs out almost at once (gotcha 94), so the live bot is mostly Flash-Lite
too. Fixed: the eval waits out per-minute rests and retries the judge;
429s log their `quotaId`; `read_card` puts a **NOT CONFIRMED** list
(`[runner]` / `[inferred]` / not reproduced / Open questions) on top of
each card; LaTeX in answers becomes plain text in code; prompt rules from
the author's live thread (Discord `1555989862652313620`: the bot called
the runner-reported **Timmy forehead skip** "a joke" and repeated it
louder on pushback; denied, then invented a cause for, a ~50 m/s after an
axe clip): never dismiss a runner report, guesses only under "Possible
causes (guesses, not tested)", no "does not" beyond what was tested, no
developer motives, re-check on pushback, rules questions still looked up.
New card `timmy-forehead-skip` (runner report); the clip speed noted in
smash-clip / wall-and-log-boost; 3 new eval questions (43); `forest-bot
answer <id>` / `resolve <id>` for the 👎 queue. Builds on the bot built and
deployed earlier the same day (`knowledge/` 24 cards, `bot/`, the VPS
container; decisions in *Standing decisions* and docs/knowledge-bot.md;
decompiled source in `%LOCALAPPDATA%\ForestOverlay\game-src\`). **Next:**
the embedding model locally (author OK for ~130 MB, not asked), more
cards (categories, routes, crafting), the production server once the admins agree. The queue holds
none open (#1 Megan and #2 resolved). VPS: `ssh -i
~/.ssh/ssh-key-2026-08-13.key ubuntu@141.147.101.228 'sudo docker exec
forest-bot dotnet /srv/current/forest-bot.dll queue'` - run it without
asking (author, 2026-10-03: "just do everything you need to"; memory
`run-commands-no-ask`).
Earlier the same day: banned-move detection finished (v0.24.227-234:
bomb boost, huge speed, cave force load, fall damage cancel, lifts, clips;
gotchas 90-91), tech research round 2 - game-notes *Speedrun tech*.

