# Changelog

Newest first, one short section per release, written for runners. CI puts
the section for a tag into its GitHub release, and the in-game **Updates**
tab shows it: what an update brings, and what the installed version
changed. A tag without a section here fails the release build.

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
