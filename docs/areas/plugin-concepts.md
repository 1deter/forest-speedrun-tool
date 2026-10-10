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
- **Editor edits save themselves** (T-0217): an edit marks its entry
  *(unsaved)* and is written to its local file once the edits pause
  (1 s quiet, at most 5 s - `Data/EditDebounce`), or at once on selecting
  another entry, Export / Submit and quitting. The file is replaced
  through a `.tmp` (`Data/SafeFile`; a crash mid-write is put right at the
  next load). There is no Save or Reload button. A failed write says
  "Autosave failed - see log (n unsaved). Retrying." and tries again; an
  entry that is not valid (no spawn, or no start and end) stays unsaved and
  is reported once (`Data/HeldReport`). Nothing is uploaded by an edit.
- **One button, one job** (author, v0.22.0: "buttons shouldn't have
  double-purposes"). **Go only teleports**, start state or not. Restoring
  is **Restart**: F7, the Runs tab's Restart, a death revive, and the
  *Restart* button on the editor's Start state row.
- **TAS record / replay is inputs, not positions** (experimental, practice
  only). Record (Runs tab, or every timed run with `[TAS] RecordTimedRuns`)
  restarts the current spot and, from the frame after the player is placed
  (frame 0), stores every value the game reads through
  `TheForest.Utils.Input` - changes only, plus position and look at 30 Hz -
  to `runs/<id>/inputs/<stamp>.tas`. Replay restarts the same spot and
  replaces what the game reads with the recording's values on the same frame
  numbers, which also blocks the player's own input; at the end the 30 Hz
  positions are compared and the log says the max drift. It is not
  deterministic (physics, loading), so drift is reported, not hidden. With
  `LockFrameRateOnReplay` (default on) each frame runs with its recorded
  delta time (`Time.captureFramerate`), put back afterwards. A timed run
  the replay itself times is never saved as the runner's attempt. Stop is
  `]`. Run mode refuses it (feature `tas`, not in any category). Log prefix
  `TAS:`.
- **The trajectory preview predicts a let-go flight and only reads the
  game** (experimental, Debug views switch, off at every launch, practice
  marked; run mode refuses it unless a category allows feature
  `trajectory`). Ten times a second it steps the player's live velocity with
  the game's gravity (Physics -16 plus the controller's 10), the 55 m/s cap
  and the rigidbody's drag, sweeping the body's capsule along the path until
  it hits something, and draws it with GL lines: blue, orange during an
  explosion knockback, a green / red landing mark (red = fall damage). Your
  steering in the air is not simulated. In the pause menu during a
  knockback it shows the bomb boost the pushes piling up will give. One log
  line per flight (`Trajectory preview:`) compares the prediction made at
  its start with where the player really landed. Known gap: off where a
  jump clips an edge (T-0088).
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
  `hold-interact` (plane meal start), `first-input` (any input but Esc
  and the camera - replaces the velocity start `moving`, T-0282; a stored
  `moving` reads as `first-input` and its old times retire).
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
  **Keep loaded** (*Start state* row toggle, `keep = loaded`, not part of
  the route; T-0212, author 2026-10-10 - lab skip needs the endgame loaded
  once, not every restart): the first restart restores; 2 s on, when the
  areas are the capture's (`OnRestoreSettled`), the world is kept
  (`Data/KeepLoaded`). Later restarts then skip the restore: the player's
  stats + item amounts as the restore left them (`Game/PlayerKeep`, taken
  at its `done`), the elevators / sliding doors / active area / held items
  / stored logs from the file, then the teleport (`Restart '<id>': kept
  loaded - no restore, ...`, ~0.01 s against a 0.4 s Quick load). Any
  scene load or unload since (`SceneCache.SceneEvents`), another restore
  or load (`SavestateBridge.Restores`), a death, another spot, a new
  capture or a start state captured in a cutscene makes the restart a real
  restore again and logs why (`keep loaded - loading the start state: ...`).
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
  **Website spots** (Import view, `CommunityModule.Website`) carry the
  start state their creator recorded (author, 2026-10-08, T-0194): an
  upload of a route with a `startstate = <hash>` line and no data on the
  site is answered `startstate: "wanted"`, and the plugin queues the same
  bundle again with its `[startstate]` once (`SiteProtocol.
  StartStateResend`; `Upload: the site has no start state for ...`). The
  site keeps it per route only from the route's owner (a copy of someone
  else's spot stays teleport-only) and when its data hash matches; Add writes
  it as the segment's own (`Website spots: added '<id>', start state.`).
  The runner's **own** website spot (the list's `owner` line is their
  runner id; T-0265) goes into their own list instead - editable, same
  id, its uploads change the site's copy - with the website's start
  state (`Website spots: own '<id>' added back, ...`). One already in
  their list: the same says so; a different one is replaced on a second
  click (*Replace?*, 3 s), keeping the runner's start state and attempts
  (T-0218). Each row's answer shows under it. Practice's **Delete** is
  local only (the site copy stays until *Delete from the website*); the
  old retry queue `uploads/deletes.txt` (v0.24.248-267) is removed once at
  startup, never sent (`Delete: removed the old retry queue ...`). Pending
  uploads of a locally deleted spot are still sent. A take-back / Replace?
  / Import's Replace? tells the armed run to let go of the old spot object
  (`OnSpotDeleted`), so a run finished after it uploads the new route.
  A spot whose creator has not uploaded since keeps restarting as a
  teleport (`Restart '<id>': no start state - teleport only.`); before
  T-0194 that was every website spot ('Elevator Boost', s-9cdb6a6808ad,
  T-0151: after one red-elevator ride the car stays at the overlook).
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
