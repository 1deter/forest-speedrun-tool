# Area: the plugin - key concepts

How the plugin's features behave and why: spots, triggers, split order,
endgame events, deaths, savestates, sharing, runs, loads. Read the
concept before changing its feature. Where the code lives:
[`plugin.md`](plugin.md) *Where things live*. Restore detail:
[`docs/savestates.md`](../savestates.md).

- **Spots and segments are one thing.** Every Practice entry is somewhere to
  teleport; tick "Timed segment" and it gains start/end triggers and
  checkpoints. There is no separate "anchor". **F7 restarts the *current*
  spot** (the last one teleported to or captured on), not the editor's
  selection.
- **One button, one job** (author, v0.22.0: "buttons shouldn't have
  double-purposes"). **Go only teleports**, start state or not. Restoring
  is **Restart**: F7, the Runs tab's Restart, a death revive, and the
  *Restart* button on the editor's Start state row.
- **Triggers** (`zone`, `box`, `item`, `event`, `manual`) are the spine —
  splits, segment bounds and eventually autosplits are all "a trigger fired".
  Item triggers can be **relative** (`+3` = three more than at the start).
  `event` triggers fire from `Game/GameEvents`; the segment editor steps
  through the known names with `<` `>`.
- **Split order** (`Data/SplitSequence`, v0.22.7, tested): the start fires
  on *crossing*; checkpoints fire **in order**, and **the end only once all
  have fired** (reached early, it says which checkpoint is missing; F12
  skips one). A checkpoint that becomes current because the previous one
  fired **fires at once if it already holds** (keycard in the bag, standing
  in its zone). The one prime kept: a *zone* that is current when the clock
  starts must be entered (a loop route's end is its start zone).
- **Endgame events**, route order: `vault-door`, `timmy-pickup`,
  `megan-transform` (approaching Megan), `megan-pickup`, `megan-to-machine`
  (Megan into the artifact), `gold-door`, `red-elevator`, `game-end`
  (`end-crash` / `end-shutdown`). Also `endgame-cutscene` (every cutscene —
  the old autosplitter's behaviour), `keycard-door`, `keycard-door-<itemId>`,
  `timmy-goodbye`, `raft-out-of-world`. Each fires on the rising edge of
  `endGameCutScene`, so split times are **frame-identical to the LiveSplit
  autosplitter**; the Harmony postfix only says which cutscene it was.
  The rest of the autosplitter (v0.24.184, `Game/WorldEvents`):
  `cave-enter-<cave>` / `cave-exit-<cave>` (cave01..cave10, hellcave,
  snowcave, underwatercave, underwatercave2/3) and `cave-enter` /
  `cave-exit`, `clothing-<id>`, `passenger-<n>` / `passenger`,
  `hold-interact` (plane meal start), `moving` (velocity start).
- **Deaths** (Deaths tab), decided in `DeathModule.Decide`:
  1. a current spot **with a start state** → revive and restore it, the
     segment's way, **even with practice mode off** (author: "if the runner
     wants to practice with a save state then it should reload entirely on
     death"; a load-mode state = a full load per death);
  2. practice mode on + a current spot → **revive** at the spot (health
     100, blood cleared, no reload, marks practice);
  3. otherwise **Reload save on death** (was "quick-load"; toggle, on;
     config keys still `QuickLoad*`) — every death, the first-death
     capture and the boss-fight wake-up each with their own toggle (both on;
     the boss toggle is the author's call). Never permadeath or multiplayer.
  The reload **skips the title screen** by default (`QuickLoadSkipMenu`,
  author: "faster load with no compromise") via `LevelSerializer.Resume()`,
  falling back to the menu path. The author rules it **allowed in
  normal runs**: it is the game's own load of the same save.
  A revive from a fatal hard landing cancels the landing's aftermath (a
  postfix on `FirstPersonCharacter.HandleLanded`; game-notes *Deaths*).
- **Savestates** (no tab: Practice start states + the bridge; practice-only):
  the game's own level serialization to `config/ForestOverlay/savestates/*.fosave`,
  never a save slot. **Quick load** = in place (`LoadNow` + the keepers put back
  what the save misses), **Full load** = `LoadSavedLevel` + fix-ups after the load;
  works across saves (`AdoptPlayer`), across Creative / survival and from the title screen as a Full load (v0.24.211-212); Quick load refused at the
  title screen. What each keeper restores, by version:
  [`docs/savestates.md`](../savestates.md) - read it before touching a restore.
- **Segment start states** (Practice editor, *Start state* row: Capture /
  Delete / Restart): a savestate at
  `savestates/segments/<safe segment id>.fosave`, restored on every restart
  before the usual teleport (which then skips its terrain-based cave guess).
  **In place by default; `restore = load` on the segment** for the full
  reset (author: fastest by default, the validated method as the
  alternative). Capturing moves the spawn to where you stand, makes the
  segment current and **saves the segment**. No start state = the restart
  keeps the game as it is (routes need that). **A new start state is a new
  route** (author's call): capture writes `startstate = <FNV hash of the
  data>`, folded into `RouteFingerprint()` only when set, so old times
  retire as when a zone moves; Capture and Delete ask for a second click
  when attempts would be retired (`AttemptStore.CountOnRoute`). A restart
  whose file does not match the hash logs a warning. Each restart logs
  `Restart '<id>': ...`; a refused or failed one still teleports and says
  why under the buttons, or — window closed — in `Ctx.Notice`.
- **Sharing and community packs** (v0.24.71-72): a `.foseg` file is one
  segment: `[segment]` + optional `[startstate]` (.fosave verbatim) +
  `[attempt]`s (.run verbatim) - `Data/SegmentBundle`, tested. Export
  (Share row) writes `config/ForestOverlay/shared/<name>.foseg`; Import
  lists that folder; the same id = the same original (a second click
  replaces). Community packs: the repo's `community/*.foseg` +
  `index.txt` (hash per file, `scripts/community-index.py`; CI checks
  it), fetched from raw.githubusercontent 5 s after startup and on
  *Check community now*; written to `segments/community.txt`, shown
  under **Community**, read-only (Duplicate = own copy, start state
  included); the runner's ids win; attempts ignored.
- **Runs** record position at 30 Hz and ~60 named player-state channels at
  5 Hz (read only when a sample is due), discovered by reflection so a game
  update adds stats for free. Attempts persist per segment id and carry a
  **route fingerprint**, so moving a zone retires old times instead of
  letting them compete. Lines are cleared when the current entry is a plain
  spot or another segment.
- **Loads and memory** (Debug views, bottom; drawn by `SavestateModule.DrawOptions`): `Game/LoadWatcher`
  sees every load by `Scene.FinishGameLoad`; 1.5 s later `Game/MemoryCensus`
  logs the heap, destroyed Unity objects still reachable from statics (per
  root, with growth) and Unity objects by type (switch
  `Diagnostics.MemoryCensusAfterEveryLoad`, **off** since v0.23.6 - a
  hitch of ~0.6-1.0 s after a load, noticed by the author; the old key
  `MemoryCensusOnLoad` is orphaned). *Memory census now* runs it on demand.
  `Game/LeakedThreads` (switch `Fixes.StopLeakedThreadsOnLoad`, on) stops
  the two threads each load leaves running; `Game/StaleSubscribers`
  (switch `Fixes.PruneDeadSubscribersOnLoad`, on) drops the old world's
  event subscribers the game keeps until the title screen - memory only,
  no gameplay effect, so not practice-only.
- **Tabs know when they are showing**: `OverlayModule.TabShowing` (the main
  window open on this tab). `PanelOpen` is only for a module's OWN window
  and is never set for a tab - the Inventory tab refreshed behind it and
  opened empty (fixed v0.22.7).
