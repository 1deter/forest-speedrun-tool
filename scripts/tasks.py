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
           [--qa "the line testers read + a message link"]
    python scripts/tasks.py start T-0001 --by main [--scope ..] [--verify ..] [--keep ..]
    python scripts/tasks.py set T-0001 [--status S] [--needs N] [--question Q | --answer A]
           [--priority P] [--commit SHA ...] [--release vX] [--layer L] [--notes ..]
           [--title ..] [--behavior ..] [--verify ..] [--keep ..] [--blocked-by ID ...]
           [--by NAME]
    python scripts/tasks.py evidence T-0001 "what proves it" --by forest-tester
    python scripts/tasks.py brief T-0001   # what the checker reads: contract + diff + suites
    python scripts/tasks.py review T-0001 --verdict accept|revise|block --by forest-checker
           --scores correctness=2,verification=2,scope=2,restart=n/a,legible=2,handoff=1
           [--faults "file:line - what is wrong" ...]
    python scripts/tasks.py note T-0001 "multi-session notes line"
    python scripts/tasks.py stats [--since YYYY-MM-DD] [--until YYYY-MM-DD]
           # a window: only tasks finished (first built) in it - the monthly review's before / after
    python scripts/tasks.py check          # validate the file (CI)
    python scripts/tasks.py render         # rewrite docs/tasks.md
    python scripts/tasks.py qa-todo        # the #qa-todo-list text (qa_todo from_tasks posts it)

