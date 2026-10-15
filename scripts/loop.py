"""The assisted loop (docs/harness.md 12, Stage A): a goal runner over the task file.

One main session orchestrates rounds back to back; each round is one task
taken from `tasks.py next`, worked by a fresh subagent for its area, checked,
evidenced and closed with a one-paragraph summary (author, 2026-10-07). The
next step is read from the task's state in tasks/tasks.jsonl, never from the
model's say-so, and the stop conditions are machine checks.

tasks/loop.jsonl is append-only (one event per line, merges cleanly):
begin / round / park / end / intervene / stop / smoke-fail, and the chain's
chain-begin / chain-dispatch / chain-stop.

    python scripts/loop.py begin [--bridge] [--max-rounds 5] [--max-context N] [--unattended --chain C-n]
    python scripts/loop.py next          # the open round's next action, or opens the next round, or STOP
    python scripts/loop.py peek          # what next would say, writing nothing
    python scripts/loop.py end --summary "one paragraph the author can skim"
    python scripts/loop.py intervene "what the author fixed or decided"
    python scripts/loop.py stop --reason "..."                 # ends the run early
    python scripts/loop.py report [R-0001]                     # the run: rounds, passes, parked, summaries
    python scripts/loop.py smoke-fail T-n --report PATH        # a failed post-release smoke: park, stop the chain

Stage B, the unattended chain (docs/harness.md 12, T-0016; author 2026-10-15): the author
starts it from a conversation and steps away; that conversation (the driver) asks `chain next`
and starts one fresh session per action - a due job (the weekly cleanup) or one unattended run
(`begin --unattended --chain C-n`: nothing asked, the target only) - and is woken when it ends.

    python scripts/loop.py chain begin [--target T-1,T-2 | --area A] [--bridge]
    python scripts/loop.py chain next [--again]   # the driver's next action: due job, run, wait or STOP
    python scripts/loop.py chain status           # progress to the target, the days to each due item
    python scripts/loop.py chain stop --reason "..."

Stop conditions (author, 2026-10-07): max rounds done (5), nothing left in the
pool (`tasks.py next`: needs none, + bridge with --bridge), no progress for 3
rounds in a row; and the orchestrator's own context past 250k (author,
2026-10-09, T-0249, raised from 200k 2026-10-10, 250k from 2026-10-11 T-0319: the night run's main session grew to 541k and was 15 % of
four days' usage) - `begin` refuses then too, so the next run starts in a
fresh session. A task given a third checker revise in its round is parked
(needs author-decision, the faults as its question) and the loop moves on.

A chain stops when its target / pool is empty, a run of it made no progress, a
post-release smoke failed (`smoke-fail` parks the release's task; no rollback -
author 2026-10-15), the driver's context passes the cap, or on `chain stop`; its
summary goes to a file on the Desktop and the session-start report.

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
MAX_CONTEXT = 250000    # tokens in the orchestrator's context; past it the run stops (T-0249; 250k: T-0319)
EXIT_STOP = 3
# A round ends on one of these; progress = the task moved forward for good.
PROGRESS = ("built", "released", "confirmed", "wontfix")
# Who does the work, by area (docs/areas/workflow.md *Subagents*).
WORKER = {
    "plugin": "forest-dev in a worktree (forest-researcher when the game side is unknown or it needs live proof;"
              " its build plan in tasks/notes/ then goes to forest-dev)",
    "site": "forest-site in a worktree",
    "bot": "forest-knowledge (small bot/ fixes) or forest-dev",
    "knowledge": "forest-knowledge",
    "research": "forest-researcher (a plain fix: its build plan in tasks/notes/ goes to forest-dev)",
    "harness": "main itself (forest-dev for a large script)",
    "docs": "main itself",
    "release": "main itself (skill release)",
}
# Routing by tier (T-0313, author 2026-10-11; tasks/notes/T-0313.md): normal (or unset) is WORKER at the
# agents' frontmatter defaults, exactly as before. Each entry: (agent, model, effort); agent "main" =
# main itself. Measured per tier with `agent-cost.py --tiers`, then moved to the best output per usage.
ROUTES = {
    "easy": {"plugin": ("forest-dev", "sonnet", "low"), "research": ("forest-dev", "sonnet", "low"),
             "bot": ("forest-dev", "sonnet", "low"), "site": ("forest-site", "sonnet", "low"),
             "knowledge": ("forest-knowledge", "sonnet", "low"), "harness": ("main", None, None),
             "docs": ("main", None, None), "release": ("main", None, None)},
    "hard": {"plugin": ("forest-researcher", "opus", "high"), "research": ("forest-researcher", "opus", "high"),
             "bot": ("forest-dev", "opus", "high"), "site": ("forest-site", "opus", "high"),
             "knowledge": ("forest-knowledge", "opus", "high"), "harness": ("main", None, "high"),
             "docs": ("main", None, "high"), "release": ("main", None, "high")},
}
# Where an agent works (WORKER's wording), for the routed line.
WHERE = {"forest-dev": " in a worktree", "forest-site": " in a worktree"}


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
            r = {"id": e["run"], "begin": e, "rounds": [], "interventions": [], "stop": None, "smoke": []}
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
        elif kind == "smoke-fail":
            r["smoke"].append(e)
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


def context_reason(tokens, limit=MAX_CONTEXT):
    if tokens is not None and tokens > limit:
        return ("the orchestrator's context is %dk (over %dk) - hand off and end this session; "
                "the next run starts in a fresh one" % (tokens // 1000, limit // 1000))
    return None


def stop_reason(run, tasks):
    """Why the run must stop before opening another round, or None."""
    why = context_reason(context_tokens(), run["begin"].get("max_context", MAX_CONTEXT))
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


def in_target(t, target):
    """The chain's target: None = the whole pool, {"ids": [...]} or {"area": A}."""
    if not target:
        return True
    if target.get("ids"):
        return t["id"] in target["ids"]
    return t["area"] == target.get("area")


