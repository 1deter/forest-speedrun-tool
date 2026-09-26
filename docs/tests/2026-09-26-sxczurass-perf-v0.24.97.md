# sxczurass - performance patches (v0.24.97), 2026-09-26

Posted in #general (message `1553362967758905544`), reply to sxczurass.

```
1) Play normally for 5+ minutes with the default switches: do the regular small freezes feel rarer or shorter than before the update?
2) Go into a cave and back out a few times: does the hitch on the way in feel shorter?
3) Turn the Experimental switch on and load a save from the title screen a few times, then turn it off and do the same: can you feel control coming back sooner? Anything odd right after the load (enemies, buildings)?
4) Send a report (QA tab) at the end - the log keeps "Perf (30 s)" and "Load timing" lines with the real numbers, so no need to time anything yourself.
```

What each checks:
1. The five behaviour-preserving garbage patches (`PerfPatches` 0-3, 5): GC
   frequency in play on another machine - the report's `Perf (30 s)` lines.
2. `MergeAssetUnloads`: one asset sweep on a cave entry (`Load timing:`).
3. `SaveLoadNoFixedWait` (experimental): hand-over time felt, and whether
   the late building nav update is ever noticed.
4. The numbers behind 1-3.
