# Changelog

Newest first, one short section per release, written for runners. CI puts
the section for a tag into its GitHub release, and the in-game **Updates**
tab shows it: what an update brings, and what the installed version
changed. A tag without a section here fails the release build.

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
