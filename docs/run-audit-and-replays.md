# Run audit log and replays (ideas, 2026-10-03)

The author's ideas from the session that built banned-move detection
(v0.24.227-229). Nothing here is built or scheduled yet. They sit after
the current *Next* list (CLAUDE.md) unless the author moves them up.
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
stats at 5 Hz and the item changes (`Data/RunRecorder`). Ghosts and run
lines in game; on the site, the 2D / 3D map with lines, ghosts, a scrub
bar and the *State* panel. **No look direction and no animation are
recorded** - the ghost is a marker.

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

**Open questions for the author:**
1. Audit log: for verifiers (anti-cheat), for runners (learning / route
   review), or both? It decides how much detail goes in and whether it
   must be in the hash chain.
2. Which interactions matter most first - building, items, fights, doors?
3. First-person replay: in game only, or on the site too? Is the view
   enough at first, with the arms later?
4. Grenade camera: a camera following the player's flight, a camera on the
   path's arc you can scrub, or a prediction shown *before* the move
   (practice aid, Experimental)?
5. Should practice runs (not only run mode attempts) keep the audit log?

## Order, if picked up

1. Look direction in the recording (cheap, needed by everything else).
2. Audit `event` lines for what is already seen + the attempt page's
   timeline.
3. Buildings and interactions on the in-game and site replays.
4. Trajectory view (bomb boost first, then ziplines).
5. First-person replay with animations.
