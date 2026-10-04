# Run mode and anti-cheat

Designed with the author on 2026-10-02. The goal is to let runners use the
tool in real runs, so they can practise with it and run with it, **without
asking more of new runners than installing the plugin, and without adding
work for verifiers**. The speedrun.com moderators have not ruled yet; this
gives them something concrete to judge.

## The honest limit

Anything on the runner's PC can be beaten by someone who rewrites it. The
plugin is open source, so a fork can report anything, and no key hidden in
the DLL stays hidden. The author's example: a mod that makes Megan repeat one
attack. Such a mod can cheat an **unmodded** run today, and nothing catches
it. So the bar is:

1. During a run the tool can do nothing the unmodded game cannot.
2. It makes the common cheats harder than they are without the tool,
   splicing above all.

**Why splicing is the verifiers' worry:** savestates turn "a perfect segment
to splice in at a cave animation" into an unlimited supply. Without the tool,
a splicer needs two full attempts that reach the same point.

**Security rests on what the server knows** (the nonce, when each checkpoint
arrived, which upload came first, whether the video's codes match the log),
never on secrecy. The format is public, so an admin who knows it has no
advantage.

## Decisions (author, 2026-10-02)

- **Runs start from preset saves, not new games** (author, after phase 1
  shipped: "we don't do runs from starting a new game. we already have
  preset saves that we could potentially transfer into spots"). Decided
  and built in v0.24.213 - see *What starts a run* below. What a run
  allows should come mainly from the **community spots** (one per
  category, made from the preset saves) - "don't worry too much about it"
  (author) for the per-feature details now.

- **Automatic, fully locked.** Every new game from the title screen is a run
  attempt. Practice features are *locked*, not just marked, so an accident
  cannot spoil a run. Leaving takes a two-click *End run mode* (Runs tab), or
  loading a save instead of starting a new game. Practice earlier in the
  session does not block a run; the report notes it.
- **Back to the title screen = a reset** (author). The next new game is the
  next attempt. Run mode stays on in between.
- **The F2 window** no longer counts as practice. During a run it opens only
  over the game's pause menu, which already holds the player (holding the
  player mid-run could stop a fall).
- **Reload save on death stays on in a run** (author, 2026-10-02: an
  optional QoL that saves runners menuing and load waits). The attempt goes
  on through that reload (v0.24.210). The practice revive (back to a spot)
  stays locked. Run mode **never switches it on**; it follows the runner's
  own setting. It stays unlocked **only if it is confirmed to give the same
  game as a manual reload** (exit to the main menu -> Continue -> the save)
  - author, 2026-10-02. To verify: both paths (*Skip the title screen* =
  `LevelSerializer.Resume`, and through the menu) against a manual reload
  of the same save. Compare player state, inventory, time of day, enemies,
  the world and the cave state. If one differs, lock that path in a run.
  (Its default is on for everyone today, from before run mode; ask the
  author whether that should change.)
- **Anti-splice codes** beside the timer: big, readable on YouTube, out of the
  runner's way.
- **Receipts for every attempt**, resets included, uploaded by default. The
  server has ~200 GB, shared with other uses. Estimate: ~1 KB/s raw for a run
  log (a 137 s `.run` = 143 KB), so 1 h is ~4 MB raw and ~1 MB compressed;
  a reset's receipt is a few KB. That is well under 10 GB for years at this
  community's size. If it grows: keep full logs for PBs and submitted runs,
  receipts for the rest.
- **Reports are public** (runners check their own before submitting; anyone
  can double-check). Admins can verify or reject (in the admin log), but
  nobody can edit a receipt or a report; deleting a run is owner-only.
- **Offline runs** are queued and uploaded later in order. The link exists at
  once (the run id is made on the PC). They show **amber**: "checked by the
  video's codes only". An edited queued log no longer matches the video's
  codes. The moderators decide per category whether amber is accepted.
- **Changed game code is named in plain words**, by area ("Megan boss fight",
  "player movement"), never as code. The internal names sit behind a details
  fold.
- **Categories are defined by the moderators on the site** (/admin, their own
  keys): difficulty / mode, the overlay features allowed in a run, banned
  moves (e.g. a bomb boost flagged automatically), *Reload save on death*,
  the linked community practice spot. The floor cannot be configured away:
  state-changing features are always locked. Seed the drafts from
  speedrun.com's categories **and their rules**, as a head start for the
  moderators.
- **Anti-splicing is optional per category / run spot** (author, QA
  2026-10-02: "may not be really necessary for the manhunt vs a new player
  getting a WR"). A moderators' setting in phase 4: off = no code on
  screen and no codes / checkpoint judgement on the attempt's page.

## What starts a run (author, 2026-10-02; v0.24.213)

- **A run spot.** A spot with a run category (`run = Any%`, the editor's
  *Run* field; author: only *marked* spots, so packs can still hold
  practice segments) and a start state. Restart (F7, the Runs tab, the
  editor) on it restores the start state with a **Full load, always**
  (author: "i don't trust [Quick load] to fully replicate everything" -
  Quick load gets a thorough test later to speed run starts up), and the
  attempt starts once the player stands at the spot. The report's
  `started` line names the category, the spot (community or own, its id)
  and the start-state hash.
- **Restart on the run spot during a run is a reset** - the attempt ends,
  the next starts after the load. Only a run spot's Restart is allowed in
  run mode; every other one stays locked. The title screen is a reset too
  (Restart from it works - v0.24.212's title load).
- **A game loaded any other way** (the menu, a new game) ends run mode.
- **The fallback: Start run mode** (Runs tab) for a category with no run
  spot - the attempt starts in the game as it is; after a reset, the next
  game loaded (new or saved) is the next attempt.
- **A new game no longer starts a run** (phase 1's trigger, removed).
- **The game mode follows the start state** (v0.24.211): a Creative
  capture in a Normal game switches the game to Creative with the load and
  back. Saving in game afterwards writes that mode into the slot (author:
  fine - "they could do the same in reverse"; never blocked).

## Codes and receipts (phase 2, v0.24.216)

**The log** (`src/Data/AttemptChain`, shared by the plugin and the site):
text, one record a line - a header (attempt id `a-<16 hex>`, runner,
plugin, category, spot + start-state hash, a local random seed, the PC's
start time), then a `step` a second (real ms since the start, the timer's
ms, the player's position in whole cm), `nonce`, `split`, `flag` (run
mode's flags as they happen), `move` (detected moves), `event` (the run
audit log - docs/run-audit-and-replays.md part 1) and `end` (reason,
final timer). Every line
before `[report]` is folded into a SHA-256 chain (head = SHA256(head +
line)); the run report follows unfolded (it is the plugin's own claim).
The **code** is the first 20 bits of the head after each step, as four
Crockford base32 characters (no I, L, O, U; a typed I / L reads as 1, O as
0).

**What the site knows that a cheater cannot make up**: the nonce (random,
given when the attempt starts, so no code can be computed before then)
and its own clock for every checkpoint (the head after a step, about once
a minute). `site/ForestSite/Attempts.Judge` (tested):
- **red**: the log contradicts the site - a checkpoint's head differs
  (a splice: the uploaded log is not the one the game was hashing), a
  nonce that is not the site's, a log whose clock runs ahead of real time
  (a checkpoint or the log itself arrived before it could have), or run
  mode flagged the attempt.
- **amber**: parts only the video's codes can check - started offline
  (no nonce: the attempt still goes up later, in order), the nonce
  arriving more than 30 s in, a stretch over 150 s without a checkpoint,
  a late checkpoint (> 45 s), no end (the game closed: the open log is
  kept and sent on the next launch).
- **green**: online from the start, every checkpoint matches, and they
  cover the attempt in real time.

**Why a splice fails**: the video's codes come from the chain the game was
hashing. A piece from another attempt shows codes from that attempt's
chain (another nonce, other positions) - checking a few codes from that
part against the log fails. A fork that keeps "continuing" the first
attempt's chain during the second still has to send the site checkpoints
in real time: the gap between the two attempts shows as a missing or late
checkpoint (amber) or a mismatch (red).

**Plugin**: `Modules/RunModeModule.Codes` (the chain: a step a second from
a `Stopwatch`, a checkpoint a minute once online, the log rewritten every
30 s to `run-reports/attempt-<time>.log` and `uploads/attempts/open/`,
ended on every attempt end incl. a finished timed run and the game
closing; the code box, `[RunMode] CodeX / CodeY / CodeSize`, drawn by
`DrawScreenAlways` - shown with F5's overlay hidden too),
`Modules/RunUploadModule.Attempts` (the nonce request, tried ~25 s; the
checkpoint POST; the outbox `uploads/attempts/*.attempt`, sent oldest
first, refused ones to `refused/`; `sent.txt` + the Runs tab's last 5
with **Copy link**; `[Site] SendAttempts`, on).

**Site**: `POST /api/attempts` (nonce; a retry gets the same one),
`POST /api/attempts/<id>/checkpoints` (one per 20 s, 600 max),
`POST /api/attempts/<id>/log` (judged on arrival; never replaced - the
same text again is fine, another is 409), `GET /api/attempts/<id>` (the
public view: verdict, why, report), `GET .../log`,
`GET .../code/<code>` (where a code from the video shows: phase 3's
"check a code" box), `DELETE /api/admin/attempts/<id>` (owner only). Rate
limit `attempt`: 3000 an hour per address. Logs in
`<data>/attempts/<id>.log.gz`.

**Not yet**: the full 30 Hz `.run` of a finished attempt still goes up
as an ordinary run, not linked to the attempt.

## The report page (phase 3)

**`/attempt/<id>`** (`wwwroot/attempt.js`; the server adds the verdict to
the link preview, `Pages.AttemptSummary`): the verdict in plain words
(Checked / Partly checked / Problems found / In progress), the facts
(category, runner, spot, game mode, timer, real length, end, online,
plugin), a **check a code** box (where a code from the video shows in
the log, real time and timer), *What the site saw during the run* (the
receipt's lines) and *What ran in the game* (the report's findings),
the report as written behind a fold, and the log's download.

**The verdict = the worse of two halves**, worked out when the page is
read (so the allow-list applies to old attempts too):
- the receipt, `Attempts.Judge` (stored when the log arrives);
- the report, `Attempts.JudgeReport` (tested; reads the report with
  `RunReport.Parse`, linked into the site): an unknown or unreadable game
  hash, another mod / patcher / outside code, a foreign Harmony patch, a
  game cheat = red; no report or a hash still being read = amber;
  practice before the attempt = a note. Run mode's flags are judged by the
  receipt (they are in the chain), not again.

**Allowed mods** (`/admin/allowed`, any admin): every mod, patcher,
outside code and patch owner a report has named (`attempt_items`, filled
when a log arrives), with Allow / Stop allowing (`allowed_code`). The
entry is exact - a new version is a new entry; a mod and its Harmony
patches are two entries (a plugin's patches are listed by its Harmony
id). Logged in the activity log with the entry.

**A changed game, part by part (v0.24.217).** When Assembly-CSharp's file
hash is not a known build, `Game/RunIntegrity.HashTypes` hashes every
top-level type of the *loaded* game code on the hashing worker thread
(SHA-256 over the sorted lines `<type>::<method> <IL hash>` of every
declared method and constructor, nested types folded in, plus one
`<type> type` line per type; 16 hex). The report carries them as
`typehash = <type> <hash>` (~3,700 lines, only for a changed game; the
page leaves them out of the shown report). The site (`GameCode.cs`)
compares them with `GameCode/steam-types.txt` - the Steam build's table,
**written in game by the same code**: `call
static:ForestOverlay.Game.RunIntegrity WriteTypeHashes "<path>"` on a
clean install - and names the changed / added / missing parts by area
(`GameCode/areas.txt`, regexes on the type name, first match wins, case
counts), the type names behind a fold. The same code in a different file
(only resources differ) is amber. Fields are not hashed (a constant's
change shows in the IL that reads it). An older plugin's report without
type hashes stays "not the Steam game's", unnamed.

## Categories (phase 4, v0.24.218)

Decided with the author (2026-10-02):
- **Every option is customisable** by the moderators ("in case they want
  to host some experimental events"), the obvious ones locked by default.
  Each feature is *locked*, *runner's choice* or *forced on* (switches
  only - an action like Go can be allowed, never forced). The floor in
  *Decisions* ("state-changing features are always locked") is replaced
  by these defaults.
- **Categories follow speedrun.com's** ("as long as it follows the
  speedrun.com categories i don't care"): one per category x subcategory
  value (Any% - Normal, ...). A run spot's `run = ...` names its category
  (id or name) and wins; the Runs tab's pick is for Start run mode only.
- **speedrun.com sync**: daily + "Check speedrun.com now". New ones are
  drafts; a category nobody edited follows speedrun.com by itself; an
  edited one shows "speedrun.com changed this" (before / now) with Accept
  (name + rules taken, settings kept) or Keep ours; one removed there is
  marked (Accept hides it).
- **One role**: any admin edits categories, every save in the activity
  log (author: "i'll leave it up to you"; 5-10 active runners - admin /
  moderator / verifier roles are not worth it yet).
- **Banned moves are text** for now, shown on the attempt page; seeded
  from speedrun.com's "No ..." rule lines. The tech is researched
  (2026-10-03, *Banned moves: detection* below); detection not built yet.
- **Reload save on death** is a feature like the others (default:
  runner's choice). **Later** (author): the check that both reload paths
  give the same game as a manual reload, then circle back.
- A **Manhunt** preset draft (logs in the inventory, item caps, fast
  building forced on; multiplayer; no anti-splice) - "just do your best",
  the admins review configurations before real use.

How it works:
- `src/Data/RunCategory` (pure, tested, linked by the site): the text
  format, `Features` (key, label, switch or action, default, the practice
  mark reasons it covers), `GameMismatch`, `Find`, `Slug`.
- **Site** (`site/ForestSite/Categories.cs`): tables `categories` (current
  text + speedrun.com's last text + a pending change) and
  `category_versions` (every save); `GET /api/categories.txt` (published,
  the plugin's), `GET /api/categories/<id>/<version>`; admin `GET
  /api/admin/categories`, `PUT .../<id>` (one block; the site sets the
  version), `POST .../<id>/accept|dismiss`, `POST .../sync`. The sync
  runs a minute after start, then daily (`FOREST_SRC_SYNC=off` in tests).
  /admin's *Categories* tab edits them.
- **Judging** (`Attempts.Overall`, `Categories.Judge`): the report's
  `category = <id> v<n>` picks the version; the game must match
  (difficulty / Creative / multiplayer: red); features used are listed
  (allowed, or red if that version locks them); a category the site does
  not have is amber; **anti-splice off** = the receipt's amber (offline,
  gaps) is not judged, its red still is; **amber not accepted** turns
  amber red. The attempt page shows the version's rules, banned moves and
  unlocked features.
- **Plugin** (`Modules/RunModeModule.Categories`): fetched 6 s after
  startup and on *Check categories*, kept in
  `config/ForestOverlay/categories.txt`; picked with `<` `>` (config
  `RunMode.Category`). `Core/RunMode`: `Begin(..., category)`,
  `Refuse(feature, what)`, `Locks` / `Forces`, `Use` (an allowed
  feature's practice mark is recorded as used, not flagged). The report
  gains `category`, `difficulty`, `creative`, `multiplayer`, `used`.
  Anti-splice off: no code on screen (the chain and receipts still run).
  No category known: the defaults (everything locked, Reload save on
  death the runner's).

Done in v0.24.220 (author, 2026-10-02, after v0.24.219; confirmed over
the bridge against a local site):
- **Values, not only switches.** A manhunt host sets the numbers
  (sxczurass): the category carries the log cap and item caps (item by
  name -> max) under their features. *Forced on* applies the category's
  numbers and greys the Inventory tab's fields during the run; *runner's
  choice* keeps each runner's own. The host changes them by editing the
  category (a new version).
- **Refresh without a click** (author: "Check categories" may never be
  pressed; decided here - no push: Unity 5.6 has no cheap long-lived
  connection, and polling is negligible at 5-10 runners): the plugin
  re-checks `/api/categories.txt` every 2 minutes and right before each
  attempt starts, with an ETag (the site answers 304 when unchanged). A
  new version applies at the next attempt, never mid-attempt. The button
  stays as a manual refresh.
- How: `RunCategory.LogCap` / `ItemCaps` (`logcap = 8`, `cap Rock = 50`;
  names are the game's item names, matched case- and space-blind by
  `InventoryReader.IdForName`; a log in the caps is refused - logs have
  the log cap); `InventoryModule.ResolveRunCaps` (per category version,
  again once the item list is readable); `RunModeModule.Categories`
  (`RefreshEvery` 120 s, `CheckCategoriesSoon` on an attempt's end and a
  run spot's start, `_etag`; a change during a run is a notice + the Runs
  tab line "from the next attempt"); `WebRequest.Send` with headers and
  one response header back; the site's `Categories.ETag` / `Unchanged`.

## Banned moves: detection (bomb boost + huge speed v0.24.227-228, cave force load v0.24.229, fall damage cancel v0.24.230, lifts + clips v0.24.231-234)

How each common move works is in [`game-notes.md`](game-notes.md) *Speedrun
tech and the endgame gate*. Whether a move is a glitch is the community's
call; this is what the plugin could see. Each would be a run event in the
attempt log (time, place, numbers), shown on the attempt page and matched
against the category's banned moves - a flag for a verifier, never an
automatic reject.

| Move | What the plugin would watch | Certainty |
|---|---|---|
| Bomb boost (pause during a knockback) | postfix on `PlayerStats.Explosion(float,bool)` (knockback started: `isExplode` rose) + `HudGui.TogglePauseMenu(true)` within its 0.5 s of game time; log the seconds paused and the speed after | exact - the boost cannot happen without both |
| Any huge speed | the player's speed over ~150 m/s (a sprint is ~10, a knockback ~100) outside our own teleports / restores | exact as a number; says nothing about how |
| Cave state force load | `InACave` sent by `playerEnterCaveAction.doCave` while the player is not under the terrain (the game's own rule: `SampleHeight - y > 3`); also cave state above the terrain for > 2 s away from a cave mouth | high |
| Fall damage cancel | prefix on `FirstPersonCharacter.HandleLanded`: our own fastest downward speed over the last ~0.2 s > 28 m/s while the game's `prevVelocity` <= 28 (or > 3.8 s in the air and no death) | high for "a fast landing took no damage"; a long natural slide may show too - amber |
| Wall / axe clip | a line from the capsule's last centre to the new one (each FixedUpdate) crossing a static, non-trigger collider, outside known movers (our teleports, cutscenes, elevators, the death warp) | medium - needs a filter list built from real runs |
| Log boost | an upward speed spike not from a jump, explosion, ride or cutscene, with a log wall / structure within ~2 m | medium |

Cost: the two exact ones are two Harmony postfixes and a comparison; the
clip line is one `Physics.Linecast` per physics step. All read-only.
Order to build, if wanted: the bomb boost and huge-speed flags (exact,
cheap), the cave state flag, then fall damage, then clips / log boosts
with recorded runs to tune them.

**Built (v0.24.227-228, 2026-10-03)** - the first two rows, done better
than the table's plan:
- **Bomb boost**: not "explosion + pause menu within 0.5 s" but the
  mechanism itself - a prefix / postfix on the knockback coroutine's
  `MoveNext` (`Game/MoveWatch`) counts every push made while
  `Time.deltaTime` is 0 (both push phases, IL in the file). One stop of
  game time = one boost, reported after 1 s of game time with the seconds
  stopped, pause menu or not, the time since the blast, the frames piled
  up, the peak speed and the distance. Fewer than 4 piled pushes are not
  reported; a stop the player never comes back from (a load, the player
  gone) is dropped and logged.
- **Huge speed**: 200 m/s, not 150 (a plain knockback reaches ~260; a fall
  is capped by the game at 55.4 m/s, live). Both the rigidbody speed and
  the distance really covered over 0.1 s of game time; a frame moving
  further than the speed allows over a physics step is a teleport and
  restarts the window (v0.24.228: a tp while the body held 300 m/s was
  reported in v0.24.227); kinematic bodies skipped; excused from a
  knockback's first push until under 30 m/s for 1 s.
- **Where it goes**: `Data/MoveDetector` (pure, tested with the false-flag
  scenarios) -> `Modules/RunModeModule.Moves` -> a log line `Move seen:`
  always (practice too) and, during an attempt, a `move|ms|kind|x|y|z|text`
  line folded into the chain (`Data/AttemptChain`), listed in the Runs tab.
  **Never a flag** - the attempt stays valid; the site lists the moves on
  the attempt page (*Moves the game saw*), each with the category's banned
  move it may be (`Attempts.MoveNotes`: bomb / explosi / knockback in the
  rule's words), and leaves the verdict alone.
- **Live checks (v0.24.227, bridge, 288 fps)**: silent for a plain
  knockback (154 pushes), a 3 s pause with no knockback, a 3-push pause
  (below the line), a 1,050 m fall, death by explosion + Reload save on
  death, a forced 300 m/s that the game's air control held to ~78 m/s of
  real movement; reported: ESC 1.02 s 0.02 s after the blast (217 frames,
  1,904 m/s, 293 m), a late pause 0.62 s after (229 frames, 35 m - the
  late regime's hop, a real boost), a real 300 m/s with the controller
  off (146 m in 0.5 s). In run mode the move reached the attempt log and
  the live site's page (test attempt deleted). One false positive found
  and fixed (the teleport above).
- **Cave state force load (v0.24.229)**: not "InACave while above the
  terrain" but the entrance's own let-go - a prefix / postfix on
  `playerEnterCaveAction/<doCave>c__Iterator0::MoveNext` notes where an
  entry (`enter`) took hold and, when it returns false (the player let
  go; the Timmy goodbye hand-over skipped), logs `MoveWatch: a cave
  entrance let go of the player at ..., N m under the terrain, in cave
  state ...` and reports an entry ending in cave state no more than 3 m
  under the terrain. Survey (2026-10-03, bridge, every crawl and swim
  entrance): normal let-gos 7.5 m (Cave 2) to 300 m under; the mouths
  stand 0-3 m above. Live: normal entries silent; an entry cut short
  (`Animator.Rebind` after `InACave`) let the player go standing at the
  mouth in cave state - reported. The site's `MoveNotes` matches banned
  moves containing "cave". **Not built**: a general "cave state above the
  terrain" watch - the climb holes (Cave 9 ledge 2.1 m under), swim
  mouths (0.7 m above) and the sinkhole (terrain 0, cave state from y 0)
  sit inside its margin; it would need data from real runs. An entry the
  game never lets go of (killed coroutine) is not seen.
- **Fall damage cancel (v0.24.230)**: a prefix on
  `FirstPersonCharacter.HandleLanded` reads what the game is about to
  judge (`prevVelocity`, `allowFallDamage`, `jumpingTimer`, `jumpLand`,
  shell ride / glider over 32 m/s, `Clock.planecrash`, `swimming`);
  `MoveDetector.Landed` reports a landing that passes every gate but
  `prevVelocity` (<= 28) while the player really fell faster than 30 m/s
  within the last 0.25 s of game time (two half-window buckets fed every
  frame, plus the body's speed at the landing; kinematic frames clear
  them). The text gives the real speed, the judged one and the damage it
  would have been (fatal over 3.8 s in the air). `MoveWatch: a landing
  ...` is logged for every landing where the two speeds disagree, reported
  or not. Live (bridge): an 82 m drop onto the ground is judged at 55 m/s
  (silent); a drop into the big lake judged at 0 but swimming (silent);
  **steep terrain never builds speed** - the game holds the player on a
  55-80 degree slope (~3 m/s), so terrain slides are not a false-flag
  source; the cancel's game state faked (`prevVelocity` zeroed every frame
  of an 82 m fall) was reported (55 m/s, judged 0, 100 damage). 71
  landings seen, no other report. The site's `MoveNotes` matches "fall
  damage" / "fall cancel" / "slide cancel" (not "waterfall"). Not
  reproduced: the runners' own input (sliding on bodies, Cave 6) - the
  author's hands-on list.
- **Lifts out of a structure and clips (v0.24.231-234)**: `Game/ClipWatch`,
  a FixedUpdate on the plugin's own object (never a component on the
  player), reads the body capsule's centre each physics step; contacts come
  from the game's own collision proxies on the player (Harmony postfixes on
  `OnCollisionEnterProxy` / `OnCollisionExitProxy`) - the only way to see
  what the player really collides with, since the game unhooks pairs with
  `Physics.IgnoreCollision` in 50+ places and Unity 5.6 cannot read them
  back. One OverlapSphere + one raycast a step, nothing allocated.
  - **Lift** (log boost, custom wall boost): depenetration leaves no
    velocity (live: a box 0.8 m into the feet lifted the player 0.8 m at
    velocity 0), so `MoveDetector.PhysicsStep` sums the rise beyond what the
    vertical speed allows; reported at 1 m **only when a player-built
    structure was touched** in the episode (`BuildingHealth` /
    `BuildingHealthChunk` up the parents) - walking into the yacht cabin's
    bench lifts the 4.6 m capsule 1.2 m too (logged, not reported).
  - **Clip**: the line from the last place the capsule's centre was clear
    of every touched solid to the next one enters a solid through a front
    face (ending inside a rock counts; a face crossed from behind does not)
    - counted only within 1.5 s of one of the runners' two ways in (author,
    2026-10-03): an **axe ground smash** (`playerAnimatorControl
    .doingGroundChop`, ~1.5 s per smash, read through a DynamicMethod
    getter; the text says whether crouch was released) or **a structure
    they built** touched (a log / stone wall squeezing them into thin rock,
    the keycard cave clip in true any%). Other crossings are logged
    (`Move watch: a crossing, not reported`).
  - Never counted: 0.5 s after any teleport (the player is set down
    overlapping things and pushed out), kinematic steps, a solid that moved
    in the last second (the yacht's hull bobs on a kinematic body), terrain,
    triggers, moving bodies.
  - **Live (bridge)**: silent - the red elevator car tp, ride and walk out,
    sprint + jumps on rough ground, scraping a wall with jumps, the plane
    wreck, Cave 6, walking the yacht and into its cabin bench. Reported - a
    leaf hut's collider moved 1.2 m into the feet ("out of a structure they
    built ('LeafHutBuilt(Clone)')"), a 0.1 m wall with the centre set past
    its middle 0.15 s after a real ground smash ("not standing up from a
    crouch"); the same without a smash only logged, and set short of the
    middle silent. Not reproduced: the runners' own inputs (a panel /
    elevator smash clip, a log boost, the keycard cave wall clip) - the
    author's hands-on list. The site matches "clip" and "log boost" /
    "wall boost" in a category's banned moves.
- **Next**: the detected moves done for real (the author's hands-on list).

## Other uses of locked settings

- **Manhunt** (sxczurass, QA 2026-10-02: two players finish the game while
  four hunt them, with logs in the inventory and other inventory mods,
  locked so nobody changes them mid-game). Author, 2026-10-02: a community
  spot configured with the settings - the category settings of phase 4
  cover it once they exist; no separate mode.

## Known gaps (phase 1)

- The game's debug console has `_timescale`, `_speedyrun` and more
  (game-notes *The game ships a debug console*). The report flags
  `Cheats.DebugConsole` being on, but not what was typed into it. A
  `Time.timeScale` watch could be added.
- A bridge `go` / `restart` refused by run mode still answers `ok`.
- The report reads the game's file on disk at startup, not the loaded
  assembly. Per-type hashes (phase 3) can hash the loaded code instead.
- BepInEx's own patches on .NET methods are skipped by assembly (mscorlib,
  System*, Mono.*), whoever owns them.

## Phases

1. **Run mode, done in v0.24.206-209.** It was confirmed over the bridge
   (`docs/confirmed.md`); the author has not yet tried it by hand.
   - `Core/RunMode`: the lock. Entry points call `Ctx.Run.Refuse(what)`;
     gameplay switches read `Ctx.Run.Active`.
   - `Modules/RunModeModule`: starts an attempt on the load edge when
     `GameSetup.IsNewGame`; draws its section at the top of the Runs tab.
   - `Game/RunIntegrity` + `Data/RunReport` (tested): the report, written to
     `config/ForestOverlay/run-reports/attempt-<time>.txt`.
   - Locked: Go, F7 / Restart, savestate capture and restore, freecam,
     aerial capture, death revive, god mode, no blood / stagger, item caps,
     logs in the inventory, fast building, the experimental performance
     patches. The test bridge stays usable but flags the attempt.
   - The report covers: the Assembly-CSharp hash against the Steam build,
     other BepInEx plugins and patchers, code loaded from outside the game,
     Harmony patches with a foreign owner, and the game's `Cheats` statics
     (GodMode, InfiniteEnergy, NoSurvival, UnlimitedHairspray, DebugConsole).
     Re-read every second.
2. **Codes and receipts, built in v0.24.216** (see *Codes and receipts*
   below).
   - A server nonce when the attempt starts, and a hash chain over the nonce,
     the IGT and positions.
   - A code changing about once a second, shown beside the timer.
   - A checkpoint POST about once a minute; a receipt on every reset; the full
     log on a finish.
   - An outbox queue with links (offline).
3. **The report page** on the site, built in v0.24.217 (see *The report
   page* above).
   - Green / amber / red, in plain words, plus a "check a code" box.
   - On a hash mismatch, per-type hashes, compared on the server against a
     table built from the real game files and mapped to areas a runner
     understands.
   - Moderators can allow known-harmless mods.
4. **Categories on /admin, built in v0.24.218** (see *Categories* above).
