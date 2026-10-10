"""The assisted loop (docs/harness.md 12, Stage A): a goal runner over the task file.

One main session orchestrates rounds back to back; each round is one task
taken from `tasks.py next`, worked by a fresh subagent for its area, checked,
evidenced and closed with a one-paragraph summary (author, 2026-10-07). The
next step is read from the task's state in tasks/tasks.jsonl, never from the
model's say-so, and the stop conditions are machine checks.

tasks/loop.jsonl is append-only (one event per line, merges cleanly):
begin / round / park / end / intervene / stop.

    python scripts/loop.py begin [--bridge] [--max-rounds 5]   # opens a run, lists parked questions
    python scripts/loop.py next          # the open round's next action, or opens the next round, or STOP
    python scripts/loop.py peek          # what next would say, writing nothing
    python scripts/loop.py end --summary "one paragraph the author can skim"
    python scripts/loop.py intervene "what the author fixed or decided"
    python scripts/loop.py stop --reason "..."                 # ends the run early
    python scripts/loop.py report [R-0001]                     # the run: rounds, passes, parked, summaries

Stop conditions (author, 2026-10-07): max rounds done (5), nothing left in the
pool (`tasks.py next`: needs none, + bridge with --bridge), no progress for 3
rounds in a row; and the orchestrator's own context past 300k (author,
2026-10-09, T-0249, raised from 200k 2026-10-10: the night run's main session grew to 541k and was 15 % of
four days' usage) - `begin` refuses then too, so the next run starts in a
fresh session. A task given a third checker revise in its round is parked
(needs author-decision, the faults as its question) and the loop moves on.

Errors print WHAT / WHY / FIX and exit 1, like tasks.py. `next` exits 0 with
an action, 3 with STOP (so a shell loop can tell them apart).
"""
import argparse
import datetime
import glob
import json
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tasks as T  # noqa: E402

LOOP = os.path.join(T.ROOT, "tasks", "loop.jsonl")
MAX_ROUNDS = 5          # per run (author, 2026-10-07)
MAX_REVISES = 2         # checker revises a task may take in its round; the next one parks it
NO_PROGRESS_STOP = 3    # rounds in a row without progress stop the run (docs/harness.md 12)
MIN_SUMMARY = 40        # characters; a summary is a paragraph, not "done"
MAX_CONTEXT = 300000    # tokens in the orchestrator's context; past it the run stops (T-0249)
EXIT_STOP = 3
# A round ends on one of these; progress = the task moved forward for good.
PROGRESS = ("built", "released", "confirmed", "wontfix")
# Who does the work, by area (docs/areas/workflow.md *Subagents*).
WORKER = {
    "plugin": "forest-dev in a worktree (forest-researcher when the game side is unknown or it needs live proof)",
    "site": "forest-site in a worktree",
    "bot": "forest-knowledge (small bot/ fixes) or forest-dev",
    "knowledge": "forest-knowledge",
    "research": "forest-researcher",
    "harness": "main itself (forest-dev for a large script)",
    "docs": "main itself",
    "release": "main itself (skill release)",
}


class LoopError(T.TaskError):
    pass


def now():
    return datetime.datetime.now().replace(microsecond=0).isoformat()


# ---------------------------------------------------------------- state file

def parse(text):
    events = []
    for n, line in enumerate(text.splitlines(), 1):
        line = line.strip()
        if not line:
            continue
        try:
            events.append(json.loads(line))
        except ValueError as e:
            raise LoopError("loop.jsonl line %d is not JSON (%s)" % (n, e),
                            "the loop state is one JSON event per line",
                            "fix the line by hand (likely a merge leftover)")
    return events


def load(path=LOOP):
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        return parse(f.read())


def append(new, path=LOOP):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with T.locked(path):  # one write, so lines of two processes never interleave
        with open(path, "a", encoding="utf-8", newline="\n") as f:
            for e in new:
                f.write(json.dumps(e, ensure_ascii=False) + "\n")