Errors print WHAT / WHY / FIX and exit 1, so a chain joined with && stops.
"""
import argparse
import datetime
import json
import os
import re
import subprocess
import sys
import time
from contextlib import contextmanager

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
# Plugin behaviour needs the game (the system layer, docs/harness.md 7e): the checker's
# accept gates the release, in-game evidence from one of these confirms (author, 2026-10-07).
IN_GAME_BY = ("forest-tester", "e2e", "author")  # and "qa:<tester>"
# The checker's rubric (docs/harness.md 9d): each 0-2, or n/a.
RUBRIC = ["correctness", "verification", "scope", "restart", "legible", "handoff"]
VERDICTS = ["accept", "revise", "block"]
# A brief longer than this is cut; the checker reads the rest with git show.
BRIEF_LIMIT = 60000
# Tasks an agent can take alone (docs/harness.md 6a); bridge ones need the game up.
ALONE = ("none",)
ALONE_BRIDGE = ("none", "bridge")
# The tool itself comes first (author, 2026-10-08: "get the tool done ASAP"): next takes these
# areas before the site, the bot and the knowledge base, whatever their priority.
FOCUS_AREAS = ("plugin", "release", "research")
# Areas a plugin release ships; bump.py marks their built tasks released when a commit touches a plugin
# path (scripts / docs-only ones stay built and confirm on test evidence, T-0196; docs/harness.md 6c).
RELEASED_AREAS = ("plugin",)
# What testers still have to do (author, 2026-10-07: only that, nothing done or planned).
QA_OPEN = ("todo", "in-progress", "built", "released")
# Field order in the file, so diffs stay readable.
ORDER = ["id", "title", "area", "priority", "status", "needs", "question", "behavior",
         "scope", "verify", "keep", "qa", "checker", "source", "owner", "maker", "commits",
         "release", "evidence", "reviews", "layer", "blocked_by", "notes", "created", "updated", "log"]


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
    # On Windows opening a file while os.replace swaps it raises PermissionError (or, for an
    # instant, FileNotFoundError): retry briefly; Linux never shows either.
    deadline = time.time() + 5.0
    while True:
        try:
            with open(path, encoding="utf-8") as f:
                return parse(f.read())
        except FileNotFoundError:
            if not os.path.exists(path) and time.time() > deadline - 4.9:
                return []
            if time.time() > deadline:
                raise
        except PermissionError:
            if time.time() > deadline:
                raise
        time.sleep(0.005)


LOCK_TIMEOUT = 20.0   # seconds a writer waits for the lock before it fails
LOCK_STALE = 60.0     # a lock file older than this belongs to a dead process


def _lock_error(path, lock, why):
    return TaskError("%s is locked" % os.path.basename(path), why,
                     "retry in a moment; delete %s if no such process is running" % lock)


def _take_over_stale(lock, stale):
    """Remove a lock older than `stale`. The file is first renamed to a name only this process
    knows, and its age is judged again there: when a waiter has replaced the stale lock by a
    fresh one meanwhile, that fresh lock is put back (os.link fails if somebody made a newer
    one) instead of deleted."""
    mine = "%s.stale.%d.%s" % (lock, os.getpid(), os.urandom(4).hex())
    try:
        os.rename(lock, mine)  # exactly one of several takers wins this
    except OSError:
        return
    try:
        if time.time() - os.path.getmtime(mine) <= stale:  # not the stale one: give it back
            try:
                os.link(mine, lock)
            except OSError:
                pass
    except OSError:
        pass
    finally:
        try:
            os.remove(mine)
        except OSError:
            pass


@contextmanager
def locked(path=TASKS, timeout=None, stale=None):
    """Exclusive lock for a read-modify-write of `path`: a `<path>.lock` file made with
    O_CREAT|O_EXCL (works on Windows and Linux). Retries for `timeout` seconds; a lock older
    than `stale` seconds is taken over (_take_over_stale). A lock that cannot be taken raises
    TaskError (WHAT / WHY / FIX), never a bare OSError. Hold it from load to save only."""
    timeout = LOCK_TIMEOUT if timeout is None else timeout
    stale = LOCK_STALE if stale is None else stale
    lock = path + ".lock"
    try:
        os.makedirs(os.path.dirname(lock), exist_ok=True)
    except OSError as e:
        raise _lock_error(path, lock, "the folder cannot be made: %s" % e)
    deadline = time.time() + timeout
    while True:
        try:
            fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            try:
                os.write(fd, str(os.getpid()).encode("ascii"))
            finally:
                os.close(fd)
            break
        except OSError as e:
            if not os.path.exists(lock):
                if time.time() > deadline:  # not a held lock (a permission error, say)
                    raise _lock_error(path, lock, "%s cannot be created: %s" % (os.path.basename(lock), e))
                time.sleep(0.02)
                continue
            try:
                if time.time() - os.path.getmtime(lock) > stale:
                    _take_over_stale(lock, stale)
                    continue
            except OSError:
                pass  # the holder released it between our checks
            if time.time() > deadline:
                raise _lock_error(path, lock,
                                  "another tasks.py / loop.py / bump.py process holds %s" % os.path.basename(lock))
            time.sleep(0.02)
    try:
        yield
    finally:
        try:
            os.remove(lock)
        except OSError:
            pass


def atomic_write(path, text, retries=50):
    """Write `text` to a temp file next to `path` and os.replace() it in: a reader sees the
    old file or the new one, never half of it, and a crash leaves the old one. On Windows
    os.replace raises PermissionError while another process has the file open; retry ~1 s."""
    tmp = "%s.tmp.%d.%s" % (path, os.getpid(), os.urandom(4).hex())
    try:
        with open(tmp, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
            f.flush()
            os.fsync(f.fileno())
        for i in range(retries):
            try:
                os.replace(tmp, path)
                return
            except PermissionError:
                if i == retries - 1:
                    raise
                time.sleep(0.02)
    finally:
        if os.path.exists(tmp):
            try:
                os.remove(tmp)
            except OSError:
                pass


def save(tasks, path=TASKS, view=VIEW):
    validate(tasks)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    atomic_write(path, dump(tasks))
    if view:
        atomic_write(view, render(tasks))


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
        if t["needs"] == "tester" and t["status"] in QA_OPEN and not t.get("qa"):
            raise TaskError("%s needs a tester but has no qa line" % tid,
                            "the #qa-todo-list message is rendered from qa lines (docs/harness.md 6d); "
                            "a tester item says what to do and what to expect, plus the message link",
                            "`tasks.py set %s --qa \"<who>: <what to do> - <what you should see>: <link>\"`, "
                            "or --needs bridge if a session can check it" % tid)
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
    if status in ("released", "confirmed") and t.get("checker"):
        review_gate(t, status)
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


def accepted(t):
    """The task's latest review is an accept that covers every commit the task lists."""
    rs = t.get("reviews") or []
    if not rs or rs[-1].get("verdict") != "accept":
        return False
    return set(t.get("commits") or []) <= set(rs[-1].get("commits") or [])


