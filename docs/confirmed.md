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
