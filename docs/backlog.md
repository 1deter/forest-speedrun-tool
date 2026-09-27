# Deferred runner feedback

Moved out of CLAUDE.md (2026-09-26). **Deferred** until *Next up* is done (author: finish the list, then QoL/UX), unless critical. New runner requests that are not scheduled go here.

**Deferred** until Next up is done (author: finish the list, then QoL/UX),
unless critical.

- **Deaths:** revive is confusing, worse with practice mode on and another
  spot selected - one clear choice of what a death does (reload the save,
  restore the start state Quick / Full, revive).
- **Runs:** hide zones individually or show only the next; Runs tab:
  when each time was set, more detail, the HUD shows the **previous** time
  too; **runs continue at the main menu** - abort automatically; ghost: a
  custom model, buildings in the replay; **checkpoint savestates**
  ("saveloc", like KSF surf) - capturing on the fly without a hitch.
  Author (QA Discord, 2026-09-26): **restart from checkpoint** for long
  timed segments (full-run practice per category), tied to savestates
  captured as each checkpoint fires - needs the capture hitch solved
  (or the capture deferred / async) so a real attempt is not disturbed.
- **Run lines (QA Discord, 2026-09-26):** an opacity slider 0-100%
  (sxczurass: full opacity hides the best time), the best run's line in
  a different colour from the current one (sxczurass), and a window
  option - show the comparison line only a set time ahead of where you
  are, slider, default ~5 s (author). Not yet scheduled - ask the
  author whether they jump the queue (the author asked for the last).
- **Status overlay** (author, QA Discord 2026-09-26): make active
  practice changes obvious - maks had No stagger on and thought he had
  found a new lineup. For the UI / UX refactor in a late update.
- **Settings / HUD:** settings do not persist (run lines, practice mode...;
  maks raised it again on the QA Discord, 2026-09-26)
  - persist all; more control over the top-left HUD, less clutter.
- **Debug views:** more detailed colliders (hitboxes), a better collider
  filter (items share generic names); colliders that change between
  attempts and make no-fall-damage tech inconsistent (cave drop, rebreather
  cave stalagmite drop, keycard cave body slide, wall climbs).
- **maks, QA Discord 2026-09-26:** start a practice savestate from the
  title screen without loading a save first (restores there are refused
  since v0.24.73 - it needs a scene loaded first); keep the run lines of
  **failed** runs for analysis (sxczurass too, 2026-09-26: failed attempts
  should **count as attempts** - only completed ones do now); shared runs
  show **the runner's name**
  (`.foseg` attempts carry none yet - matters for the website too).

Shipped (summary): practice QoL (v0.17), endgame splits (v0.18), deaths
and caves (v0.19), nature guide (v0.15), savestates and no-menu reload
(v0.20-0.21), start states and cross-save restores (v0.22), ordered
checkpoints, the changelog, the load leak fixed (v0.22.7-0.23.6), updates
under any file name (v0.23.7), the Practice list fix and savestate
completeness (v0.23.8-0.24.7), the live test bridge and everything found
with it (v0.24.13-0.24.37: cannibals rebuilt as captured, Megan's
cutscene after a Full load, the endgame / lab after a Full load, taken
pickups removed, Quick / Full load naming, the swing / smash cut on a reset with the
attack FSM ended, Megan after a Quick load, cutscene sounds in step,
thrown spears removed, the Quick / Full load switch (v0.24.38, awaiting maks), the red elevator / endgame areas / held items after a load (v0.24.40-0.24.43)), turned checkpoint boxes (v0.24.75), coordinates as text fields (v0.24.76, maks; numbers only v0.24.78 / v0.24.83), savestates during the red elevator / keypad door cutscenes replayed (v0.24.79-82), no stray build icon on a blueprint after a Quick load (v0.24.84), a Quick load past the vault door loads the endgame first (v0.24.85, maks confirmed).

Idea (author, QA #general, 2026-09-26): a ranked or custom 1v1 system - later,
when the tool is mainly finished and mature.

Requests (sxczurass, QA #general, 2026-09-27, messages
`1553776769826295818` / `1553778115413540885`; picture in
`Downloads\qa-reports\sxczurass\image-1553776769826295818.png`):
- **Any item's carry cap**, like logs in the inventory: an Inventory-tab
  filter (type an item, *Add*) adds an "<item> it holds [n]" row, *Delete*
  removes it (e.g. 50 rocks instead of 5). Gameplay mod, practice.
- **Creative-speed building in any mode**: hold to keep adding resources
  to a blueprint (Creative does), not one click per item - speed only, no
  free resources. Gameplay mod, practice.


- **A teleport into the endgame from a save without it loaded falls
  through the world** (author's report, 2026-09-27, v0.24.155): F7 on
  "Labskip Jumping Section" (start state captured in Creative) in a Normal
  save - the restore is refused (cross-mode), the teleport alone lands in
  the lab with `endgame_streaming` not loaded. Same for a plain Go. The
  author: keep teleport as it is for now, "some ideas for this
  functionality later" - ask before building. A candidate, not decided:
  nothing below the spot (raycast) and the endgame not loaded -> the
  game's EndgameLoader first (`EndgameLoader.EnsureLoaded`), then place.

## For the final exhaustive feature testing

- **Megan fight: health bar empty, died only a few hits later** (author,
  2026-09-27, v0.24.143, during the Megan-fight Quick load tests; log
  rotated out). Probably the game's last stand (`hitFromEnemy`, game-notes
  *Deaths*: ~1 health left, adrenaline), but that says the *next* hit
  kills - "a few" does not fit. Check live: god mode off, Quick load
  `ruben-megan`, log `Stats.Health` every few tenths while the author
  fights, and note which hit drops it and which kills (Megan's vs the
  babies' damage path; the start state's own health).
