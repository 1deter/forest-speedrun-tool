# Confirmed in game

What has been confirmed in game, by version and by whom (moved out of CLAUDE.md, 2026-09-26). Add a few words per confirmation at the end.

Confirmed by the author: game-input block and freecam hold (v0.17.0), pitch
kept across teleport / window close (v0.17.1), every endgame split incl.
vault / gold door / red elevator (v0.18.x), quick-load and practice revive
(v0.19.2), cave teleport both ways (v0.19.1), Inventory tab item names
(v0.19.4), savestates in a cave in Creative (v0.20.1), quick-load without
the menu (v0.21.0), the self-updater end to end, segment start states on F7
both ways (v0.21.1); death at a start-state spot with practice mode off
restores it; a restore out of a cave sets the surface state; Go only
teleports and Restart restores; cross-save restores in place give **one
player and one inventory**; the ESC menu keeps its cursor when the window
closes over it; the fall revive has no stagger and **jump comes back at
once** (v0.22.6, author); text wraps and sits under its buttons; the
v0.23.0 census ran after every load without trouble (0.4-0.8 s); **the
load leak fixed** (v0.23.3-0.23.5: threads flat, heap flat, loads ~5 s;
building, chopping and killing across reloads fine); the menu route (exit
to title -> Continue) flat too, 10 trips (v0.23.7).

Confirmed by runners / the author on 2026-09-24 (v0.22.0-v0.24.30): the
retire warning on a new start state, the Practice list (unsaved reminder,
never sticks, "Save (n)"), no blood / no stagger, auto-restart (the flash
display to be improved), cannibals rebuilt as captured after a Quick load
(surface) and a Full load, cave panels healed and straightened, the
lighter, the book page, no landing damage / stagger after a mid-air
restore, checkpoints in order (keycard), Megan's cutscene fast-forward
after a Full load (held until she exists, player frozen, the spear back),
the endgame area and the lab floor after a Full load, the red elevator put
back by a Full load, pickups taken before a capture removed after a Full
load (surface), the Updates tab's "downloaded - restart to install";
the smash and swing cut on a reset, next swing at once (bridge,
v0.24.32-0.24.34); Megan after a Quick load - taken before, during or
after her transformation, babies and body cleared, the cutscene replayed
and fast-forwarded (bridge + author, v0.24.35), also straight after a
load from the title screen (v0.24.37); the fast-forwarded cutscene's
sounds put in step (log: `music_transformation` moved to 41.7 s; author:
"sounded perfect", v0.24.36); spears thrown since the capture removed
(v0.24.36); the red elevator after a Quick load (car back and ridable,
the hallway as at capture - `ElevatorKeeper`, `AreaKeeper`), a Go out of
the endgame after the ride (vault door cave and Sahara outside normal),
held axe / lighter usable after a Full load (v0.24.40-0.24.43, author
with the bridge; game-notes *The red elevator and the endgame areas*);
the survival book closed by a Quick load (v0.24.44, bridge); the
captured cannibals back at once after a Full load (v0.24.45, bridge);
the lighter kept through a Full load restart with swings (v0.24.46,
bridge, scripted swings); the swing / smash cut on a reset
(sxczurass, QA 1-5: Quick and Full load); a blueprint put away / brought back on Quick load,
Full load and F7 (v0.24.47, bridge); skinny families placed after a
Full load (v0.24.48, bridge); cave cannibals put back after Quick and
Full load, babies not doubled (v0.24.49-50, bridge, cave 6); cave 5's
coins / cash taken before a capture gone after a Full load, nothing
else removed, and back / gone as captured after a Quick load (v0.24.54,
bridge); every tab drawn in game, the Inventory tab filled on first open,
"What's new in v0.24.56 (installed)" in the Updates tab, the QA tab's
Mark / result / report zip (v0.24.56, bridge tab sweep: `OpenMyTab` on
each `_modules[i]` + `shot`); `capture` / `tp` refused at the title
screen (v0.24.59, `PlayerRef.AtTitleScreen`) and the notice drawn
over the main window (v0.24.59-60, bridge); trees chopped / half-chopped
since a capture regrown by a Quick load, their logs removed, bushes and
saplings cut after it back and their sticks removed, ones cut before it
left cut; a teleport from the lab to the surface clears the endgame
lighting (v0.24.61-62, bridge); no false "Axe Plane" pickups after Quick
loads (v0.24.63), a restore mid red-elevator ride keeps the car and
player down and the ride works again (v0.24.64), bushes cut at capture
stay cut after a Full load, also for a capture taken after restores
(v0.24.65-66), the plane axe taken before a capture not back after a
Quick load (v0.24.68) - all bridge. v0.24.129 (bridge): the post-restore
"can't carry any more" message hidden inside a restore, still shown
12 s after one; a hand-locked restore logs `hands still busy after 2 s`. v0.24.130-132
(bridge): passengers found / missing by seat, sorted; nature guide page
names from the book; no repeating title-screen Slow ticks. v0.24.133
(bridge): 43 nature guide entries named as printed in the book. maks: Quick load restarts in the
red elevator "tested and working" (v0.24.64 retest, 2026-09-26).
Bridge, v0.24.79-82: savestates 1.6 s and 4.6 s into the red elevator's
keycard cutscene, Quick / Full load / F7 / F7 mid-ride - ride replayed,
landed on the captured time, player free at the overlook; the vault
door (after a real trigger crossing; red corridor loaded after a Full
load) and the gold door the same, Quick and Full load.

