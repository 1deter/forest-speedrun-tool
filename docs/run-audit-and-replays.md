# Run audit log and replays (ideas, 2026-10-03)

The author's ideas from the session that built banned-move detection
(v0.24.227-229). Parts are built since - each section's *Built*
paragraph says what; the rest sit after the open tasks
(`scripts/tasks.py`) unless the author moves them up.
Each idea comes with what the plugin already records, so the first
session on it starts from facts.

## 1. An audit log of the run ("what happened during the run")

**Idea (author):** smarter run detection and reports - detect in-game
interactions and keep an audit log of everything that happened in a run.

**What exists today:**
- The attempt log (`Data/AttemptChain`, hash-chained, uploaded) has `step`
  (IGT + position), `split`, `flag` and `move` lines. Moves are leads for
  the verifier, never flags.
- Events the plugin already sees: the endgame cutscenes (`Game/GameEvents`),
  cave enter / exit per cave, clothing, passengers, plane meal start,
  first input, rope grab / leave (`Game/WorldEvents`), carried items as
  changes (`Game/ItemCounter`, the run's `ItemChange` track), deaths and
  Reload save on death (`Game/DeathHooks`), rides (zipline, sled, glider,
  cliff climb - `Game/RideModes`), the pause menu (`Game/MenuClose`),
  bomb boosts / huge speed / cave force loads (`Game/MoveWatch`).
- The site's attempt page already lists moves with a time, a place and
  the category's banned move they may be.

**What a full audit log would add** (each an `event` line beside `move`,
same shape: real ms, IGT, kind, place, plain-words detail):
- Building: a blueprint placed (kind, place, turn), finished, destroyed
  (`Game/BlueprintKeeper` already reads blueprints).
- Crafting and inventory: combines, item pickups / drops / uses (the item
  track has the counts; the *why* - picked up, crafted, eaten - needs the
  game's own calls).
- Fights: damage taken (from what), cannibals / mutants killed, the
  player knocked down.
- World: doors and keycards (`keycard-door` exists), saves at a shelter,
  sleeping, the time of day jumping, the console / cheats (the report
  already re-reads `Cheats` each second).
- Movement: falls with their landing speed (the fall-damage detector,
  *Next*), climbs, swims, each cave entered and how.

**Built (2026-10-04, not yet released or checked in game):** run mode
attempts write `event|ms|timer|kind|x|y|z|text` lines (`Data/AttemptChain`,
folded; old logs without them still read). Kinds, labels, groups, the
item merge, bursts and the rundown: `Data/RunAudit` (tested, linked by
the site). Plugin: `Modules/RunModeModule.Audit` takes GameEvents /
WorldEvents (cutscenes, keycard doors, caves, clothing, passengers, the
first input, ropes), ItemCounter (merged: a line after 2 s quiet, at most
every 10 s), DeathHooks (`Deaths` counter) and Reload save on death, and
`Game/AuditWatch` - one postfix on the game's own event bus
`EventRegistry.Publish` (built, crafted, used / eaten, enemy / animal
kills, hits taken, trees, bombs, sleep, story, endgame area, settings
changed), `HudGui.TogglePauseMenu(bool)`, and the rides' enter / exit
methods (then `RideModes.Current()` for 3 s). At most 3,000 lines; one
`audit-full` line counts the rest. Runs tab: the last attempt's rundown +
its last 10 lines. Site: *What happened in the run* on the attempt page
(rundown, then the timeline with filter chips per group). Not done:
blueprints *placed* (only finished ones, via the bus), damage amounts,
falls, climbs / swims.

**Where it would show:** a timeline on the attempt page, each entry with
its time on the video (real ms from the start - the run code already
ties the video to the log), filterable by kind; the Runs tab shows the
last attempt's. A verifier jumps from a line to the moment in the video.

**Cost / risk:** every event is a postfix that writes a line - cheap.
Log size: a long run might have a few thousand lines (fine; the server
has room, docs/run-mode.md). Detection rules stay leads, never verdicts.

## 2. Replays that show what happened

**Idea (author):** improve replays in game and on the website - show
interactions, buildings as schematics where they were placed, first-person
replays that play the real first-person animations from the runner's
point of view, and a way to see the trajectory of tech (bomb boosts,
ziplines) - "maybe something similar to the CS grenade camera".

**What exists today:** a run records position at 30 Hz, about 60 player
stats at 5 Hz, the item changes and (2026-10-04) its interactions and
buildings (`Data/RunRecorder`, below). Ghosts and run
lines in game; on the site, the 2D / 3D map with lines, ghosts, a scrub
bar and the *State* panel. Since the replay camera (below) the look
direction is recorded too (`l|` lines); **no animation is recorded**.

**What each part needs:**
- **Interactions and buildings on the replay:** the audit log's events
  (part 1) drawn at their time and place: icons on the line, a blueprint
  as a ghost outline (in game: the game's own blueprint ghost model; on
  the site: the building's model from the world export, see-through).
- **First-person replay true to the runner's view:** record the camera's
  yaw / pitch with the position (30 Hz) and the player's animator - each
  layer's state hash + normalized time and the parameters
  (`Game/AnimProbe` already reads them; ~6 layers x 8 bytes x 30 Hz =
  small). Playback in game: a camera at the recorded eye, a copy of the
  first-person arms driven with `Animator.Play(hash, layer, time)`, the
  held item from the item track. On the site: the recorded view over the
  3D world (no arms at first).
- **Trajectory view:** for a bomb boost, the path after the unpause with
  marks (blast, pause, unpause, peak speed, landing), the predicted arc
  from the velocity (gravity, drag 0 - game-notes) beside the real one,
  and a "grenade camera" that follows the player along it in slow motion.
  For ziplines: the rope's two ends (from the placed blueprint), the ride
  along it, and before placing, an aim helper showing where the rope
  would go (Next up 6, "placing ziplines precisely"). Works in game
  (`Game/DebugDraw` lines) and on the site (3D lines coloured by speed).

**Built (2026-10-04, not yet released or checked in game):**
interactions and buildings on the in-game replay (*Order* item 3, in
game). Every timed run - practice or run mode - records two more tracks
in its `.run` (`Data/AttemptFormat`; older readers and the site skip
unknown lines, old runs parse with empty tracks; tested in
`ReplayMarksTests`):
- `e|t|kind|x|y|z|detail` - what the runner did, in the run audit's kinds
  (`Data/RunAudit`, so labels and groups match the attempt log). Sources
  (`Modules/PracticeRunModule.Replay`): `Ctx.Events`' general names
  (caves, clothing, passengers, ropes, keycard doors, the endgame, and the
  bus events from `Game/AuditWatch` - built, crafted, used, kills, hits,
  trees, bombs, sleep, story, endgame area - and rides; the mapping is
  `Data/ReplayMarks.KindFor`), the pause menu (`AuditWatch.PauseOpen` /
  `PauseToggles`, every run) and deaths (`DeathHooks.Deaths`). At most
  2,000 a run.
- `b|t|state|kind|x|y|z|rx|ry|rz|cx|cy|cz|sx|sy|sz` - a blueprint placed
  or a structure finished: place, rotation, and its box in its own frame
  from the blueprint's meshes (`Game/BuildWatch`, read-only prefixes on
  `Create.ClearReferences` and `Craft_Structure.Build`; game-notes *A
  blueprint placed / a structure finished*), else a default box per kind
  (`ReplayMarks.DefaultSize`). At most 500 a run. One `Replay:` log line
  per structure.

Playback (`Game/ReplayDraw`, GL lines, main view only): the comparison
run's (the ghost's) buildings as wireframe boxes from their time on -
blueprints pale blue until finished at the same place (1.5 m, same kind),
finished ones orange; a marker (a post with a diamond, coloured by the
audit group) at each interaction along its line - behind the ghost in full
colour, ahead of it faded; labels ("Crafted: Bomb") over the 6 markers
nearest the camera within 25 m (picked 5 times a second, cached text,
drawn on Repaint). With no run going it shows the run's end state. Runs
tab: *Replay shows: buildings / interaction markers* (config
`[Runs] ReplayBuildings` / `ReplayMarkers`, on) and a line saying how many
the comparison run has. Site: `/api/runs/<id>` carries `events` and
`buildings`; the State panel lists the last 5 things done up to the scrub
time.

**Built (2026-10-04, branch `ghost-camera`, not yet released or checked
in game): a ghost with a body and the replay camera** (*Order* items 1,
4's follow camera and 5's camera view, in game):
- **Look track.** Every timed run records `l|t|yaw|pitch|eye` with each
  position sample (30 Hz): the view camera's world yaw / pitch (Unity's
  angles, pitch + = down) and its height above the recorded position
  (`PracticeRunModule.ReadLook`; skipped while freecam / the replay
  camera flies the view). `Data/AttemptFormat` (F2 numbers); older
  readers and the site skip the lines, old runs parse with no looks.
