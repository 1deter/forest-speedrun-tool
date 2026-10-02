# Open investigations, unverified items and test assets

Detail for threads that run across sessions. CLAUDE.md *Pick up here*
names them in one line each and links here; read the section before
working on it. When one finishes: a line in `docs/confirmed.md` (or the
fix's commit / CHANGELOG), and delete it here.

## Not seen by the author / needs hands

Checked headless or over the bridge only - ask the author to look:
- **Site map polish, all nine** (2026-10-01, live): south at the top, the
  3D flicker (gotcha 74), sea over inland pits (gotcha 75), Water button
  in 3D; screen blood in the capture, the black lake stand-ins at tile
  edges (gotcha 76), the sinkhole (2D far plane to y -320, 3D terrain
  holes), the yacht (moved at run time, gotcha 77). The backlog's *The
  map, after the 2026-10-01 recapture* item is deleted once the author has
  looked. Also the map list of 2026-10-01: brightness bands, Water button,
  3D textures "breaking" (lakes as water, the detail patch follows the
  orbit), playback stutter.
- **3D lab, load size, phone scroll** (v0.24.182-183 + site): the lab's
  floors / signs / whiteboards (`WorldDump.AreaMembers`), glass, mesh
  packs, the spot page at phone width.
- **Cave 6 in 3D** (the stale-page fix, gotcha 71), the 3D switches per
  kind, texture packs (Labskip 3D view 188 requests, was 604).
- **The mountains past the south edge** (the Elevator Boost end, -456,
  707, -1969 is in the overlook room): blurry grey up close (256 px
  textures, no snow top layer) where the game shows rock with snow -
  worth fixing? (docs/website.md).
- **One-click `.lss` import** (v0.24.186-189), checked with the author's
  `The Forest Coop Any%.lss`. A teleport never sets `_currentCave`, so an
  imported spot starting inside a cave misses its cave-exit after a plain
  Go; a start state (or the spot's `cave`, v0.24.193) fixes it.

- **Run mode by hand** (v0.24.206-209, docs/run-mode.md): start a new
  game, press F2 in play (a notice, no window), ESC then F2 (opens; the
  Runs tab's section and report at the top), close ESC (the window
  closes), F7 / Go (a notice), End run mode with two clicks.

- **Reload save on death vs a manual reload** (run mode condition,
  docs/run-mode.md): confirm both paths load the same game as exit to the
  menu -> Continue. Needs a death, so it is partly hands-on.

Needs hands (no bridge call):
- `hold-interact`: hold E on something with the Runs tab's event line open.
- `first-input`: press a key after a restart. `rope-grab` / `rope-leave`
  on a real rope (QA list too).
- **Fast building** (v0.24.196): hold Build - log `FastBuild: 2 Creative
  read(s)` seen, the hold itself not; on no QA list yet.
- **The red line of an unfinished run** (v0.24.200): the line is kept (16
  points); the drawing shows only for the editor's selected entry, which a
  bridge `go` does not set (QA item 8).
- **Rides ended on F7 and put back** (v0.24.195, 201-202,
  `Game/RideModes`): bridge-confirmed with real rides built in Creative
  (zipline, sled, glider, cliff), entered by calls, not input; a runner's
  feel (camera on the line, the sled push, the glide) is QA items 7 and
  9. A long cliff climb needs a steep wall (game-notes *Rides*).

## Tom's native crashes - paused until Tom answers (author)

QA `1554188909246681119`, v0.24.173; reports in
`Downloads\qa-reports\tomyoshi_i\`. "Reload while in a cutscene" /
"reload as you die": native crashes, no dumps sent yet (asked for the
`crash-<date>` folder beside TheForest.exe -> `scripts/symbolize-crash.py`).

**Not reproduced (2026-10-01):** his exact sequence (fresh launch, title
load of Slot 2, Go to 'Cave 6 boss thing', F7 with his start state), also
with the restore on the frame after the tp (cave scenes streaming); F7
0.3 / 1 / 3 s after the Cave 6 keycard pickup; a fall death then F7 next
frame; deaths 0-0.45 s into an F7 restore (all revived, `Busy` refuses
the second restore); F7 during / after the Megan-transformation replay.
**Common to both crashed sessions:** a map-sized pathfinding update in
flight (`navRemoveRoot.startRemove`, 1536 x 1406 m). v0.24.177
recalculates removals place by place (PerfPatches 15; game-notes
*Pathfinding*) - a likely cause gone, not a proven one. **Left:** F7 in
the vault door / Megan pickup cutscenes.

**Tom's v0.24.123 reports** (`Downloads\qa-reports\d.eter\ForestOverlay-report-Tom-*`,
list `docs/tests/2026-09-26-tom-v0.24.123.md`): all fixed except "could
not move after a Full load" - not reproduced; the `Savestate after the
load: player 3 s after in game - ...` line (`Game/PlayerHold`) says what
held him next time - ask for it. Options -> Graphics with F7 untested.

## maks

- **Crash** (v0.24.129, QA `1553525841344856146`, log in
  `Downloads\qa-reports\yirequ\`): native, right after a title-screen
  load, log ends at the player bind after `terrain grass camera off` -
  the signature v0.24.140 fixed. Asked to update and for the crash folder.
- **Fog after a Quick load**: not reproduced with his state (savestate
  `maks-boost`); probably live weather, which no savestate restores
  (backlog *Weather in savestates*). Waiting on his screenshot
  (`1553853576554610781`).
- **Unlit red elevator after a Quick load** (explained v0.24.157): his
  state was captured right after a title load in the car, no endgame Area
  active; `call Sections/HellCorridor Area.OnEnter null` brings it back. A
  capture in a section with no area active now warns
  (`AreaKeeper.UnenteredSection`). Asked to re-capture after walking in.
- **Rope list** (v0.24.104-105, `1553421208794431648`): unanswered.
- **Performance** (`1553417650607235164`): i7-9700KF, RTX 2070 Super,
  150-170 fps; 3-7 GCs per 30 s at 120-150 ms - a session of ~50 Quick
  loads (each forces ~1 collection + two ~520 ms streamed-scene hitches).

## Quick load physics parity (maks; active but deferred)

Author: "no conclusive evidence and current issues are mainly
anecdotal"; gone after a game restart for maks. Decide with the author
whether it leaves "deferred".

After a Quick load, movement tech reacts differently from a real run:
- **Elevator boost**: trigger the red elevator, full swing / smash the axe
  into the door corner, release crouch and spam jump to clip through and
  get shot forwards.
- **Logboosting**: a log wall squeezing the player against a cave wall
  pushes them up - "not identical to the in run circumstance".

**Ruled out (bridge, 2026-09-26, Slot 1)** - identical between a natural
arrival and a Quick load: the player's Rigidbody, capsule / head sphere,
physic materials, every `FirstPersonCharacter` / `RigidBodyCollisionFlags`
/ `Buoyancy` field, parent, every collider on the player (the held axe's
too); the red elevator 10 s into the ride and after (car Rigidbody, door,
panels, 20 colliders within 9 m); `fixedDeltaTime` 0.0167 throughout (the
game's 50 Hz path, PlayMaker `ScaleTime`, not hit); Physics globals; heap
/ full-GC pause flat over 10 Quick loads. What is left is dynamic:
maks's answers (posted 2026-09-26: Full vs Quick, capture before the
trigger, settles after moving?, clip vs launch), then a per-FixedUpdate
physics trace he records in a run and after a load. Savestates `physA`,
`elevPre` (in the car, before the trigger), `elevMid` (2.6 s into the
ride).

## Raw FPS (an investigation: one session, high effort)

Author, 2026-09-26: "a game changer for runners on lower-end machines".
Every number: game-notes *Frame time: where the main thread goes* and
*Graphics options, measured live*.
- Tools: `Frame (30 s):` line (`Game/FrameTimer`, `FrameTimer.Snapshot`),
  `System:` line, Debug views **Frame test** (+1 ms of main-thread work:
  main-thread vs render bound), `Game/RenderProbe` (`CameraContents`,
  `TextureUsers`, `LayerContents`, `TimeRender <mask> <n>`, `TimeCamera`,
  `ToggleLights`, `ToggleRenderers`, `RenderersByRoot`, `ShadersNear`).
- A camera render costs ~0.2 ms of Unity's overhead whatever it draws -
  the lever is fewer camera renders.
- On by default: `TerrainGrassCameraOff` (10), `EndgameScreenOnDemand`
  (11). Experimental, off: `SunShadowsEveryOtherFrame` (12; no visible
  difference - author), `GrassBendingOffInCaves` (13). `Physics30Hz` (14) was **removed** in
  v0.24.210 (author: maks found it changes physics noticeably). The
  author's config has 12 and 13 on.
- Never skip a screen camera mid-frame (v0.24.119 froze the screen,
  gotcha 51).
- **sxczurass is rendering-bound** (Frame test `1553515759936999506`):
  only fewer camera renders / draw calls help him. Asked
  (`1553517261149442159`, `docs/tests/2026-09-26-sxczurass-fps-v0.24.128.md`)
  for 1 min at a lower resolution (fps up = GPU, same = render thread)
  and the two camera switches on vs off; also his hardware specs.

**Next:** (1) sxczurass's answer: render-thread bound -> draw-call cuts
(ActionIconCamera, ParticleCam, then the main camera's draw calls); GPU ->
resolution is his lever. (2) (dropped: 30 Hz physics, removed). (3)
Cheesecake's Frame test (`1553493823840321557`). (4) ActionIconCamera by
hand-`Render()` only with the author's eyes on the picture.

## Performance / loads - what is left, by payoff

Tools: `_modules[8].ToggleAllocations` (`Allocations (30 s):` lines),
`ToggleProfiler`, `TogglePerfPatch i` (A/B live), `_perf.ListLayoutUsers`,
`Load timing:` lines, `_modules[10]._census.RunScene "x"`. Rules:
behaviour-preserving patches on by default, each with its own switch and
one log line, measured before / after in one session; anything changing
timing or outcomes is Experimental, labelled (*Standing decisions*).

1. Garbage in play (~216 KB/s idle, ~2 MB/s in play per maks): strings,
   `MaterialTween` `SendMessage` boxing, Unity's collision objects.
   Measure during play (`AllocationTrackerAtStartup` + restart).
2. The old world held 30-70 s after each Full load / death reload (~25 ms
   longer pauses meanwhile). Root unknown; low payoff.
3. The live heap: mostly the A* navmesh, needed.
4. The big frame at a load's scene start-up (750-900 ms) and the Quick
   load's streamed-scene reload - the game's own work; only if a cheap
   cause shows up.
5. Not checked: a natural arrival / death in the endgame with many
   buildings after v0.24.141-143 (should be the game's grouped nav cut).

## Savestates - open, not blocking

- **A climbing rope on a wall** (`PlayerClimbWallAction`, shares the
  rope's `onRope`): ended on a restore, not put back (`RopeClimb` keys on
  `PlayerClimbRopeAction._currentRopeRoot`). Same recipe as the rides
  (game-notes *Rides*) if a runner needs it.
- **The endgame flag on a Go**: fixed for the vault entrance only
  (v0.24.112, `AreaKeeper.InVaultEntrance`); a Go straight into the lab
  leaves `IsInEndgame` false. Not seen to break anything - check it first
  if an endgame trigger or the lighting misbehaves after a Go.
- **Cutscenes that parent the player**, none replayed: Megan's pickup
  (`pickupGirlRoutine`), Timmy's goodbye, the raft out of the world, a
  rope-down into a cave (`playerEnterCaveAction.doCave`), the intro hang.
  The position fix covers them.
- **Phantom stick** (once): waits for a `Pickup gone, inventory
  unchanged: ...` line; candidate cause in game-notes *Greebles*.
  Unanswered: was the stick count at its max (10)?
- `Small Rock x1` "not at capture" after restores (an `LOD_PickUps` rock
  probably not spawned 8 s after a teleport); `phantom-a` after a title
  load listed 19 (bones, skulls, a booze). Not looked into.
- Trees: a Quick load regrows a half-chopped tree fully (as a Full load
  does); once, one of two new sapling sticks was not removed; a Full load
  does not put back a cut sapling's sticks.
- Time of day sweep (v0.24.67): not reproduced; `SunSync` logs `sun: ...
  snapped` when it acts.
- Cheesecake's hands held 2 s: a `hands still busy after 2 s - ...` line
  says what next time.
- **Logs in the inventory** (v0.24.134-135): a log onto a zipline needs
  the arms; a save with more than 2 stored, loaded with the mod off,
  keeps 2. QA on the sled / repairs / forced drops pending
  (`docs/tests/2026-09-27-logs-v0.24.135.md`).

## Test assets

- **Slots**: Slot 2 is the only Normal slot; 1 and 5 Creative, 3 Hard, 4
  Peaceful. A survival start state restores only in a survival game.
- **Savestates kept**: `zipB` / `sledC` / `gliderB` / `cliffB` (Slot 1,
  on each ride, v0.24.201; their world has two ziplines, a sled and a
  glider built near (395-435, 75, -40..234) - restore any of them to get
  the rides back), `audit-base` (Slot 1, plane wreck, axe + lighter
  held - Quick load audits), `phantom-a`, `keycard-pickup-testing`,
  `physA`, `elevPre`, `elevMid`, `rope104`, `axe-held` / `axe-lighter`
  (Slot 2), `maks-boost`, Tom's `tom-c6boss`, `tom-c6`, `tom-c6exit`,
  `tom-bigjump`, `tom-megan` (Normal).
- **Test spots to remove** (CLAUDE.md *Removing test spots*):
  `s-splitstest01` ("Splits test"; backup before it
  `%TEMP%/my-segments.before-splits-test.txt`), and once Tom answers
  `s-191b90c5ab6f` (Cave 6) / `s-afcb5c720847` (Megan) with their
  `savestates/segments/*.fosave`.
- **Backups**: photo map `%TEMP%/claude/aer/` (`aerial-0928`, `final` =
  v0.24.178 set, `v180` = live); world export before texture packs
  `%TEMP%/claude/world-out-backup-1002`.
