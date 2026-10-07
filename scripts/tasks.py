"""The project's task file: one source of truth for open work (docs/harness.md, 6).

tasks/tasks.jsonl holds one task per line (merges cleanly between sessions).
This script owns it - never edit a status by hand; the gates below are the
point (docs/harness.md 7a, 9c). Every write also re-renders docs/tasks.md.

    python scripts/tasks.py list [--open] [--status S] [--area A] [--needs N]
    python scripts/tasks.py next [--bridge] [--by NAME]
    python scripts/tasks.py show T-0001
    python scripts/tasks.py add "Title" --area plugin --behavior "..." [--priority 2]
           [--source qa:123] [--needs none] [--verify STEP ...] [--keep "..."]
           [--scope "..."] [--question "..."] [--notes "..."] [--checker | --no-checker]
    python scripts/tasks.py start T-0001 --by main [--scope ..] [--verify ..] [--keep ..]
    python scripts/tasks.py set T-0001 [--status S] [--needs N] [--question Q | --answer A]
           [--priority P] [--commit SHA ...] [--release vX] [--layer L] [--notes ..]
           [--title ..] [--behavior ..] [--verify ..] [--keep ..] [--blocked-by ID ...]
           [--by NAME]
    python scripts/tasks.py evidence T-0001 "what proves it" --by forest-tester
    python scripts/tasks.py note T-0001 "multi-session notes line"
    python scripts/tasks.py stats
    python scripts/tasks.py check          # validate the file (CI)
    python scripts/tasks.py render         # rewrite docs/tasks.md

Errors print WHAT / WHY / FIX and exit 1, so a chain joined with && stops.
"""
import argparse
import datetime
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TASKS = os.path.join(ROOT, "tasks", "tasks.jsonl")
NOTES = os.path.join(ROOT, "tasks", "notes")
VIEW = os.path.join(ROOT, "docs", "tasks.md")

STATUSES = ["todo", "in-progress", "built", "released", "confirmed", "wontfix", "blocked"]
NEEDS = ["none", "bridge", "author-eyes", "tester", "moderators", "author-decision", "author-present"]
AREAS = ["plugin", "site", "bot", "knowledge", "harness", "docs", "release", "research"]
LAYERS = ["spec", "context", "environment", "verification", "state"]
DONE = ("confirmed", "wontfix")
# Areas whose behaviour changes get a fresh-context checker (author, 2026-10-07).
CHECKED_AREAS = ("plugin", "site", "bot")
# Tasks an agent can take alone (docs/harness.md 6a); bridge ones need the game up.
ALONE = ("none",)
ALONE_BRIDGE = ("none", "bridge")
# Field order in the file, so diffs stay readable.
ORDER = ["id", "title", "area", "priority", "status", "needs", "question", "behavior",
         "scope", "verify", "keep", "checker", "source", "owner", "maker", "commits",
         "release", "evidence", "layer", "blocked_by", "notes", "created", "updated", "log"]


class TaskError(Exception):
    def __init__(self, what, why, fix):
        Exception.__init__(self, "ERROR: %s\nWHY: %s\nFIX: %s" % (what, why, fix))


def today():
    return datetime.date.today().isoformat()


# ---------------------------------------------------------------- file

def parse(text):
    """Lines of JSON -> list of task dicts. Blank lines are skipped."""
    tasks = []
    for n, line in enumerate(text.splitlines(), 1):
        line = line.strip()
        if not line:
            continue
        try:
            tasks.append(json.loads(line))
        except ValueError as e:
            raise TaskError("tasks.jsonl line %d is not JSON (%s)" % (n, e),
                            "the file is one JSON object per line",
                            "fix the line by hand (likely a merge leftover) and run `tasks.py check`")
    return tasks