Confirmed 2026-09-25/26 (bridge, the author's clicks where noted):
maks's vanished window was hide-all UI (author); sticks around a
pooled tree back as captured after a live Quick load, a later spawn and
a Full load (v0.24.70); Export 315 KB with all sections, Import of a new
id and a Replace (author's clicks), an imported start state restoring
under an id with `/` (v0.24.71); a community pack downloaded, not
re-downloaded, restarted from, and removed with its start state; the
demo pack fetched from GitHub at startup (v0.24.72, v0.24.76); a
title-screen restore refused (v0.24.73); new spots get `s-` ids and the
editor has no Id field (v0.24.74); a 45-degree box drawn diagonal
(v0.24.75); both coordinate fields drawn, no overlap (v0.24.77). The
author (2026-09-26): a Community entry's read-only view and its
Duplicate, typing into coordinate fields, the Import list's wrapped
rows - "all 3 seem fine"; letters refused in a coordinate box (v0.24.78),
no trailing spaces and no unsaved marker for them (v0.24.83) - author.
A custom wall blueprint brought back by a Quick load after a wall was
placed shows only its own icons (v0.24.84, bridge, maks's start state).
A Quick load in the red elevator car with the endgame unloaded loads it
first and keeps the player in the car (v0.24.85, bridge).
A reset 0.5-0.7 s into opening the book leaves the pitch free (v0.24.91,
bridge); the performance patches cut idle garbage to 216 KB/s with no
visible change, a cave entry runs one asset sweep (v0.24.92-94, bridge).
The endgame loaded in the background by a Full load and by a Quick load
that needs it - no frame over 100 ms, trigger / HUD / anim prefabs as
after the game's load, the red elevator rides after it (v0.24.99); a
teleport mid-ride stops the ride and the vault door cave stays whole
(v0.24.100) - both bridge, on the released builds. On a runner's machine
too (Cheesecake, v0.24.116, QA list item 2): two Quick loads of a
post-vault-door start state loaded the endgame in the background in
2.8 / 3.3 s (longest frame 25 ms), areas the same as at capture, nothing
missing; held ~4-5 s, camera free. The God mode toggle
keeps `Cheats.GodMode` on (re-set within a second when cleared), and
unticking clears it only when the toggle set it (v0.24.101, bridge).
With toggle crouch, Quick and Full loads put back the captured stance
both ways - crouched -> standing capture stands, standing -> crouched
capture crouches (v0.24.102, bridge, released build). The QA tab's note
box wraps a 234-character note over lines, and Write report puts it in
report.txt without a Mark (v0.24.103, bridge; the per-item boxes use the
same helper - the bridge cannot write an array element to test one).
v0.24.106 (bridge): no Savestates tab; Debug views ends with its toggles and
the Memory section; Deaths has no Clear blood button; `restore` still
works. A savestate taken on the cave 4 rope puts the player back on it - Quick
load from the surface and from inside the cave, Full load - no launch;
`tp` lets go of a climb (v0.24.104-105, bridge, released builds; maks's
own file reproduced the ~48 m/s launch first).

A Go / `tp` to the vault entrance keeps or sets the endgame flag, and the
door then loads the endgame; the surface and behind the `LoadEndgame` box
still clear it (v0.24.112, bridge, released build).

`Frame (30 s):` lines and `FrameTimer.Snapshot` report waiting / scripts /
each camera (v0.24.114-115, bridge). The two camera patches act and let go
live, 5.11 -> 4.5 ms/frame on the surface, and the endgame plane screen
shows the same picture rendered on demand (v0.24.116, bridge, released
build).
Sun shadows every second frame (`SunShadowsEveryOtherFrame`, v0.24.125):
no visible difference in play (author, 2026-09-26); 0.66 -> 0.32 ms a
frame (bridge). Caves: no grass bending (v0.24.128): off in the cave,
back on in the frame you leave, no hitch at the entry (bridge).

Logs in the inventory (v0.24.134-135, bridge, released builds): arm logs
move to the store on switching on, a real `PickUp.Collect` stores one, the
cap refuses with a notice, the axe equips with a full store,
`RemoveItem(78)` takes from the store, off puts 2 back in the arms and
drops the rest, Quick and Full load give back the captured count.

Freecam with the game's lighting (v0.24.136, bridge, released build):
the flown view keeps fog, shadows and post-processing (screenshots on /
flown / outside / off); `PlayerCamLocation.PlayerLoc` and the Grabber
stay at the player; off puts the camera's local pose, its 6 children and
both paused scripts back. WASD / mouse flying not driven by hand yet.

Cross-save restore keeps the inventory views (v0.24.137, bridge,
released build, Ruben's Megan-fight start state in Slot 2): `deleted 0`,
`kept 54 inventory view object(s)`, no duplicate views; the inventory
opens and closes (our close and the game's toggle), timeScale back to 1.

Death reload out of the Megan fight without the crash (v0.24.140: the
controller's Update skipped in the exact state that crashed v0.24.139,
reload carried on; earlier, v0.24.139,
bridge, released build, the author's switches, Ruben's `ruben-megan`
state in Slot 2): two BossWake reloads (one the author's own death) load
and play on; forcing the controller's camera to be drawn last (every
other camera off) puts the scene grass camera back - by the tick guard,
and with that blinded by the Update prefix (`its Update skipped once`) -
and a forced texture rebuild then runs safely.

- **Death reload freeze out of the Megan fight gone (v0.24.142-143,
  bridge, 2026-09-27, Slot 2 + `ruben-megan`):** graph updates after a
  Quick load 0.3-0.6 s (were one 16-60 s update), a repeat Quick load
  0.65 s (was 31.5 s), `Death (BossWake)` reload waits 116 ms in
  `AstarPath.OnDestroy` (was 28.6 s), no crash.
- **Mid-fight Megan Quick load leaves one Megan (v0.24.144, bridge,
  2026-09-27, Slot 2 + `ruben-megan`):** boss moved 116 m from her seat,
  then a Quick load: `removed girlMutant(Clone), girlSpawnGo`, one
  `girlMutant(Clone)` at the seat, transformation replayed (v0.24.143
  left two).
- **Endgame load trigger crossed outside the vault door's cutscene is the
  game's own load (v0.24.145, bridge, 2026-09-27, Slot 2):** `Performance:
  endgame load in play outside a cutscene ... no hold`, one 4610 ms frame,
  the player not pinned (maks was held for the whole load before).
- **Splits table (v0.24.146-147, bridge, 2026-09-27, Slot 2, `s-splitstest01`):**
  two teleport runs then a third mid-segment: panel + Runs tab showed PB
  splits, +0.50 on the first split, a live +5.04 on the current row, sum
  of best 7.11 and best possible 13.15 (all hand-checked); attempts saved
  with `2 split time(s) saved, runner 'deter' (r-...)`; the tab scrolls
  with the options open (v0.24.147).
- **Splits follow-ups (v0.24.149-151, bridge, 2026-09-27):** column titles
  on the panel, background opacity (30% shown), one runner field
  pre-filled `deter`, the finished run kept through an auto-restart
  (-1.11 gold / -0.52 / -0.53 still shown after it), times at 3 decimals.
  Dragging the panel: works (author, v0.24.151, windowed; log `Splits
  panel moved to (1066, 0)`); the left edge fixed in v0.24.152 and confirmed
  by the author (flush, no jump).
- **Run uploads (v0.24.153)**, over the bridge against a local copy of the
  site: first upload registers (`Upload: registered on ... as 'deter'`),
  "Upload this spot's saved runs" sent 6, a finished run uploaded by
  itself, and with the site down it waited ("retrying in 30 s") and was
  sent when the site came back.
- **Other runners' PBs as comparisons (v0.24.155)**, over the bridge
  against a local site with a fake second runner: `Site board ...: 1
  runner(s), 1 to compare with.`, the pick fetched their run (`Site run 1
  (...): 52 samples`), table titled "vs" them with split deltas, live HUD
  delta and ghost in a run, own sum of best / attempts unchanged, the
  cycle key Runner -> Best -> ... -> Runner.
- **v0.24.161 item track hooks** (bridge, 2026-09-27, Slot 2): `Run item
  track: 9 inventory methods watched.`; `ItemCounter.Version` 6 -> 8 on
  `AddItem`, -> 10 on `RemoveItem`, unchanged over 3 s idle. (v0.24.160's
  fixed channels had read 3 sodas as 3 the same way.) Not seen yet: the
  `i|` lines of a real finished run - the site's Carrying row was checked
  with a hand-made track on the local site.
- **v0.24.177 building removals by place** (bridge, 2026-10-01, Slot 2,
  installed through the updater): three removals at once, two 50 m apart
  and one 780 m away -> 2 graph updates (40 x 31 m and the lone one),
  done in 0.3 s; on v0.24.176 two of them made one 500 x 600 m update of
  6.8 s.
- **Site: another build's world files refused** (19b319b, 2026-10-01):
  live `?v=<other>` -> 404 `no-store`; local end-to-end: a page holding
  build A, `world.json` re-uploaded as B -> 44 refused, re-read, started
  over, Cave 6 drawn from B's files.
- **Map polish, the game items (bridge + headless, 2026-10-01, v0.24.180-
  181):** a lake's `LakeFake` switch follows its `LOD_GroupToggle`
  distance (raised: the real lake within a second, back: the stand-in);
  test tiles 10_2 / 11_2 without the black patch, 8_8 with the sinkhole's
  floor; the full recapture's 25 progress lines all `weather Idle / rain
  None, overcast 0.1` (v0.24.179's hold); the red-edge scan has 13_12 at
  the top of the old set (+8.6), gone from the new; the yacht dump 198
  parts, its 3D look against a game `shot` from the same camera; the 3D
  sinkhole open on the local and live site. Not yet seen by the author.
- **3D world caves (site, 86a9d87, 2026-10-01)** - the author on the live
  site: "looks good now" - cave pieces at their placeholder's scale and the
  underground cutaway. Still open: a couple of models missing and the cave
  floor (CLAUDE.md *Pick up here*).
- **Autosplitter events + `.lss` comparisons (v0.24.184-185, bridge,
  2026-10-01)** - cave enter / exit (cave-to-cave too), passenger count,
  clothing ("Bathrobe" named from the game's database), `moving` fired;
  teleports leave `_currentCave` alone (no fake cave splits). The author's
  Coop Any% `.lss` matched 4/4 by name; a run started on `moving` split on
  Cave 6 enter / exit with deltas against the file's PB. Not seen:
  `hold-interact` from a real hold; the author's eyes on the UI.

Checked over the bridge on 2026-10-01 night (v0.24.186-189, not yet by a
runner): the one-click LiveSplit import with the author's own `.lss`
(4 rows named and matched, velocity start, cave 6 enter / exit and a
rebreather pickup split, F12 finish, its PB picked as the comparison);
a velocity-start spot stays armed after a restart from a cave; a Quick
load un-ticks a nature guide entry found since the capture (its mark
back and hidden) and ticks one the capture had; the book's to-do tasks
set up again after a Quick load (GOs, status callback); a capture with
the book open restores with the axe and the lit lighter in the hands.

Checked over the bridge 2026-10-01 night (v0.24.190-193): an imported
runner's faster run is offered as "another runner (file)" and races in the
table while own attempts / sum of best leave it out; the 3D world's four
kind switches each hide their kind (local + live headless); spot pages'
link-preview tags live; a Quick load with the capture's items in hand
keeps the lighter lit (and a different hand still re-equips); practice
mode and run lines kept across a game restart; a Go to a spot with
`cave = cave06` sets the game's current cave, streams only Cave 6's
props (16 scenes before) and fires no cave event.

**2026-10-02, bridge (v0.24.201-202, Slot 1 Creative, rides built over
the bridge and entered by calls):** a capture on a zipline restores on
the line at the captured spot at its speed (21 m/s), Quick and Full
load; pushing a sled - back pushing it at its place, also from a
v0.24.200 capture whose sled had gone to the world origin; a capture in
flight keeps gliding, and its restore is back in the air gliding at the
captured velocity; a cliff climb with the climbing axe is back on the
cliff. The restore deletes a glider dropped since (not in the save).

**2026-10-02, bridge (v0.24.203, Slot 1 Creative, the Quick load audit's
last three cases):** blueprints placed, part-filled and built since the
capture are deleted and a blueprint finished since comes back (dump diff
clean); a blueprint given logs / sticks since is rebuilt from the save -
drawn as captured, build HUD 6 / 6 / 6 as a Full load gives, dump diff
clean; a drifted HUD tally is recounted (`item 57 0 -> 6`). A capture
with the inventory open and items on the crafting cog closes it first
(1.2 s, was 121 s), the items back in the inventory; a Quick load and a
Full load started with the inventory open (items on the cog) close it
and give back the captured counts, timeScale 1.

Confirmed over the bridge 2026-10-02 (v0.24.204-205): the splits table's
PB chance (no PB, Congrats, "< 0.01%", ~50 / 25 / 16.7% as computed by hand
from the pool) and total playtime (live while running) lines, panel and
Runs tab; unfinished runs written to unfinished.txt on abort / re-arm.

Confirmed over the bridge 2026-10-02 (v0.24.206-209): run mode - a Normal
and a Creative new game each start an attempt (log + HUD "RUN MODE -
attempt 1"); Go and F7 refused (the player did not move); god mode switched
on in Deaths stays off (Cheats.GodMode False) and comes back after End run
mode; F2 refused in play, opens over the pause menu and closes with it;
experimental perf patches suspended; the title screen ends the attempt as a
reset; loading Slot 1 ends run mode; the report file and Runs tab findings
(Steam build recognised, no other mods, BepInEx's patches not counted).

Confirmed over the bridge 2026-10-02 (v0.24.211-213): a start state from
the title screen (the menu's load, in game after 11 s, slot 1); Normal ->
Creative (GameSetup.Game Creative, GodMode / InfiniteEnergy / NoSurvival
on) and back (all off); a capture writes `basedifficulty`; a run spot's
Restart from the title screen starts attempt 1 (report names the spot),
another spot's Restart refused, the run spot's Restart = reset to attempt
2, End run mode unlocks.

Confirmed over the bridge 2026-10-02 (v0.24.216, live site): run mode codes
and receipts - Start run mode by hand in Slot 1 got the site's start code
after 142 ms; the code drawn top centre, changing each second, and still
drawn with the overlay hidden (F5); the checkpoint at step 61 taken; End run
mode sent the log, judged red for the bridge flag only (nonce + checkpoint
matched); `GET /api/attempts/<id>`, `/log` and `/code/<code>` (lower case
typed) answer; the Runs tab lists it with Copy link; the owner's DELETE
removes it. Not seen: a green attempt (needs the bridge off), a finished
timed run ending an attempt, an offline attempt sent later.

Confirmed 2026-10-02 (v0.24.217, local site + bridge): the attempt page (`/attempt/<id>`) for green / amber / red attempts, the check-a-code box (a real code found, a wrong one explained), the report's findings judged (another mod red, allowed on /admin/allowed -> allowed, taken off -> red), phone width without sideways scroll; `RunIntegrity.WriteTypeHashes` gives the same 3,682 type hashes twice and after a restart (289 ms, main thread); a clean install hashes no types at startup; a synthetic changed-game report is named by area with the parts behind the fold. Not seen by the author yet.

Confirmed 2026-10-02 (v0.24.218, bridge + local site): run categories - the first speedrun.com sync (24 drafts, one per category x difficulty), /admin's Categories tab (edit, save as a new version, published to `/api/categories.txt`); the plugin's startup fetch from the live site ("no published categories yet") and from the local one (2), Start run mode under a picked category: an allowed feature unlocked (`Locks godmode` false) while Go stays refused, a forced one on with the runner's own setting off (Manhunt: logs in the inventory + fast building, ON NOW on the HUD, off again after End run mode), no code on screen without anti-splice; the attempt page names the category version, its unlocked features, "set up as Manhunt asks", the forced features, and does not judge the recording's timing. Not seen: a run spot naming a category (no community run spot yet), a category with a locked Reload save on death, a difficulty mismatch in game (tested on the site only), the author's eyes on any of it.

Confirmed 2026-10-02 (v0.24.220, bridge + local site): category values and the automatic refresh - Manhunt saved on /admin with log cap 8, Rock 50, Stick 30 and an unknown item; in a run started by hand the HUD read `Logs 0 / 8`, the game's `GetMaxAmountOf` gave 60 / 30 (after v4), the Inventory tab showed the category's numbers greyed and "Not in the game's item list, so not capped: NotAnItem"; a second check answered 304 ("no change"); v4 published mid-run reached the game by the 2-minute check ("Changed: Manhunt v4 - from the next attempt") while the attempt kept v3; End run mode put the runner's own back (caps off, Rock 5, log cap 5), the next attempt ran v4. The live site sends the ETag and answers 304. Not seen: a run spot's start fetching first, the author's eyes on it.

Confirmed 2026-10-02 (site only, local site): /admin's item caps picker - hints from the game's 231 items (prefix first; listed items and Log left out), Enter / click add at the game's cap, cap edited, Remove, an old unknown name tagged "not a game item", Save wrote `cap Rock = 75` etc. as v5; no sideways scroll at 375 px. Live `items.json` served. Not seen by the author.

Confirmed 2026-10-02 (site, the author on the live site): the 2D map smooth at max zoom - the relief drawn only on screen (`reliefPart`; it was ~700,000 px wide at 200 px/m), one draw per frame (`draw()` asks, `drawNow()` draws), bilinear when enlarging, zoom out capped at the world x 1.5.

Confirmed 2026-10-02 (site, the author on the live site: "seems to work well"): the 3D world's far copies (build 1790961787) - each instance drawn with the lightest copy under a pixel of error at its distance; seen on a MacBook and an iPhone 13 mini (author, 2026-10-02).

Confirmed 2026-10-02 (site, the author on the live site, Follow in a cave): the 3D view's view-cone culling - "it's fine"; the wall over the right of the view was the Follow camera inside the cave rock, not culling.

Confirmed 2026-10-03 (site, the author on the live site): the photo map's lakes (BigLake_v2, the inland basins, the sinkhole's pool - "looks good now"); the 3D view's middle lake as surface water ("seems fine"); the south mountains' textures and the overlook / boss room no longer black; caves on the 2D map ("a little bit hard to read but it's fine"); Follow's camera kept out of cave rock ("works great now"); ground-level and per-instance culling (nothing odd seen); the long N-S snow shadow gone in the v0.24.222 recapture.

Confirmed 2026-10-03 (game, bridge, v0.24.230): fall damage cancel detection - an 82 m drop onto the ground silent (judged at 55 m/s), a drop into the big lake silent (swimming), a steep terrain slope silent (the game holds the player at ~3 m/s), the cancel's state faked (`prevVelocity` zeroed through an 82 m fall) reported with 55 m/s, judged 0, 100 damage; installed through the in-game updater.
Confirmed 2026-10-03 (game, bridge, v0.24.229): cave state force load detection - three normal Cave 1 entries silent (let go 12.6 m under the terrain), an entry cut short reported (let go at the mouth 1.9 m above, in cave state); every crawl / swim entrance's normal let-go is 7.5 m or more under the terrain.

Confirmed 2026-10-04 (game, the author by hand): the trap boost - the large swinging rock trap's knockback stacks in the pause menu like a bomb's (game-notes *Speedrun tech*); "cool i guess but longer to build and less versatile than a small bomb trap".

Confirmed 2026-10-03 (the author, live): the game-knowledge bot (`bot/`) runs on the VPS (`forest-bot` container, deployed by CI) and answers in the QA server - mentions and `/ask` work after v1daab00's fixes; first answers through Gemini 3.8 Flash were correct and sourced (bomb boost fps, max fall speed); the decompiled code (3,668 files) and the embedding model loaded there.

Confirmed 2026-10-04 (game, bridge, v0.24.235-236 installed through the in-game updater; not seen by the author): the info box settings (Settings -> Info box (HUD): compact "FO v0.24.236" and short values, text size 24 with the box grown to fit and nothing clipped, title line off, Reset the info box back to defaults); polygon zones (a `poly` checkpoint read from the file, the editor's p1-p5 rows with Here / x, Add point here and x recentre the zone, the prism drawn with the window closed and during a run); zone display during a run (All draws 2 zones, Next only 1, Off none); quitting to the title mid-run ("Run aborted: the game went back to the title screen. Kept as an unfinished attempt." on screen, `aborted - left the level` + `unfinished after` log lines, `runs/<id>/unfinished.txt` written); the Runs tab's dates (each attempt, the PB and every gold); a capture over a start state asks "Sure?" and names the one it replaces; Deaths' "When I die" (each of the five choices with its own description and Next death line; a death on Automatic restarted the spot with a start state - `Death (Capture, Automatic)`; on Revive at the current spot health came back and nothing was restored - `Death (Capture, ReviveAtSpot)`); weather in savestates (a Quick load of a clear capture during rain: `weather: Idle ... put back (was Raining (Heavy) ...)`, State Idle; a Full load of a rainy capture: `Savestate after the load: weather: Raining (Heavy) ... put back`, State Raining / Heavy). Shots `v236-*` in the bridge folder.

Confirmed 2026-10-04 (game, bridge, v0.24.237-241 installed through the in-game updater; not seen by the author): the results panel after a timed run (first run "NEW PB", the second's delta, segment and lost time, best possible, PB chance, attempts, playtime; `Results panel:` lines; in run mode also the attempt, its last code, report and receipt state); event checkpoints `tree-cut` (TreeHealth.Hit), `used` (a soda eaten, `Game event: used (Soda)` - one checkpoint, not two), `slept` (Stats.GoToSleep), each moving the run on once; a tp into the vault entrance (endgame flag set) and back out fired no `endgame-area-enter` / `-leave`; checkpoint states (Capture at checkpoints: `.cp1` / `.cp2` written, ~295 ms of frame work each; Restart from checkpoint 2 -> "resumed from checkpoint 2/2" at its clock, from checkpoint 1 too); Reload the save in place (a death at the vault door, save and player both in the endgame: `reloaded in place in 0.86 s`; a death on the surface: `in place cannot apply: the save is in the endgame and you are not` and the game's own load); a surface capture after a tp out of the lab restored with no endgame loading, Quick and Full (Full 5.9 s, `endgame no`); `tp 428 78 -4 90` faces 90 (player and camera); the trajectory preview (a fall from 115 m: predicted landing 0.0 m off, a running jump 0.1 m, the HUD's Flight line); TAS record and replay (651 frames with Run / Vertical / Jump, replay max drift 0.22 m, 0.00 m at the end, 1.896 vs 1.832 s); run mode timing a run spot's Restart with F9 off (`timed by run mode (practice mode is off)`), the audit's event lines (items, tree, used) in the attempt log. Shots `v241-*` in the bridge folder.

Confirmed 2026-10-04 (game, bridge, v0.24.242 installed through the in-game updater; not seen by the author): replay buildings and markers. A timed practice run in Creative recorded `b|placed|LeafShelter` (book -> `CreateBuilding` -> `press Build`; the Trigger-child test held) and `b|built|LeafShelter` (holding Build on it) with the same place, rotation (6.4, 0.6, 10.4) and box (6.1 x 5.7 x 8.4, centre -0.5 / 0.4 / -0.9) as the finished `LeafHutBuilt(Clone)` (same transform; its LOD mesh 6.0 x 5.4 x 8.4); a Shelter blueprint cancelled (`CancelPlace`) left no `b|` or `Replay:` line; `e|` lines for built, crafted (StickUpgraded via the crafting cog), used (Soda) and pause-open / pause-close ("open 2.5 s"); one `Replay:` line per structure. On the next run the comparison drew the placed box from 8 s and the finished one later, markers with labels, the end state while idle; Runs tab "Replay shows: buildings / interaction markers" each switch the drawing off and on, and "Comparison run: 5 interactions, 2 buildings". Seen, not fixed: every replay line draws pure white in game (day and night; the run lines' centres too - the colour shows only in the bloom), so blueprint vs finished and the group colours cannot be told apart; labels of interactions done in one spot print over each other. Shots `bw-*` in the bridge folder.

Confirmed 2026-10-04 (game, bridge, v0.24.244 installed through the in-game updater; not seen by the author): **idle garbage fixed (v0.24.244)** - window closed, standing still on Slot 1: five 30 s `Perf` lines in a row read `overlay +4.8 / 4.8 / 5.3 / 5.7 / 5.7 KB/s`, `GC x0`, heap +54-60 KB/s (before: ~150 KB/s), the `(most: ...)` list names `collectibles ~2.1`, `runinfo ~2.0-2.5` (+ `practicerun 0.3`, `runmode 0.1` once); no `Slow tick` in the idle stretch. During a timed run / replay camera with bridge commands the overlay read +67 to +233 KB/s (`practicerun` 12-119, `bridge` 51-110, `GC x1` once): the replay and run paths still allocate - not looked into. **Ghost figure (v0.24.243)**: with a comparison run the ghost is a white wire figure (head sphere, capsule body, arm ticks, legs), drawn at the comparison's position for the run time (checked at run 2.0 s and 3.3 s against the first run's tp points); the ghost look setting read `Figure`. **Replay camera (v0.24.243)**: `ToggleReplayCamera` is refused while a run is going ("A run is going - finish or abort it first.") and with auto-restart on a finished run re-arms and starts at once; with the run Armed it logs `Replay camera: on - ... 5.11 s, 140 look sample(s), 141 position(s), view chase`, shows the controls notice and the HUD line `Replay chase 00:01.965 / 00:05.110 x1`; chase = the camera behind the figure (through foliage here), `FirstPerson` = the run's own look through the zone shells, `Trajectory` = the figure with head and arms from outside; it plays to the end ("end - Space plays again"); toggled off the camera was back at the player (camera 1.96 m above the player, hands and held lighter / axe visible), log `Replay camera: off - turned off`. Shots `ghost-c`, `replay-chase5`, `replay-fp`, `replay-traj`, `replay-off` in the bridge folder.

Confirmed 2026-10-04 (game, bridge, v0.24.245 installed through the in-game updater; not seen by the author): the Map tab. First open logged `Map: relief made and cached as relief-3500x3500-1024.png in 0.35 s`; after a game restart `Map: relief read from relief-3500x3500-1024.png in 0.09 s` (the cache works). The tab shows the shaded island relief (water, sand, snow), the category legend, 17 spots (7 underground triangles, squares, hollow community ones), the white player arrow, the Fit / On me / zoom buttons and the help text, nothing clipped; `ZoomAt 4` zooms and `CentreOn` pans; a QuickSaveSpot appeared at the player with the white selection ring, its name / "My spots, on the surface at 428, 78, -4" under the map and Go / Centre / Edit in Practice enabled, spot count 18. Shots `map245-a/b/c` in the bridge folder.

Confirmed 2026-10-05 (game, bridge, v0.24.247 installed through the in-game updater; not seen by the author): **v0.24.247** - after a new Normal game, `restore <name> load` finishes in 10.7 s with `prefab list filled as the menu's load does (352 prefabs; empty after a new game this launch)` (no LOADING hang); in a new Hard game a Quick load of that Normal capture logs `restores with a Full load - captured in a Normal game, this one is Hard - the load sets the difficulty` and `difficulty Hard -> Normal for the load`; the death reload after a new game (`Death (Capture): quick-loading without the menu`, `Quick-load: loading slot 0 from in game (no menu)`) came back alive, no hang (the load logs no end line; the player was back at the plane intro). **v0.24.246** - a timed run across a Full load: log `load-removed 00:37.435 (1 load, 8.16 s)` and the results panel's "Load-removed time" line, `.run` carries `loads|1|8.159` (and `loads|0|0.000` for a run with none); the Perf line names `practicerun 37.9, runinfo 3.6` among the allocators; the run line draws yellow in game (shot `t246-run-colours2`; not white). Not checked: colours by night, the ghost and replay overlays.

Confirmed 2026-10-05 (game, bridge, v0.24.248 installed through the in-game updater; not seen by the author): **website spots in game (v0.24.248)** - Import's "Website spots" lists 4 (`Website spots: 4 listed.`, "1st logboost by maks - 16 runs, best 0:05.72"); Add logs `added 's-6a42bcf056fb'`, writes `segments/website.txt`, the entry shows under "Website" read-only ("Community spot (read-only)"), Go teleports; armed, the board reads `Other runners: 1 on this version of the spot` (maks, 5.721 s); Update logs `updated`, Remove logs `removed` and deletes website.txt. **Delete clears the armed spot (v0.24.248)** - `Run 's-594113f10bdf': disarmed - the spot was deleted.`, HUD "Run: no timed segment selected". **Delete comes off the site (v0.24.248)** - a spot with an uploaded run: `Delete: ... on https://forest.deter.cloud - HTTP 200`, gone from /api/spots.txt; a never-uploaded one: `HTTP 404 (nothing on the site)`. Offline retry not checked. Shots `web-added`, `web-runs4`, `del-after1`.


Confirmed 2026-10-07 (game, bridge, v0.24.249): **unfinished run's red line (T-0119)** - restart mid-run keeps the line (`_failedLine` 5 points, red in shot `t119-failed-line-on`), the next unfinished run replaces it, Line options off draws none (`FailedCount 0`). **Run lines off with a marker up (T-0027, marker half)** - `WantsLateDraw` False, only the beacon drawn (shot `t27-lines-off-marker-up`); replay half not checked. **Run lines cleared on a plain spot (T-0049, half)** - counts 0 after Go. Not clean: Go from a running segment to another timed one leaves the first run's red line (ArmRun's KeepFailed after LoadAttemptsFor). **Build mission tally after an in-place restore (T-0052)** - `_amountNeeded` 16 -> 0, ghost gone; the HUD text itself not seen. Bridge 2026-10-07.
Confirmed 2026-10-07 (game, bridge, v0.24.250): **no stutter on a run finish (T-0150)** - 10 manual-spot finishes took 3 ms each (one 8 ms, one 85 ms with a GC; was 26-33), no "Slow tick: practicerun" on a finish frame, no "Plane site:" warning, `plane|355.700|66.275|1055.419|11.979` in the .run. bridge 2026-10-07.

Confirmed 2026-10-08 (game, bridge, v0.24.252): **no hitches after an in-place spot restart (T-0148)** - Labskip Jumping Section, restarts 2-6 no `Load timing: hitch` after `in place: done`, `Plane wreck:` line each time, `after the load: 36 ms, 0 scene search(es)` (was ~420 ms of two frames), post-restore frames 3-5 ms; wreck at a new place not checked. bridge 2026-10-08.

Confirmed 2026-10-08 (game, bridge, v0.24.253): **endgame sun post-process material leak stopped (T-0149)** - log "Performance patch 'SunPostProcessKeepMaterial': on."; Labskip spot, Material count 2321 -> 2321 over 5 s and blitMaterial id -59206 unchanged; switch off 2636 -> 4236 (+1600) and id changes; on again 5643 -> 5643, id -164704 steady; shots `t0149-on` / `t0149-off` look the same. bridge 2026-10-08.

Confirmed 2026-10-08 (game, bridge, v0.24.254): **a restart into the red elevator after its ride keeps the endgame loaded (T-0151)** - 5 restarts of Elevator Boost 6.5 s into the ride, player at (-712.17, -430.72, 968.85) each time, no ExitEndgame / endgame_streaming unload after; a tp lab to surface still sends ExitEndgame. bridge 2026-10-08.

Confirmed 2026-10-08 (game, bridge, v0.24.255): **aerial capture from the endgame leaves the area and returns home (T-0192)** - log "Aerial capture: endgame left as walking out does (ExitEndgame sent)", then "endgame flag set (vault entrance...)", player back at (44.52, -383.77, 1323.38); **log cap written once typing stops (T-0193)** - flush path driven by hand (typing not drivable): held until the deadline, cap 8 kept after a restart, no hitch line on the writes; **run mode refuses bridge go / restart with the refusal text (T-0110)** - "Go is locked during a run", player did not move, e2e runmode pass. bridge 2026-10-08.
