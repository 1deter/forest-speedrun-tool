"""Quick and Full loads through the bridge's `restore` (no restart teleport): where the player lands,
and a bush cut before the capture through the chain Full -> Quick -> capture -> Full (gotchas 38, 40)."""
import e2e

NAME = "restores: landing position, cut bush kept through the chain"
SMOKE = False
TASKS = ["T-0137", "T-0138"]

BUSH = (428.0, 78.0, -4.0)  # GreenBush_40 is 4 m away in Slot 1
AWAY = (385.0, 76.0, 285.0)
STATES = e2e.CONFIG / "savestates"


def lands(t, name, load):
    """Restore away from the capture; the player within 1 m of the header's position (gotcha 40)."""
    g = t.game
    head = e2e.savestate_header(STATES / (name + ".fosave"))
    want = e2e.parse_vec(head["position"])
    g.run("tp %.2f %.2f %.2f" % AWAY, "wait 1.5")
    mark = t.log.mark()
    g.run("restore %s%s" % (name, " load" if load else ""), "wait 2", timeout=240)
    kind = "Full load" if load else "Quick load"
    if load:
        t.log.wait(r"Load \d+ finished", mark, 60, "the Full load")
    t.near(g.pos(), want, 1.0, "%s of %s: player at the header's position" % (kind, name))
    return head


def run(t):
    g = t.game
    bush = t.cut_bush(BUSH)
    g.run("tp %.2f %.2f %.2f" % BUSH, "wait 1.5", "capture e2e-a", timeout=180)
    head = e2e.savestate_header(STATES / "e2e-a.fosave")
    t.check(bush in head.get("cutbushes", ""), "e2e-a lists %s as cut" % bush, head.get("cutbushes", "(no cutbushes line)"))

    lands(t, "e2e-a", load=True)
    t.check(not t.has(bush), "after the Full load: %s still cut" % bush)
    lands(t, "e2e-a", load=False)
    t.check(not t.has(bush), "after the Quick load: %s still cut" % bush)

    g.run("tp %.2f %.2f %.2f" % BUSH, "wait 1.5", "capture e2e-b", timeout=180)
    head = e2e.savestate_header(STATES / "e2e-b.fosave")
    t.check(bush in head.get("cutbushes", ""), "e2e-b (captured after the chain) lists %s as cut" % bush,
            head.get("cutbushes", "(no cutbushes line)"))
    lands(t, "e2e-b", load=True)
    t.check(not t.has(bush), "after the last Full load: %s still cut" % bush)
