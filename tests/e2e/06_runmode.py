"""Run mode's integrity check on this install: a run started from a Normal and from a Creative start
state reports no NOT OK but the bridge's own flag (gotcha 82). A run starts only on a run spot's
Restart (docs/run-mode.md *What starts a run*); each attempt is deleted from the site after (10c)."""
import shutil
import time
from pathlib import Path

import e2e

NAME = "run mode integrity: runs from a Normal and a Creative start state"
SMOKE = False
TASKS = ["T-0139"]

LISTS = ["OtherPlugins", "OtherPatchers", "OtherCode", "ForeignPatches", "Cheats", "Flags"]
# Start states kept with the suite (Slot 1 is a Creative save): Normal on the
# surface (cm-normal-2, v0.24.245), Creative on the surface (Cave 5's start).
STATES = Path(__file__).parent / "states"
SPOTS = [("e2e-run-normal", "Normal", STATES / "normal.fosave"),
         ("e2e-run-creative", "Creative", STATES / "creative.fosave")]


def attempt(t, spot, mode):
    g = t.game
    rm = g.mod("runmode")
    mark = t.log.mark()
    g.run("restart " + spot, timeout=240)
    t.log.wait(r"Run mode: attempt \d+ started", mark, 120, "the %s run spot's Restart" % mode)
    m = t.log.wait(r"Run mode: attempt \d+ is (\S+?) \(", mark, 60, "the site's attempt id")
    t.attempts.append(m.group(1))
    t.ok("%s: attempt %s" % (mode, m.group(1)))

    end = time.time() + 120
    while not g.get(e2e.PLUGIN, rm + "._report.GameHash"):
        if time.time() > end:
            raise e2e.Fail("%s: the game's code was not hashed within 120 s" % mode)
        time.sleep(2)
    time.sleep(5)  # a periodic check (cheats, patches) after the hash
    started = g.get(e2e.PLUGIN, rm + "._report.Started")
    t.check(g.get(e2e.PLUGIN, rm + "._report.Creative") == str(mode == "Creative"),
            "%s: the report says Creative = %s" % (mode, mode == "Creative"), started)
    gh = g.get(e2e.PLUGIN, rm + "._report.GameHash")
    known = e2e.parse_items(g.run("get static:ForestOverlay.Data.RunReport KnownGameHashes")[0].lines)
    t.check(gh.lower() in [k.lower() for k in known], "%s: the game's code is a known Steam build" % mode, gh)
    bad = []
    for name in LISTS:
        for v in e2e.parse_items(g.run("get %s %s._report.%s" % (e2e.PLUGIN, rm, name))[0].lines):
            if not (name == "Flags" and "test bridge" in v):
                bad.append("%s: %s" % (name, v))
    t.check(not bad, "%s: no NOT OK but the bridge's flag" % mode, "; ".join(bad))
    g.run("call %s %s.EndRunMode" % (e2e.PLUGIN, rm))


def run(t):
    g = t.game
    for spot, mode, state in SPOTS:
        shutil.copyfile(state, e2e.CONFIG / "savestates" / "segments" / (spot + ".fosave"))
        e2e.segments(t, """
[segment]
id       = %s
name     = e2e run (%s)
category = e2e
spawn    = %s 0.00 0.00
run      = Any%%
""" % (spot, mode, e2e.savestate_header(state)["position"]))
    try:
        for spot, mode, state in SPOTS:
            attempt(t, spot, mode)
    finally:
        g.run("call %s %s.EndRunMode" % (e2e.PLUGIN, g.mod("runmode")), errors_ok=True)
        e2e.to_title(t)
        e2e.load_slot(t)  # leave the game in Slot 1's world, as the suite found it