def review_gate(t, status):
    if accepted(t):
        return
    tid = t["id"]
    rs = t.get("reviews") or []
    last = rs[-1] if rs else None
    if not last:
        what = "%s has no checker review" % tid
    elif last.get("verdict") != "accept":
        what = "%s's last review says %s" % (tid, last.get("verdict"))
    else:
        what = "%s has commits its accepted review did not see" % tid
    raise TaskError(what + " - it cannot be " + status,
                    "a behaviour change is checked by a fresh context before it ships, never by its maker "
                    "(author, 2026-10-07; docs/harness.md 7d)",
                    "spawn the forest-checker agent with \"Check %s\" (it records `tasks.py review`); "
                    "fix what it finds first if it said revise" % tid)


def in_game(by):
    return bool(by) and (by in IN_GAME_BY or by.startswith("qa:"))


def confirm_gate(t):
    tid = t["id"]
    ev = t.get("evidence") or []
    if not ev:
        raise TaskError("%s has no evidence" % tid,
                        "confirmed needs recorded proof (docs/harness.md 7a)",
                        "`tasks.py evidence %s \"<log line / shot / test name>\" --by <checker>`" % tid)
    commits = t.get("commits") or []
    # A plugin task whose commits touch no plugin path (scripts / docs only) has no behaviour to see in
    # game: test / lint evidence suffices, like a harness task (T-0196).
    scripts_only = bool(commits) and not touches_plugin(commits)
    if t.get("checker") and t.get("area") == "plugin" and not scripts_only:
        maker = t.get("maker")
        if not any(in_game(e.get("by")) and e.get("by") != maker for e in ev):
            raise TaskError("%s has no in-game evidence from a checker" % tid,
                            "plugin behaviour is confirmed in the game, by someone other than its maker "
                            "(docs/harness.md 7e; author, 2026-10-07)",
                            "run forest-tester (or the e2e script) after `update_game` and record it: "
                            "`tasks.py evidence %s \"<log line / shot>\" --by forest-tester` "
                            "(also: --by author, --by qa:<tester>)" % tid)


def pick_next(tasks, bridge=False, by=None, skip=()):
    """The highest-priority task an agent can do alone, or None.

    A worker with a task in progress gets that one back (WIP = 1). skip: ids
    to pass over (the loop's tasks already taken this run, scripts/loop.py)."""
    if by:
        mine = [t for t in tasks if t["status"] == "in-progress" and t.get("owner") == by
                and t["id"] not in skip]
        if mine:
            return mine[0]
    allowed = ALONE_BRIDGE if bridge else ALONE
    ids = {t["id"]: t for t in tasks}
    ready = []
    for t in tasks:
        if t["status"] != "todo" or t["needs"] not in allowed or t.get("question") or t["id"] in skip:
            continue
        if any(ids[d]["status"] not in ("built", "released", "confirmed") for d in t.get("blocked_by") or []):
            continue
        ready.append(t)
    ready.sort(key=lambda t: (t["area"] not in FOCUS_AREAS, t.get("priority", 3), id_num(t["id"])))
    return ready[0] if ready else None


# ---------------------------------------------------------------- releases

