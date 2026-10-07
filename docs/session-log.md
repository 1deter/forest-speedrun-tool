# Older handoffs

Pick up here entries replaced at later handoffs, kept for reference (moved out of CLAUDE.md 2026-10-04). Facts in them live in their own homes (game-notes, cards, run-mode.md); this is the narrative.

## Before that (2026-10-07, before the first loop run)

- **Start:** skill `session-start` (the SessionStart hook's report is
  its first step).
- **Harness roadmap** (docs/harness.md, author 2026-10-06: first; what
  each step built: its *Status log*): steps 1-5 done (5: the e2e suite,
  `scripts/e2e.py`, T-0010 - the release skill runs its smoke), 3e
  skills, 8c, 9a, 9b, 10d (T-0013), **12 Stage A (T-0015): the assisted
  loop**, `scripts/loop.py` + skill `work-loop`, not yet run on real
  tasks; T-0007 / T-0113 built. Left: T-0014 cleanup loop + review,
  T-0016 Stage B (after Stage A runs a week without a fix), 14 small
  lint / test checks (T-0122..T-0135). Bot direction (author,
  2026-10-07): the author out of the feedback loop - T-0141, T-0140.
  **Order (author, 2026-10-07):** harness first, the redesign after (the
  redesign tasks are `author-present`, so the loop skips them).
  **Next: the first loop run** - skill `work-loop`; it takes T-0026,
  T-0028, T-0090, T-0140, T-0141 by priority.
- **New from the 2026-10-07 game session:** T-0143 native crash on a
  title load (P2, bridge); T-0049 is a real bug (another segment's Go
  keeps the red line); T-0144 Runs-tab dead links (question parked);
  T-0027 marker half and T-0052 tally confirmed, their other halves open.
- **Worktrees:** only `ui-redesign` (pushed; tasks T-0018..T-0025). **Released:** v0.24.249. Nothing unreleased.
- **Nothing is published yet:** all live categories are drafts (the
  moderators publish); no community run spot exists (the author's call).

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

## Handoff of 2026-10-06 (moved 2026-10-07, when its lists became tasks)

### Pick up here (2026-10-06, short session on a near-empty weekly quota)

**Start here, in order** (author, 2026-10-06: the harness work first -
"it will benefit all following tasks in the long term"):
1. `qa_read new_only` (the standing session start; file anything new,
   act on it after the harness unless it is critical).
2. **The harness plan, docs/harness.md**: first the deeper pass on the
   course (https://walkinglabs.github.io/learn-harness-engineering/en/ -
   every lecture, the projects, the templates `AGENTS.md` /
   `feature_list.json` / `claude-progress.md`, the frontier breakdowns),
   correcting the doc as its *Source caveat* says; then its roadmap from
   step 1 (the tasks file). Questions for the author go in the session,
   not into guesses (*Ground rule*). Needs high effort for the design
   parts - say so (memory `effort-level-switching`).
3. The author's two report zips (QA `1556842694758760559`, 2026-10-06
   01:57 / 02:36) - not read yet (the harness's report reader, 9b, would
   make this cheap).
