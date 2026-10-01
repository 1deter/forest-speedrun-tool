# QA list: LiveSplit import + Quick load book fixes (v0.24.189, sent 2026-10-01)

Saved verbatim as sent; the QA tab shows it from `qa/2026-10-01-lss-import.txt`.

What each item checks:
1. The one-click import (v0.24.186): `Data/LssAutoSplit` reads the runner's real
   `<AutoSplitterSettings>`; the built spot splits on the next listed event. Checked
   here over the bridge with the author's file (cave enter / exit, an item pickup, a
   velocity start); a runner's own route and settings are not.
2. Settings from a layout `.lsl` (layout-loaded ASL) - tested only with a hand-written
   layout in the unit tests.
3. NatureGuideKeeper / TodoListKeeper (v0.24.187-188): the bridge checked the fields;
   "finding it again ticks it" and the to-do list ticking need a real inspect / task.

```
1) Put your .lss in BepInEx/config/ForestOverlay/livesplit/, stand where your run starts, then Practice -> Import -> Import on your file. You get a timed spot with your LiveSplit split names. Turn practice mode on (F9) and run it: does it start and split exactly where LiveSplit does (your autosplitter settings - velocity / plane meal start, caves, item pickups, endgame cutscenes)?
2) If your autosplitter is in your layout instead (Edit Layout -> Scriptable Auto Splitter), put the .lsl beside the .lss and Import again: does the line under the file say "from the layout" and split as LiveSplit does?
3) Capture a start state, then find a new animal or plant (nature guide tick) and Quick load (F7): is the tick gone? Find it again: does it tick again? Does the book's to-do list still tick off things you do after a Quick load?
```
