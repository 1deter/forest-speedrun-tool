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

- **The lighter stays lit through a Quick load** (author, QA #general
  2026-09-27 20:30, `1553851224502182012`): now the hands are put away
  (`SavestateBridge.StashHands`) and the game's own load re-equips
  (`PlayerInventory.OnDeserialized`: `HideAllEquiped`, then `Equip` 1.5 s
  later - game-notes *Held items across an in-place restore*), so the
  lighter re-ignites and a runner waits for it. Wanted: when the held
  items already match the capture, keep them (no stash, and the game's
  hide / re-equip step skipped for those slots). Needs a patch on that
  coroutine - look at it with `ilscan body` first.
- **Website links as Discord embeds** (author, `1553852129234518177`):
  a shared spot link previews in Discord (OpenGraph), maybe with an
  embedded replay. Site work, `docs/website.md` *Next*.
- **More event checkpoints** (author, `1553852597138493522`): entering a
  cave, grabbing a rope, other common interactions as `event` triggers -
  the author's autosplitter (memory `autosplitter-repo`) lists ideas.
- **Weather in savestates** (maks's fog after a Quick load, 2026-09-27,
  waiting on his screenshot `1553853576554610781`): the save has no
  weather (`TheForest.World.WeatherSystem`: State, CurrentType, cloud
  values), so the live game's clouds / rain carry into a restore. If it
  is confirmed, capture and put back those fields.

- **Run mode and anti-splicing** (maks `1553862408836087920`, author
  `1553862524430975130` + the ideas dump `1553857387687968869`,
  2026-09-27): a *run mode* toggle that disables practice features until
  the game restarts, with clear indicators of every mode / cheat feature
  on (no stagger, no blood, ...); the clock starts on the first input (the
  rules' "takes control with intent") and ends as the game does, in the
  background, shown on a results screen after the run so the video's time
  can be compared (maks). Anti-splice, no game files needed (maks asked
  to build on it): at run start the plugin registers the run with the
  site (runner id, a random run key, server time); the HUD shows a short
  rolling code derived from that key and the run clock (changes every
  few seconds); the finish uploads the run. A verifier opens the run on
  the site and scrubs the video: every visible code must match the clock
  beside it, and a splice shows a jump. Or (author) a verifier tool that
  checks uploaded files. Not before the rules talk with the moderators
  (*Project intent*); ask before building.
- **Category start states true to the game** (author, QA
  `1553867722964607110`, if runs are allowed with the tool): a savestate
  for a category loads the player into the right game mode with no
  difference from vanilla - for route / tech analysis and comparisons.
  Also: replay blueprint placements and other interactions from a run.
- **Start on first input** as an event trigger (author
  `1553860590295056475`), for rules compliance; plus the other common
  interactions as events (entering a cave, a rope - see *More event
  checkpoints*) and how to display them all (`1553860730900455527`).
- **Site: spots** (author, 2026-09-27): categories for runners' spots
  (theirs, later admin-set), collapsible groups (community spots will be
  mostly teleports, runners' spots the timed ones - maybe less prominent),
  a runner deleting their own spot (or moderators with accounts later).
  The owner's name is shown since the owner change (docs/website.md).
- **The author's ideas dump** (`1553857387687968869`, and the ideas file
  on their desktop): an in-game 3D map of saved spots and routes (caves
  too), 1v1 on the website, tournament / practice / run mode indicators,
  a customisable info overlay (UI/UX overhaul), a knowledge-base Discord
  bot on the game's internals, tech hunting (the game without a keycard),
  archiving the conversation history, confirming the Megan boss AI notes.

## For the final exhaustive feature testing

- **A full security audit of forest.deter.cloud** before 1.0, once the site is finished (author,
  QA 2026-10-01: "ensure there's no vulns left in by accident") - start from docs/website.md
  *Security* (what the 2026-10-01 review covered) and re-check everything added since.
- **Megan fight: health bar empty, died only a few hits later** (author,
  2026-09-27, v0.24.143, during the Megan-fight Quick load tests; log
  rotated out). Probably the game's last stand (`hitFromEnemy`, game-notes
  *Deaths*: ~1 health left, adrenaline), but that says the *next* hit
  kills - "a few" does not fit. Check live: god mode off, Quick load
  `ruben-megan`, log `Stats.Health` every few tenths while the author
  fights, and note which hit drops it and which kills (Megan's vs the
  babies' damage path; the start state's own health).

## Website (author, QA #general 2026-09-27)

- **An exact 3D world** (author, 2026-10-01, the long-term goal): the terrain as the game draws
  it (splat textures, not photos under the models - "a tree model on top of a photo of the
  tree"), for planning 100% routes; the 2D map can keep photos. Also greebles / pickups /
  every prop, so the map is complete.
- Community spots on the site: keep them - they can carry **official category saves** (start
  states), not just teleport spots; shared runner times stay the main feature.