4. The redesign fixes in docs/backlog.md *Author's notes in QA* (on
   `ui-redesign`, worktree clean + pushed), then the cursor re-check, QA, merge + release (the
   author's call, see below).
5. **Unreleased on main:** `706ac88` run lines off now stop drawing
   (`Game/LatePass`); ships with the next release - check in game with
   run lines off and a marker / replay up.
6. The bot settings page (*Next* below). Once the tasks file exists,
   these items move into it.

Worktrees / branches: only `ui-redesign` is left; the merged ones were
removed 2026-10-06.

*Earlier (2026-10-05 night):*

**v0.24.248** (released, confirmed over the bridge): Practice -> Import ->
*Website spots* (site `/api/spots.txt` + `/api/spots/{id}/foseg`,
`Data/SiteSpots`, `Modules/CommunityModule.Website.cs`, entries in
`segments/website.txt` under "Website"); a deleted spot disarms the run
(`PracticeRunModule.OnSpotDeleted`) and is deleted from the site by itself
(`uploads/deletes.txt` queue, `RunUploadModule.DeleteSpotQuietly`).
v0.24.246-247 confirmed over the bridge (docs/confirmed.md) except the
late pass at night and the ghost / replay overlays.
The runner PBs Discord channel (`1556505548269027399`, webhook "PB
Notifier") works: a test post went through 2026-10-05; real posts only for
community / published-category spots.

**UI / UX redesign DRAFT** (author, 2026-10-05: modern, UX friendly for
new runners, a Momentum Mod style HUD customiser - isolate a value like
speed, place and resize it): branch `ui-redesign` (pushed, NOT merged or
released), design in the redesign doc on that branch (ui-redesign.md); `Core/UiKit`
(palette, skin, sections, tooltips), `Data/HudLayout` (tested,
`hud-layout.txt`), HUD widgets + edit mode, window chrome, Runs tab
regrouped, results panel no longer overlapped. **Never seen in game** - a
build is on the author's Desktop (`ForestOverlay-ui-redesign-draft.dll`)
for them to try. Author tried it 2026-10-05 and gave a 10-item list (branch doc,
*Author's verdict*); the same evening most of it was built and seen in
game: yellow accent, filled toggles, value-only widgets, no drag-out,
total speed its own line, toasts, info box off by default, font-size
flicker and linear-texture colour fixes. Later: transparent widgets, toasts,
tooltips (`UiText.Note`), opaque window, ON NOW as a list. Left (branch
doc): the tooltips are done (Map / 100% keep live statuses); the cursor re-check, QA, then
merge + release (author's call). The author runs the draft.

**Read first:** the author's QA notes of 2026-10-05/06 (redesign fixes, run lines, trajectory, bot, two report zips) are sorted in docs/backlog.md *Author's notes in QA* - the redesign ones belong before its merge. Branches other than `ui-redesign` are all merged.

**Harness / autonomy plan** (author, 2026-10-06: "truly fully autonomous ... minimal human input aside from when new features are being added"): [`docs/harness.md`](harness.md) - the 12 harness-engineering principles scored, work items with checks, a roadmap (tasks file first). The author will do a deeper pass on the course before step 1.

**Next:** 0) the harness plan (docs/harness.md, author 2026-10-06: first); 1) the bot settings page on /admin (author, 2026-10-05;
design in docs/knowledge-bot.md *Bot settings page*); 2) the redesign's
remaining items (branch doc); then the backlog (colliders
that change between attempts, the Megan health check with the author, a
maintainability review).
A session picking this up mid-way: `git worktree list` / branches
`worktree-*` show unmerged work.

Older handoffs (2026-10-03 and before: the knowledge bot's cards / eval, banned-move detection, tech research) are in [`docs/session-log.md`](session-log.md).

**Session plan (author, 2026-10-02):** one item per session. Start each
session with `qa_read new_only`. Run mode and anti-cheat: every decision
is in [`docs/run-mode.md`](run-mode.md) - read it before touching run
mode, the report or anything a run uploads. **Earlier
(2026-10-03, research, no code):** the runners' tech read from IL and the
bridge - docs/game-notes.md *Speedrun tech and the endgame gate*: the bomb
boost is the knockback coroutine's per-frame `AddForce` piling up while the
pause menu stops physics (live: 1 s paused = 1,564 m/s); fall damage is
judged on the last collision ENTER's vertical speed (the slide cancel);
the cave force load is `doCave`'s timed `InACave`; the keycard bounty is
closed in code for single player (the end buttons live in
`endgame_streaming`, loaded only after the vault door or from a save made
inside the loaded lab). The swinging rock trap's knockback stacks
like a bomb (confirmed by the author 2026-10-04: works, but slower to build
and less versatile than a small bomb trap). Detection designs (not built):
docs/run-mode.md *Banned moves: detection*. Before that (same day): the
author looked at the website work of the last
sessions and confirmed all of it (docs/confirmed.md, 2026-10-03): the photo
map's lakes, the 3D middle lake, the south mountains' / lab textures, caves
on the 2D map ("a little bit hard to read but it's fine"), Follow's camera
in caves; the long N-S snow shadow is gone in the recapture. Two decisions
made (*Standing decisions*: teleports into the endgame; Quick load physics
is maks's call - asked him, QA `1555760732086476832`). Before that, site
only: the photo map's lakes and the 3D surface water (docs/website.md
*Terrain, sea and water*), the 3D view's culling and far copies (*Load
size*), caves on the 2D map, the photo map recaptured on v0.24.222 (the
sun back after a tp out of the endgame, gotcha 87). Run mode phases 1-4
(v0.24.206-220): docs/run-mode.md. **Nothing is published yet**: all live
categories are drafts - publishing is the moderators' job. No community run
spot exists yet - making one is the author's call.

**Next, in order (one per session):**
1. **Banned-move detection** - built (v0.24.227-234, every row of
   docs/run-mode.md *Banned moves: detection*); left: the moves done for
   real (item 5).
2. **Tech research, round 2** - done 2026-10-03 (*Pick up here*). Left
   from it: check *Reload save on death* gives the same game as a manual
   reload (docs/run-mode.md *Decisions*), the elevator skip done by hand
   with `anim watch`, the multi-thrower / bodies slide, Megan's FSMs.
3. **The game-knowledge Discord bot** (author, 2026-10-03; plan and
   decisions in [`docs/knowledge-bot.md`](knowledge-bot.md)): the
   knowledge base (29 cards) and the bot (`bot/`) are built and live in
   the QA server; eval + tuning done (87%, 2026-10-03); `megan-boss`,
   `cannibal-ai`, `categories-and-rules`, `routes`,
   `crafting-and-building` done (2026-10-03); next the research queue
   and the bot's queue (`knowledge/README.md`).
4. Then the main *Next up* list below. **Ideas waiting (author,
   2026-10-03):** a run audit log (every interaction, on the attempt
   page's timeline) and richer replays (buildings as schematics,
   first-person replays with animations, a trajectory / "grenade camera"
   view for bomb boosts and ziplines) - [`docs/run-audit-and-replays.md`](run-audit-and-replays.md)
   (decisions there: run mode only, a skimmable rundown, in game first).
   **After v1** (author, 2026-10-03): UI work, refactoring inefficient /
   bad code, and a lighter repo with only useful information - plus
   feature / QoL ideas as they come.
5. **Last, when every task is done** (author, 2026-10-03: "leave these for
   later when we're done with all tasks"): run mode by hand with the author
   - a real ESC + F2, the Runs tab section, End / Start run mode by
   clicking, a run spot's F7, the run code on a real recording (`CodeSize`
   40 px, top centre), the attempt page with a real attempt, /admin's
   *Categories* tab (investigations *Not seen by the author*); the
   detected moves done for real - a bomb boost, a cave force load (smash
   in the air), a fall damage cancel (sliding on bodies, Cave 6), a smash
   clip (a cave panel / the red elevator door), a log boost, the keycard
   cave wall clip (true any%) - read the `Move seen:` / `Move watch:` /
   `ClipWatch:` lines after each.
- Website: done apart from texture arrays in the export - only if frame
  times call for them (docs/website.md *Load size*).

**Decisions waiting for the author:** none open. Categories to publish
and their run spots are the moderators' (author, 2026-10-02: "i've given
them the tools").

**Waiting on testers** - the QA to-do list (`qa_todo`) is the record:
maks on Quick load physics (`1555760732086476832`: still different? which
move / spot / Quick or Full? else close it), the overnight lists
(`1555319960941756437`, `1555327671276273677` + `1555328852698333277`), the
per-tester lists (maks `1553807713593597984`, sxczurass
`1553811127278764167`), Tom's crashes (paused - author), maks's fog /
elevator / rope list, sxczurass's FPS answers + specs + the crouch fix
(v0.24.102), Cheesecake's Frame test, Ruben's inventory. Detail:
investigations.

**Investigations** (each stays in one session when picked up; detail in
investigations): raw FPS, performance / loads (garbage in play), Quick
load physics parity (maks decides - waiting on his answer), Tom's crashes
(paused).

**Awaiting an in-game check** — ask before building on these (the
current items are in docs/investigations.md):
- **v0.23.6's census off by default** - no hitch after a load.
- **Run lines cleared** on a plain spot / another segment (v0.22.7).
- **Weapon-upgrade receivers kept** on a cross-save restore (v0.22.7): the
  restore line says `kept N weapon-upgrade receiver(s)`; the adoption line
  lists `other misses:` - read it to see why they did not adopt.
- **Whether a timed run still arms after an F7 restore** — runs do not log
  arming; add a log line if it is ever in doubt.
- **"GATHER LOGS 0/4"** after an in-place restore (v0.20.2).
- **Savestates outside the easy case:** a busy surface area; the stick
  oddity (first stick picked up after an in-place restore went to the
  inventory, not the hand; seen once).
- **Boss-fight quick-load toggle** (v0.19.4) — a boss-fight death is rare.
- `end-shutdown`, `timmy-goodbye`, `raft-out-of-world` never seen in a log.

### Next up

The author's feature list, ordered by what runners feel soonest. Done
(detail in CHANGELOG.md): the load leak, updates under any name,
savestates + fix list + sharing, practice QoL, passengers, logs in the
inventory, god mode, freecam lighting, LiveSplit import, the website's
first versions. All dev/alpha: nothing is used in real runs until the
admins rule. The author: "work through the current list so we can move
onto expanding more features".

1. **Quick load physics parity** *(runner maks; up to him - author,
   2026-10-03; asked QA `1555760732086476832`)* - investigations.
2. **Performance: can patches make the game faster?** - raw FPS and
   loads, investigations. Done so far: v0.24.86-143 (profiler, tracker,
   load timing, PerfPatches 1-15, the reload freeze, the load crash).
3. Reload the slot **in place** on death (author's idea; a *Quick load the
   slot's save* button did it until v0.24.106 -
   `SavestateBridge.ReadSlotData` in git history). Also the flashed
   time's display (maks).
4. **forest.deter.cloud** *(runner)* - its *Next* in docs/website.md;
   maks's YouTube side-by-side (QA `1554074251831672943`, site only).
5. **TAS** - exploratory only, on savestates and the recorder.
6. **Speedrun tech research** (author, 2026-09-26, "later down the
   line"): placing ziplines precisely (the big schematic, a short window)
   and the expected trajectory; **bomb boosting** (explode, open the
   menu, wait, close - distance from the velocity at the menu, the time in
   it and fps; maybe a boost view, Experimental; sxczurass's measurements
   by fps: `1553447134911664168`, `Downloads\qa-reports\sxczurass\image.png`);
   panel / axe clipping.
7. Freeform zone shapes.

### Deferred runner feedback

Runner QoL / UX requests waiting until *Next up* is done (author: finish the list first, unless critical): [`docs/backlog.md`](backlog.md) - deaths clarity, runs / run lines, checkpoint savestates, status overlay, settings that persist, debug views, maks's list, and a *final exhaustive feature testing* section (checks to run before a wide release). New unscheduled requests go there.