- **Ghost look** (Runs tab, `[Runs] GhostLook` = Figure | Marker, Figure
  by default): `Data/GhostFigure` - a capsule body (rings, sides, rounded
  ends), a head, an arrow out of the chest along the facing and a gaze
  line along the look; 164 GL line vertices built in Tick into
  `RunLineBehaviour.FigureVerts`. Height = the recorded eye + 0.13 m
  (0.9 - 2.2; a crouch is shorter), else 1.8 m; feet at the recorded
  position, as the marker. Older runs face where they moved
  (`ReplayCamera.HeadingAt`, over +-0.25 s).
- **Replay camera** (Runs tab *Watch the comparison run*, hotkey
  `run.replayCamera` unbound; Experimental, practice - marks "replay
  camera", run mode feature `replaycam`, locked by default): plays the
  comparison run on its own clock (`Data/ReplayClock`: pause, seek, a
  frame step, 0.1x - 2x, a scrub slider in the tab) and flies the game's
  own camera through a second `FreeCamBehaviour` (gotchas 56 / 58: nothing
  copied or switched off; `FreeCamBehaviour.InUse` refuses a second flyer,
  so freecam and the replay camera exclude each other). Views
  (`Data/ReplayCamera`, tested): **chase** (4.5 m behind, above, looking
  at the chest, smoothed at rate 6), **first person** (the recorded eye
  and look, not smoothed; no look track = the heading, level), and
  **trajectory** (side-on to the path from 2 s before to 4 s after,
  distance fitted to the camera's fov / aspect, smoothed at rate 2.5; the
  framed stretch drawn white). The player is held (HoldsPlayer) and no
  triggers are read while it is on, so no run arms or starts. Keys with
  the window closed: Space, Left / Right (Shift 5 s), `,` / `.`, Up /
  Down, 1 2 3 / V, R, Esc. Ends on Esc, its key, F9 off, run mode, a run
  starting, or a load tearing the camera down. Log lines `Replay camera:
  on / view / off`; HUD `Replay`.
- **Bridge check:** a segment with a finished run (practice mode on, Go
  to it), then `call BepInEx_Manager OverlayPlugin._host._modules[11].ToggleReplayCamera`,
  `wait 2`, `shot replay-chase`; `set ..._modules[11]._camView FirstPerson`,
  `shot replay-fp`; `set ..._camView Trajectory`, `wait 2`, `shot
  replay-traj`; `set ..._clock.Paused true` + `set ..._clock.T <s>` to
  pick the moment; `call ..._modules[11].ToggleReplayCamera` ends it
  (check `_camOn` false and the view back on the player). A run recorded
  on this version first, for the look track (`l|` lines in
  `runs/<id>/*.run`).

**Built (2026-10-04, site, not yet deployed or seen with a real run):
buildings and markers on the site's maps.** The spot page's 2D / 3D maps
draw the focused run's structures (footprints / wireframe boxes, a
blueprint pale until finished, a finished one orange) and interactions
(diamonds in the audit group's colour along the line, faded ahead of the
scrub time; hover / tap for the label, a tap moves the clock there), with
*Buildings* / *Markers* switches kept per browser. `/api/runs/<id>` gained
each event's group and each building's box centre, tilt and blueprint end
- docs/website.md *Buildings and interaction markers on the maps*.