def git_ok(args):
    try:
        return subprocess.run(["git"] + args, cwd=ROOT, capture_output=True,
                              stdin=subprocess.DEVNULL, timeout=30).returncode == 0
    except (OSError, subprocess.TimeoutExpired):
        return False


def first_tag(commits):
    """The earliest v* tag holding every commit, or None (not released yet)."""
    common = None
    for c in commits:
        tags = set(git_out(["tag", "--contains", c, "--list", "v*"]).split())
        common = tags if common is None else common & tags
    if not common:
        return None
    return min(common, key=lambda v: [int(x) for x in re.findall(r"\d+", v)])


# What a plugin release ships (skill release: src/, patcher/, locations/, collectibles/, qa/).
PLUGIN_PATHS = ("src/", "patcher/", "locations/", "collectibles/", "qa/", "ForestOverlay.csproj")


def commit_files(sha):
    """Paths a commit touches, or None when git cannot say (no such commit here). A merge
    counts against its first parent (-m --first-parent: plain `git show` lists nothing for it,
    T-0196); -z keeps a path with spaces whole."""
    try:
        r = subprocess.run(["git", "show", "-m", "--first-parent", "--name-only", "-z", "--format=", sha],
                           cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace",
                           timeout=30, stdin=subprocess.DEVNULL)
    except (OSError, subprocess.TimeoutExpired):
        return None
    return [f for f in r.stdout.split("\0") if f.strip()] if r.returncode == 0 else None


def touches_plugin(commits, files_of=None):
    """True when a commit touches a plugin path; unknown commits count as plugin. No commits: False."""
    files_of = files_of or commit_files
    for c in commits:
        files = files_of(c)
        if files is None or any(f.startswith(PLUGIN_PATHS) for f in files):
            return True
    return False


def release_plan(tasks, version, in_head=None, tag_of=first_tag, files_of=None):
    """[(task, release)] for built plugin tasks whose commits are all in HEAD and touch a plugin
    path (the rule loop.py's needs_release uses); raises if one of them may not be released yet
    (the checker gate), so bump.py stops before editing."""
    in_head = in_head or (lambda c: git_ok(["merge-base", "--is-ancestor", c, "HEAD"]))
    plan, refused = [], []
    for t in tasks:
        if t["status"] != "built" or t["area"] not in RELEASED_AREAS or not t.get("commits"):
            continue
        if not all(in_head(c) for c in t["commits"]):
            continue  # on another branch (e.g. a worktree's): ships with its merge
        if not touches_plugin(t["commits"], files_of):
            continue  # scripts / docs only: nothing to release (T-0154)
        if t.get("checker") and not accepted(t):
            refused.append(t["id"])
            continue
        plan.append((t, tag_of(t["commits"]) or "v" + version.lstrip("v")))
    if refused:
        raise TaskError("built %s in this release without a checker accept" % ", ".join(refused),
                        "a behaviour change is checked by a fresh context before it ships (router rule 10)",
                        "spawn forest-checker with \"Check %s\" first; nothing was changed" % refused[0])
    return plan


def mark_released(tasks, plan, by="bump.py"):
    for t, release in plan:
        t["release"] = release
        transition(tasks, t, "released", by=by)
        t["updated"] = today()


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


def qa_todo(tasks, version=None, day=None):
    """The #qa-todo-list message: only what testers still have to do (author, 2026-10-07)."""
    rows = sorted([t for t in tasks if t["needs"] == "tester" and t["status"] in QA_OPEN and t.get("qa")],
                  key=lambda t: (t.get("priority", 3), id_num(t["id"])))
    head = "**ForestOverlay - QA to-do** (%s%s)" % (day or today(), ", " + version if version else "")
    if not rows:
        return head + "\n\nNothing to test right now."
    return head + "\n\n**Please test**\n" + "\n".join("- " + t["qa"].strip() for t in rows)