def dump(tasks):
    lines = []
    for t in sorted(tasks, key=lambda t: id_num(t["id"])):
        ordered = {k: t[k] for k in ORDER if k in t}
        ordered.update({k: v for k, v in t.items() if k not in ordered})
        lines.append(json.dumps(ordered, ensure_ascii=False))
    return "\n".join(lines) + ("\n" if lines else "")


def id_num(tid):
    m = re.fullmatch(r"T-(\d+)", tid or "")
    return int(m.group(1)) if m else 10 ** 9


def load(path=TASKS):
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        return parse(f.read())


def save(tasks, path=TASKS, view=VIEW):
    validate(tasks)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(dump(tasks))
    if view:
        with open(view, "w", encoding="utf-8", newline="\n") as f:
            f.write(render(tasks))


def find(tasks, tid):
    tid = normalise_id(tid)
    for t in tasks:
        if t["id"] == tid:
            return t
    raise TaskError("no task %s" % tid, "ids are T- plus a number",
                    "`tasks.py list` shows the ids")


def normalise_id(tid):
    m = re.fullmatch(r"[Tt]-?(\d+)", tid.strip())
    return "T-%04d" % int(m.group(1)) if m else tid


def new_id(tasks):
    return "T-%04d" % (max([id_num(t["id"]) for t in tasks if id_num(t["id"]) < 10 ** 9] or [0]) + 1)


# ---------------------------------------------------------------- rules

def validate(tasks):
    """Checks every task against the rules; raises on the first problem."""
    seen = set()
    for t in tasks:
        tid = t.get("id")
        if id_num(tid) >= 10 ** 9:
            raise TaskError("bad id %r" % tid, "ids are T- plus four digits", "renumber it")
        if tid in seen:
            raise TaskError("two tasks share id %s" % tid,
                            "two sessions added a task at once and git merged both lines",
                            "edit the newer line's id by hand to %s (the next free one), then "
                            "`tasks.py check`" % new_id(tasks))
        seen.add(tid)
        for field, allowed in (("status", STATUSES), ("needs", NEEDS), ("area", AREAS)):
            if t.get(field) not in allowed:
                raise TaskError("%s has %s %r" % (tid, field, t.get(field)),
                                "%s is one of: %s" % (field, ", ".join(allowed)),
                                "`tasks.py set %s --%s <value>`" % (tid, field))
        if t.get("layer") not in (None,) + tuple(LAYERS):
            raise TaskError("%s has layer %r" % (tid, t.get("layer")),
                            "a failure's layer is one of: " + ", ".join(LAYERS),
                            "`tasks.py set %s --layer <layer>`" % tid)
        if not t.get("title"):
            raise TaskError("%s has no title" % tid, "a task needs a title", "`tasks.py set %s --title`" % tid)
        if t["needs"] == "author-decision" and not t.get("question"):
            raise TaskError("%s needs an author decision but has no question" % tid,
                            "a parked task carries its question, the options and what each changes "
                            "(docs/harness.md *Ground rule*)",
                            "`tasks.py set %s --question \"...\"`" % tid)
        if t["status"] == "confirmed" and not t.get("evidence"):
            raise TaskError("%s is confirmed with no evidence" % tid,
                            "confirmed needs recorded proof (docs/harness.md 7a)",
                            "`tasks.py evidence %s \"...\" --by <checker>`" % tid)
        for dep in t.get("blocked_by") or []:
            if dep not in {x["id"] for x in tasks}:
                raise TaskError("%s is blocked by unknown task %s" % (tid, dep), "blocked_by lists task ids",
                                "`tasks.py set %s --blocked-by <ids>`" % tid)


def contract_problems(t):
    """What the task still lacks before work may start (docs/harness.md 9c)."""
    missing = []
    if not t.get("behavior"):
        missing.append("--behavior (what it does when done)")
    if not t.get("verify"):
        missing.append("--verify (the steps that prove it)")
    if not t.get("keep"):
        missing.append("--keep (what must not change; '-' if nothing)")
    if t.get("question"):
        missing.append("an answer to its open question (--answer)")
    return missing


