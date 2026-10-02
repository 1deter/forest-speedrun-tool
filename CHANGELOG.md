# Changelog

Newest first, one short section per release, written for runners. CI puts
the section for a tag into its GitHub release, and the in-game **Updates**
tab shows it: what an update brings, and what the installed version
changed. A tag without a section here fails the release build.

## v0.24.201 - 2026-10-02

- Savestates on a zipline, sled, hang glider or cliff climb: a Quick or
  Full load now puts you back on it as captured (on the line at the same
  spot and speed, pushing the same sled, gliding at the same speed, on the
  cliff), as cave ropes already were.
- A capture while gliding no longer drops you out of the air.
- A capture while pushing a sled no longer loses the sled on a restore
  (it was put near the middle of the map).

## v0.24.200 - 2026-10-02

- The line of your last unfinished run (a restart, abort or death) stays
  on screen in red until the next one fails, to see where it went wrong.
  Runs -> Line options can turn it off.

## v0.24.199 - 2026-10-02

- Debug views: colliders and triggers are drawn in their real shape -
  turned boxes, spheres and capsules - instead of a box around each
  (mesh colliders still show their bounds).

## v0.24.198 - 2026-10-02

- Debug views: the collider / trigger filter takes a path part
  (`Caves/Cave6`), a layer (`layer:Prop`) and "show only" fragments
  (`+layer:PickUp`, `+Keycard`) as well as names - many hitboxes share
  generic names like "Collision".

## v0.24.197 - 2026-10-02

- 100% tab: under Timmy drawings, the drawing pieces you have found
  (held or put up on the wall), by number.

## v0.24.196 - 2026-10-01

- Inventory -> **Fast building** (gameplay mod, practice): hold the build
  button to keep adding resources to a blueprint, at Creative's pace -
  every resource still comes from your inventory.
- Attempts now count every run you start, finished or not, as LiveSplit
  does: the splits table and HUD show "23 (9 finished)".

## v0.24.195 - 2026-10-01

- A Go, Restart or Quick load during a cliff climb, sled push, glider
  flight or zipline ride now ends it the game's own way first, as it
  already did for cave ropes - the mode no longer carries on with your
  body somewhere else. (A savestate taken on one still restores you at
  the spot without it.)

## v0.24.194 - 2026-10-01

- The HUD says which game-changing options are on right now ("ON NOW: no
  stagger, god mode, item caps ..."), above the practice marker - so a
  practice setting is never mistaken for real gameplay.
- The HUD keeps the previous run's time ("Last") while the next one runs.

## v0.24.193 - 2026-10-01

- New events for segments: **first-input** (the first button or movement
  after a moment idle - the rules' "takes control", not while a menu or
  the overlay window is open), **rope-grab** and **rope-leave** (a cave
  rope). Event picker: Starts and Rope.
- A spot remembers which cave it is in (set where you stand; Practice
  editor -> Cave, < >). Go tells the game you are in that cave, as walking
  in does: cave splits (enter / exit) work from it, and the game streams
  only that cave's props instead of every cave's.
- A teleport or restart no longer counts as entering or leaving a cave.

## v0.24.192 - 2026-10-01

- Inventory -> **Item caps** (gameplay mod, practice): carry as many of
  any item as you set - find the item, add it, type its cap (e.g. 50
  rocks). Off by default; logs keep their own option.

## v0.24.191 - 2026-10-01

- Quick load keeps what is in your hands when it is what the savestate
  had: the lighter stays lit, no putting away and taking out again.
- Settings now stay as you left them across launches: practice mode, run
  lines, the Compare to choice, "show zones", the 100% tab's filters and
  the Inventory tab's toggles.
- Runs -> **Line options**: the run lines' opacity, and an option to show
  only the next few seconds of the comparison line (slider, 5 s to start).

## v0.24.190 - 2026-10-01

- Other runners' attempts that came in with an imported `.foseg` no longer
  count as yours (they were in your PB, best segments and sum of best).
  Each of those runners' best is now under Runs -> Compare to -> "another
  runner", marked "(file)", next to the website's - no download needed.
- "Upload saved runs" sends only your own runs.

## v0.24.189 - 2026-10-01

- A savestate captured with the survival book open really gives your
  held items back now (v0.24.188 put them in your hands, and the game put
  them away again a moment later).

## v0.24.188 - 2026-10-01

- A spot that starts on moving (a LiveSplit velocity start) no longer
  starts its clock on its own restart: the little settle after a long
  teleport is not you moving.
- A savestate captured with the survival book open now gives back the
  items you held (they came back empty-handed).
- After a Quick load the survival book's to-do list keeps updating (it
  stopped until the next Full load).

## v0.24.187 - 2026-10-01

- Quick load now puts the nature guide back as it was at the capture:
  entries found since are un-ticked (finding them again ticks them), and
  entries ticked in the savestate are ticked.
- A LiveSplit import with no category in the file is named after the
  file ("The Forest Coop Any%") instead of just "The Forest".

## v0.24.186 - 2026-10-01

- **Import a LiveSplit file as a spot, in one click**: put your `.lss`
  in `BepInEx/config/ForestOverlay/livesplit/`, stand where the run
  starts, then Practice -> **Import** -> the file's **Import** button.
  It makes a timed spot with one split per LiveSplit split (same names),
  started and split the way your autosplitter settings say - velocity or
  plane meal start, cave enter / exit, item pickups, clothing,
  passengers, endgame cutscenes. The file is linked as the comparison, so
  Compare to shows its PB straight away.
- If the autosplitter's settings live in your layout (`.lsl`) instead,
  put the layout beside the `.lss` - it is read from there. No settings
  at all: the splits are made by hand (F12).
- A spot's **Autosplit** list (Practice editor) says what the `autosplit`
  checkpoints split on, and can be edited.

## v0.24.185 - 2026-10-01

- LiveSplit splits files as comparisons: put a `.lss` in
  `BepInEx/config/ForestOverlay/livesplit/`, open Runs -> **LiveSplit
  file** and pick it for the segment. Your split names are matched to
  LiveSplit's in order (any row can be set by hand), real or game time.
  Compare to -> **LiveSplit** then offers the file's PB, its best segments
  and any other comparison it holds. They are comparisons only - your own
  PB and golds stay your own.

## v0.24.184 - 2026-10-01

- Segments can split on everything the LiveSplit autosplitter splits on:
  entering or leaving each cave (or any cave), putting on a clothing item,
  the found-passenger count, and the two autosplitter starts - the
  hold-to-interact press (the plane meal) and starting to move.
- The event picker in the segment editor has a group button (Endgame,
  Starts, Caves, Clothing, Passengers); `<` `>` step within the group.

## v0.24.183 - 2026-10-01

- Developer tooling only: the endgame area list names each object, so the
  website's lab keeps the whiteboard drawings on boards that physics moved.

## v0.24.182 - 2026-10-01

- Developer tooling only: the world dump lists what each endgame area
  switches on, so the website's 3D lab gets its floors, signs and
  whiteboard drawings.

## v0.24.181 - 2026-10-01

- Developer tooling only: the world dump keeps every part of the yacht
  (its hull and sails were left out).

## v0.24.180 - 2026-10-01