# A task is finished when it first reaches one of these: its maker is done (the monthly harness
# review counts a window of finished tasks, docs/harness.md 12d).
FINISHED = ("built", "released", "confirmed")


def finished_on(t):
    """The date the task first reached built (or later), or None."""
    for e in t.get("log") or []:
        if e.get("to") in FINISHED:
            return e["at"][:10]
    return None


def in_window(day, since=None, until=None):
    return bool(day) and (not since or day >= since) and (not until or day <= until)


def finished_between(tasks, since=None, until=None):
    return [t for t in tasks if in_window(finished_on(t), since, until)]


def stats(tasks, since=None, until=None, events=None):
    out = []
    if since or until:
        tasks = finished_between(tasks, since, until)
        out.append("window: %s..%s, %d task(s) finished" % (since or "start", until or "today", len(tasks)))
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
    reviewed = [t for t in tasks if t.get("reviews")]
    if reviewed:
        first = len([t for t in reviewed if t["reviews"][0].get("verdict") == "accept"])
        rounds = sum(len(t["reviews"]) for t in reviewed) / float(len(reviewed))
        out.append("checker: %d task(s) reviewed, %d accepted first time, %.1f reviews per task"
                   % (len(reviewed), first, rounds))
    else:
        out.append("checker: no reviews yet")
    import loop  # the loop's own state file (scripts/loop.py imports this module)
    events = loop.load() if events is None else events
    if since or until:
        events = [e for e in events if in_window(e.get("at", "")[:10], since, until)]
    out.append(loop.stats_line(events))
    return "\n".join(out)


# ---------------------------------------------------------------- checker

# Changed paths -> the suites the checker re-runs (the same commands as CI).
SUITES = [
    (("src/", "patcher/", "tests/", "locations/", "collectibles/", "qa/", "ForestOverlay.csproj"),
     "dotnet test tests/ForestOverlay.Tests/ForestOverlay.Tests.csproj -c Release --nologo"),
    (("site/", "src/Data/", "community/"), "dotnet test site/ForestSite.Tests -c Release --nologo"),
    (("bot/", "knowledge/"), "dotnet test bot/ForestBot.Tests -c Release --nologo"),
    (("scripts/", ".githooks/"),
     "python scripts/tests/test_tasks.py && python scripts/tests/test_loop.py && "
     "python scripts/tests/test_session.py && "
     "python scripts/tests/test_lint.py && python scripts/tests/test_hooks.py && "
     "python scripts/tests/test_audit.py && "
     "python scripts/tests/test_watch_deploy.py && python scripts/tests/test_read_report.py"),
]
# Generated or bookkeeping files left out of the brief's diff.
BRIEF_SKIP = ("tasks/tasks.jsonl", "docs/tasks.md")


def suites_for(paths):
    out = []
    for prefixes, cmd in SUITES:
        if any(p.startswith(prefixes) for p in paths) and cmd not in out:
            out.append(cmd)
    out.append("python scripts/lint.py")
    return out


def git_out(args):
    # stdin closed: under the MCP server (qa_todo from_tasks) it is the server's never-closing
    # pipe, and git blocked on it (T-0007).
    r = subprocess.run(["git"] + args, cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
                       errors="replace", timeout=30, stdin=subprocess.DEVNULL)
    if r.returncode != 0:
        raise TaskError("git %s failed: %s" % (" ".join(args[:2]), r.stderr.strip()[:200]),
                        "the brief reads the task's commits from git",
                        "check the sha with `git show --stat <sha>`; fix it with `tasks.py set T-n --commit`")
    return r.stdout


