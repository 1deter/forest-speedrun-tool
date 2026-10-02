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
2. **Codes and receipts.**
   - A server nonce when the attempt starts, and a hash chain over the nonce,
     the IGT and positions.
   - A code changing about once a second, shown beside the timer.
   - A checkpoint POST about once a minute; a receipt on every reset; the full
     log on a finish.
   - An outbox queue with links (offline).
3. **The report page** on the site.
   - Green / amber / red, in plain words, plus a "check a code" box.
   - On a hash mismatch, per-type hashes, compared on the server against a
     table built from the real game files and mapped to areas a runner
     understands.
   - Moderators can allow known-harmless mods.
4. **Categories on /admin**: the plugin fetches them; the runner picks one in
   the Runs tab; each report records the category version.