- Developer tooling only: the website map's photo capture keeps the
  player fed and switches off the screen's blood / frost effects (a hungry
  player put blood on a tile), shows lakes near a tile's edge as water
  instead of a black patch, and sees all the way down into the sinkhole.
  A new world dump writes the yacht where it really floats.

## v0.24.179 - 2026-10-01

- Developer tooling only: the website map's photo capture holds the
  weather clear while it runs (rain or clouds rolling in made the rest of
  the map darker), and logs the weather with its progress.

## v0.24.178 - 2026-10-01

- Developer tooling only: the website map's photo capture now uses one
  fixed exposure for every tile, so the map no longer comes out in bands of
  different brightness. The capture no longer takes the coast a second time
  without the sea (the game's ocean never showed in it); the website draws
  the water itself.

## v0.24.177 - 2026-10-01

- When buildings disappear at the same moment in different parts of the
  map (a Quick load from a savestate made in another save deletes the ones
  it does not have), enemy paths are now recalculated around each place
  instead of over one box covering most of the map. On slower PCs that box
  kept the game busy in the background for up to a minute, and it was
  running in both sessions of a reported crash. Part of the "Buildings
  removed" performance patch (Debug views -> Performance patches, on by default).

## v0.24.176 - 2026-10-01

- Developer tooling only: the 3D map dump also covers the ferns, bushes,
  rocks and cave spikes that greebles place. Nothing changes in game.

## v0.24.175 - 2026-10-01

- Developer tooling only: the 3D map dump also lists the sticks and rocks
  around trees and the bits on cave walls. Nothing changes in game.

## v0.24.174 - 2026-10-01

- Developer tooling only: the 3D map dump can list the small things the game
  scatters around (sticks, rocks, bodies, stalactites), for the website's 3D
  map. Nothing changes in game.

## v0.24.173 - 2026-09-28

- Developer tooling only: the 3D map dump keeps each object's own scale.
  Nothing changes in game.

## v0.24.172 - 2026-09-28

- No changes (published by mistake before its fix; see v0.24.173).

## v0.24.171 - 2026-09-28

- Developer tooling only: a dump of the trees, rocks and cave pieces the
  game places around the player, for the website's 3D map. Nothing changes
  in game.

## v0.24.170 - 2026-09-28

- Developer tooling only: the map photo capture now takes the sea too, and
  a second copy of the coast with the water hidden (the website's Water
  switch). Nothing changes in game.

## v0.24.169 - 2026-09-27

- Developer tooling only: even exposure across the map photo capture.
  Nothing changes in game.

## v0.24.168 - 2026-09-27

- Developer tooling only: the mouse can no longer turn the camera during
  the map photo capture. Nothing changes in game.

## v0.24.167 - 2026-09-27

- Developer tooling only: steady sunlight in the map photo capture.
  Nothing changes in game.

## v0.24.166 - 2026-09-27

- Developer tooling only: no cloud shadows in the map photo capture.
  Nothing changes in game.

## v0.24.165 - 2026-09-27

- Developer tooling only: the map photo capture now looks the same across
  the whole world. Nothing changes in game.

## v0.24.164 - 2026-09-27

- Developer tooling only: a top-down photo capture of the world for the
  website's map. Nothing changes in game.

## v0.24.163 - 2026-09-27

- Finished runs now remember where your save's plane crashed, so the
  website's map can show that plane instead of all twelve possible sites.

## v0.24.162 - 2026-09-27

- Developer tooling only: the overlay can write the world's terrain
  (heights and ground textures) to files, for the website's map. Nothing
  changes in game.

## v0.24.161 - 2026-09-27

- Runs now record every item you carry, not a fixed list: sodas, coins,
  energy mix, anything a route picks up. Only changes are written, and the
  inventory is read only when the game changes it, so it costs nothing
  while your bag stays the same.

## v0.24.160 - 2026-09-27

- Timed runs now record what you carry (sodas, booze, meds, sticks, rocks,
  logs, bombs and more), so the website can show your inventory at any
  point of a run.

## v0.24.159 - 2026-09-27

- Submit to community now always shows its answer under the button (it
  could stay blank the first time).

## v0.24.158 - 2026-09-27

- **Submit to community** (Practice tab, Share row): sends the selected
  spot and its start state to the website for review. Once approved it
  goes out to everyone as a community spot. Your times are not sent. A
  second submit replaces the one still waiting. An older spot with an
  old-style id has to be Duplicated first; the message says so.
- "Open on the website" opens a clean address (forest.deter.cloud/spot/...);
  old links with `#/` in them still work.

## v0.24.157 - 2026-09-27

- Capturing a savestate or start state inside an endgame section the game
  has not "turned on" (right after loading a save there, e.g. in the red
  elevator) now warns you: its walls, lamps and doors are not drawn, and
  every Quick load would keep it that way. Walk out through a doorway and
  back in, then capture again.

## v0.24.156 - 2026-09-27

- QA tab: each tester's own open-items list is in the tab now (maks,
  sxczurass). Type your name in "Your name" and the tab opens on your
  list; everyone else still gets the general one (< > steps through all).
- The QA name field saves once you stop typing, not on every key.

## v0.24.155 - 2026-09-27

- Race other runners: on a timed spot, the Runs tab lists every other
  runner's best from forest.deter.cloud under "Compare to" (pick one
  with < >). The splits table, the delta, the ghost and the run lines
  then race their run. Only runs of the same version of the spot show,
  and their times never count toward your own PB or best segments.
- The comparison key now cycles through the other runners too.

## v0.24.154 - 2026-09-27

- "Upload this spot's saved runs" works with practice mode off too: it
  uploads the runs of the spot you last went to.

## v0.24.153 - 2026-09-27

- Your finished timed runs now upload to the new website,
  forest.deter.cloud, where everyone's runs of a spot are shown together:
  lines on a map, ghosts, and splits. A run sends its path, split times
  and your runner name (your Steam name unless you set one); your Steam id
  itself is never sent.
- The Runs tab has a Website section: switch uploads off there, see what
  was sent, upload a spot's older saved runs, or open the spot on the
  website.
- Runs wait on disk while the game is offline or the site is down and are
  sent later.

## v0.24.152 - 2026-09-27

- Dragging the splits panel to the left edge of the screen now stops
  there, flush with the edge, instead of jumping to the right side.

## v0.24.151 - 2026-09-27

- **Time precision** for the splits (Splits options): 0 to 3 decimal
  places, set separately for times and for deltas, as in LiveSplit.
  Your attempts always save milliseconds, whatever is shown.

## v0.24.150 - 2026-09-27

- **Your last run stays on the splits** after it finishes, through the
  restart (auto-restart too), so you can read your times and deltas.
  They clear when the next run's clock starts; golds and your personal
  best update at that moment, as in LiveSplit.
- The opacity slider and the runner name no longer cause a small hitch
  while you move / type: the setting is saved once you stop.

## v0.24.149 - 2026-09-27

- **Move the splits panel by dragging it** with the mouse while the
  ForestOverlay window is open (F2). Splits options keep a button to
  reset it to the top right.
- **Background opacity slider** for the splits panel (Splits options);
  the text stays solid.
- The on-screen panel now shows the column titles.
- Runner name is one field, already filled in with your Steam name -
  change it there if you want another name on your times.

## v0.24.148 - 2026-09-27

- Splits options: the runner name and panel size lines show as soon as
  the options open (they stayed empty until a run changed the table).

