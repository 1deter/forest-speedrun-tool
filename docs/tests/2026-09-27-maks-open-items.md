# maks - everything still open (2026-09-27, v0.24.148)

Consolidated on the author's request (#general, reply to the reminder
`1553725259579002942`: "clarify the entire list please so that maks can get
on it"). Replaces the scattered asks of 2026-09-26 / 27. Posted verbatim:

```
Everything still open for maks, in one place (update to v0.24.148 first, Updates tab):

Elevator boost after a Quick load (the physics item, paused until these come in):
1) Take a savestate in the red elevator BEFORE pressing the button, then Quick load it and try the boost. Does it work?
2) Same savestate set to Full load (Practice editor, Start state row). Does the boost work there?
3) If it fails after a Quick load: walk around for ~10 s, go back and try again. Does it start working?
4) When it fails, what happens: you don't clip through the door at all, or you clip but don't get launched?
5) On a fresh game start, using only Quick loads: do boosts ever stop working after a while? If they do, press Mark right then and send a report (QA tab).

Rope start state (cave 4 rope entrance):
6) Capture the start state again while on the rope (the old one is from before the rope fix). Then F7 from the surface, and again from inside the cave: do you land back on the rope every time, with no launch?
7) Same with the spot set to Full load.
8) After a few restarts, go out through the entrances: do the textures stay loaded?

Game crash (v0.24.129):
9) This is very likely fixed since v0.24.140. If the game ever crashes again: send the crash folder (in The Forest game folder, a folder named by date and time with error.log / crash.dmp) plus your log.

Reply with the numbers, e.g. "1) yes 2) no ...". Skip anything you can't do.
```

What each item checks:
- 1-4: whether the boost failure is Quick-load-specific, capture-point
  dependent, or settles (dynamic state after the restore); clip vs launch
  says which part of the tech breaks (collision vs velocity).
- 5: the heap / long-session lead (boosts stopped after ~50 restores).
- 6-8: `Game/RopeClimb` (v0.24.104-105) on a real start state, both load
  modes, and the texture unload it was thought to cause.
- 9: the native crash, fixed by v0.24.140's grass-camera prefix
  (`Camera::SetTargetTextureBuffers`); only a new crash needs the dump.