def brief(t, show=git_out, limit=BRIEF_LIMIT):
    """What a fresh-context checker reads: the contract, the maker's evidence, earlier reviews,
    the task's diff (bookkeeping files left out, cut at limit) and the suites to re-run."""
    tid = t["id"]
    commits = t.get("commits") or []
    if not commits:
        raise TaskError("%s has no commits" % tid, "the checker reviews the task's diff",
                        "`tasks.py set %s --commit <sha>`" % tid)
    out = ["# Checker brief: %s %s" % (tid, t["title"]), "",
           "area %s, status %s, maker %s, commits %s" % (t["area"], t["status"], t.get("maker") or "?",
                                                          " ".join(commits)), "",
           "## Contract", "", "behavior: %s" % (t.get("behavior") or "-"),
           "scope: %s" % (t.get("scope") or "-"), "keep: %s" % (t.get("keep") or "-"), "verify:"]
    out += ["  - " + v for v in t.get("verify") or []]
    if t.get("notes"):
        out.append("notes: %s" % t["notes"])
    out += ["", "## Evidence recorded so far", ""]
    out += ["- %s (%s): %s" % (e.get("at"), e.get("by"), e.get("what")) for e in t.get("evidence") or []] or ["- none"]
    if t.get("reviews"):
        out += ["", "## Earlier reviews (check these faults are fixed)", ""]
        for r in t["reviews"]:
            out.append("- %s %s by %s: %s" % (r.get("at"), r.get("verdict"), r.get("by"),
                                              "; ".join(r.get("faults") or []) or "no faults"))
    skip = [":(exclude)" + p for p in BRIEF_SKIP]
    paths = []
    diffs = []
    for sha in commits:
        names = show(["show", "--name-only", "--format=", sha, "--", "."] + skip)
        paths += [p for p in names.splitlines() if p.strip() and p not in paths]
        diffs.append(show(["show", "--stat", "--patch", "--format=commit %H%n%n%B", sha, "--", "."] + skip))
    out += ["", "## Re-run (from the repo root)", ""] + ["    " + c for c in suites_for(paths)]
    diff = "\n".join(diffs)
    out += ["", "## Diff", ""]
    if len(diff) > limit:
        out += [diff[:limit], "", "... cut at %d of %d characters: read the rest with "
                "`git show <sha> -- <file>`; files: %s" % (limit, len(diff), ", ".join(paths))]
    else:
        out.append(diff)
    return "\n".join(out)


def parse_scores(text):
    scores = {}
    for part in (text or "").split(","):
        if not part.strip():
            continue
        k, _, v = part.partition("=")
        k, v = k.strip(), v.strip()
        if k not in RUBRIC or v not in ("0", "1", "2", "n/a"):
            raise TaskError("bad score %r" % part.strip(),
                            "each rubric item is %s, scored 0, 1, 2 or n/a" % ", ".join(RUBRIC),
                            "--scores " + ",".join(r + "=2" for r in RUBRIC))
        scores[k] = v if v == "n/a" else int(v)
    missing = [r for r in RUBRIC if r not in scores]
    if missing:
        raise TaskError("scores missing %s" % ", ".join(missing), "the rubric is scored in full (docs/harness.md 9d)",
                        "--scores " + ",".join(r + "=2" for r in RUBRIC))
    return scores


# ---------------------------------------------------------------- commands