## v0.24.147 - 2026-09-27

- Runs tab: everything below the buttons now scrolls, so the splits
  table, its options and your attempt list all fit. Splits options show
  the panel's width and row count beside their buttons.

## v0.24.146 - 2026-09-27

- **Splits, LiveSplit style.** A timed segment now has a splits table: on
  the game screen while the segment is current (F5 hides it) and in the
  Runs tab. Each checkpoint is a row and the end is the last one, with the
  delta (green / red, gold for a best segment), split time, segment time,
  segment delta, best segment and possible time save as columns, and
  previous segment, sum of best, best possible time, current pace,
  personal best and attempts below. Pick what shows under Runs ->
  Splits options, along with the panel's position, width and rows.
- **Compare to** has a new choice, best segments, and it drives
  everything: the splits, the delta, the ghost and the lines. A new
  (unbound) key cycles it mid-run.
- Checkpoints (and the end) can be named in the Practice editor's new
  *split* fields. Renaming does not retire any times.
- Attempts now save their split times and who ran them (your Steam name
  by default; change it under Splits options). Times recorded before this
  version show by their total only.

## v0.24.145 - 2026-09-27

- **Endgame load trigger outside the vault door's cutscene**: walking back
  through the endgame's load trigger after the door opened (e.g. after a
  Go, when the endgame had not loaded) held you in place until the load
  was done. That load is now the game's own again: one short freeze, and
  you keep your speed. The door's cutscene still loads in the background.

## v0.24.144 - 2026-09-27

- **Quick load of a Megan start state during the fight no longer leaves
  the old Megan behind.** When the boss had run far across the boss room,
  the restore missed her: a new Megan sat down while the old one kept
  fighting. The fighting Megan is now removed wherever she is.

## v0.24.143 - 2026-09-27

- New performance patch (on by default, Debug views -> Performance):
  **when a building or the plane wreck is removed, enemy paths are
  recalculated only where it stood.** The game remembered every earlier
  removal and recalculated all of those places again each time, which
  after a Quick load could cover most of the map (16 s of background
  work, and a death or another Quick load in that time froze until it
  was done).

## v0.24.142 - 2026-09-27

- **Quick load no longer leaves the game busy for half a minute** in a
  save with buildings. After a Quick load the game recalculated enemy
  paths for one huge area covering every building on the map, which
  took 16-60 s. Meanwhile enemies could not find new paths, and a death
  (Reload save on death) or another Quick load froze until it finished
  (about 30 s out of the Megan fight). The buildings are now handled
  the way a normal save load handles them: in small groups, done in
  moments.

## v0.24.141 - 2026-09-27

- Diagnostics for the long freeze when a death reloads the save out of
  the Megan fight: the log now says which pathfinding updates the game
  starts after a Quick load, how long they run, and how long the next
  load waits for them. No change to gameplay.

## v0.24.140 - 2026-09-27

- The crash after dying in the Megan fight with **Reload save on death**
  could still happen on v0.24.138-139 (it crashed again in testing). The
  grass patch now always holds the game's grass code back for the moment
  where it would crash, whatever else happened in that frame.

## v0.24.139 - 2026-09-27

- The crash on **Reload save on death** after dying in the Megan fight is
  now really fixed: v0.24.138 still crashed in testing. The grass camera
  patch now checks right before the game's grass code runs, instead of a
  frame later.

## v0.24.138 - 2026-09-27

- Fixed a game crash on **Reload save on death** after dying in the Megan
  fight (and possibly other reloads out of the endgame). The performance
  patch that switches off the terrain's unused grass camera could switch it
  off in the middle of the load, which made the game crash a moment after
  the save loaded.

## v0.24.137 - 2026-09-27

- A start state captured in another save no longer breaks the inventory.
  Restoring one used to remove some of the inventory's item pictures, and
  after that the inventory could not be closed again (stuck in it, e.g. in
  the Megan fight). Restart the game once if it already happened to you.

## v0.24.136 - 2026-09-27

- Freecam now looks like the game: it flies the game's own camera, so
  the lighting, fog, shadows and effects stay on (it used to go dark).

## v0.24.135 - 2026-09-27

- Logs in the inventory: savestates (Quick and Full load) now bring
  back the number of stored logs from the capture.

## v0.24.134 - 2026-09-27

- New gameplay mod, Inventory tab: **Logs in the inventory**. Picked-up
  logs are stored (5 by default, set how many) instead of carried, so
  your hands stay free. Building, fires, the log sled, holders and
  repairs use the stored logs. Off by default; turning it on marks the
  session as practice.

## v0.24.133 - 2026-09-27

- 100% tab: nature guide entries use the names printed in the book
  ("Chanterelle Mushroom", "Oval-Leaves Blueberries", "Loggerhead Sea
  Turtle") instead of the game's internal ones.

## v0.24.132 - 2026-09-27

- 100% tab: nature guide pages now carry the book's own names (Plant
  Life 1, Plant Life 2, Animals 1-3) instead of "15 0 Info Tick Off 1".

## v0.24.131 - 2026-09-27

- 100% tab: passenger seats are listed in order (1A, 1B, ... 11E), like
  the paper manifest.

## v0.24.130 - 2026-09-27

- 100% tab: a new **Passengers** section - every seat on the passenger
  manifest, found or not, with the total (also on the HUD with "show
  totals on the HUD"). It warns when you are not carrying the manifest:
  the game only counts a passenger while you have it.
- No more small hitches every few seconds on the title screen (the 100%
  and Inventory tabs were searching for a game that was not loaded yet).

## v0.24.129 - 2026-09-26

- Savestates: no more stray "can't carry any more plane axes / lighters"
  message a moment after a restore. It came from the game's own re-equip
  after a load and changed nothing in your inventory; it is now hidden
  during a restore (other "can't carry any more" messages are unchanged).

## v0.24.128 - 2026-09-26

- New experimental switch (Debug views, Performance patches, off by
  default): **Physics at 30 Hz** - turns on the game's own "Low Quality
  Physics" option, which its options menu no longer shows. Physics runs
  30 times a second instead of 60: faster on slower processors, but
  movement and physics tricks can behave differently. For testing.
- Caves: no grass bending (v0.24.127) no longer causes a short hitch the
  first time you enter a cave.

## v0.24.127 - 2026-09-26

- New experimental performance switch (Debug views, Performance patches,
  off by default): **Caves: no grass bending while inside**. The game keeps
  drawing where grass bends around you even inside caves, where there is
  no grass. Saves about 0.25 ms a frame in caves (more on some laptops).
  The only visible difference: looking out of a cave mouth, the grass
  outside does not bend around enemies until you leave the cave.

## v0.24.126 - 2026-09-26

- Developer tools only: one more measurement for the frame-rate work.
  Nothing changes in game.

## v0.24.125 - 2026-09-26

- New experimental performance switch (Debug views, Performance patches,
  off by default): **Sun shadows: redraw every second frame**. Uses the
  game's own built-in option; saves about 0.3 ms a frame (a few percent
  fps). Still shadows look the same, shadows of moving things update at
  half your frame rate.

## v0.24.124 - 2026-09-26

- Developer tools only: new measurements for the frame-rate work. Nothing
  changes in game.

## v0.24.123 - 2026-09-26