def unattended_skip(tasks, target):
    """What an unattended run passes over: outside the target, or worked by main at an effort this
    session may not be on (nobody is there to switch it)."""
    return {t["id"] for t in tasks if not in_target(t, target) or needs_bump(t)}


def pick(run, tasks):
    b = run["begin"]
    skip = {r["task"] for r in run["rounds"]}
    if b.get("unattended"):
        skip |= unattended_skip(tasks, b.get("target"))
    return T.pick_next(tasks, bridge=b.get("bridge", False), by="main", skip=skip)


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


def route(t):
    """How the task's work is routed: {"tier", "agent", "model", "effort", "override"}; agent "main" is main
    itself, None = WORKER's choice; model / effort None = the agent's frontmatter default (for main: this
    session's own effort)."""
    tier = t.get("tier") or "normal"
    agent, model, effort = ROUTES.get(tier, {}).get(t["area"], (None, None, None))
    over = T.parse_route(t["route"]) if t.get("route") else {}
    return {"tier": tier, "agent": over.get("agent") or agent, "model": over.get("model") or model,
            "effort": over.get("effort") or effort, "override": t.get("route")}


def needs_bump(t):
    """The effort this session must be on before main works the task itself, or None."""
    r = route(t)
    if r["agent"] == "main" and r["effort"] in ("high", "xhigh", "max"):
        return r["effort"]
    return None