def cmd_add(tasks, a):
    t = {"id": new_id(tasks), "title": a.title.strip(), "area": a.area, "priority": a.priority,
         "status": "todo", "needs": a.needs, "question": a.question, "behavior": a.behavior,
         "scope": a.scope, "verify": a.verify or [], "keep": a.keep, "qa": a.qa,
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
    for field in ("title", "behavior", "scope", "keep", "notes", "release", "layer", "needs", "area", "qa"):
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


def cmd_review(tasks, a):
    t = find(tasks, a.id)
    tid = t["id"]
    if a.by == t.get("maker") or a.by == t.get("owner"):
        raise TaskError("%s reviewed by its maker (%s)" % (tid, a.by),
                        "a review is a fresh context that did not write the change (docs/harness.md 7d)",
                        "spawn the forest-checker agent with \"Check %s\"" % tid)
    if t["status"] not in ("built", "released"):
        raise TaskError("%s is %s" % (tid, t["status"]), "the checker reviews built (or released) work",
                        "`tasks.py show %s`" % tid)
    scores = parse_scores(a.scores)
    faults = [f.strip() for f in a.faults or [] if f.strip()]
    if a.verdict == "accept" and (scores["correctness"] != 2 or 0 in scores.values()):
        raise TaskError("accept with correctness %s and scores %s" % (scores["correctness"], a.scores),
                        "accept needs correctness 2 and no 0 (docs/harness.md 9d)",
                        "--verdict revise with the faults, or rescore")
    if a.verdict != "accept" and not faults:
        raise TaskError("%s with no faults" % a.verdict, "the maker needs to know what to fix or decide",
                        "add --faults \"file:line - what is wrong\" ...")
    t.setdefault("reviews", []).append({"at": today(), "by": a.by, "verdict": a.verdict, "scores": scores,
                                        "faults": faults, "commits": list(t.get("commits") or [])})
    if a.verdict == "revise" and t["status"] == "built":
        try:
            transition(tasks, t, "in-progress", by=t.get("maker") or "main")
        except TaskError:
            transition(tasks, t, "todo", by=a.by)  # the maker is busy: back on the list
    elif a.verdict == "block":
        t["question"] = "Checker (%s) blocked it: %s" % (a.by, "; ".join(faults))
        t["needs"] = "author-decision"
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
        sp.add_argument("--qa")
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
    sp = sub.add_parser("brief")
    sp.add_argument("id")
    sp = sub.add_parser("review")
    sp.add_argument("id")
    sp.add_argument("--verdict", choices=VERDICTS, required=True)
    sp.add_argument("--scores", required=True)
    sp.add_argument("--faults", nargs="*")
    sp.add_argument("--by", required=True)
    sp = sub.add_parser("stats")
    sp.add_argument("--since")
    sp.add_argument("--until")
    sub.add_parser("check")
    sub.add_parser("render")
    sub.add_parser("qa-todo")
    a = p.parse_args(argv)
    # Titles carry emoji; a Windows console or pipe defaults to cp1252 and crashed `list`.
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")

    try:
        if a.cmd in ("list", "next", "show", "brief", "stats", "check", "qa-todo"):
            return run(a)
        with locked():  # every other command ends in save(): load -> change -> save is one step
            return run(a)
    except TaskError as e:
        print(e, file=sys.stderr)
        return 1


def run(a):
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
        elif a.cmd == "brief":
            print(brief(find(tasks, a.id)))
        elif a.cmd == "stats":
            for d in (a.since, a.until):
                if d and not re.match(r"^\d{4}-\d{2}-\d{2}$", d):
                    raise TaskError("bad date %r" % d, "the window is whole days", "--since / --until YYYY-MM-DD")
            print(stats(tasks, a.since, a.until))
        elif a.cmd == "check":
            validate(tasks)
            with open(VIEW, encoding="utf-8") as f:
                if f.read() != render(tasks):
                    raise TaskError("docs/tasks.md is out of date", "it is generated from tasks.jsonl",
                                    "`python scripts/tasks.py render` and commit it")
            print("tasks: %d ok" % len(tasks))
        elif a.cmd == "qa-todo":
            tags = git_out(["tag", "--list", "v*", "--sort=-v:refname"]).split()
            print(qa_todo(tasks, tags[0] if tags else None))
        elif a.cmd == "render":
            save(tasks)
            print("rendered " + os.path.relpath(VIEW, ROOT))
        else:
            if a.cmd == "add":
                if not a.area:
                    raise TaskError("no --area", "every task has an area", "--area " + "|".join(AREAS))
                t = cmd_add(tasks, a)
            else:
                t = {"set": cmd_set, "start": cmd_start, "evidence": cmd_evidence, "note": cmd_note,
                     "review": cmd_review}[a.cmd](tasks, a)
            save(tasks)
            print(line(t))
    except TaskError as e:
        print(e, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