- Cave wooden panels: after a Quick load, a panel you had broken could
  come back unbreakable - hits did nothing and it never fell apart.
  Fixed; a broken panel now comes back as a normal one every time.
- F7 (and Restart, and a death revive) with the ESC menu, its options
  screens or the inventory open: the menu is closed first. Before, the
  restore stayed half done until you closed the menu yourself.
- Going to a spot inside a cave (Go, F7, a restore) now removes the black
  wall in the cave mouth, as walking in does. Leaving a cave by teleport
  puts the walls back.
- Log: a few seconds after a Full load, one line says what could be
  holding the player in place (locks, menus, ropes, cutscenes). If you
  ever cannot move after a Full load, send the log.

## v0.24.122 - 2026-09-26

- Log only: the `System:` line also lists the game's own graphics options
  (preset, draw distance, shadows, post effects, grass...).

## v0.24.121 - 2026-09-26

- The log now names your processor, graphics card, resolution and the
  game's graphics settings (a `System:` line), so a performance report
  needs no screenshots.
- Debug views: **Frame test** - adds 1 ms of work every frame while it is
  on. With it on for a minute and off for a minute, the log shows which
  part of your processor limits your frame rate. Off at every launch.

## v0.24.120 - 2026-09-26

- **Fix: v0.24.119's action-icon change is withdrawn.** It could leave
  the picture frozen (sound playing, image only moving when you tab out).
  If you are on v0.24.119, update now; nothing else changes.

## v0.24.119 - 2026-09-26

- **A little more frame rate:** the HUD's camera for action icons (take,
  light, the aim marker...) is skipped in frames where no icon is showing -
  most of the time. Icons still appear in the same frame as before. On by
  default under Debug views -> Performance patches.

## v0.24.118 - 2026-09-26

- Developer tools only (one more rendering test). Nothing in the game
  changes.

## v0.24.117 - 2026-09-26

- Developer tools only (measuring what the game draws). Nothing in the
  game changes.

## v0.24.116 - 2026-09-26

- **Higher frame rate, nothing changed in the picture:** two of the game's
  cameras drew every frame for nothing. The terrain's spare grass camera
  (the grass bends from another one) is switched off, and the endgame's
  plane-screen camera draws only when that screen is actually on screen
  (it is off until the end-crash ending). Each camera costs about a
  quarter of a millisecond every frame here, more on a slower processor.
  Both are on by default under Debug views -> Performance patches.

## v0.24.115 - 2026-09-26

- Log only: the `Frame (30 s):` line lists every camera the game draws
  with, not just the first eight. Nothing in the game changes.

## v0.24.114 - 2026-09-26

- The log now says where each frame's time goes, every 30 s next to the
  `Perf` line: a `Frame (30 s):` line (waiting for the graphics card,
  the game's scripts, each camera's drawing). If the game runs slowly
  for you, send your log - it tells whether your graphics card or your
  processor is holding the frame rate back. Nothing in the game changes.

## v0.24.113 - 2026-09-26

- Log wording only: the vault door's background endgame load no longer
  calls itself Experimental in the log (it has been on by default since
  v0.24.111).

## v0.24.112 - 2026-09-26

- **Go / teleport to the vault door** (or anywhere in the tunnel before it)
  now keeps the game's "in the endgame" state, as walking there does.
  Before, opening the vault door after a Go never loaded the endgame and
  you walked into an empty lab. A teleport out to the surface still clears
  it.

## v0.24.111 - 2026-09-26

- **Endgame: load it in the background at the vault door** is now on by
  default and no longer Experimental. The vault door's cutscene plays
  smoothly instead of freezing for about 5 seconds, and a run's time is
  the same as without it. It can still be turned off in Debug views ->
  Performance patches.

## v0.24.110 - 2026-09-26

- **Loads: skip the endgame-animation clean-up** is now on by default.
  Measured: it frees nothing (the same number of objects after the load
  either way), and a save load (Full load, death reload, Continue) gets
  to the game about 0.4 s sooner (5.4-5.7 s -> 5.0-5.2 s here). Turn it
  off in Debug views -> Performance patches if you ever suspect it.

## v0.24.109 - 2026-09-26

- New switch in Debug views -> Performance patches, off for now while it
  is measured: **Loads: skip the endgame-animation clean-up**. Every save
  load (a Full load, a death reload, Continue) spends about 1 second on
  an asset clean-up that runs too early to free anything it was meant
  to. The switch skips just that one.

## v0.24.108 - 2026-09-26

- Correction to the new Experimental switch **Endgame: load it in the
  background during play**: it does not make a run shorter. The game
  counts its ~5 s frozen frame as time passed, so the door's cutscene
  ends at the same moment either way. With the switch on, the cutscene
  plays smoothly instead of the picture freezing for 5 seconds. The
  switch's text in Debug views now says so.

## v0.24.107 - 2026-09-26

- New Experimental switch in Debug views -> Performance patches:
  **Endgame: load it in the background during play**. Off by default.
  When the vault door opens, the game normally freezes for about 5
  seconds while it loads the endgame area. With the switch on, that load
  runs during the door's cutscene instead, so there is no freeze. If the
  load is still going when the cutscene ends, you are held in place
  until it is done. (This first said a run gets about 5 s shorter; it
  does not - see v0.24.108.)

## v0.24.106 - 2026-09-26

- The Savestates tab is gone: it was the early testing panel. Start
  states in the Practice tab are how savestates work now (Capture and
  Restart on a spot, F7). Its remaining options - restoring across
  Creative and survival, putting enemies back after a restore, and the
  memory / load fixes - moved to the bottom of the Debug views tab.
- The Deaths tab's "Clear blood overlay" button (and its hotkey) is
  gone; the No blood toggle does the same job and keeps it off.

## v0.24.105 - 2026-09-26

- A Full load of a savestate taken on a cave rope now also puts you back
  on the rope (v0.24.104 did it for Quick loads only).

## v0.24.104 - 2026-09-26

- Savestates taken while on a cave rope (e.g. the cave 4 rope entrance)
  now put you back on the rope. Before, a restore threw you high into
  the air out of the rock around the hole. Savestates taken on a rope
  before this update don't know about the rope - capture them again.
- Go and teleports now let go of a rope you are climbing.

## v0.24.103 - 2026-09-26

- QA tab: note boxes wrap long text and grow downwards instead of
  scrolling sideways.
- QA tab: Write report now includes the note at the top, even without
  pressing Mark. Before, a note that was never marked was left out.

## v0.24.102 - 2026-09-26

- Savestates keep your stance: with toggle crouch, a Quick or Full load
  used to leave you crouched if you were crouched before it. A savestate
  now puts you back crouched or standing, the way it was captured
  (savestates made before this version leave the stance alone).

## v0.24.101 - 2026-09-26

- Deaths tab: a **God mode** toggle (practice) - the game's own cheat,
  no damage taken. It stays on through loads while ticked, and unticking
  it only switches god mode off if the toggle switched it on.

## v0.24.100 - 2026-09-26

- Teleporting (Go) while the red elevator is riding now stops the ride.
  Before, the ride finished on its own about 30 seconds later and left
  the cave you had teleported to (e.g. the vault door cave) partly
  invisible.

## v0.24.99 - 2026-09-26