def runs(events):
    """Replays the events: a list of runs, oldest first, each
    {id, begin, rounds: [{task, round, start, end, park}], interventions, stop}."""
    out = []
    by_id = {}
    for e in events:
        kind = e.get("event")
        if kind == "begin":
            r = {"id": e["run"], "begin": e, "rounds": [], "interventions": [], "stop": None}
            out.append(r)
            by_id[e["run"]] = r
            continue
        r = by_id.get(e.get("run"))
        if r is None:
            continue
        if kind == "round":
            r["rounds"].append({"task": e["task"], "round": e["round"], "start": e, "end": None, "park": None})
        elif kind in ("end", "park") and r["rounds"]:
            r["rounds"][-1][kind] = e
        elif kind == "intervene":
            r["interventions"].append(e)
        elif kind == "stop":
            r["stop"] = e
    return out


def open_run(events):
    rs = runs(events)
    return rs[-1] if rs and rs[-1]["stop"] is None else None


def open_round(run):
    if run and run["rounds"] and run["rounds"][-1]["end"] is None:
        return run["rounds"][-1]
    return None


def new_run_id(events):
    n = len([e for e in events if e.get("event") == "begin"])
    return "R-%04d" % (n + 1)


# ---------------------------------------------------------------- rules

def progressed(t):
    """The task moved forward for good: built and through its checker (or none needed),
    released, confirmed or dropped - and nothing waits on the author."""
    if t.get("question") or t["needs"] in ("author-decision", "author-present"):
        return False
    if t["status"] == "built" and t.get("checker"):
        return T.accepted(t)
    return t["status"] in PROGRESS


def revises_this_round(t, rnd):
    new = (t.get("reviews") or [])[rnd["start"].get("reviews_before", 0):]
    return [r for r in new if r.get("verdict") == "revise"]


def context_tokens(repo=None, home=None):
    """The context size of this repo's most recent main session (its newest transcript's last
    API call: input + cache reads + cache writes), or None when it cannot be read. Claude Code
    keeps main transcripts as ~/.claude/projects/<repo slug>/<session>.jsonl. The newest by mtime
    is taken as the orchestrator: a second live session in the repo can be read instead (a wrong
    stop / refusal says the size, so it is visible), and an unreadable tail returns None, which
    leaves the guard off rather than blocking the loop."""
    home = home or os.path.expanduser("~")
    slug = re.sub(r"[^A-Za-z0-9]", "-", os.path.abspath(repo or T.ROOT))
    files = glob.glob(os.path.join(home, ".claude", "projects", slug, "*.jsonl"))
    if not files:
        return None
    try:
        with open(max(files, key=os.path.getmtime), "rb") as f:
            f.seek(0, 2)
            f.seek(max(0, f.tell() - 2000000))
            lines = f.read().decode("utf-8", "replace").splitlines()
    except OSError:
        return None
    for line in reversed(lines):
        try:
            e = json.loads(line)
        except ValueError:
            continue
        u = (e.get("message") or {}).get("usage") if isinstance(e, dict) else None
        if e.get("type") == "assistant" and u:
            return sum(u.get(k) or 0 for k in ("input_tokens", "cache_read_input_tokens",
                                               "cache_creation_input_tokens"))
    return None


