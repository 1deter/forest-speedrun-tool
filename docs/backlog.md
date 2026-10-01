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

- **The map, after the 2026-10-01 recapture (author's review, screenshots in
  that session's transcript).** Tiles are the capture's `<ix>_<iz>` (x0
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
  4. **3D: the Water button does nothing** (flickers the textures - the
     terrain re-textures with the `-dry` photo, which is the same under
     the 3D sea plane). `map3d.js` draws its own sea plane (0x0c2231, at
     `terrain.meta.sea`) and the lakes as water (`world3d.js`): with a
     `-dry` layer chosen, hide the sea plane (and decide whether the lakes
     go too).
  5. **3D: dark "water" over land, coming and going with the camera
     angle** (Elevator Boost, the lakes round the sinkhole). The lakes'
     models (shader The Forest/Water, drawn as water since 2026-10-01)
     are larger than the lakes; the terrain they hide under is the coarse
     mesh (every 4th height sample, ~14 m) away from the detail patch, and
     the patch now moves with the camera (`lookRegion`) - so land that
     dips below the lake level in the coarse mesh shows water, and it
     changes as the patch moves. Options: clip the water against the real
     heights (a shader check against the heightmap), or draw lakes only
     inside the detail patch, or a polygon offset; compare with the game
     at one lake.
  6. **3D: the terrain flickers between its photo and white with the
     camera at one spot** - **when the camera's centre is outside the map's
     bounds** (author, confirmed). That fits `lookRegion` (map3d.js, added
     2026-10-01) in a loop: `regionAt` clamps the patch inside the map,
     so near the edge the centre never counts as covered and the patch is
     rebuilt every 0.4 s - and each `setRegion` calls `textures()`, which
     rebuilds **both** the island's 4096 px photo and the patch's (white
     while a new texture uploads). Fix: skip when the new region equals
     the current one (or test coverage against the clamped region), and
     rebuild the island's texture only on a layer change, not per region.
  7. **3D: the coast flickers / seems to move up and down while the camera
     moves.** Suspects, in order: the patch following the camera (coarse
     mesh, ~14 m between samples, swapped for the full-resolution patch
     where the camera stops - the shore line moves with it); the sea
     plane against the terrain at sea level (z-fighting - it has a
     polygon offset); `clip()` changing the near plane with the zoom
     (`dist * 0.004`, depth precision). Turning `lookRegion` off on the
     local site tells the first apart.
  8. **3D: the yacht is untextured** (flat grey). Its export: `yacht_body`
     / `yacht_chrome` (shader Standard, no `_MainTex`, colour 0.588),
     `YachtCovering` (Standard Specular, no texture), `yacht_glass`,
     `yacht_cushions` (Lux, no main texture, a top layer only) - so the
     game's look comes from something the export does not read (a detail
     / mask map, vertex colour, or the colour alone looks different with
     the game's lighting). Compare with a `shot` of the yacht in game, then
     read the material's properties with UnityPy (`world-extract.py`).
  9. **Turn the map 180 degrees: the snow at the top** (author: "this is
     what all maps are oriented at" - the runners' maps put south up).
     2D (`map.js`): draw rotated (x and z both reversed on screen), with
     the pointer's pan / pick / zoom-at-cursor, the grid labels and the
     north marker following; the aerial tiles are drawn per tile at world
     positions, so they rotate with the transform (a JPEG need not
     change). 3D (`map3d.js`): the start view (`fit`: `yaw` 0 -> pi), the
     follow camera unchanged. Check the spot page, the home page's map
     and anything else drawing `RunMap`.

- **An exact 3D world** (author, 2026-10-01, the long-term goal): the terrain as the game draws
  it (splat textures, not photos under the models - "a tree model on top of a photo of the
  tree"), for planning 100% routes; the 2D map can keep photos. Also greebles / pickups /
  every prop, so the map is complete.
- Community spots on the site: keep them - they can carry **official category saves** (start
  states), not just teleport spots; shared runner times stay the main feature.
