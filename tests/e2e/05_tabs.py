"""Every tab opened with a long message pushed through, one shot each (gotcha 31). The shots are for
eyes - a clipped line is judgement (docs/harness.md 8d); the journey proves each tab opens and draws."""
import e2e

NAME = "tab sweep: each tab with a long message, shot"
SMOKE = False
TASKS = []

LONG = ("A long message to see how this line wraps: the quick brown fox jumps over the lazy dog, "
        "then over it again, and once more for good measure (e2e).")


def run(t):
    g = t.game
    n = int(g.get(e2e.PLUGIN, e2e.HOST + ".Count"))
    tabs = []
    for i in range(n):
        base = "%s._modules[%d]" % (e2e.HOST, i)
        if g.get(e2e.PLUGIN, base + ".HasTab") == "True":
            tabs.append((g.get(e2e.PLUGIN, base + ".Id"), base))
    t.check(len(tabs) >= 5, "%d tabs found" % len(tabs), ", ".join(i for i, _ in tabs))

    checker = g.mod("update") + "._checker.Message"
    was = g.get(e2e.PLUGIN, checker)
    try:
        g.run('set %s %s "%s"' % (e2e.PLUGIN, checker, LONG))
        for mid, base in tabs:
            # A module's own status line, where it has one.
            g.run('set %s %s._status "%s"' % (e2e.PLUGIN, base, LONG), errors_ok=True)
            g.run("call %s %s.OpenMyTab" % (e2e.PLUGIN, base), "wait 0.5")
            t.shot("tab-" + mid)
            g.run('set %s %s._status ""' % (e2e.PLUGIN, base), errors_ok=True)
    finally:
        g.run('set %s %s "%s"' % (e2e.PLUGIN, checker, was))
