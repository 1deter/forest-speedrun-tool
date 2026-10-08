"""F7 on a spot with a start state: the restore runs and the player lands at the spawn."""
import shutil

import e2e

NAME = "spot restart (F7) with a start state, in place"
SMOKE = True
TASKS = []

SPAWN = (428.0, 78.48, -4.0)  # Slot 1: a flat spot, no cannibals (docs/bridge.md)
AWAY = (385.0, 76.0, 285.0)


def run(t):
    g = t.game
    g.run("tp %.2f %.2f %.2f 50" % SPAWN, "wait 2")
    g.run("capture e2e-start", timeout=180)
    src = e2e.CONFIG / "savestates" / "e2e-start.fosave"
    t.check(src.exists(), "start state captured")
    dst = e2e.CONFIG / "savestates" / "segments" / "e2e-restart.fosave"
    dst.parent.mkdir(exist_ok=True)
    shutil.copyfile(src, dst)
    e2e.segments(t, """
[segment]
id       = e2e-restart
name     = e2e restart
category = e2e
spawn    = %.2f %.2f %.2f 50.00 0.00
""" % SPAWN)
    spots = "\n".join(g.run("spots e2e-restart")[0].lines)
    t.check("e2e-restart" in spots, "spot listed")

    g.run("tp %.2f %.2f %.2f" % AWAY, "wait 1.5")
    mark = t.log.mark()
    g.run("restart e2e-restart", timeout=180)
    t.log.wait(r"Restart 'e2e-restart': restoring its start state in place\.", mark, 30)
    t.ok("Restart 'e2e-restart': restoring its start state in place.")
    t.log.wait(r"Savestate restore .*in place", mark, 30)
    t.ok("in-place restore lines logged")
    t.near(g.pos(), SPAWN, 1.5, "player at the spawn")
