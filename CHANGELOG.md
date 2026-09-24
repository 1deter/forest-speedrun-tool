# Changelog

Newest first, one short section per release, written for runners. CI puts
the section for a tag into its GitHub release, and the in-game **Updates**
tab shows it: what an update brings, and what the installed version
changed. A tag without a section here fails the release build.

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