def transition(tasks, t, status, by=None, note=None):
    """Moves t to status, enforcing the gates. Returns nothing; raises TaskError."""
    old = t["status"]
    tid = t["id"]
    if status == old:
        return
    if old == "confirmed":
        raise TaskError("%s is confirmed and cannot move to %s" % (tid, status),
                        "a confirmed task never goes back (docs/harness.md 7a, lecture 8)",
                        "open a new task for the regression: `tasks.py add \"Regression: ...\" "
                        "--source %s`" % tid)
    if status == "in-progress":
        start_gate(tasks, t, by)
    if status == "blocked" and not (t.get("question") or t.get("blocked_by") or note or t.get("notes")):
        raise TaskError("%s blocked with no reason" % tid,
                        "a blocked task says what blocks it",
                        "add --question, --blocked-by or --notes")
    if status == "built" and not t.get("commits"):
        raise TaskError("%s built with no commit" % tid,
                        "built means committed code; the commit links the task to its release",
                        "add --commit <sha>")
    if status == "released" and not t.get("release"):
        raise TaskError("%s released with no version" % tid, "released names the release",
                        "add --release vX.Y.Z")
    if status == "confirmed":
        confirm_gate(t)
    if status == "wontfix" and not (note or t.get("notes")):
        raise TaskError("%s wontfix with no reason" % tid, "say why it is dropped", "add --notes")
    if status in ("built",) and by:
        t["maker"] = by
    if status != "in-progress" and old == "in-progress":
        t.pop("owner", None)
    t["status"] = status
    t.setdefault("log", []).append({"at": today(), "from": old, "to": status, "by": by or "?"})


def start_gate(tasks, t, by):
    tid = t["id"]
    if not by:
        raise TaskError("starting %s without --by" % tid,
                        "WIP = 1 is per worker, so the worker is named",
                        "add --by <session or agent name>, e.g. --by main")
    if t["needs"] not in ALONE_BRIDGE and t["needs"] != "author-present":
        raise TaskError("%s needs %s" % (tid, t["needs"]),
                        "only tasks an agent can do (needs none / bridge, or author-present with "
                        "the author here) are started",
                        "clear it first: `tasks.py set %s --needs none`" % tid)
    missing = contract_problems(t)
    if missing:
        raise TaskError("%s has no full contract: missing %s" % (tid, ", ".join(missing)),
                        "work starts only once done and out-of-scope are written down "
                        "(docs/harness.md 9c, lecture 11)",
                        "`tasks.py start %s --by %s` with those flags" % (tid, by))
    open_deps = [d for d in t.get("blocked_by") or []
                 if find(tasks, d)["status"] not in ("built", "released", "confirmed")]
    if open_deps:
        raise TaskError("%s waits on %s" % (tid, ", ".join(open_deps)),
                        "a task starts after the ones it depends on are built",
                        "do those first, or drop the dependency with --blocked-by")
    busy = [x["id"] for x in tasks if x["status"] == "in-progress" and x.get("owner") == by and x is not t]
    if busy:
        raise TaskError("%s already has %s in progress" % (by, ", ".join(busy)),
                        "one active task per worker (WIP = 1, lecture 7)",
                        "finish it or park it: `tasks.py set %s --status blocked --notes \"...\"`" % busy[0])
    t["owner"] = by


def confirm_gate(t):
    tid = t["id"]
    ev = t.get("evidence") or []
    if not ev:
        raise TaskError("%s has no evidence" % tid,
                        "confirmed needs recorded proof (docs/harness.md 7a)",
                        "`tasks.py evidence %s \"<log line / shot / test name>\" --by <checker>`" % tid)
    if t.get("checker"):
        maker = t.get("maker")
        if not any(e.get("by") and e.get("by") != maker for e in ev):
            raise TaskError("%s has evidence only from its maker (%s)" % (tid, maker or "?"),
                            "a behaviour change is confirmed by a fresh-context checker, never its maker "
                            "(author, 2026-10-07; docs/harness.md 7d)",
                            "run a checker agent (in game: the e2e script or forest-tester) and record it: "
                            "`tasks.py evidence %s \"...\" --by forest-tester`" % tid)


