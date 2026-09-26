# Changelog

Newest first, one short section per release, written for runners. CI puts
the section for a tag into its GitHub release, and the in-game **Updates**
tab shows it: what an update brings, and what the installed version
changed. A tag without a section here fails the release build.

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