- Savestates: when a restore has to load the endgame area (a Full load
  of a state taken with it loaded, or a Quick load past the vault door),
  it now loads it in the background while the restore holds you in
  place, instead of the game's single frozen frame of about 5 seconds.
  Walking into the endgame during play is unchanged. Switch: Debug
  views, Performance patches.

## v0.24.98 - 2026-09-26

- When the game says "can't carry any more" of an item, the log now says
  what caused it - for the "can't carry any more plane axes" seen once
  after a Full load. Nothing changes in game.

## v0.24.97 - 2026-09-26

- One short freeze less after every load (about 0.1 s, just as you get
  control): the tool no longer forces a memory clean-up to write its
  "Load finished" log line.

## v0.24.96 - 2026-09-26

- Debug views, Performance patches: a new **Experimental /
  gameplay-altering** section, off by default. Everything in it changes
  what the game does, not only how fast it runs, and says what under it.
- First experimental switch: **skip the game's fixed wait before you get
  control** after a save load. Measured: the end of a load from the title
  screen 2.7 s -> 1.35 s, a savestate Full load 2.4 s -> 1.5 s. What it
  changes: the game's nav-mesh update around buildings can still be
  finishing for a moment after you can move.

## v0.24.95 - 2026-09-26

- Save loads: the log now breaks the last stage of a load down step by
  step, to find what can be made faster.
- Testing, off by default: a switch in Debug views (Performance patches)
  that ends a save load without the game's fixed 0.6 s wait at the end.
  It stays off until it has been checked to change nothing else.

## v0.24.94 - 2026-09-26

- Entering a cave and loading a save hitch less: when the game asks
  for its unused-asset clean-up several times at once, it now runs once
  instead of once per request.
- One more small source of per-frame garbage removed (the VR switcher,
  which does nothing outside VR).
- Both have their own switch in Debug views (Performance patches).

## v0.24.93 - 2026-09-26

- The log now records where loading time goes (entering caves, the
  endgame scenes, save loads) - groundwork for faster loads.
- Correction to v0.24.92: measured in game, the performance patches cut
  the game's garbage by about half (not 60%) - still about half as many
  collection hitches.

## v0.24.92 - 2026-09-26

- First game performance patches: the game makes a lot less garbage
  while you play (about 60% less standing still), so its regular
  garbage-collection hitch comes about half as often. The game looks and
  plays exactly the same. Each patch has its own switch in Debug views
  (Performance patches) if you ever want the game's own code back.

## v0.24.91 - 2026-09-26

- Resetting (F7 or a savestate) just as the survival book finished
  opening no longer leaves the camera stuck: looking up and down works
  again straight after the reset.
- Allocation tracker (Debug views): now actually counts - v0.24.90's
  showed nothing.

## v0.24.90 - 2026-09-26

- Debug views: an **Allocation tracker** switch shows exactly what the
  game allocates, by type (and by script while the Game profiler runs) -
  for finding what triggers the regular garbage-collection hitch. Off
  unless switched on; `AllocationTrackerAtStartup` in the config makes it
  complete at the cost of a little speed.

## v0.24.89 - 2026-09-26

- Memory census: the largest roots now say how many objects they hold,
  and a deeper scene census (for the developer, through the test bridge)
  shows which of the game's scripts hold the memory - groundwork for
  shorter garbage-collection hitches.

## v0.24.88 - 2026-09-26

- Savestates tab: *Memory census now* no longer fails with "Collection was
  modified" when the game changes a list while it is being counted.

## v0.24.87 - 2026-09-26

- Game profiler: can also time every coroutine or any named method on all
  game scripts (`GameProfilerExtra`, e.g. `*::MoveNext`), for tracking down
  what makes garbage.
- The performance line's garbage-collection frame lengths are measured
  right (a collection late in a frame was matched to the frame before).

## v0.24.86 - 2026-09-26

- Debug views: a **Game profiler** switch (off at every launch). It times
  the game's own scripts and writes the slowest ones, and the ones that
  make the most garbage, to the log every 30 s and to the tab. For
  performance reports - the game runs a little slower while it is on.
- The 30 s performance line in the log now says how long the frames with a
  garbage collection took.

## v0.24.85 - 2026-09-26

- A Quick load of a spot past the vault door, on a save that has not
  opened it yet, no longer drops you through the world: the endgame area
  is loaded first (you are held for a few seconds), then the Quick load
  runs.

## v0.24.84 - 2026-09-26

- A blueprint brought back by a Quick load after you placed a building no
  longer shows a stray rotate icon over its own place icons.

## v0.24.83 - 2026-09-26

- Coordinate boxes also drop spaces and commas typed after a complete
  position, and typing the same position again no longer marks the spot
  as changed.

## v0.24.82 - 2026-09-26

- A Full load of a savestate taken just after the vault door opened
  loads the red corridor behind it (it stayed empty, and the load held
  you in place for 30 seconds).

## v0.24.81 - 2026-09-26

- Savestates taken during a keycard door's animation (the vault door,
  the gold door) work like the red elevator's: the door closes again,
  its animation plays again and is fast-forwarded to the captured
  moment, with you in the right place.

## v0.24.80 - 2026-09-26

- Red elevator savestates also work when taken late in the keycard
  animation, after the elevator has already reached the top.
- A savestate taken during a cutscene now lands exactly on the captured
  moment after a Full load (it could run half a second past it).

## v0.24.79 - 2026-09-26

- Savestates taken during the red elevator's keycard animation work: a
  Quick load or Full load plays the ride again and fast-forwards it to
  the moment you captured, instead of leaving the elevator at the bottom
  with a button that does nothing.
- A Full load of such a savestate no longer puts you outside the lab
  with the world unloaded, and a Quick load no longer drops you far
  away. (The keycard animation holds the player inside the elevator,
  which confused where the save thought you were.)

## v0.24.78 - 2026-09-26

- Coordinate boxes only take numbers: letters and other characters
  are ignored as you type or paste. The box still turns red while the
  value is incomplete (fewer than three numbers).

## v0.24.77 - 2026-09-26

- Fixed: the spawn coordinates box had "(none - cannot teleport here)"
  drawn over it.

## v0.24.76 - 2026-09-26

- Spot and zone coordinates in the Practice editor are text boxes: select
  and copy them, or type new ones ("x y z"; commas work too). A box
  turns red while what you typed is not three numbers. (maks)
- A demo community spot, "Demo - plane crash dash", shows how community
  packs work until real ones are added.

## v0.24.75 - 2026-09-26

- Box zones can turn: a new box, or Here on a box, faces the way you are
  looking (its depth runs along your view), and a new "turn" slider
  fine-tunes it. Handy for checkpoints across diagonal paths and
  doorways. Existing boxes and their times are unchanged.

## v0.24.74 - 2026-09-26

- The Practice editor no longer shows an Id field: every entry gets a
  hidden key of its own, and you only ever see its name. Renaming an
  entry is always safe. Existing entries and their times are unchanged.
- Shared files are named after the entry (cave-5-practice-run.foseg), and
  importing someone's spot no longer clashes with an unrelated one of
  yours that happened to have the same automatic id.

## v0.24.73 - 2026-09-26

- A savestate or start state restore at the title screen is refused
  (load a game first), as capture already was. Before, it loaded the
  save into the menu.

## v0.24.72 - 2026-09-26