def pick_next(tasks, bridge=False, by=None):
    """The highest-priority task an agent can do alone, or None.

    A worker with a task in progress gets that one back (WIP = 1)."""
    if by:
        mine = [t for t in tasks if t["status"] == "in-progress" and t.get("owner") == by]
        if mine:
            return mine[0]
    allowed = ALONE_BRIDGE if bridge else ALONE
    ids = {t["id"]: t for t in tasks}
    ready = []
    for t in tasks:
        if t["status"] != "todo" or t["needs"] not in allowed or t.get("question"):
            continue
        if any(ids[d]["status"] not in ("built", "released", "confirmed") for d in t.get("blocked_by") or []):
            continue
        ready.append(t)
    ready.sort(key=lambda t: (t.get("priority", 3), id_num(t["id"])))
    return ready[0] if ready else None


# ---------------------------------------------------------------- views

def line(t):
    flags = ""
    if t["needs"] != "none":
        flags += " [%s]" % t["needs"]
    if t.get("question"):
        flags += " ?"
    return "%s P%s %-11s %-9s %s%s" % (t["id"], t.get("priority", 3), t["status"], t["area"], t["title"], flags)


def render(tasks):
    out = ["# Tasks", "",
           "Generated by `scripts/tasks.py` from `tasks/tasks.jsonl` - never edit by hand.", ""]
    open_ = [t for t in tasks if t["status"] not in DONE]
    q = [t for t in open_ if t.get("question")]
    if q:
        out += ["## Questions for the author", ""]
        for t in q:
            out.append("- **%s %s** - %s" % (t["id"], t["title"], t["question"].replace("\n", " ")))
        out.append("")
    groups = [("In progress", lambda t: t["status"] == "in-progress"),
              ("Built, not released", lambda t: t["status"] == "built"),
              ("Released, awaiting a check", lambda t: t["status"] == "released"),
              ("Blocked", lambda t: t["status"] == "blocked"),
              ("To do", lambda t: t["status"] == "todo")]
    for name, pred in groups:
        rows = sorted([t for t in open_ if pred(t)], key=lambda t: (t.get("priority", 3), id_num(t["id"])))
        if not rows:
            continue
        out += ["## %s (%d)" % (name, len(rows)), "",
                "| Id | P | Area | Needs | Title |", "|---|---|---|---|---|"]
        for t in rows:
            out.append("| %s | %s | %s | %s | %s |" % (t["id"], t.get("priority", 3), t["area"], t["needs"],
                                                      t["title"].replace("|", "/")))
        out.append("")
    done = len([t for t in tasks if t["status"] in DONE])
    out.append("%d open, %d done (confirmed or wontfix)." % (len(open_), done))
    return "\n".join(out) + "\n"


def stats(tasks):
    out = []
    for field, values in (("status", STATUSES), ("needs", NEEDS), ("area", AREAS)):
        counts = [(v, len([t for t in tasks if t.get(field) == v])) for v in values]
        out.append("%-7s " % field + "  ".join("%s %d" % c for c in counts if c[1]))
    layers = [(l, len([t for t in tasks if t.get("layer") == l])) for l in LAYERS]
    out.append("failures by layer: " + ("  ".join("%s %d" % c for c in layers if c[1]) or "none recorded"))
    weeks = {}
    for t in tasks:
        for e in t.get("log") or []:
            if e["to"] == "confirmed":
                d = datetime.date.fromisoformat(e["at"])
                wk = "%d-W%02d" % d.isocalendar()[:2]
                weeks[wk] = weeks.get(wk, 0) + 1
    out.append("confirmed per week: " + ("  ".join("%s %d" % w for w in sorted(weeks.items())) or "none yet"))
    return "\n".join(out)


