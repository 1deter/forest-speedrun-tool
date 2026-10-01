# Autosplitter splits + LiveSplit files (v0.24.185), sent 2026-10-01

Posted in #general: https://discord.com/channels/1553092607134011542/1553092608509874318/1555283690832138291

```
1) Practice -> edit a timed segment -> a trigger set to "event": the new group button (Endgame / Starts / Caves / Clothing / Passengers). Try a cave enter / exit as a checkpoint and walk through a real cave mouth - does it split when LiveSplit would?
2) Start = Starts -> "hold-interact" (the autosplitter's plane meal start): hold E on something (a pickup, a fire) - does the clock start?
3) Put your .lss (LiveSplit -> Save Splits As) in BepInEx/config/ForestOverlay/livesplit/, then Runs -> "LiveSplit file" -> pick it. Are your split names matched to the right LiveSplit splits? Then tick "LiveSplit" under Compare to and run it - do the times / deltas look right against your LiveSplit PB?
```

What each checks:
1. `Game/WorldEvents` cave events through a real cave trigger (`activateCave` / `CaveTriggers` -> `SetCurrentCave`); the bridge only called `SetCurrentCave` directly. Same frame as the ASL.
2. `hold-interact` from a real hold - the bridge could not set `Input.DelayedActionIsDown` (cleared the same frame).
3. The `.lss` UI and matching on runners' own files (names, subsplits, game vs real time) - checked here on the author's Coop Any% file only.