- Community spots: shared spots and timed segments now download on their
  own a few seconds after the game starts and show in the Practice list
  under Community. They are read-only; Duplicate makes your own copy,
  start state included. Practice -> Import -> Check community now fetches
  them on demand. Your own spots are never changed.
- Duplicate now copies an entry's start state too.
- The Import list wraps long names instead of cutting them off.

## v0.24.71 - 2026-09-25

- Share spots and timed segments: the Practice editor's new Share row
  exports the selected entry as one file (its start state included, your
  attempts if ticked) to BepInEx/config/ForestOverlay/shared. Import (top
  of the Practice tab) lists the files in that folder and adds them; an
  entry you already have is only replaced on a second click.

## v0.24.70 - 2026-09-25

- Sticks and rocks around trees come back where they were at capture
  after a Quick or Full load. Before, leaving an area and restoring
  could put them in other spots (the game re-rolls them). Only savestates
  taken on this version or later carry it.

## v0.24.69 - 2026-09-25

- Opening the window while all UI is hidden (F5) shows the UI again.
  Before, the window opened invisibly and only freed the mouse.
- The window can no longer end up off screen (after a resolution change).

## v0.24.68 - 2026-09-25

- A Quick load no longer leaves a second plane axe at the plane wreck when
  the axe was already taken at capture.
- If a pickup disappears without reaching the inventory while savestates
  are in use, the log says so (`Pickup gone, inventory unchanged`) - send
  that line if a stick ever vanishes on you.

## v0.24.67 - 2026-09-25

- After a Quick load or Full load, if the sun is still out of step with the
  restored time of day, it is snapped into place instead of sweeping round
  through the night.

## v0.24.66 - 2026-09-25

- A savestate captured after a Quick load or a Full load now remembers the
  bushes that were already cut, so its own Full load keeps them cut too.

## v0.24.65 - 2026-09-25

- Full load now keeps bushes and saplings that were cut when the savestate
  was captured cut, as Quick load already did. (Their sticks are not put
  back yet.)

## v0.24.64 - 2026-09-25

- A restart or Quick load while the red elevator is riding (or during the
  keycard animation before it moves) now stops the ride and puts the car
  back. Before, the ride carried on a few seconds later and took the car -
  and you - up to the top.

## v0.24.63 - 2026-09-25

- The Quick load log no longer lists the plane axe as "not at capture"
  (it was read while the plane wreck was being rebuilt).

## v0.24.62 - 2026-09-25

- Quick load keeps a bush or sapling that was already cut at capture cut,
  with its sticks where they lay - only the ones cut after the capture come
  back.

## v0.24.61 - 2026-09-25

- Quick load now brings back trees chopped since the capture (half-chopped
  ones too), and cut bushes and saplings. The logs and sapling sticks those
  cuts dropped are removed; logs lying there at capture stay.
- A teleport out of the endgame back to the surface no longer leaves the
  lighting looking like a cave.

## v0.24.60 - 2026-09-25

- On-screen notices have a solid background, so the window behind them no
  longer shows through the text.

## v0.24.59 - 2026-09-25

- On-screen notices now show on top of the ForestOverlay window instead of
  under it.
- Capturing a savestate at the title screen now says to load a game first,
  instead of writing an empty file.

## v0.24.58 - 2026-09-25

- The info box (top left) wraps long lines and grows to fit, instead of
  cutting them off - update messages, long spot names in the PRACTICE
  line.

## v0.24.57 - 2026-09-25

- Settings: long key names (Keypad *) are no longer cut off.
- Savestates tab: "Current save slot" shows the slot straight away
  instead of "?" until the first capture.

## v0.24.56 - 2026-09-25

- New **QA** tab for testers: the current test list, with Pass / Fail /
  Skip and a note per item, saved as you go. Where the log shows an item
  ran, the line appears under it.
- **Mark now** (or a Mark key you bind in Settings) writes "something
  weird happened" into the log with the time, place and current spot,
  plus an optional note.
- **Write report** puts one zip on your desktop with your answers, your
  last logs, your settings, segments and savestates. Send that file.

## v0.24.55 - 2026-09-25

- The last 3 sessions' logs are now kept in
  `BepInEx/config/ForestOverlay/logs`, one file per game launch, so a
  restart no longer loses the log you meant to send. The number is
  `KeptLogs` in the `[Diagnostics]` config section.

## v0.24.54 - 2026-09-25

- Full load: v0.24.52 and v0.24.53 could remove a few items you never
  took (skulls, a Timmy drawing, a photo) - fixed. They come back with
  the next load of your save.

## v0.24.53 - 2026-09-25

- Full load: a first attempt at the fix above (did not fix it).

## v0.24.52 - 2026-09-25

- Full load: items that land a little differently after a load (a
  bottle, the modern axe) or at another of their spawn points are no
  longer mistaken for ones you picked up before capturing and removed.

## v0.24.51 - 2026-09-25

- Full load: coins and cash you picked up before capturing no longer
  come back (cave 5's first pile, maks).

## v0.24.50 - 2026-09-25

- Quick load in a cave: the cave's cannibals stay and are moved back at
  once, instead of vanishing and popping back in a few seconds later
  (the armsy, the babies).

## v0.24.49 - 2026-09-25

- Savestates in a cave: the cave's cannibals are put back where they
  were at capture, after a Quick load and a Full load (maks: "0 of 5
  placed").
- Quick load in a cave no longer doubles the cave babies each time.

## v0.24.48 - 2026-09-25

- Full load: skinny cannibals (the early-game families) are put back
  where they were at capture again. Since v0.24.45 they were left
  wherever the game spawned them.

## v0.24.47 - 2026-09-25

- Savestates: a blueprint you have out is put away on a reset, instead
  of staying in your hands (sxczurass).
- Savestates: a blueprint out when you capture comes back out after
  every restart, Quick or Full load (maks).

## v0.24.46 - 2026-09-25

- Full load: the lighter you held no longer goes missing when you swing
  through the restart (sxczurass).

## v0.24.45 - 2026-09-25

- Full load: the cannibals from your savestate are back within about a
  second of the load, not ~6 s after you get control (maks).
- Quick and Full load: they no longer stand around awake at their camp
  for a moment before being put back in place.

## v0.24.44 - 2026-09-25

- Restarting with the survival book open closes it: before, the book
  stayed in your hands after a Quick load (runner sxczurass).

## v0.24.43 - 2026-09-24

- Full load: the axe and lighter you held are usable again straight
  away (the axe hung at your side and the lighter clicked without light).

## v0.24.42 - 2026-09-24

- Go out of the endgame after the red elevator ride: the vault door cave
  and the Sahara cave's outside look normal again (the ride's overlook
  state stayed on after the teleport).

## v0.24.41 - 2026-09-24

- Quick load after the red elevator: the hallway looks as it did at the
  savestate again (it came back textured when it was invisible). Needs a
  savestate taken on this version.

## v0.24.40 - 2026-09-24

- Quick load puts the red elevator back where it was at the savestate, so
  it can be ridden again (it stayed at the top). Needs a savestate taken
  on this version.

## v0.24.39 - 2026-09-24

- Full load: pickups taken before the savestate are now also removed when
  they appear a few seconds after the load (cave coins came back).

## v0.24.38 - 2026-09-24

- Practice editor: Quick load / Full load is now a two-button switch, so
  the active mode is clear at a glance. The Restart button says which one
  it does.

## v0.24.37 - 2026-09-24

- Fixed: a Quick load into Megan's cutscene with Megan still seated (for
  example straight after loading the game) could break the cutscene and
  leave you out of it.