# ---------------------------------------------------------------- commands

def cmd_add(tasks, a):
    t = {"id": new_id(tasks), "title": a.title.strip(), "area": a.area, "priority": a.priority,
         "status": "todo", "needs": a.needs, "question": a.question, "behavior": a.behavior,
         "scope": a.scope, "verify": a.verify or [], "keep": a.keep,
         "checker": a.checker if a.checker is not None else a.area in CHECKED_AREAS,
         "source": a.source, "commits": [], "release": None, "evidence": [], "layer": None,
         "blocked_by": [normalise_id(x) for x in a.blocked_by or []], "notes": a.notes,
         "created": today(), "updated": today(), "log": []}
    if t["question"] and t["needs"] == "none":
        t["needs"] = "author-decision"
    tasks.append(t)
    return t


def cmd_set(tasks, a):
    t = find(tasks, a.id)
    for field in ("title", "behavior", "scope", "keep", "notes", "release", "layer", "needs", "area"):
        v = getattr(a, field, None)
        if v is not None:
            t[field] = v
    if a.priority is not None:
        t["priority"] = a.priority
    if a.verify:
        t["verify"] = a.verify
    if a.commit:
        t["commits"] = (t.get("commits") or []) + [c for c in a.commit if c not in (t.get("commits") or [])]
    if a.blocked_by is not None:
        t["blocked_by"] = [normalise_id(x) for x in a.blocked_by]
    if a.checker is not None:
        t["checker"] = a.checker
    if a.question:
        t["question"] = a.question
        if t["needs"] in ("none", "bridge"):
            t["needs"] = "author-decision"
    if a.answer:
        q = t.get("question") or ""
        t["notes"] = ((t.get("notes") or "") + "\nQ: %s\nA (%s): %s" % (q, today(), a.answer)).strip()
        t["question"] = None
        if t["needs"] == "author-decision":
            t["needs"] = "none"
    if a.status:
        transition(tasks, t, a.status, by=a.by, note=a.notes)
    t["updated"] = today()
    return t


def cmd_start(tasks, a):
    t = find(tasks, a.id)
    for field in ("scope", "keep", "behavior"):
        v = getattr(a, field, None)
        if v is not None:
            t[field] = v
    if a.verify:
        t["verify"] = a.verify
    if t["status"] not in ("todo", "blocked", "built"):
        raise TaskError("%s is %s" % (t["id"], t["status"]), "only todo, blocked or built (rework) tasks start",
                        "`tasks.py show %s`" % t["id"])
    transition(tasks, t, "in-progress", by=a.by)
    t["updated"] = today()
    return t


def cmd_evidence(tasks, a):
    t = find(tasks, a.id)
    t.setdefault("evidence", []).append({"at": today(), "by": a.by, "what": a.text})
    t["updated"] = today()
    return t


