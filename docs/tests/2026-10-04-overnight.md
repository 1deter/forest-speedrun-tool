# QA list: overnight batch (v0.24.235-245, sent 2026-10-04)

Saved verbatim as sent; the QA tab shows it from `qa/2026-10-04-overnight.txt`.
Everything dated 2026-10-04 in docs/confirmed.md was checked over the bridge; these ask for real play and feel.

What each item checks:
1. v0.24.236 "When I die" choices and the "Next death" line (bridge: forced deaths; real ones with each choice not).
2. v0.24.241 Reload the save in place (bridge: 0.86 s, state read back; whether it feels the same as the game's reload not).
3. v0.24.238 checkpoint savestates (~295 ms a capture measured; whether the hitch is noticeable in a real run not) and Restart from checkpoint N.
4. v0.24.237 results panel after real runs (what is missing / useless).
5. v0.24.235 info box options: drag, hide lines, compact, text size (feel and clipping).
6. v0.24.240 trajectory preview with a real bomb boost (bridge: 0.0-0.1 m off for a scripted jump; a real boost not).
7. v0.24.245 Map tab (readability, spot positions).
8. v0.24.243 ghost figure look and the replay camera (smoothness, direction).
9. v0.24.235 polygon zones in the editor and firing in a real walk.

```
Update to v0.24.245 first. Answer by number: Pass, Fail or Skip, and a note where it helps.
1) Deaths tab: pick "Reload the save" under "When I die", die for real a few times, then try "Restart the current spot" and "Revive at the current spot". Does each do what the "Next death" line said?
2) Deaths tab: tick "Reload the save: in place (fast)" and die. Does it feel the same as the game's own reload (same world, same state, nothing odd), just quicker?
3) Runs tab: tick "Capture at checkpoints" on a long segment (several checkpoints) and run it. Is the hitch at each checkpoint noticeable? Then "Restart from checkpoint N" - are you where you expected?
4) Finish a few timed runs and read the results panel. Is anything missing that you would want (or anything useless)?
5) Settings -> Info box (HUD): drag it around with the window open, hide a few lines, try compact mode and the text size. Anything awkward or clipped?
6) Debug views: trajectory preview on. Do a REAL bomb boost (open the pause menu in the knockback). Does the prediction match where you land (distance, landing speed)?
7) Open the Map tab: zoom, drag, click a spot, Go. Is it readable? Any spot in the wrong place?
8) Runs tab: ghost look, and the replay camera (Experimental) on a comparison run - behind, through the eyes, side-on. Does the figure face the right way and the camera follow smoothly?
9) Practice editor: make a zone with "poly" and a few corners (a ledge or a cave mouth). Does it fire where you expect when you walk in?
```