## v0.24.36 - 2026-09-24

- Quick load removes spears thrown or dropped since the savestate (they
  stayed on the floor while the inventory had them back).
- A cutscene fast-forwarded after a restore keeps its sounds in step:
  they are muted while it skips, then picked up where a normal run would
  have them, instead of starting late and running on into the fight.

## v0.24.35 - 2026-09-24

- Quick load in Megan's boss room puts seated Megan back: a savestate
  taken before or during her transformation now plays the cutscene again
  (fast-forwarded to the captured moment), instead of leaving the boss,
  her body or her babies behind. Older savestates taken during the
  cutscene work too.

## v0.24.34 - 2026-09-24

- After a restart cuts a swing, the next click swings at once (it could
  be swallowed for up to 3 s).

## v0.24.33 - 2026-09-24

- A restart right as a swing starts now cuts the swing too (it could
  play on).
- The cut blends back to the resting pose in 0.1 s (was about 0.25 s).

## v0.24.32 - 2026-09-24

- The swing cut on a restart now also handles the downward smash (it
  was mistaken for the resting pose).

## v0.24.31 - 2026-09-24

- Cutting a swing on a restart is smoother: your arms go straight back
  to how you hold the weapon, without the one-frame look into your
  character's neck, and the reset no longer swallows your next swing.

## v0.24.30 - 2026-09-24

- Developer test tools only (the test bridge): an animation recorder
  that runs alongside other commands, and screenshots. Nothing changes
  for runners.

## v0.24.29 - 2026-09-24