def cmd_note(tasks, a):
    t = find(tasks, a.id)
    os.makedirs(NOTES, exist_ok=True)
    path = os.path.join(NOTES, t["id"] + ".md")
    new = not os.path.exists(path)
    with open(path, "a", encoding="utf-8", newline="\n") as f:
        if new:
            f.write("# %s %s\n\nRunning notes across sessions (docs/harness.md 11a).\n\n" % (t["id"], t["title"]))
        f.write("- %s: %s\n" % (today(), a.text))
    if not (t.get("notes") or "").startswith("tasks/notes/"):
        t["notes"] = ("tasks/notes/%s.md " % t["id"] + (t.get("notes") or "")).strip()
    return t


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)

    def common(sp, setting=False):
        sp.add_argument("--behavior")
        sp.add_argument("--scope")
        sp.add_argument("--verify", nargs="+")
        sp.add_argument("--keep")
        sp.add_argument("--notes")
        sp.add_argument("--priority", type=int, choices=[1, 2, 3, 4], default=None if setting else 3)
        sp.add_argument("--needs", choices=NEEDS, default=None if setting else "none")
        sp.add_argument("--area", choices=AREAS, default=None)
        sp.add_argument("--blocked-by", nargs="*")
        sp.add_argument("--question")
        g = sp.add_mutually_exclusive_group()
        g.add_argument("--checker", dest="checker", action="store_true", default=None)
        g.add_argument("--no-checker", dest="checker", action="store_false")

    sp = sub.add_parser("list")
    sp.add_argument("--open", action="store_true")
    sp.add_argument("--status", choices=STATUSES)
    sp.add_argument("--area", choices=AREAS)
    sp.add_argument("--needs", choices=NEEDS)
    sp = sub.add_parser("next")
    sp.add_argument("--bridge", action="store_true")
    sp.add_argument("--by")
    sp = sub.add_parser("show")
    sp.add_argument("id")
    sp = sub.add_parser("add")
    sp.add_argument("title")
    sp.add_argument("--source")
    common(sp)
    sp = sub.add_parser("start")
    sp.add_argument("id")
    sp.add_argument("--by", required=True)
    sp.add_argument("--behavior")
    sp.add_argument("--scope")
    sp.add_argument("--verify", nargs="+")
    sp.add_argument("--keep")
    sp = sub.add_parser("set")
    sp.add_argument("id")
    sp.add_argument("--status", choices=STATUSES)
    sp.add_argument("--title")
    sp.add_argument("--answer")
    sp.add_argument("--commit", nargs="+")
    sp.add_argument("--release")
    sp.add_argument("--layer", choices=LAYERS)
    sp.add_argument("--by")
    common(sp, setting=True)
    sp = sub.add_parser("evidence")
    sp.add_argument("id")
    sp.add_argument("text")
    sp.add_argument("--by", required=True)
    sp = sub.add_parser("note")
    sp.add_argument("id")
    sp.add_argument("text")
    sub.add_parser("stats")
    sub.add_parser("check")
    sub.add_parser("render")
    a = p.parse_args(argv)

    try:
        tasks = load()
        if a.cmd == "list":
            rows = [t for t in tasks
                    if (not a.open or t["status"] not in DONE)
                    and (not a.status or t["status"] == a.status)
                    and (not a.area or t["area"] == a.area)
                    and (not a.needs or t["needs"] == a.needs)]
            rows.sort(key=lambda t: (t["status"] in DONE, t.get("priority", 3), id_num(t["id"])))
            print("\n".join(line(t) for t in rows) or "(none)")
        elif a.cmd == "next":
            t = pick_next(tasks, a.bridge, a.by)
            print(json.dumps(t, ensure_ascii=False, indent=1) if t else "(nothing an agent can take alone)")
        elif a.cmd == "show":
            print(json.dumps(find(tasks, a.id), ensure_ascii=False, indent=1))
        elif a.cmd == "stats":
            print(stats(tasks))
        elif a.cmd == "check":
            validate(tasks)
            with open(VIEW, encoding="utf-8") as f:
                if f.read() != render(tasks):
                    raise TaskError("docs/tasks.md is out of date", "it is generated from tasks.jsonl",
                                    "`python scripts/tasks.py render` and commit it")
            print("tasks: %d ok" % len(tasks))
        elif a.cmd == "render":
            save(tasks)
            print("rendered " + os.path.relpath(VIEW, ROOT))
        else:
            if a.cmd == "add":
                if not a.area:
                    raise TaskError("no --area", "every task has an area", "--area " + "|".join(AREAS))
                t = cmd_add(tasks, a)
            else:
                t = {"set": cmd_set, "start": cmd_start, "evidence": cmd_evidence, "note": cmd_note}[a.cmd](tasks, a)
            save(tasks)
            print(line(t))
    except TaskError as e:
        print(e, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
