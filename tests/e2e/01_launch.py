"""Launch: a fresh game, the plugin loaded at the expected version with no error, Slot 1 loaded."""
import re

import e2e

NAME = "launch, version, no plugin errors, Slot 1 loaded"
SMOKE = True
FATAL = True  # the rest need the game in Slot 1's world
TASKS = []


def run(t):
    g = t.game
    if not t.fresh_launch:
        t.note(g.call("game", {"action": "restart"}).splitlines()[-1])
    text = t.log.since(0)
    m = re.search(r"ForestOverlay v(\S+) loading", text)
    t.check(m is not None, "plugin loading line in LogOutput.log")
    t.check(m.group(1) == t.version, "plugin v%s (the csproj's)" % m.group(1),
            "v%s expected - install it: scripts/e2e.py --update" % t.version)
    e2e.load_slot(t)
    bad = [l for l in t.log.since(0).splitlines()
           if re.search(r"Exception|^\[(Error|Fatal)\s*:", l)]
    t.check(not bad, "no exception / error line since the launch", "\n".join(bad[:8]))
    s = g.status()
    t.check(s.get("savestates") == "idle", "savestates idle", s["raw"])