- A restart, a Quick load or a teleport cuts a swing or other action in
  progress instead of letting it play on. (For now you may see into your
  character's head for one frame when it does - being refined.)

## v0.24.28 - 2026-09-24

- A Full load now really holds you in place until the world is loaded:
  at the spot you captured, until every area loaded at capture is back
  (the invisible lab section after the lab had its floor load half a
  second too late and you still fell through).
- A savestate taken during Megan's transformation gives your weapon
  back when the cutscene ends, as the game does - capture it again with
  this version, older files do not know what you held before.

## v0.24.27 - 2026-09-24

- New names: **Quick load** (restore in place) and **Full load** (restore
  with a scene load). The Deaths tab's option is now **Reload save on
  death**.
- A Full load puts the cannibals back where they were at capture. Before,
  the load rolled new families elsewhere and none were near you.
- A Full load no longer brings back pickups you took before the capture
  (cash, tape and the like - the game re-creates them on every load).

## v0.24.26 - 2026-09-24

- An in-place restore after the red elevator clears the overlook-area
  state the ride leaves behind (it changes what the game draws). The
  elevator itself still needs a restore with a load - more to come.
- A restore with a load holds you in place until the world has finished
  loading, so you no longer drop into the floor before it exists.
- While Megan's transformation waits for Megan after a restore, you are
  held where you entered instead of being able to walk off.

## v0.24.25 - 2026-09-24

- A savestate in the invisible section after the lab, restored with a
  load, loads the endgame area again instead of dropping you through the
  map. Out of bounds there the game's own load skips the endgame; the
  restore now asks the game to load it when it was loaded at capture.

## v0.24.24 - 2026-09-24

- Megan practice: after restoring a savestate, Megan's transformation
  waits until the game has set Megan up (about 7 s after a load) instead
  of starting without her and leaving you stuck. No more walking around
  the boss room first.
- A savestate taken during the transformation now fast-forwards to its
  moment at up to 25x speed instead of 6x - about 2-3 s instead of 10.

## v0.24.23 - 2026-09-24

- Cave panels look whole again after an in-place restore. Their health
  was already put back (they break on the same hit as before); now the
  boards that each hit knocks crooked are straightened too, also on a
  panel that was broken and rebuilt. A panel already chipped when you
  captured keeps its look.

## v0.24.22 - 2026-09-24

- Cannibals asleep at capture fall asleep right after an in-place restore
  instead of running about for a few seconds first, and any that wandered
  off before sleeping are put back on their spot.

## v0.24.21 - 2026-09-24

- Cannibals that were asleep at capture stay asleep after an in-place
  restore. The game reuses cannibals, and one that had been searching
  earlier woke up again a few seconds after the restore.
- Restoring or teleporting while in the air: the landing that follows
  no longer plays the stagger, even with "No stagger" off (it already
  did no damage).

## v0.24.20 - 2026-09-24

- A cannibal the game had spawned standing on another's head is put on
  the ground by an in-place restore. Left in the air it could not get
  back to sleep, ran off and woke its whole family.

## v0.24.19 - 2026-09-24

- Cannibals that were asleep when you captured a savestate go back to
  sleep on the same spot after an in-place restore, and the family leader
  gets the leader's spot.

## v0.24.18 - 2026-09-24

- Fixed: restoring in place spawned every cannibal twice for a moment
  (the extras were removed at once, right in front of you).

## v0.24.17 - 2026-09-24

- Restoring a savestate in place now brings back the same cannibal
  families that were there at capture - the same kinds (big painted
  leaders stay big painted leaders, not the weaker skinny ones), the same
  members, where they stood and with their health - about 2 seconds after
  the restore. Only for savestates captured from this version on.

## v0.24.16 - 2026-09-24

- Savestates now remember where each cannibal was. After restoring in
  place, the cannibals are moved back to where they stood at capture, with
  the health they had (a few seconds after the restore, once the game has
  brought its families back). Only for savestates captured from this
  version on.

## v0.24.15 - 2026-09-24

- Restoring in place now really clears dead cannibal bodies (v0.24.14
  kept them).
- Restoring in place now brings back all the cannibal families, not just
  one: if fewer come back than were there before, the game's setup runs
  again a few seconds later.

## v0.24.14 - 2026-09-24

- Restoring a savestate in place no longer leaves the world without
  cannibals: if none came back a few seconds later, the game's own setup
  runs again.
- Restoring in place clears dead cannibal bodies, washes blood off you
  and your weapon, and no longer piles up extra copies of the plane wreck
  (with an extra plane axe each time).

## v0.24.13 - 2026-09-24

- New developer tool, off by default: a **test bridge** (Settings) that
  lets a helper outside the game look at and change the running game
  through a text file. Runners can leave it off; it marks the session
  as practice when it changes anything.

## v0.24.12 - 2026-09-24

- Restarting a spot (F7, auto-restart, a death) now stops the running
  timer at once, instead of letting it run on through the restore.
- Restoring in place clears severed arms, legs and heads left by kills
  since the capture.
- Fixed a small stutter every few seconds at the title screen.

## v0.24.11 - 2026-09-24

- Quitting to the title screen now stops a timed run (not saved) and
  clears its line; go to the spot again to run it.
- A timed spot's lines only show while that spot is selected in the
  Practice list.

## v0.24.10 - 2026-09-24

- Fixed: restoring in place no longer removes every enemy in a Creative
  game with "Allow enemies" off. With enemies on, the ones you killed
  come back the game's own way.
- Restoring in place now clears dead bodies left since the capture.
- Savestates now really keep the survival book's page - the book was
  never found before. Capture a new savestate to get it.
- The restore log no longer says "Equip refused" for held items the game
  put back in your hands a moment later.

## v0.24.9 - 2026-09-24

- Practice list: changing a spot's category to one that already exists
  now moves it under that heading, instead of making a second heading of
  the same name at the bottom. Capital letters and stray spaces in a
  category no longer split it either.
- The savestate area report (for the lab / red elevator case) now lists
  the streamed areas; it listed none before.

## v0.24.8 - 2026-09-24

- Fixed: after restoring a savestate in place, the axe (or any weapon)
  swung but hit nothing - no chopping, no enemy hits, cave panels passed
  through. The restore was deleting the hit parts of the weapons you were
  not holding. If a session is already broken, restore with a load once.

## v0.24.7 - 2026-09-24

- Two practice toggles in the Deaths tab, each on its own and off by
  default: **No blood** keeps the blood overlay off at all times, and
  **No stagger** skips the stagger (and the frozen, jumpless second after
  it) on every hard landing. They work in Creative too, where you cannot
  die.

## v0.24.6 - 2026-09-24

- Auto-restart (Runs tab): tick it and a timed spot restarts by itself as
  soon as you finish - your time flashes on screen, the attempt is saved,
  and you are back at the start (with its start state, like F7). Off by
  default.

## v0.24.5 - 2026-09-24

- Enemies you killed come back after an in-place restore. The game's own
  enemy restart runs, so they spawn where the game puts them - as after a
  load - not exactly where they stood. Turn it off in the Savestates tab
  ("Respawn enemies after an in-place restore").

## v0.24.4 - 2026-09-24

- For the lab / hellcave savestate problem: savestates now write down which
  parts of the world are loaded when you capture, and the log compares
  that with what is loaded after each restore. If the lab or hellcave
  comes back wrong for you, capture, restore, and send your
  LogOutput.log - that is what the fix needs.

## v0.24.3 - 2026-09-24

- A savestate captured during an endgame cutscene (like Megan's
  transformation) now comes back at the moment you captured it: the
  cutscene still starts over, but plays fast until it reaches that
  moment, then runs at normal speed. Capture a couple of seconds before
  it ends to keep its last seconds as your reference.

## v0.24.2 - 2026-09-24

- The wooden panels in caves come back as they were when you captured the
  savestate: axe clips no longer wear them down over restores, and a panel
  you broke is put back by an in-place restore. Savestates captured before
  this version do not know the panels; capture them again.

## v0.24.1 - 2026-09-23

- Restoring a savestate in place puts back what you were holding when you
  captured it - no more taking the lighter out again after every reset.
  Savestates captured before this version do not know what you held;
  capture them again.
- The "CANNOT CARRY ANY MORE LIGHTERS" message after a restore should be
  gone.

## v0.24.0 - 2026-09-23

- Savestates and start states remember the survival book's page: restoring
  one (in place or with a load) opens the book where it was when you
  captured it. A quick-load after a death still opens it on the game's
  usual page. Savestates captured before this version leave the book as
  it is - capture them again to keep the page.

## v0.23.9 - 2026-09-23

- Restoring a savestate or a start state while falling no longer carries
  the fall over: you arrive standing, with no landing damage. The same
  goes for Go on a spot in mid-air.

## v0.23.8 - 2026-09-23

- The Practice list no longer gets stuck on one spot. With unsaved
  changes it used to refuse to switch; now clicking a spot always
  switches, and your edits are kept.
- Spots with unsaved changes say "(unsaved)" in the list, and the button
  shows how many are waiting ("Save (2)"). Save now saves all of them.
  Leaving a spot with unsaved changes tells you so.
- Just looking at a zone no longer counts as editing it.

## v0.23.7 - 2026-09-23

- Updates now install even if your browser saved the plugin under another
  name, like `ForestOverlay(1).dll`. The overlay renames itself when it
  downloads an update, and after the restart you have a normal
  `ForestOverlay.dll` again (the old version is kept as
  `ForestOverlay.dll.bak`, as always).
- If your plugin file is already called something else, rename it to
  `ForestOverlay.dll` by hand once, with the game closed - older versions
  cannot fix this themselves. From this version on it is automatic.

## v0.23.6 - 2026-09-23

- Load slowdown: fixed. 20 reloads in a row now all take about 5 s, and
  memory stays flat after the first couple of loads (it used to climb to
  2.7 GB and 13 s a load).
- The memory report no longer runs after every load, so the short hitch a
  second after loading is gone. "Memory census now" in the Savestates tab
  still runs it on demand, and the switch can turn it back on.

## v0.23.5 - 2026-09-23

- Load slowdown: v0.23.4 worked - after 21 reloads memory stayed around
  400-600 MB (it used to reach 2.7 GB) and every load took about 5 s
  (it used to creep to 13 s). This version closes the gap it left: the
  first few reloads still grew until you killed, built or chopped
  something. Also cleans up dead tree-cutting listeners and the overlay's
  own list of picked-up items after each load.

## v0.23.4 - 2026-09-23

- Load slowdown, third try - the likely real cause: the game's event system
  keeps listeners from every previous load (it only clears them at the
  title screen), and each one keeps that old world in memory. The overlay
  now removes the dead ones after every load. Switch in the Savestates tab.
- The background-thread fix from v0.23.3 works (thread count stays flat)
  and stays on.

## v0.23.3 - 2026-09-23

- Load slowdown, second try: every load left two of the game's background
  threads running (one to organise world updates, one for the "window lost
  focus" sound), about two more per load. The overlay now stops the old
  ones. Whether this is what kept the ~120 MB a load is still being
  tested. Switch in the Savestates tab.
- The v0.23.1 pathfinding fix is removed: the game does clean up its
  pathfinding on a reload, so it never did anything.
- The short hitch about a second after a load is the memory report in the
  log (Savestates tab, "Memory census after every load"). It stays on
  while the slowdown is being tracked down.

## v0.23.2 - 2026-09-23

- Load slowdown: the v0.23.1 fix never had anything to do on a reload,
  and memory still grows about 120 MB a load, so pathfinding was not the
  cause. This version only adds detail to the memory report in the
  log (sizes, background threads, objects kept across loads) to find the
  real cause. Nothing changes in game.

## v0.23.1 - 2026-09-23

- Load slowdown, first fix: every quick-load or savestate load kept the old
  world's pathfinding (about 120 MB) in memory, because the game skips its
  cleanup when it reloads over itself. The overlay now runs that cleanup.
  Switch in the Savestates tab if you need it off.

## v0.23.0 - 2026-09-23

- Updates tab: shows what's new in the latest version (this list).
- Memory: every load now logs the game's memory use, plus a report of what
  the game keeps from earlier loads - for tracking down the load slowdown.
  Savestates tab: "Memory census now", and a switch to turn the per-load
  report off.

## v0.22.7 - 2026-09-23

- Timed runs: checkpoints can no longer be skipped. The end waits until
  every checkpoint has fired (the Runs tab says which one is missing; F12
  skips it). A checkpoint that is already met when you reach it - the
  keycard already in your bag, already standing in its zone - fires at once.
- Run lines from a previous segment are cleared when you go to another
  spot.
- Inventory tab fills in as soon as it opens.
- Savestates from another save no longer delete the weapon-upgrade spots
  (feathers, teeth, glass) on your weapons.
- Messages show under the button you clicked; less work every frame while
  the window is open.
