"""A timed segment run across its zones: start, a checkpoint, the end - and nothing uploaded."""
import re

import e2e

NAME = "timed segment: start, checkpoint, finish, uploads off"
SMOKE = False
TASKS = []

# A gentle slope east of the plane in Slot 1's world, at ground height
# (measured: a tp from above, then the player's y).
SPAWN = (808.55, 85.41, 621.46)
START = (814.59, 86.20, 621.46)
CHECK = (831.95, 87.50, 620.12)
END = (861.34, 86.64, 618.83)


def run(t):
    g = t.game
    up = g.mod("upload")
    t.check(g.get(e2e.PLUGIN, up + "._enabled.Value") == "False", "uploads are off")
    e2e.segments(t, """
[segment]
id       = e2e-timed
name     = e2e timed
category = e2e
spawn    = %.2f %.2f %.2f 90.00 0.00
start    = zone %.2f %.2f %.2f 3.00
check    = zone %.2f %.2f %.2f 3.00
split    = Middle
end      = zone %.2f %.2f %.2f 3.00
split    = Finish
""" % (SPAWN + START + CHECK + END))
    mark = t.log.mark()
    g.run("go e2e-timed", "wait 2")
    t.near(g.pos(), SPAWN, 1.5, "player at the spawn after Go")
    for what, p in (("start", START), ("checkpoint", CHECK), ("end", END)):
        g.run("tp %.2f %.2f %.2f 90" % p, "wait 1.5")
    t.log.wait(r"Run 'e2e-timed': checkpoint 1/1 at", mark, 10, "the checkpoint")
    t.ok("checkpoint 1/1 split")
    m = t.log.wait(r"Run 'e2e-timed': finished in (\S+)", mark, 10, "the finish")
    t.ok("finished in %s" % m.group(1))
    uploads = [l for l in t.log.since(mark).splitlines() if re.search(r"Upload:.*e2e", l)]
    t.check(not uploads, "no upload of the test run", "\n".join(uploads))
    run = g.mod("practicerun")
    t.check(g.get(e2e.PLUGIN, run + "._resultsOpen") == "True", "results panel shown")
    g.run("call %s %s.CloseResults" % (e2e.PLUGIN, run))  # it would cover the later journeys' shots