def worker_line(t):
    """(who does the work and how it is called, main works it itself)."""
    r = route(t)
    tag = "tier %s%s" % (r["tier"], ", route " + r["override"] if r["override"] else "")
    if r["agent"] is None:  # normal: today's worker at its defaults
        who = WORKER.get(t["area"], "main itself")
        by_main = who.startswith("main itself")
        if not (r["model"] or r["effort"] or t.get("tier")):
            return who, by_main
        if by_main or not (r["model"] or r["effort"]):
            return "%s (%s)" % (who, tag), by_main
        agent = who.split(" ")[0]  # an override of model / effort only: the area's first-named agent
    elif r["agent"] == "main":
        bump = needs_bump(t)
        return "main itself (%s)%s" % (tag, " - this session must be on %s effort: ask the author to switch "
                                       "it before the work (Claude cannot)" % bump if bump else ""), True
    else:
        agent = r["agent"]
    parts = (['model "%s"' % r["model"]] if r["model"] else []) + (['effort "%s"' % r["effort"]] if r["effort"] else [])
    call = (", Agent call with " + " and ".join(parts)) if parts else ""
    return "%s%s%s (%s)" % (agent, WHERE.get(agent, ""), call, tag), False


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
                "then `tasks.py start %s --by main --behavior .. --scope .. --verify .. --keep .. --tier "
                "easy|normal|hard` (`--route agent/model/effort` for one that fits no tier); an open "
                "choice (intent, design, wording, scope) is a question, not a default: "
                "`tasks.py set %s --question \"...\" --needs author-decision`, then end the round" % (tid, tid))
    if s == "in-progress":
        rv = revises_this_round(t, rnd)
        if rv:
            return ("revise %d of %d: hand the checker's faults (`tasks.py show %s`, last review) back to the "
                    "maker (SendMessage to the agent that built it, or a fresh one with the faults), commit, "
                    "then `tasks.py set %s --status built --commit <sha> --by main`"
                    % (len(rv), MAX_REVISES, tid, tid))
        who, by_main = worker_line(t)
        if by_main:
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

