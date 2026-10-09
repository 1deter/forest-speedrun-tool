# Decisions

The author's decisions in one place: what, who, when, and the why where it
was given. A decision made with the author is written here in the same
session, with "(author, date)". Area docs and the router link here instead
of repeating them. Decisions kept elsewhere on purpose: run mode's phases
and rules in [`docs/run-mode.md`](run-mode.md) (the knowledge bot reads that
file), the working rules (handoff, subagents, switching sessions, docs) in
[`docs/areas/workflow.md`](areas/workflow.md), and the harness's in
[`docs/harness.md`](harness.md) *Decisions*.

## Project direction

### Current phase: explore the capability envelope

**Legality enforcement is explicitly not the priority.** The speedrun.com
moderators have not been asked; the plan is to hand them a working tool so they
can judge concretely. Do not gate, disable or refuse a feature because it
*might* be ruled illegal.

Keep the honest **labelling** though — `IsPracticeOnly`, the sticky HUD marker,
the info-only/state-altering split. It costs nothing and makes that
conversation concrete. Once there is a ruling, circle back and enforce it.

Flower/plant coordinate display is **out of scope by the author's own call**.

### The tool first (author, 2026-10-08)

"Focus development on the tool rather than anything external to it, as i want
to get the tool done ASAP." `tasks.py next` takes plugin, release and research
tasks before site, bot and knowledge ones, whatever their priority
(`FOCUS_AREAS` in `scripts/tasks.py`). The author still picks external work
when they want it. **Compounding work first** (author, 2026-10-09): "work
that will compound and provide value to lesser tasks should be prioritised
first" - `next` takes harness tasks ahead of the tool (`FIRST_AREAS`).

### Conventions

- **Data, not code.** Locations, segments and the 100% checklist are text files
  so they can be shared, diffed and edited by non-programmers. Anything
  admin-decided or community-contributed belongs in a file.
- **Everything must be editable in the GUI.** The text formats exist for
  sharing, not as the interface. A data-driven feature without an editor is
  not finished.