def context_reason(tokens):
    if tokens is not None and tokens > MAX_CONTEXT:
        return ("the orchestrator's context is %dk (over %dk) - hand off and end this session; "
                "the next run starts in a fresh one" % (tokens // 1000, MAX_CONTEXT // 1000))
    return None


def stop_reason(run, tasks):
    """Why the run must stop before opening another round, or None."""
    why = context_reason(context_tokens())
    if why:
        return why
    done = [r for r in run["rounds"] if r["end"]]
    limit = run["begin"].get("max_rounds", MAX_ROUNDS)
    if len(done) >= limit:
        return "%d rounds done (the run's limit)" % limit
    tail = done[-NO_PROGRESS_STOP:]
    if len(tail) == NO_PROGRESS_STOP and not any(r["end"].get("progress") for r in tail):
        return "no progress for %d rounds in a row (%s) - look at what keeps failing" % (
            NO_PROGRESS_STOP, ", ".join(r["task"] for r in tail))
    return None


def pick(run, tasks):
    taken = {r["task"] for r in run["rounds"]}
    return T.pick_next(tasks, bridge=run["begin"].get("bridge", False), by="main", skip=taken)


PLUGIN_PATHS = T.PLUGIN_PATHS
commit_files = T.commit_files   # tests patch L.commit_files


def needs_release(t, files_of=None):
    """A plugin-area task ships in a release only if a commit touches a plugin path; one that
    changed only scripts / docs has nothing to release (T-0154). Unknown commits count as plugin.
    The same rule as tasks.release_plan (T-0196)."""
    if t["area"] not in T.RELEASED_AREAS:
        return False
    commits = t.get("commits") or []
    if not commits:
        return True
    return T.touches_plugin(commits, files_of or commit_files)


def action(t, rnd):
    """The one thing the round's task needs next, from its state alone."""
    tid = t["id"]
    end = "`python scripts/loop.py end --summary \"...\"` (one paragraph: what changed, how it was proved, what is left)"
    if t.get("question") or t["needs"] in ("author-decision", "author-present"):
        return "parked (%s): %s" % (t["needs"], end)
    s = t["status"]
    if s == "todo" and revises_this_round(t, rnd):
        return ("revise: the checker sent %s back while main was busy - `tasks.py start %s --by main`, then "
                "fix the faults of its last review" % (tid, tid))
    if s == "todo":
        return ("contract: check git, the code and docs/confirmed.md for it first (migrated tasks can be stale), "
                "then `tasks.py start %s --by main --behavior .. --scope .. --verify .. --keep ..`; an open "
                "choice (intent, design, wording, scope) is a question, not a default: "
                "`tasks.py set %s --question \"...\" --needs author-decision`, then end the round" % (tid, tid))
    if s == "in-progress":
        rv = revises_this_round(t, rnd)
        if rv:
            return ("revise %d of %d: hand the checker's faults (`tasks.py show %s`, last review) back to the "
                    "maker (SendMessage to the agent that built it, or a fresh one with the faults), commit, "
                    "then `tasks.py set %s --status built --commit <sha> --by main`"
                    % (len(rv), MAX_REVISES, tid, tid))
        who = WORKER.get(t["area"], "main itself")
        if who.startswith("main itself"):
            return ("do the work: %s, from the contract (`tasks.py show %s`); commit, then `tasks.py set %s "
                    "--status built --commit <sha> --by main`" % (who, tid, tid))
        return ("do the work: %s, told only \"%s: the contract is `tasks.py show %s`\"; merge its commits, "
                "then `tasks.py set %s --status built --commit <sha> --by main`" % (who, tid, tid, tid))
    if s == "built":
        if t.get("checker") and not T.accepted(t):
            return "check: spawn forest-checker with only \"Check %s\" (it records tasks.py review)" % tid
        if needs_release(t):
            return ("release: skill release (bump.py moves %s to released), then the post-release smoke" % tid)
        if t["area"] in T.CHECKED_AREAS and t["area"] not in T.RELEASED_AREAS:
            return ("ship: push to main, skill deploy-watch, then `tasks.py evidence %s \"<the live check>\" "
                    "--by deploy-watch` and `tasks.py set %s --status confirmed --by main`" % (tid, tid))
        return ("evidence: one `tasks.py evidence %s \"<proof>\" --by <who checked>` per verify step, then "
                "`tasks.py set %s --status confirmed --by main`" % (tid, tid))
    if s == "released":
        return ("in-game evidence (the game must be up): `python scripts/e2e.py --evidence` when a journey "
                "covers %s, else forest-tester told \"Check %s in game\"; then `tasks.py set %s --status "
                "confirmed --by main`. With no game, end the round - released counts as progress and the "
                "in-game check waits" % (tid, tid, tid))
    return "finished (%s): %s" % (s, end)


# ---------------------------------------------------------------- commands

def cmd_begin(events, tasks, max_rounds=MAX_ROUNDS, bridge=False, by="main"):
    run = open_run(events)
    if run:
        raise LoopError("run %s is still open" % run["id"], "one loop run at a time",
                        "`loop.py next` continues it, `loop.py stop --reason \"...\"` ends it")
    why = context_reason(context_tokens())
    if why:
        raise LoopError("no new run in this session", why, "the handoff, then a new session (/clear) runs `loop.py begin`")
    rid = new_run_id(events)
    new = [{"at": now(), "event": "begin", "run": rid, "max_rounds": max_rounds, "bridge": bridge, "by": by}]
    parked = [t for t in tasks if t.get("question") and t["status"] not in T.DONE]
    lines = ["run %s begun: at most %d rounds, pool needs none%s" % (rid, max_rounds, " + bridge" if bridge else "")]
    if parked:
        lines.append("parked questions - ask the author now (`tasks.py set T-n --answer \"...\"`):")
        lines += ["  %s %s\n    Q: %s" % (t["id"], t["title"], t["question"]) for t in parked]
    else:
        lines.append("no parked questions")
    lines.append("next: `python scripts/loop.py next`")
    return new, "\n".join(lines), 0


def cmd_next(events, tasks):
    """Returns (new events, text, exit code); peek is the same call with the events dropped."""
    run = open_run(events)
    if not run:
        raise LoopError("no open run", "a round belongs to a run (its limits and its report)",
                        "`python scripts/loop.py begin`")
    new = []
    out = []
    rnd = open_round(run)
    if rnd:
        t = T.find(tasks, rnd["task"])
        rv = revises_this_round(t, rnd)
        if t["status"] in ("in-progress", "todo") and len(rv) > MAX_REVISES:
            faults = "; ".join(rv[-1].get("faults") or []) or "see its last review"
            park = {"at": now(), "event": "park", "run": run["id"], "round": rnd["round"], "task": t["id"],
                    "why": "%d checker revises in one round" % len(rv), "question":
                    "Loop: %d checker revises in one round - the last faults: %s. Fix how, or drop it?"
                    % (len(rv), faults)}
            end = {"at": now(), "event": "end", "run": run["id"], "round": rnd["round"], "task": t["id"],
                   "status": "blocked", "progress": False, "parked": True,
                   "summary": "Parked after %d checker revises in this round; the faults are its question."
                   % len(rv)}
            new += [park, end]
            out.append("round %d: %s parked (%s) - its question is set; the round is closed"
                       % (rnd["round"], t["id"], park["why"]))
            rnd["park"], rnd["end"] = park, end
        else:
            return [], "round %d: %s %s [%s]\nnext: %s" % (rnd["round"], t["id"], t["title"], t["status"],
                                                          action(t, rnd)), 0
    why = stop_reason(run, tasks)
    t = None if why else pick(run, tasks)
    if not why and t is None:
        why = "nothing left in the pool (`tasks.py next`%s)" % (" --bridge" if run["begin"].get("bridge") else "")
    if why:
        new.append({"at": now(), "event": "stop", "run": run["id"], "reason": why})
        out.append("STOP %s: %s\nreport: `python scripts/loop.py report` - then the handoff" % (run["id"], why))
        return new, "\n".join(out), EXIT_STOP
    n = len(run["rounds"]) + 1
    start = {"at": now(), "event": "round", "run": run["id"], "round": n, "task": t["id"],
             "reviews_before": len(t.get("reviews") or [])}
    new.append(start)
    rnd = {"task": t["id"], "round": n, "start": start, "end": None, "park": None}
    out.append("round %d of %d: %s" % (n, run["begin"].get("max_rounds", MAX_ROUNDS), T.line(t)))
    out.append("next: " + action(t, rnd))
    return new, "\n".join(out), 0


def cmd_end(events, tasks, summary, tid=None):
    run = open_run(events)
    rnd = open_round(run)
    if not rnd:
        raise LoopError("no open round", "end closes the round `loop.py next` opened", "`loop.py next`")
    t = T.find(tasks, rnd["task"])
    if tid and T.normalise_id(tid) != t["id"]:
        raise LoopError("the open round is %s, not %s" % (t["id"], T.normalise_id(tid)),
                        "one round, one task", "`loop.py end --summary ...` with no id, or the right one")
    if t["status"] == "in-progress":
        raise LoopError("%s is still in progress" % t["id"],
                        "a round ends when its task passes or is written up as parked (WIP = 1, lecture 7)",
                        "finish it, or park it: `tasks.py set %s --question \"...\" --needs author-decision` "
                        "(or --status todo --notes \"why\" to give it back)" % t["id"])
    parked = bool(t.get("question") or t["needs"] in ("author-decision", "author-present"))
    if t["status"] == "built" and t.get("checker") and not T.accepted(t) and not parked:
        raise LoopError("%s is built with no accept" % t["id"],
                        "a behaviour change is checked before the round can count it (docs/harness.md 7d)",
                        "spawn forest-checker with \"Check %s\"" % t["id"])
    summary = (summary or "").strip()
    if len(summary) < MIN_SUMMARY or "\n\n" in summary:
        raise LoopError("the summary is %d characters%s" % (len(summary), " in several paragraphs"
                                                             if "\n\n" in summary else ""),
                        "every round leaves one paragraph the author can skim (docs/harness.md 12, "
                        "comprehension rot)",
                        "--summary \"what changed, how it was proved, what is left\" (one paragraph, "
                        "%d+ characters)" % MIN_SUMMARY)
    e = {"at": now(), "event": "end", "run": run["id"], "round": rnd["round"], "task": t["id"],
         "status": t["status"], "progress": progressed(t), "parked": parked, "summary": summary}
    return [e], "round %d closed: %s %s (%s)\nnext: `python scripts/loop.py next`" % (
        rnd["round"], t["id"], t["status"], "progress" if e["progress"] else "no progress"), 0


def cmd_intervene(events, what):
    run = open_run(events)
    if not run:
        raise LoopError("no open run", "an intervention is counted against a run", "`loop.py begin`")
    if not (what or "").strip():
        raise LoopError("no text", "say what the author fixed or decided", "`loop.py intervene \"...\"`")
    rnd = open_round(run)
    e = {"at": now(), "event": "intervene", "run": run["id"], "round": rnd["round"] if rnd else None,
         "what": what.strip()}
    return [e], "intervention logged on %s%s" % (run["id"], " round %d" % rnd["round"] if rnd else ""), 0


def cmd_stop(events, reason):
    run = open_run(events)
    if not run:
        raise LoopError("no open run", "nothing to stop", "`loop.py report` shows the last one")
    rnd = open_round(run)
    if rnd:
        raise LoopError("round %d (%s) is open" % (rnd["round"], rnd["task"]),
                        "a run ends between rounds, so every round has its outcome and summary",
                        "`loop.py end --summary \"...\"` first (park the task if it cannot finish)")
    if not (reason or "").strip():
        raise LoopError("no --reason", "the report says why the run ended", "--reason \"...\"")
    e = {"at": now(), "event": "stop", "run": run["id"], "reason": reason.strip()}
    return [e], "run %s stopped: %s" % (run["id"], reason.strip()), 0


def report(events, rid=None):
    rs = runs(events)
    if rid:
        rs = [r for r in rs if r["id"] == rid]
    if not rs:
        return "(no loop runs)"
    r = rs[-1]
    rounds = r["rounds"]
    done = [x for x in rounds if x["end"]]
    passed = len([x for x in done if x["end"].get("progress")])
    parked = len([x for x in done if x["end"].get("parked")])
    lines = ["%s begun %s by %s: %d round(s), %d progressed, %d without progress (%d parked), "
             "%d intervention(s)%s" % (r["id"], r["begin"]["at"], r["begin"].get("by", "?"), len(rounds), passed,
                                        len(done) - passed, parked, len(r["interventions"]),
                                        "" if r["stop"] else " - still open")]
    if r["stop"]:
        lines.append("stopped %s: %s" % (r["stop"]["at"], r["stop"]["reason"]))
    for x in rounds:
        if x["end"]:
            lines.append("%d. %s -> %s%s: %s" % (x["round"], x["task"], x["end"]["status"],
                                                 "" if x["end"].get("progress") else " (no progress)",
                                                 x["end"]["summary"]))
        else:
            lines.append("%d. %s - open" % (x["round"], x["task"]))
    for i in r["interventions"]:
        lines.append("intervention%s: %s" % (" (round %s)" % i["round"] if i.get("round") else "", i["what"]))
    return "\n".join(lines)


def stats_line(events):
    """One line for `tasks.py stats` (docs/harness.md *Measuring the harness*)."""
    rs = runs(events)
    if not rs:
        return "loop: no runs yet"
    done = [x for r in rs for x in r["rounds"] if x["end"]]
    passed = len([x for x in done if x["end"].get("progress")])
    inter = sum(len(r["interventions"]) for r in rs)
    return ("loop: %d run(s), %d round(s), %d progressed, %d without progress, %d intervention(s), "
            "%.1f rounds per run" % (len(rs), len(done), passed, len(done) - passed, inter,
                                     len(done) / float(len(rs))))


def main(argv=None, loop_path=None, tasks_path=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    sp = sub.add_parser("begin")
    sp.add_argument("--bridge", action="store_true")
    sp.add_argument("--max-rounds", type=int, default=MAX_ROUNDS)
    sp.add_argument("--by", default="main")
    sub.add_parser("next")
    sub.add_parser("peek")
    sp = sub.add_parser("end")
    sp.add_argument("id", nargs="?")
    sp.add_argument("--summary", required=True)
    sp = sub.add_parser("intervene")
    sp.add_argument("what")
    sp = sub.add_parser("stop")
    sp.add_argument("--reason", required=True)
    sp = sub.add_parser("report")
    sp.add_argument("run", nargs="?")
    a = p.parse_args(argv)
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")
    loop_path = loop_path or LOOP
    try:
        if a.cmd in ("report", "peek"):
            return run(a, loop_path, tasks_path)
        return run(a, loop_path, tasks_path)  # git calls run unlocked; only a park takes the lock
    except T.TaskError as e:
        print(e, file=sys.stderr)
        return 1


def run(a, loop_path, tasks_path):
    try:
        events = load(loop_path)
        tasks = T.load(tasks_path or T.TASKS)
        if a.cmd == "report":
            print(report(events, a.run))
            return 0
        if a.cmd == "begin":
            if a.max_rounds < 1:
                raise LoopError("--max-rounds %d" % a.max_rounds, "a run takes at least one round",
                                "--max-rounds %d (the default)" % MAX_ROUNDS)
            new, text, code = cmd_begin(events, tasks, a.max_rounds, a.bridge, a.by)
        elif a.cmd in ("next", "peek"):
            new, text, code = cmd_next(events, tasks)
            if a.cmd == "peek":
                new = []
        elif a.cmd == "end":
            new, text, code = cmd_end(events, tasks, a.summary, a.id)
        elif a.cmd == "intervene":
            new, text, code = cmd_intervene(events, a.what)
        else:
            new, text, code = cmd_stop(events, a.reason)
        if new and any(e["event"] == "park" for e in new):
            park = [e for e in new if e["event"] == "park"][0]
            with T.locked(tasks_path or T.TASKS):  # load -> save of the task file is one step
                tasks = T.load(tasks_path or T.TASKS)  # fresh: another writer may have saved meanwhile
                t = T.find(tasks, park["task"])
                t["question"] = park["question"]
                t["needs"] = "author-decision"
                T.transition(tasks, t, "blocked", by="loop")
                t["updated"] = T.today()
                T.save(tasks, tasks_path or T.TASKS, None if tasks_path else T.VIEW)
        append(new, loop_path)
        print(text)
        return code
    except T.TaskError as e:
        print(e, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