def cmd_begin(events, tasks, max_rounds=MAX_ROUNDS, bridge=False, by="main", max_context=MAX_CONTEXT,
              unattended=False, chain=None):
    run = open_run(events)
    if run:
        raise LoopError("run %s is still open" % run["id"], "one loop run at a time",
                        "`loop.py next` continues it, `loop.py stop --reason \"...\"` ends it")
    why = context_reason(context_tokens(), max_context)
    if why:
        raise LoopError("no new run in this session", why, "the handoff, then a new session (/clear) runs `loop.py begin`")
    ch = None
    if chain or unattended:
        ch = open_chain(events)
        if not (chain and unattended):
            raise LoopError("--unattended and --chain go together", "an unattended run is one run of a chain "
                            "(its target, its stop conditions, its report)",
                            "`loop.py begin --unattended --chain C-n` (the id `chain next` printed)")
        if not ch or ch["id"] != chain:
            raise LoopError("chain %s is not open" % chain, "a run belongs to the open chain",
                            "`loop.py chain status` names it; `chain begin` opens one")
        bridge = bridge or ch["begin"].get("bridge", False)
    rid = new_run_id(events)
    new = [{"at": now(), "event": "begin", "run": rid, "max_rounds": max_rounds, "bridge": bridge, "by": by}]
    if max_context != MAX_CONTEXT:
        new[0]["max_context"] = max_context
    if ch:
        new[0].update({"unattended": True, "chain": ch["id"], "target": ch["begin"].get("target")})
    parked = [t for t in tasks if t.get("question") and t["status"] not in T.DONE]
    lines = ["run %s begun: at most %d rounds, pool needs none%s, context limit %dk"
             % (rid, max_rounds, " + bridge" if bridge else "", max_context // 1000)]
    if ch:
        lines.append("unattended, chain %s (%s): ask nothing - an open choice is parked (`tasks.py set T-n "
                     "--question ... --needs author-decision`) and the round ends; %d parked question(s) wait "
                     "for the author; unset tiers run as normal; tasks main would work at a higher effort "
                     "are skipped" % (ch["id"], target_text(ch["begin"].get("target")), len(parked)))
        lines.append("next: `python scripts/loop.py next`")
        return new, "\n".join(lines), 0
    if parked:
        lines.append("parked questions - ask the author now (`tasks.py set T-n --answer \"...\"`):")
        lines += ["  %s %s\n    Q: %s" % (t["id"], t["title"], t["question"]) for t in parked]
    else:
        lines.append("no parked questions")
    lines += tier_lines(tasks, max_rounds, bridge)
    lines.append("next: `python scripts/loop.py next`")
    return new, "\n".join(lines), 0


def forecast(tasks, max_rounds, bridge):
    """The tasks the run would take, in order, if each round finishes its task (a forecast: a park or
    a new task changes it)."""
    out, skip = [], set()
    for _ in range(max_rounds):
        t = T.pick_next(tasks, bridge=bridge, by="main", skip=skip)
        if t is None:
            break
        out.append(t)
        skip.add(t["id"])
    return out


def tier_lines(tasks, max_rounds, bridge):
    """begin's list of the run's likely tasks with their routes: open tiers and session bumps are
    settled now, before the author steps away, never mid-run (author 2026-10-11, T-0313)."""
    ahead = forecast(tasks, max_rounds, bridge)
    if not ahead:
        return []
    lines = ["the run's likely tasks and their routes:"]
    for t in ahead:
        lines.append("  %s %s %s - %s" % (t["id"], t["area"], t.get("tier") or "tier unset (= normal)",
                                          worker_line(t)[0]))
    unset = [t["id"] for t in ahead if not t.get("tier")]
    if unset:
        lines.append("tiers to settle now, with the author (`tasks.py set T-n --tier easy|normal|hard`): "
                     + ", ".join(unset))
    bumps = [(t["id"], needs_bump(t)) for t in ahead if needs_bump(t)]
    if bumps:
        lines.append("main works %s itself at %s effort: ask the author to switch this session now"
                     % (", ".join(b[0] for b in bumps), "/".join(sorted(set(b[1] for b in bumps)))))
    return lines


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
    bump = needs_bump(t)
    if bump:
        out.append("tier %s, worked by main: ask the author now to switch this session to %s effort "
                   "(Claude cannot), before the contract" % (t.get("tier"), bump))
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
    r = route(t)
    e = {"at": now(), "event": "end", "run": run["id"], "round": rnd["round"], "task": t["id"],
         "status": t["status"], "progress": progressed(t), "parked": parked, "summary": summary,
         "tier": r["tier"], "route": "%s/%s/%s" % (r["agent"] or "-", r["model"] or "-", r["effort"] or "-")}
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
            lines.append("%d. %s%s -> %s%s: %s" % (x["round"], x["task"],
                                                   " [%s]" % x["end"]["tier"]
                                                   if x["end"].get("tier", "normal") != "normal" else "",
                                                   x["end"]["status"],
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


# ---------------------------------------------------------------- Stage B: the chain (T-0016)

DESKTOP = os.path.join(os.path.expanduser("~"), "Desktop")  # the chain's summary file (author 2026-10-15)
RUN_BRIEF = (
    "Unattended loop run, chain {cid} (docs/harness.md 12 Stage B, T-0016). The author is away: ask "
    "nothing. Follow skill work-loop from `python scripts/loop.py begin --unattended --chain {cid}`, then "
    "`loop.py next` until STOP. An open choice (intent, design, wording, scope) is parked with its question, "
    "never guessed. Plugin rounds release with skill release; if its post-release smoke fails: "
    "`python scripts/loop.py smoke-fail T-n --report <the e2e report path>`, end the round and "
    "`loop.py stop --reason \"smoke failed\"`. At STOP: `loop.py report`, the handoff (docs, commit, push), "
    "then end the session with one line.")
CLEANUP_BRIEF = (
    "Unattended weekly cleanup for loop chain {cid} (T-0016): the author is away, ask nothing. Run skill "
    "weekly-cleanup; anything that needs the author is filed as a task with its question (needs "
    "author-decision), never guessed. Commit and push, then end the session with one line.")
DRIVER = ("start a fresh session for it (start_session: initiation user_asked - the author started the chain -, "
          "context fresh, use_worktree false, wake_when_done true, title \"{title}\") with this brief, then end "
          "the turn; when it ends, `python scripts/loop.py chain next` again:\n\n{brief}")


def session_start():
    """scripts/session-start.py as a module (its name has a hyphen): the due rules live there."""
    import importlib.util
    spec = importlib.util.spec_from_file_location("session_start", os.path.join(T.ROOT, "scripts", "session-start.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def dues(tasks, today=None):
    """Date-gated work: [(name, line, due, brief)]. brief is the job a fresh session does when it is due;
    None = it needs the author (the harness review's start and end), so the chain only reports it. A task
    held "until DATE" needs no job: on that day it is in the pool by itself (tasks.held)."""
    today = today or T.today()
    out = []
    try:
        ss = session_start()
        qtext = ss.lint.read(ss.lint.QUALITY)
        line, due = ss.cleanup_line(ss.log_table(qtext, "Cleanup log"), today)
        out.append(("cleanup", line, due, CLEANUP_BRIEF))
        line, due = ss.review_line(ss.log_table(qtext, "Simplification log"),
                                   lambda d: len(T.finished_between(tasks, since=d)), today)
        out.append(("harness review", line + (" (needs the author: reported, not run)" if due else ""), due, None))
    except Exception as e:  # a due check never stops a chain; status shows it
        out.append(("due dates", "due dates: could not read docs/quality.md (%s)" % type(e).__name__, False, None))
    for i, (name, line, due, brief) in enumerate(out):
        m = re.search(r"next due (\d{4}-\d{2}-\d{2})", line)
        if m and not due:
            days = (datetime.date.fromisoformat(m.group(1)) - datetime.date.fromisoformat(today)).days
            out[i] = (name, "%s (%d day(s) to go)" % (line, days), due, brief)
    for t in tasks:
        d = T.hold_until(t)
        if d and t["status"] not in T.DONE:
            days = (datetime.date.fromisoformat(d) - datetime.date.fromisoformat(today)).days
            out.append((t["id"], "%s held until %s - %s" % (t["id"], d, "in the pool now" if days <= 0 else
                                                             "%d day(s) to go" % days), False, None))
    return out


def chains(events):
    """The chains, oldest first: {id, begin, dispatches, stop}."""
    out, by_id = [], {}
    for e in events:
        kind = e.get("event")
        if kind == "chain-begin":
            c = {"id": e["chain"], "begin": e, "dispatches": [], "stop": None}
            out.append(c)
            by_id[e["chain"]] = c
        elif kind in ("chain-dispatch", "chain-stop") and e.get("chain") in by_id:
            if kind == "chain-dispatch":
                by_id[e["chain"]]["dispatches"].append(e)
            else:
                by_id[e["chain"]]["stop"] = e
    return out


def open_chain(events):
    cs = chains(events)
    return cs[-1] if cs and cs[-1]["stop"] is None else None


def chain_runs(events, cid):
    return [r for r in runs(events) if r["begin"].get("chain") == cid]


def target_text(target):
    if not target:
        return "the whole pool"
    if target.get("ids"):
        return "tasks " + ", ".join(target["ids"])
    return "area " + target["area"]


def pool(tasks, bridge, target):
    """The tasks an unattended run of this chain could still take (main's own in-progress one first)."""
    skip = unattended_skip(tasks, target)
    out = [t for t in tasks if t["status"] == "in-progress" and t.get("owner") == "main" and t["id"] not in skip]
    skip |= {t["id"] for t in out}
    while True:
        t = T.pick_next(tasks, bridge=bridge, skip=skip)
        if t is None:
            return out
        out.append(t)
        skip.add(t["id"])


def progress_text(tasks, ch):
    b = ch["begin"]
    target = b.get("target")
    if target and target.get("ids"):
        ts = [T.find(tasks, i) for i in target["ids"]]
        done = [t["id"] for t in ts if progressed(t)]
        left = [t["id"] for t in ts if not progressed(t)]
        return "target: %d of %d moved forward%s%s" % (len(done), len(ts), " (%s)" % ", ".join(done) if done else "",
                                                       "; left: " + ", ".join(left) if left else "")
    n = len(pool(tasks, b.get("bridge", False), target))
    return "%s: %d task(s) left in the pool (%d when the chain began)" % (target_text(target), n, b.get("pool", 0))


def run_counts(rs):
    done = [x for r in rs for x in r["rounds"] if x["end"]]
    return (len(rs), len(done), len([x for x in done if x["end"].get("progress")]),
            len([x for x in done if x["end"].get("parked")]))


def chain_stop_reason(events, tasks, ch):
    why = context_reason(context_tokens())
    if why:
        return "the driver's own context: " + why
    fails = [e for e in events if e.get("event") == "smoke-fail" and e.get("chain") == ch["id"]]
    if fails:
        f = fails[-1]
        return "the post-release smoke failed on %s (%s) - parked for the author" % (f["task"], f.get("report") or "no report")
    rs = chain_runs(events, ch["id"])
    if rs and rs[-1]["rounds"] and not any(x["end"] and x["end"].get("progress") for x in rs[-1]["rounds"]):
        return "run %s made no progress - look at what keeps failing (`loop.py report %s`)" % (rs[-1]["id"], rs[-1]["id"])
    return None


def cmd_chain_begin(events, tasks, ids=None, area=None, bridge=False, by="main"):
    ch = open_chain(events)
    if ch:
        raise LoopError("chain %s is still open" % ch["id"], "one chain at a time",
                        "`loop.py chain next` continues it, `loop.py chain stop --reason \"...\"` ends it")
    run = open_run(events)
    if run:
        raise LoopError("run %s is open" % run["id"], "a chain starts between runs",
                        "finish it (`loop.py next`) or `loop.py stop --reason \"...\"`")
    if ids and area:
        raise LoopError("--target and --area together", "a chain has one target", "pick one")
    target = None
    if ids:
        target = {"ids": [T.find(tasks, T.normalise_id(i.strip()))["id"] for i in ids.split(",") if i.strip()]}
    elif area:
        if area not in T.AREAS:
            raise LoopError("unknown area %r" % area, "the target is a task file area", "one of " + ", ".join(T.AREAS))
        target = {"area": area}
    cid = "C-%04d" % (len(chains(events)) + 1)
    e = {"at": now(), "event": "chain-begin", "chain": cid, "target": target, "bridge": bridge, "by": by,
         "pool": len(pool(tasks, bridge, target))}
    lines = ["chain %s begun: %s, pool needs none%s, %d task(s) in it" % (cid, target_text(target),
                                                                        " + bridge" if bridge else "", e["pool"])]
    lines += ["  " + d[1] for d in dues(tasks)]
    lines.append("next: `python scripts/loop.py chain next`")
    return [e], "\n".join(lines), 0


def cmd_chain_next(events, tasks, again=False):
    ch = open_chain(events)
    if not ch:
        raise LoopError("no open chain", "chain next drives an open chain", "`loop.py chain begin`")
    cid = ch["id"]
    run = open_run(events)
    if run:
        if run["begin"].get("chain") != cid:
            raise LoopError("run %s is open outside chain %s" % (run["id"], cid), "the chain runs one run at a time",
                            "finish it or `loop.py stop --reason \"...\"`")
        return [], ("wait: run %s is open in its own session. When that session has ended without closing it, "
                    "`loop.py stop --reason \"its session ended\"` (after `loop.py end` for an open round), then "
                    "`loop.py chain next`" % run["id"]), 0
    rs = chain_runs(events, cid)
    started = [d for d in ch["dispatches"] if d["kind"] == "run"]
    if started and not again and not any(r["begin"]["at"] >= started[-1]["at"] for r in rs):
        return [], ("wait: a run session was started at %s and has not begun its run. If that session failed to "
                    "start or ended without `begin`: `loop.py chain next --again`" % started[-1]["at"]), 0
    why = chain_stop_reason(events, tasks, ch)
    if not why:
        done = {d["kind"] for d in ch["dispatches"]}
        for name, line, due, brief in dues(tasks):
            if due and brief and name not in done:
                e = {"at": now(), "event": "chain-dispatch", "chain": cid, "kind": name}
                return [e], "due: %s\n%s" % (line, DRIVER.format(title="Weekly cleanup (chain %s)" % cid,
                                                                  brief=brief.format(cid=cid))), 0
        target = ch["begin"].get("target")
        if not pool(tasks, ch["begin"].get("bridge", False), target):
            ids = (target or {}).get("ids")
            all_done = ids and all(progressed(T.find(tasks, i)) for i in ids)
            why = ("the target is done" if all_done else "nothing left it can take alone - the rest waits on the "
                   "game, a tester or the author") + " (%s)" % progress_text(tasks, ch)
    if why:
        e = {"at": now(), "event": "chain-stop", "chain": cid, "reason": why}
        return [e], "STOP chain %s: %s\nsummary: %s" % (cid, why, chain_file(cid)), EXIT_STOP
    e = {"at": now(), "event": "chain-dispatch", "chain": cid, "kind": "run"}
    return [e], "run %d of chain %s (%s)\n%s" % (len(rs) + 1, cid, progress_text(tasks, ch), DRIVER.format(
        title="Loop run %d (chain %s)" % (len(rs) + 1, cid), brief=RUN_BRIEF.format(cid=cid))), 0


def cmd_chain_stop(events, reason):
    ch = open_chain(events)
    if not ch:
        raise LoopError("no open chain", "nothing to stop", "`loop.py chain status` shows the last one")
    run = open_run(events)
    if run and run["begin"].get("chain") == ch["id"]:
        raise LoopError("run %s of the chain is open" % run["id"], "a chain ends between runs",
                        "`loop.py stop --reason \"...\"` first")
    if not (reason or "").strip():
        raise LoopError("no --reason", "the summary says why the chain ended", "--reason \"...\"")
    e = {"at": now(), "event": "chain-stop", "chain": ch["id"], "reason": reason.strip()}
    return [e], "chain %s stopped: %s\nsummary: %s" % (ch["id"], reason.strip(), chain_file(ch["id"])), 0


def chain_status(events, tasks, cid=None):
    cs = chains(events)
    if cid:
        cs = [c for c in cs if c["id"] == cid]
    if not cs:
        return "(no loop chains)"
    ch = cs[-1]
    n, rounds, passed, parked = run_counts(chain_runs(events, ch["id"]))
    lines = ["chain %s begun %s by %s: %s, %d run(s), %d round(s), %d progressed, %d parked%s" % (
        ch["id"], ch["begin"]["at"], ch["begin"].get("by", "?"), target_text(ch["begin"].get("target")), n, rounds,
        passed, parked, "" if ch["stop"] else " - open")]
    if ch["stop"]:
        lines.append("stopped %s: %s" % (ch["stop"]["at"], ch["stop"]["reason"]))
    lines.append(progress_text(tasks, ch))
    lines.append("due:")
    lines += ["  " + d[1] for d in dues(tasks)]
    return "\n".join(lines)


def chain_file(cid):
    return os.path.join(DESKTOP, "forest-chain-%s.md" % cid)


def write_chain_file(events, tasks, cid):
    """The chain's summary on the Desktop (author 2026-10-15): status, then every run's report."""
    text = ["# Loop chain %s" % cid, "", chain_status(events, tasks, cid), ""]
    for r in chain_runs(events, cid):
        text += ["## " + r["id"], "", report(events, r["id"]), ""]
    try:
        os.makedirs(DESKTOP, exist_ok=True)
        with open(chain_file(cid), "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(text))
    except OSError:
        pass  # the session-start line and `chain status` still have it


def chain_line(events):
    """session-start's line for the last chain, or None (T-0016)."""
    cs = chains(events)
    if not cs:
        return None
    ch = cs[-1]
    n, rounds, passed, parked = run_counts(chain_runs(events, ch["id"]))
    counts = "%d run(s), %d round(s), %d progressed, %d parked" % (n, rounds, passed, parked)
    if ch["stop"] is None:
        return "loop chain: %s open since %s, %s - the driver continues with `loop.py chain next`" % (
            ch["id"], ch["begin"]["at"][:16], counts)
    return "loop chain: %s ended %s, %s; stop: %s (summary %s)" % (
        ch["id"], ch["stop"]["at"][:16], counts, ch["stop"]["reason"], chain_file(ch["id"]))


def cmd_smoke_fail(events, tasks, tid, report_path):
    """A failed post-release smoke: the task is parked for the author and its chain stops (no rollback,
    author 2026-10-15). The task file change is made by run()."""
    t = T.find(tasks, T.normalise_id(tid))
    run = open_run(events)
    ch = open_chain(events)
    e = {"at": now(), "event": "smoke-fail", "run": run["id"] if run else None, "task": t["id"],
         "chain": (run["begin"].get("chain") if run else None) or (ch["id"] if ch else None),
         "report": report_path, "release": t.get("release"),
         "question": "The post-release smoke failed%s (report %s). Fix how, or revert?" % (
             " on " + t["release"] if t.get("release") else "", report_path or "none")}
    return [e], "%s parked: smoke failed%s%s" % (t["id"], " - chain %s stops at its next step" % e["chain"]
                                                 if e["chain"] else "", "; end the round, then `loop.py stop`"
                                                 if run else ""), 0


def main(argv=None, loop_path=None, tasks_path=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    sp = sub.add_parser("begin")
    sp.add_argument("--bridge", action="store_true")
    sp.add_argument("--max-rounds", type=int, default=MAX_ROUNDS)
    sp.add_argument("--max-context", type=int, default=MAX_CONTEXT,
                    help="tokens; a run's own limit when the author raises it for one session (author 2026-10-15: 350000)")
    sp.add_argument("--by", default="main")
    sp.add_argument("--unattended", action="store_true", help="a chain's run: nothing asked (T-0016)")
    sp.add_argument("--chain", help="the open chain this run belongs to (with --unattended)")
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
    sp = sub.add_parser("smoke-fail")
    sp.add_argument("id")
    sp.add_argument("--report", required=True, help="the e2e report's path")
    sp = sub.add_parser("chain")
    csub = sp.add_subparsers(dest="chain_cmd", required=True)
    cp = csub.add_parser("begin")
    cp.add_argument("--target", help="task ids, comma-separated")
    cp.add_argument("--area")
    cp.add_argument("--bridge", action="store_true")
    cp.add_argument("--by", default="main")
    cp = csub.add_parser("next")
    cp.add_argument("--again", action="store_true", help="start a run session again (the last one never began)")
    cp = csub.add_parser("status")
    cp.add_argument("chain", nargs="?")
    cp = csub.add_parser("stop")
    cp.add_argument("--reason", required=True)
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
        if a.cmd == "chain" and a.chain_cmd == "status":
            print(chain_status(events, tasks, a.chain))
            return 0
        if a.cmd == "begin":
            if a.max_rounds < 1:
                raise LoopError("--max-rounds %d" % a.max_rounds, "a run takes at least one round",
                                "--max-rounds %d (the default)" % MAX_ROUNDS)
            new, text, code = cmd_begin(events, tasks, a.max_rounds, a.bridge, a.by, a.max_context,
                                        a.unattended, a.chain)
        elif a.cmd in ("next", "peek"):
            new, text, code = cmd_next(events, tasks)
            if a.cmd == "peek":
                new = []
        elif a.cmd == "end":
            new, text, code = cmd_end(events, tasks, a.summary, a.id)
        elif a.cmd == "intervene":
            new, text, code = cmd_intervene(events, a.what)
        elif a.cmd == "smoke-fail":
            new, text, code = cmd_smoke_fail(events, tasks, a.id, a.report)
        elif a.cmd == "chain":
            if a.chain_cmd == "begin":
                new, text, code = cmd_chain_begin(events, tasks, a.target, a.area, a.bridge, a.by)
            elif a.chain_cmd == "next":
                new, text, code = cmd_chain_next(events, tasks, a.again)
            else:
                new, text, code = cmd_chain_stop(events, a.reason)
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
        if new and any(e["event"] == "smoke-fail" for e in new):
            fail = [e for e in new if e["event"] == "smoke-fail"][0]
            with T.locked(tasks_path or T.TASKS):
                tasks = T.load(tasks_path or T.TASKS)
                t = T.find(tasks, fail["task"])
                t["question"] = fail["question"]  # status stays (released): no rollback, the author decides
                t["needs"] = "author-decision"
                t["updated"] = T.today()
                T.save(tasks, tasks_path or T.TASKS, None if tasks_path else T.VIEW)
        append(new, loop_path)
        stopped = [e for e in new if e["event"] == "chain-stop"]
        if stopped:
            write_chain_file(events + new, tasks, stopped[0]["chain"])
        print(text)
        return code
    except T.TaskError as e:
        print(e, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