- **Segment ids are hidden, random keys** (author, 2026-09-26: runners
  never need them; v0.24.74): `s-` + 12 hex digits, made on New /
  Duplicate / F6, never shown (no Id field; logs and files keep them).
  The id groups an entry's attempts; the **route fingerprint** (zones +
  start state hash) decides which of them compare - a new start state
  retires old times and lines. The same id means the same original (an
  import, a community pack); a Duplicate is a fork with its own times.
  Old entries keep their old ids (`spot.my.new-spot-3`, shared by many
  runners' first spots) - give one a fresh id before publishing it.
- **Leaderboards are comparative, not competitive** — lines and ghosts, no
  verified ranking, so client-submitted times need no anti-cheat story.
- **The in-game timer aims to replace LiveSplit**, not sit beside it.

## Working with the author

- **The author runs medium effort**; say when a task needs high (memory
  `effort-level-switching`) - with the author present, pause the turn for
  the switch instead of a one-liner they can miss (author, 2026-10-07). The bridge makes fixes fast: reproduce live
  before and after a fix, and prefer a live read over an IL theory
  (gotcha 25).
- **Game data is disposable** (author, 2026-09-25: "i'm the tool dev
  after all"): closing or killing the game with unsaved progress, and
  deleting anything in the author's game or save slots, is fine while
  building or testing. Courtesy (author: "quality of life"): back up a
  slot before a test changes it (`SlotN.deter-backup`) and put it back,
  sizes checked. Still: never deploy a DLL by hand; testers' saves in
  Downloads are theirs to keep.
- **No guessing** (author, 2026-10-06): minimise guesses to ideally
  zero. If something is not obvious from the context and instructions -
  intent, a design choice, a runner-facing word, scope - **ask**; facts
  the code / docs / logs / bridge can settle are looked up, not asked.
  Unattended work parks the task with the question written out
  (docs/harness.md *Ground rule*); new features are built with the
  author present, and questions are welcome (they can spark ideas).

- **The QA to-do list holds only what testers still have to do**
  (author, 2026-10-07, T-0007): rendered from the task file (`tasks.py
  qa-todo`: `needs: tester` tasks with a `qa` line), no Done / Later /
  Decided sections ("it clogs the channel"). Testers get only what a
  session cannot do or easily do over the bridge; the rest is a `needs:
  bridge` task for automated checks. Each item says what to do and what
  to expect - past lists asked for things impossible in game, were vague
  about the expected result, or re-asked what a session had already
  confirmed.

## People and saves

- **Saves:** every slot is the author's own; Steam Cloud is off. maks's
  saves: Megan in `C:\Users\deter\Downloads\Slot4`, lab / invisible
  section / red elevator in `C:\Users\deter\Downloads\slot5` (starts
  ~840 m from the red elevator; no gold keycard needed - a game bug).
  Swap only with the game closed or at the title screen: rename the
  author's slot to `SlotN.deter-backup`, copy maks's in, afterwards
  delete it and rename the backup back.
- **QA team** (author, 2026-09-25): ~3 runners (maks among them) test
  features and what the author cannot easily do. Lists go to the team,
  kept light (volunteers; never what the author or the bridge
  confirmed), as a dated `qa/*.txt` (the QA tab shows the newest) plus
  its `docs/tests/` file, **saved verbatim with what each item checks**;
  sent in a plain-text code block numbered `1)` (memory
  `tester-lists-plain-text`). Answers come numbered against the list, per
  tester. First general list:
  [`docs/tests/2026-09-25-qa-v0.24.43.md`](tests/2026-09-25-qa-v0.24.43.md)
  (sxczurass answered 1-5; posted in #general as the QA tab's text on
  2026-09-26, message `1553230858498998286`, so the to-do list can link it). maks's older list
  [`docs/tests/2026-09-24-maks-v0.24.34.md`](tests/2026-09-24-maks-v0.24.34.md)
  numbers 1-6 swing / smash cut, 7 nature guide dump, 8 perf (the QA list
  repeats them as 1-5, 15, 19). Delete a file once its answers are dealt
  with. Never let runners run bridge scripts.

## Plugin

- **Developer tab and settings that never contradict** (author, 2026-10-08,
  T-0226): developer-only = the bridge, memory census, benchmarks and the
  experimental features runners would not risk a run on - they live in the
  last tab, *Developer*. The behaviour-preserving performance patches are
  *not* developer-only (Settings > Performance). Settings is a tab of folds,
  "clear and easy to navigate". The QA tab stays while the tool is in
  development and **goes at the public release**. Two options that
  contradict can never both be on: one control per setting, a choice of
  one (radio), or picking one disables the other (the Deaths tab's
  *Reload save on death* toggle went for this). The Info box and the
  legacy `locations/*.txt` import stay.
- **A quick load during the game's own death cleans it up in place**
  (author, 2026-10-09, T-0248): F7 / a restart / a Go while the death
  plays (the fall, the drag-away, the capture's hanging, the dead cam)
  stops it and undoes its leftovers by hand, keeping the fast restore -
  not a Full load instead; only a death the cleanup cannot end falls
  back to a Full load (a savestate / spot restore; the Deaths tab's slot
  reload in place and Go have no file to load and only log it). The death count a load gives (0) applies to every
  restore in place.
- **Deleting a spot here never deletes it online** (author, 2026-10-09,
  T-0265): the local Delete and *Delete from the website* are separate;
  the owner can add their spot back from the site, edit it and upload
  the changes over it.
- **Look: yellow on black** (author, 2026-10-09): the plugin's window and
  HUD follow the original game's art style - The Forest's yellow on black,
  the same palette as the site (*Site: Look*: the loading screen's yellow
  `rgb(229, 197, 1)` on black). This replaces the redesign draft's
  blue-grey panels and its green accent (`docs/ui-redesign.md`); T-0258
  moves the branch over.
- **Easy to learn, little at once** (author, 2026-10-09, after T-0240):
  the goal is a tool that looks good and is easy to learn without
  flooding a new runner. Less on screen, fewer things to click, is faster
  to learn. Rules on top of the 2026-09-23 / 10-05 ones (one button one
  job, name + toggle with the description on hover, no clipped text):
  - **Settings stay functionally separate.** A setting that serves two
    features is split into one per feature (or a copy of the control
    under each); controls that serve the whole HUD sit together as
    general HUD options. A child setting is not hidden while it still
    does something elsewhere - hidden-but-active settings confuse.
  - **Pictures over instructions**: instructional text a runner must read,
    match up and apply (the Map tab's lines) becomes a legend or a visual
    on the thing itself.
  - **Automatic unless it can lose work**: lists refresh by themselves
    (local ones on change or when the tab opens; site / community ones
    when the tab opens, rate-limited); settings already save on change.
    Anything that creates, overwrites or deletes keeps an explicit button,
    with a confirm or undo when it destroys - nothing saves by accident.
    The author may revise once it is built.
  - **Advanced as a per-tab toggle** (author, 2026-10-09): a tab shows the
    common options; its advanced toggle switches to a fuller version of the
    same menu that is designed and organised in its own right - never the
    old cluttered layout brought back.
  - **One journey, one place**: starting a timed segment today takes the
    Practice tab (spot, then the savestate list on its right), then the
    Runs tab (practice mode on, Restart to arm) - the author's example of
    what to fix. On-screen panels resize and move by dragging, not +/-
    buttons (the splits panel), as modern apps do.
  - A UX reviewer agent (`forest-ux`, T-0252) checks every redesign task
    and every feature that adds much UI; the author's rules are a floor,
    not the whole list - it brings in established UX guidance.
- **Performance switches need no sign-off** (author, 2026-10-08; the
  admins agree): a switch that helps and is tested not to change any game
  mechanic or logic runners rely on for fair, consistent timing ships on
  by default. Off-limits only if the same run would come out faster with
  the tool. Load-time cuts are always fine (they make the game more
  consistent for everyone). Looks (e.g. how particles draw) are not a
  mechanic.
- **Runner spots keep their start state on the site** (author,
  2026-10-08, T-0194): whether a spot uses one is the creator's choice.
  **Only the spot's owner** gives a route its state (author, 2026-10-08:
  a new state = a new route, so the owner updates it by uploading again;
  admins keep only the site moderation - deleting a spot drops its
  states; no separate "delete a state" for admins).
- **Naming** (author, v0.24.27, UI only - config keys and log lines
  unchanged): **Quick load** = restore in place, **Full load** = with a
  scene load; the death option is **Reload save on death**. Plan: polish
  Quick load to parity, keep Full load as the escape hatch. **Quick load
  is the preferred, default restore** (author, 2026-09-26): a runner's
  report about "savestates" means Quick load unless it says otherwise.
- **Decided:** a Quick load gives back the capture, not what a Full load
  does where the save is silent (author, 2026-09-25: "if a bush is cut
  and it was saved that way, then the savestate should respect that";
  gotcha 35). Greebles likewise, restore-only - normal play untouched
  (author, 2026-09-25). In Creative with "Allow enemies" off (or
  Peaceful), respect the game's state - no enemies spawned (2026-09-24).
  Runners never see segment ids (2026-09-26, *Conventions*). Community
  entries show under one "Community" category for now - sub-categories
  maybe later; the packs keep their own category (2026-09-26). Box yaw
  yes, tilt no ("probably more of a gimmick", 2026-09-26). The website
  (forest.deter.cloud) will be built by Claude and reads `.foseg` files
  ("do whatever's easiest", 2026-09-25).
- **Performance patches (author, 2026-09-26):** behaviour-preserving
  ones ship on by default; anything that changes the game (timing a
  runner can feel, what can happen during a load) may still ship, but
  **off by default under a clearly labelled "Experimental /
  gameplay-altering" section** of the Performance patches, and only when
  it genuinely improves performance or playability. "True to the game"
  is the default; the label is the rule when it is not. The author
  prefers **direct patches** over tuning settings (a settings sweep
  only "if it's light on usage").
- **Splits and comparisons** (author, 2026-09-27): a LiveSplit-style splits
  table on screen (movable, F5 hides) + in the Runs tab; every LiveSplit
  column, each toggleable; one comparison setting (the Runs tab's Compare
  to, + best segments) drives table, delta, ghost and lines. Attempts get
  their split times saved and a runner identity: **the Steam name by
  default** (editable; new users would never set one), keyed on a stable
  id so a rename does not split one runner in two. Other runners' attempts
  (`.foseg`, community, later the website) are comparisons only, never in
  your PB / golds; only the same route compares.
  The id is a **hash of the Steam id** (a plain one links to the Steam
  profile from public files); non-Steam copies get a random id. Community
  spots: **approved by the author for now**, runner-managed and hands-off
  later. Run uploads: off until the website is live, **automatic** after
  (author, 2026-09-27). Spots stay curated in the repo; runs go to the site.
- **Teleports and the endgame** (author, 2026-10-03): Go / tp behave
  like the game's own developer-console teleport - if the console does not
  load the endgame there, neither do we (a Go into the lab from a save
  without it loaded still falls through; that is the console's behaviour).
  Exceptions only for what would bug / break (ending a ride or rope in
  flight, the areas left behind); **never restore elevators or other
  savestate state on a plain teleport** - "it sort of bleeds savestate
  functionality into a teleport". A ride under way is stopped on Go / tp
  (ElevatorKeeper.StopRides), nothing is put back. The console's `goto
  <target>` fires the endgame box's crossing within 150 m of it (sets the
  endgame flag, never loads the lab without the door) - our Go keeps / sets
  the flag in the vault entrance, the same in effect (game-notes *Speedrun
  tech*).
- **Dropped:** the stats-only start state (author, 2026-09-25:
  "over-engineering what we currently have with quick and full load
  savestates") - do not propose it again.

## Run mode

- **Run mode and anti-cheat (author, 2026-10-02)**: the tool should be
  usable in real runs, with nothing asked of new runners but installing
  it and no extra work for verifiers. Runs start from **preset category
  saves** (community spots), not new games; run mode **locks** practice
  (Reload save on death stays); the title screen is a reset; anti-splice codes
  beside the timer; a receipt for every attempt (resets too) uploaded by
  default (~200 GB server, fine); reports public, never editable; offline
  runs amber; changed game code named by area in plain words; categories
  defined by the moderators on /admin, seeded from speedrun.com's rules.
  Nothing relies on secrecy (open source). Full list and phases:
  [`docs/run-mode.md`](run-mode.md).

## Site (forest.deter.cloud)

Built by Claude (author, 2026-09-25: "do whatever's easiest"); started
after *Next up* 10 (author, 2026-09-27: "get everything done and start
working on the site - i'm pretty excited to see what can be done").

### Decided with the author (2026-09-25 / 27)

- **Spots stay curated in the repo, runs go to the site.** Community
  spots (`community/*.foseg` + `index.txt`) are approved by the author for
  now; later runner-managed and hands-off. The site should be able to
  take a *spot submission* (a `.foseg`) for the author to approve, since
  runners will not open pull requests.
- **Run uploads:** off until the site is live, then **automatic** (author:
  "once the site is live i don't see a reason not to submit the runs
  automatically"). The plugin then fetches each runner's PB for the
  current spot as split comparisons (the plugin side: attempts from other
  runners kept apart from the runner's own, per-runner PB - not built yet;
  `SplitTable` takes any `float[]` comparison already).
- **Identity:** runners are keyed on `RunnerId`, shown by their newest
  `RunnerName`. The id is `r-` + 16 hex digits of SHA-256 over
  `forestoverlay-runner|steam:<steamid64>` (`Game/RunnerIdentity`) - a
  plain Steam id would link to the profile from public files. A rename
  must never split one runner in two: show the name from their newest run.
  Non-Steam copies carry a random `r-` id from the config.
- **Only the same route compares:** key runs on segment id + route
  fingerprint (`Segment.RouteFingerprint`). A spot edited after publishing
  gets a new fingerprint; old runs show under "older version", never mixed.
- The site can serve the community index as a second URL
  (`Community.Url` in the plugin config, `Modules/CommunityModule`).

### Decided at the start of the build (2026-09-27)

- **Hosting (author):** the author's Oracle free-tier VPS (aarch64, Ubuntu
  minimal), Cloudflare in front (SSL Full), Caddy in Docker (`~/website`,
  network `caddy-navidrome`) as for deter.cloud and music.deter.cloud.
- **Stack (Claude, author: "up to you"):** ASP.NET Core (.NET 10) + SQLite
  + plain HTML / JS, one process, in the stock `aspnet:10.0` container on
  Caddy's network (`reverse_proxy forest-site:8080`, no host port). The
  server **links the plugin's own pure files** (`AttemptFormat`,
  `SplitTable`, `SegmentFormat`, `SegmentBundle`, with the tests' Unity
  shim) - one parser and one splits maths for game and site.
- **Repo (author: `site/` is fine):** `site/` in this repo.
- **Deploys (author asked for automatic):** `.github/workflows/site.yml` -
  test, publish linux-arm64, tarball over SSH to `site/deploy/deploy.sh`
  (a forced command: the key can run nothing else), container restart,
  live check. One-time setup: `site/deploy/README.md`.
- **Uploads:** a token per install (`POST /api/register`, first
  registration owns the runner id; the author can reset a token). Rate
  limits per IP (`CF-Connecting-IP`) and token, 4 MB bodies. A time under
  0.8 x the route's best (3+ runs) is flagged "under review".
- **First version (author agreed):** browse spots, upload / view runs per
  spot and route, per-runner best with splits, a 2D map with a scrub bar.
  3D terrain after.
- **Look (author):** minimalist, intuitive, The Forest's loading screen:
  its progress bar's yellow `rgb(229, 197, 1)` on black, `#222` backing,
  Montserrat (read from `HUD_Ngui/LoadCam` over the bridge). The logo's
  yellow is the same (229, 197, 0, from the author's cover art); its look
  is echoed with Anton (heavy condensed, uppercase) for the wordmark and
  titles - **not the logo image itself**, plus a "not affiliated with
  Endnight" footer (Claude's advice, 2026-09-27).

## Knowledge bot

Plan and design: [`docs/knowledge-bot.md`](knowledge-bot.md).

- **Audience: the wider runner community**, not only the QA team -
  "understand complex mechanics exhaustively like bomb boosts, axe
  clips, and their deep technical reasoning and why they work and what
  an optimal version of this tech would look like". **Answers of the
  highest quality, so a runner ends with full understanding.**
- **Answer from proof, or say it is not known** (author, 2026-10-08): the
  bot either has the fact - backed by research or code that proves it -
  and answers, or it does not, says so, and the question goes to the
  research queue. No half-answers: "otherwise it's going to be spitting
  out half-answers and the learning process is going to take a lot
  longer". Inferring from the game's code is fine. Answers are clean,
  concise and informative, a short breakdown a runner can follow up on,
  not one mega-message.
  Not known: something "nearby" is shared only when it is relevant and
  benefits the question asked. A possible cause for something unexplained
  only from code, and only when it is certain the code is related to that
  context - otherwise none (author, 2026-10-08).
- **Tone follows the community** (author, 2026-10-08): once the bot learns
  from the Discord history, informal knowledge (inside jokes, memes about
  and in the community) is written and usable; when the context is right
  the bot is "memey", laid back and banters about community members.
  A banter-style question ("when will deter run the game?", queue #42)
  gets a banter-y answer built from what the bot has learned on the
  server about the person asking and the person asked about (author,
  2026-10-08; T-0238).
- **Follow-up questions about previous answers** (author): a runner
  replies to an answer and the bot carries on the conversation.
- **A regular bot** (author: "i would really just prefer a regular bot"):
  a gateway bot, not an interactions-only endpoint. **No public knowledge
  pages on forest.deter.cloud** - "people won't really be using the site
  all that much as the discord". Feedback lives on the bot's answers.
  No `/about` command.
- **Runtime on the Gemini API free tier** - operational cost ~0. The
  author's two Claude Pro plans (our sessions) build the knowledge base
  and the tools.
- **A new Discord application** for it (not the QA bot's).
- **The Gemini API key**: the author creates it (Google AI Studio) when
  the bot is built; store it as a User environment variable like the
  others, never printed.
- **Retrieval is local; the model only writes** (author asked "is there a
  better free model?", 2026-10-03; Claude's call, agreed): search runs on
  the VPS (SQLite FTS5 + the bge-small embedding model through ONNX) - free,
  private, no quota. The writer: **Gemini Flash first, Mistral's free
  "Experiment" tier as the fallback** (any OpenAI-compatible provider is a
  config line); the test questions decide the order with scores.
- **The game's code, decompiled to C#, is kept privately on the server
  and quoted freely in answers** (author: "i'm not distributing it, i'm
  simply describing its functionality ... you don't need to limit how
  much you quote"). It is never served as files or made downloadable.
- **A private copy of the game's Assembly-CSharp.dll on the server** is
  fine (author, 2026-10-03: "as long as it's not being served and just
  used as an informational lookup ... for educating speedrunners").
- **CI evals the bot on the live key, warn-only** (author, 2026-10-07,
  T-0011): a separate key would share the same per-project quota, so
  bot.yml runs a small subset on the live bot's free key after each
  deploy; a low score warns and never holds back a deploy.
- **A paid model is a later call** (author, 2026-10-07: "happy to put
  forward some money ... however first i want the knowledge to be more
  polished with research sessions"); cheap OpenAI-compatible models are
  the candidates. Planned bot reviews: T-0141; runners' reactions in the
  knowledge-testing channel feed them (T-0140).
- **The bot runs on DeepSeek, paid** (author, 2026-10-08, tired of the
  Gemini Flash limits): `deepseek:deepseek-chat@https://api.deepseek.com/v1`
  first in the VPS `.env`'s `FOREST_BOT_MODELS`, Gemini after as the
  fallback; the key is `DEEPSEEK_API_KEY` (VPS `.env` + a GitHub secret).
  **Don't drain it** (author: "make sure you don't spam the bot api"):
  the automatic subset eval after each push stays on free Gemini; the
  manual eval (bot.yml `eval-manual`) runs on DeepSeek only, and only the
  full set once per bot review or named ids once per task fix (a second
  run of the same ids needs a change in between) - never in a loop, never
  "to see if it is stable"; no local live-model runs (bot/CLAUDE.md).
- **Bot tab channels: a save with every box unticked = the bot answers
  in no channel** (author, 2026-10-07, T-0028: "Answer in no channel");
  DMs follow the DM toggle. Settings never saved on the site keep the
  `.env` channels (`FOREST_BOT_CHANNELS`).
- **The CI eval subset carries the answer-length check** (author,
  2026-10-07, T-0090: "Add to CI subset"): short-bomb-fps (with its
  "tell me more" follow-up) and one more short item join bot.yml's
  `EVAL_SUBSET`; still no local live model runs (bot/CLAUDE.md).
- **Bot review (T-0141): the full eval runs on CI by hand, the review is
  triggered by the session-start report** (author, 2026-10-07: "CI, manual
  trigger", "Session-start line"). A `workflow_dispatch` input on bot.yml
  runs the full set or a named list; local live-model runs stay off
  (bot/CLAUDE.md). The session-start report counts new 👎 / partial queue
  items and uses of the bot in knowledge-testing (`/ask` answers, mentions,
  replies to it - not runners chatting: author, 2026-10-08, three reviews
  flagged by chat) since the last review. **Sizes (author, 2026-10-09):**
  new feedback is a quick pass (queue + channel, file tasks, move the mark;
  no eval, no report) and never a `!` problem ahead of plugin work; the full
  eval + report only when knowledge/ or bot/ changed since the last eval and
  7 days passed, or when the author asks (five full reviews on 2026-10-08,
  each 20-60 min of eval wait). Runners
  mostly `/ask` in the speedrun server's general chat, which the bot cannot
  read (its admins have to grant the permission - author, 2026-10-08), so
  the answer log is the better signal (T-0220).
- **Scope: any Forest-related question** (author, 2026-10-07): "it can
  answer any forest-related question, whether it's tech, routing, or
  information about a specific runner" - everything around speedrunning
  the game, the community and its people included. The author will feed
  context from the Discord channels to fill out the community side.
- **Spots: their author's rules** (author, 2026-10-07, T-0152 / T-0153).
  A downloaded or community spot is **read-only** for everyone but its
  author; a runner can still add their own checkpoints on top, for their
  own sub-segment timings. The spot's author can **set settings per spot**
  that hold while it is run, forced off (e.g. debug colliders) or forced
  on: "it's their spot, their rules", so everyone runs it on the same
  playing field.
