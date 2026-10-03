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
- **Status overlay** (author, QA Discord 2026-09-26): make active
  practice changes obvious - maks had No stagger on and thought he had
  found a new lineup. For the UI / UX refactor in a late update.
- **HUD:** more control over the top-left HUD, less clutter (settings persist since v0.24.191).
- **Debug views:** (real collider shapes and a path / layer / show-only
  filter since v0.24.198-199); colliders that change between
  attempts and make no-fall-damage tech inconsistent (cave drop, rebreather
  cave stalagmite drop, keycard cave body slide, wall climbs).
- **maks, QA Discord 2026-09-26:** start a practice savestate from the
  title screen without loading a save first (restores there are refused
  since v0.24.73 - it needs a scene loaded first); failed runs count as
  attempts (v0.24.196) and the last one's line stays, red (v0.24.200); shared runs
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


- **More event checkpoints** (author, `1553852597138493522`): caves, the
  rope (`rope-grab` / `rope-leave`) and the first input are events since
  v0.24.184 / v0.24.193; other common interactions still to pick.
- **Weather in savestates** (maks's fog after a Quick load, 2026-09-27,
  waiting on his screenshot `1553853576554610781`): the save has no
  weather (`TheForest.World.WeatherSystem`: State, CurrentType, cloud
  values), so the live game's clouds / rain carry into a restore. If it
  is confirmed, capture and put back those fields.

- **Run mode and anti-splicing**: designed with the author 2026-10-02 and
  being built in phases - [`run-mode.md`](run-mode.md) (phase 1 shipped in
  v0.24.206). Origins: maks `1553862408836087920`, author
  `1553862524430975130`, the ideas dump `1553857387687968869`
  (2026-09-27); sxczurass's session settings lock (QA #general
  2026-10-02) is run mode's lock. Still from maks's idea, for later: the
  clock from the first input and a results screen after the run.
- **Category start states true to the game** (author, QA
  `1553867722964607110`, if runs are allowed with the tool): a savestate
  for a category loads the player into the right game mode with no
  difference from vanilla - for route / tech analysis and comparisons.
  Also: replay blueprint placements and other interactions from a run.
- **Confirm before a capture overwrites a start state** (maks, QA
  #general 2026-09-26; a second click is asked today only when attempts
  would be retired).
- **A full replay system** (sxczurass + author, 2026-09-26: "lets go all
  the way").
- How to display all the events (author `1553860730900455527`) - the
  editor's picker groups for now (`first-input` exists since v0.24.193).
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

- **The map, after the 2026-10-01 recapture (author's review, screenshots in
  that session's transcript).** **All done 2026-10-01** (v0.24.180-181,
  site a5a0077, recaptured + re-exported, live) - kept until the author
  has looked; then delete this item. Tiles are the capture's `<ix>_<iz>` (x0
  -1750, z0 -1742.631, 218.75 m; ix = (x + 1750) / 218.75, iz = (z +
  1742.631) / 218.75). Retake recipe: docs/website.md *The photo map* (back
  up the game's `aerial/` first; the live set is also in
  `%TEMP%/claude/aer/final`).
  1. **Blood on screen in a tile** - x 1100-1300, z 900-1100 = tile 13_12
     (2D, Photo, Water off), darker seams at its top right. The player was
     starving / thirsty during the clear-weather retake (rows 7-15): god
     mode stops the death, not the hurt effects, and they are in the
     frame. Fix in `Game/AerialCapture`: keep the player full (Fullness 1,
     Thirst 0, Energy / Stamina 100 every tile) and hide the hurt overlay
     (`Deaths.NoBlood`'s mechanism), then retake 13_12 and its neighbours;
     check other tiles of that run for red edges (a red-channel scan of
     the capture).
  2. **The frozen lake in the snow is cut off** at x 656.25 (tile 10_2 has
     it, 11_2 not) and **a black shape** at x ~1400-1460, z ~-1300 (tile
     14_2), whose straight edge is the row boundary z -1305.13. Both sit
     on tile edges: an object drawn only while the player (moved to each
     tile's centre by the capture) is near it, or one only part of a tile
     shows. The Water button cannot change them (they are in the capture,
     not the bake's water). Start: `find` the lake / the black object
     there over the bridge (layer, renderer, a script switching it by
     distance or trigger), then hold it on for the capture or retake those
     tiles with the player placed so it shows.
  3. **The sinkhole's inside is black** (the author wants it visible).
     Candidates, check in this order: the capture's far plane (`top - lo +
     60`, lo from a 9 x 9 height sample - may not reach the pit's floor),
     fog / shadow at that depth, a black blocker material (`LakeFake` /
     `black` - world-extract drops "black" shells for caves). A `shot`
     straight down over (0, 400, 0) with the freecam answers it.
     In 3D the terrain's heights cross the pit at about y 0 with that
     black photo, hiding the sinkhole's models below (SinkholeLower,
     SinkholeWater at -304): a hole in the 3D terrain there (the samples
     inside the rim) would show them - check the heights' shape first.
  4. **3D: the yacht is untextured** (flat grey). Its export: `yacht_body`
     / `yacht_chrome` (shader Standard, no `_MainTex`, colour 0.588),
     `YachtCovering` (Standard Specular, no texture), `yacht_glass`,
     `yacht_cushions` (Lux, no main texture, a top layer only) - so the
     game's look comes from something the export does not read (a detail
     / mask map, vertex colour, or the colour alone looks different with
     the game's lighting). Compare with a `shot` of the yacht in game, then
     read the material's properties with UnityPy (`world-extract.py`).

- **An exact 3D world** (author, 2026-10-01, the long-term goal): the terrain as the game draws
  it (splat textures, not photos under the models - "a tree model on top of a photo of the
  tree"), for planning 100% routes; the 2D map can keep photos. Also greebles / pickups /
  every prop, so the map is complete.
- Community spots on the site: keep them - they can carry **official category saves** (start
  states), not just teleport spots; shared runner times stay the main feature.

- **Discord webhooks and a game-knowledge bot** (author, QA #general
  2026-10-02, `1555519719468048445` + `1555519837353279564`): a webhook
  post when a runner sets a new PB with the tool on a community category
  spot; what else webhooks could do; how feasible a game-knowledge bot is
  in operating cost.
- **A maintainability review of the codebase** (author, QA #general
  2026-10-02, `1555520127967952967`): once the features are mostly done,
  review it so it can be picked up and updated easily in the future.
