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
Quick load (v0.24.68) - all bridge. maks: Quick load restarts in the
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
(v0.24.100) - both bridge, on the released builds. The God mode toggle
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
