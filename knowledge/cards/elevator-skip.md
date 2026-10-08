---
id: elevator-skip
title: The red elevator (no keycard needed) and the elevator skip
aliases: red elevator, elevator skip, elevator clip, elevator boost, hell corridor elevator, gold keycard elevator, keycard 242, elevator ride, elevator door, 25 seconds elevator, elevator wait
tags: endgame, tech, clip
confidence: live
checked: 2026-10-03
sources: game-notes "Speedrun tech and the endgame gate" (Overnight sweep: Red elevator: no keycard anywhere); "Endgame splits"; sxczurass's Creative Bombless Any% Guide (2025)
related: lab-skip, endgame-gate, smash-clip, endgame-splits
code: TheForest.World.ElevatorSystem.GotoRemotePoint, TheForest.World.ElevatorSystem.Goto, ButtonDoorSystem.ActivateButton, AutomatedDoorSystem, AnimationSequence, activateKeypadDoor
---

# The red elevator and the elevator skip

The red elevator in the Hell Corridor takes you up to the overlook and the
control room (the ending). Two facts make it a speedrun target:
1. **It checks no keycard.** The gold keycard animation plays if you have
   one, but nothing in the elevator's chain asks whether you own it.
2. **The ride is a teleport followed by a 25 s wait in a locked car.**
   Clipping out of the car through its thin door ("elevator skip") saves up
   to those 25 seconds.

## The chain (live wiring + code)

`Sections/HellCorridor/Elevator_01a`:
1. The **Enter Button** (`ButtonDoorSystem.ActivateButton`) opens and
   unlocks the car door.
2. Take on `Trigger_Elevator` (car door closed) -> `AnimationSequence`
   stage 1 -> `PlayerPositionTest1` (within 7 m of the car) ->
   `PlayerPositionTest2` (inside, in front of the trigger) ->
   `ElevatorSystem.GotoRemotePoint`.
3. `setKeycardId(242)` there **only picks the animation**; nothing calls
   `Owns(242)`.

Live without keycard 242: a 5 s wait (no animation played, the player not
moved) -> the car and the player **teleported together** (relative offset
kept) to the overlook (-542.44, 704.79, -1967.46) -> **25 s with the car
door locked** -> the door opens.

## One ride only

`GotoRemotePoint` runs only `if (_useLimit <= 0 || _useCount < _useLimit)`,
and every ride adds one to `_useCount`. The red elevator's use limit is **1**
(game-notes: "Use limit 1") - a second ride does nothing until the lab is
reloaded fresh [code; the value of 1 from the notes, not re-read live].

## Why the car is locked for 25 s

During the ride an object `MovingDummy` is active; its `OnEnableProxy`
sends `Lock` to the car door, its `OnDisableProxy` sends `Unlock`. The door
(`AutomatedDoorSystem`: `_alpha`, `_state`, `_locked`) ignores its trigger
while locked - entering the door trigger mid-ride does nothing; after the
ride it opens the door [live]. `Lock()` only sets the flag: a door that is
already open stays open while "locked" (the leaves move only in `Update`,
which turns off once the door finishes moving).

## The ride is a teleport

`ElevatorSystem.Goto` sets the car at the top and puts the player at the
same offset from it - twice, one physics step apart. So **a player outside
the door plane at that moment arrives outside the top door too** [live].

## The elevator skip

The door leaves are **0.1 m boxes**. Clipping through them after arriving
skips the rest of the 25 s wait. Live: a capsule set 0.07 m past a leaf's
middle is pushed out the far side by depenetration - so the clip needs only
a few centimetres past mid-leaf. From the arrival spot a plain sprint stops
against the leaf (x -538.27) [live]. The runners use the axe smash clip
(see `smash-clip`); it has not been reproduced in our tests, so the exact
angle and timing are the runners' knowledge.

## Splits

The ride (`ElevatorSystem.Goto`, with `_playKeycardAnim`) sends the
player's keycard door routine directly (`openDoorRoutine`), then waits 5 s
before the teleport - the same routine that raises the endgame cutscene
flag at the doors, so the autosplitter's "Gold Keycard (Red Elevator)"
split fires on it with the keycard. On the keyless ride live, the 5 s wait
happened but no animation played; whether the flag (and so the split) rose
is **not confirmed**. See `endgame-splits`.

## The other elevator

The overlook's second elevator (`Elevator_ToSnowCave EG`) is **one-way
down** to the snow cave: its car starts at the top and its trigger is
disabled once it moves - there is no way to ride it up from below [code +
live].

## Evidence

Live over the bridge (2026-10-03): the ride without keycard 242, the lock
during the ride, the teleport of a player outside the door, the sprint
stop, the depenetration test. Wiring read live from the UnityEvents
(`m_PersistentCalls`); code decompiled. Our scripted smash attempts at the
door did not clip (`smash-clip`) [dev].

## Open questions

- The runners' exact clip setup out of the car (angle, corner, fps).
