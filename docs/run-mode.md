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
mode's flags as they happen) and `end` (reason, final timer). Every line
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
  from speedrun.com's "No ..." rule lines. **Later** (author): research
  the runners' tech (what the community calls a glitch, what is in
  between) and detect what can be detected.
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

Next (author, 2026-10-02, after v0.24.219):
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