**Next steps (not built):** the look track on the site (a first-person
view over the 3D world; `/api/runs/<id>` does not carry it yet); the
arms / animations in first person (needs the animator in the recording,
*Order* 5); the trajectory view's other variants (the predicted arc beside
the real one, marks for blast / pause / unpause / peak / landing, a slow-
motion follow of a boost, a zipline aim helper - *Order* 4); blueprints
destroyed / cancelled after placing (the box stays until the end); the
walls' chain placement checked live.

**Decided (author, 2026-10-03):**
1. The audit log is for verifiers and runners alike; its point is a
   **rundown** - a short summary of a run's interactions a verifier can
   skim (counts and highlights per kind, e.g. "3 blueprints placed, 41
   items picked up, 2 deaths, 1 cave force load"), with the full timeline
   behind it. Not a must-have; worth it as a summary.
2. All interaction kinds matter equally.
3. First-person replay: **in game first**, the website as a later extra;
   the camera view first, the arms later if they are a lot of work.
4. Trajectory view: build every variant (follow camera, scrubbable arc,
   prediction before the move) and let runners say which ones help.
5. **Run mode attempts only** - no audit log for practice runs (the
   author saw no reason; neither did Claude: practice already has run
   lines, splits and the item track, and the `Move seen:` log lines stay
   in LogOutput.log for testing the detectors).

## Order, if picked up

1. Look direction in the recording (cheap, needed by everything else) -
   built (the `l|` track, 2026-10-04).
2. Audit `event` lines for what is already seen (run mode attempts) + the
   attempt page's rundown (summary first) and timeline.
3. Buildings and interactions on the in-game and site replays.
4. Trajectory view (bomb boost first, then ziplines).
5. First-person replay with animations.
