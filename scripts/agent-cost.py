"""What each project subagent costs, read from Claude Code's own transcripts.

    python scripts/agent-cost.py [--since 2026-10-07] [--until 2026-10-14] [--agent forest-dev]
                                 [--main] [--runs] [--tasks] [--tail]

Claude Code keeps every subagent run as
~/.claude/projects/<project>/<session>/subagents/agent-<id>.jsonl with a
.meta.json beside it (agentType, description). This reads those for this
repo (worktree sessions included) and prints, per agent type: runs, model,
API calls, the context at the first call (the cold start: system prompt,
tools, CLAUDE.md, the brief), cache reads / writes, output and active
minutes (gaps over 5 min - background waits - not counted). --runs adds one
line per run. --main counts the main sessions too (agent "main"); --tasks
sums every run whose description names a task (T-0311) by model family;
--tail gives each agent's share of input sent after its first edit to
src/, tests/ or patcher/ (the build tail T-0311 hands to forest-dev).
Use it to judge a brief / model / tool-list change before and after
(T-0113; the monthly harness review, docs/harness.md 12d; the usage
baseline, docs/harness.md *Measuring the harness*).
Tests: scripts/tests/test_agent_cost.py.
"""
import argparse
import glob
import json
import os
import re
import sys
from datetime import datetime

GAP_CAP = 300  # seconds; a longer gap is the agent waiting, not working
CODE_DIRS = re.compile(r"(?<!scripts)[/\\](src|tests|patcher)[/\\]")  # plugin code, not scripts/tests
BIG_CONTEXT = 300000  # a call sent with more context than this counts as "big" (T-0319)
TASK_ID = re.compile(r"\bT-\d{4}\b")


def project_dirs(repo, home=None):
    """Claude Code's transcript folders for this repo and its worktrees."""
    home = home or os.path.expanduser("~")
    slug = re.sub(r"[^A-Za-z0-9]", "-", os.path.abspath(repo))
    root = os.path.join(home, ".claude", "projects")
    return [d for d in glob.glob(os.path.join(root, slug + "*")) if os.path.isdir(d)]


def _time(stamp):
    return datetime.strptime(stamp[:19], "%Y-%m-%dT%H:%M:%S")


def parse_run(lines, meta=None):
    """One subagent transcript (an iterable of JSON lines) -> a dict of its costs."""
    meta = meta or {}
    usage = {}  # message id -> usage; a reply is logged once per content block, the last has the final count
    model, stamps, tools = None, [], {}
    first_edit = None  # message id of the first edit to plugin code
    for line in lines:
        line = line.strip()
        if not line:
            continue
        d = json.loads(line)
        if d.get("timestamp"):
            stamps.append(d["timestamp"])
        if d.get("type") != "assistant":
            continue
        m = d.get("message", {})
        if m.get("model") and m["model"] != "<synthetic>":
            model = m["model"]
        if m.get("id") and m.get("usage"):
            usage[m["id"]] = m["usage"]  # keeps the first-seen order
        for block in m.get("content") or []:
            if isinstance(block, dict) and block.get("type") == "tool_use":
                tools[block["name"]] = tools.get(block["name"], 0) + 1
                if (first_edit is None and block["name"] in ("Edit", "Write")
                        and CODE_DIRS.search((block.get("input") or {}).get("file_path", ""))):
                    first_edit = m.get("id")
    ids = list(usage)
    calls = [usage[i] for i in ids]
    first = calls[0] if calls else {}
    active = 0.0
    times = sorted(_time(s) for s in stamps)
    for a, b in zip(times, times[1:]):
        active += min((b - a).total_seconds(), GAP_CAP)
    def whole(u):
        return sum(u.get(k, 0) for k in
                   ("input_tokens", "cache_read_input_tokens", "cache_creation_input_tokens"))
    tail_from = ids.index(first_edit) if first_edit in usage else len(ids)
    return {
        "agent": meta.get("agentType", "?"),
        "description": meta.get("description", ""),
        "date": stamps and min(stamps)[:10] or "",
        "model": model or "?",
        "calls": len(calls),
        "start": whole(first),
        "input": sum(whole(u) for u in calls),
        "tail": sum(whole(u) for u in calls[tail_from:]),
        "big": sum(whole(u) for u in calls if whole(u) > BIG_CONTEXT),
        "cache_read": sum(u.get("cache_read_input_tokens", 0) for u in calls),
        "cache_write": sum(u.get("cache_creation_input_tokens", 0) for u in calls),
        "output": sum(u.get("output_tokens", 0) for u in calls),
        "minutes": active / 60.0,
        "tools": tools,
    }


def load_runs(dirs, main=False):
    runs = []
    for d in dirs:
        if main:  # the main sessions: <project>/<session>.jsonl
            for path in glob.glob(os.path.join(d, "*.jsonl")):
                with open(path, encoding="utf-8") as f:
                    runs.append(parse_run(f, {"agentType": "main",
                                              "description": os.path.basename(path)[:8]}))
        for path in glob.glob(os.path.join(d, "*", "subagents", "agent-*.jsonl")):
            meta_path = path[:-len(".jsonl")] + ".meta.json"
            meta = {}
            if os.path.exists(meta_path):
                with open(meta_path, encoding="utf-8") as f:
                    meta = json.load(f)
            with open(path, encoding="utf-8") as f:
                runs.append(parse_run(f, meta))
    return runs


def _k(n):
    return "%.1fM" % (n / 1e6) if n >= 1e6 else "%.0fk" % (n / 1e3)


def summarize(runs):
    """Rows of (agent, runs, models, mean calls, mean start, mean reads, writes, output, minutes)."""
    by = {}
    for r in runs:
        by.setdefault(r["agent"], []).append(r)
    rows = []
    for agent in sorted(by):
        rs = by[agent]
        n = float(len(rs))
        models = ",".join(sorted(set(r["model"].replace("claude-", "") for r in rs)))
        rows.append((agent, len(rs), models,
                     sum(r["calls"] for r in rs) / n,
                     sum(r["start"] for r in rs) / n,
                     sum(r["cache_read"] for r in rs) / n,
                     sum(r["cache_write"] for r in rs) / n,
                     sum(r["output"] for r in rs) / n,
                     sum(r["minutes"] for r in rs) / n))
    return rows


def family(model):
    for name in ("opus", "sonnet", "haiku", "fable"):
        if name in model:
            return name
    return "other"


def by_task(runs):
    """{task id: {"runs", "agents", "<family>": input tokens, "output"}} for runs naming a task."""
    out = {}
    for r in runs:
        m = TASK_ID.search(r["description"])
        if not m:
            continue
        t = out.setdefault(m.group(0), {"runs": 0, "agents": set(), "output": 0})
        t["runs"] += 1
        t["agents"].add(r["agent"])
        f = family(r["model"])
        t[f] = t.get(f, 0) + r["input"]
        t["output"] += r["output"]
    return out


def tail_share(runs):
    """Rows of (agent, runs, runs that edited code, input, share of it from the first edit on)."""
    by = {}
    for r in runs:
        by.setdefault(r["agent"], []).append(r)
    rows = []
    for agent in sorted(by):
        rs = by[agent]
        total = sum(r["input"] for r in rs)
        rows.append((agent, len(rs), sum(1 for r in rs if r["tail"]), total,
                     sum(r["tail"] for r in rs) / float(total or 1)))
    return rows


def week(day):
    y, w, _ = datetime.strptime(day, "%Y-%m-%d").isocalendar()
    return "%d-W%02d" % (y, w)


def by_week(runs, finished_days=()):
    """Rows of (week, main sessions, main input, agent opus input, agent other input, tasks finished,
    main input sent at > BIG_CONTEXT)."""
    rows = {}
    for r in runs:
        if not r["date"]:
            continue
        row = rows.setdefault(week(r["date"]), [0, 0, 0, 0, 0, 0])
        if r["agent"] == "main":
            row[0] += 1
            row[1] += r["input"]
            row[5] += r.get("big", 0)
        elif family(r["model"]) == "opus":
            row[2] += r["input"]
        else:
            row[3] += r["input"]
    for day in finished_days:
        wk = week(day)
        if wk in rows:
            rows[wk][4] += 1
    return [(wk,) + tuple(rows[wk]) for wk in sorted(rows)]


def finished_days(repo):
    """The day each task first reached built, from the task file (empty when it cannot be read)."""
    try:
        sys.path.insert(0, os.path.join(repo, "scripts"))
        import tasks as T
        return [d for d in (T.finished_on(t) for t in T.load()) if d]
    except Exception:  # noqa: BLE001 - the token report stands without it
        return []


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--since", help="only runs on or after this date (YYYY-MM-DD)")
    ap.add_argument("--until", help="only runs before this date (YYYY-MM-DD, exclusive)")
    ap.add_argument("--agent", help="only this agent type (forest-dev, general-purpose, main, ...)")
    ap.add_argument("--main", action="store_true", help="count the main sessions too")
    ap.add_argument("--runs", action="store_true", help="one line per run too")
    ap.add_argument("--tasks", action="store_true", help="input per task id, by model family")
    ap.add_argument("--tail", action="store_true",
                    help="share of each agent's input from its first src / tests edit on")
    ap.add_argument("--weeks", action="store_true",
                    help="input per ISO week (main sessions vs agents) and tasks finished; implies --main")
    ap.add_argument("--repo", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
    args = ap.parse_args(argv)

    runs = load_runs(project_dirs(args.repo), main=args.main or args.weeks or args.agent == "main")
    if args.since:
        runs = [r for r in runs if r["date"] >= args.since]
    if args.until:
        runs = [r for r in runs if r["date"] < args.until]
    if args.agent:
        runs = [r for r in runs if r["agent"] == args.agent]
    if not runs:
        print("agent-cost: no subagent transcripts found (WHY: none match the filters, or "
              "~/.claude/projects has no folder for this repo; FIX: drop --since / --agent)")
        return 1

    out = sys.stdout
    out.write("Per run, mean (start = context at the first call; reads / writes = cache tokens):\n")
    out.write("%-18s %4s %-20s %6s %7s %7s %7s %7s %6s\n"
              % ("agent", "runs", "model", "calls", "start", "reads", "writes", "output", "min"))
    for a, n, models, calls, start, cr, cw, o, mins in summarize(runs):
        out.write("%-18s %4d %-20s %6.1f %7s %7s %7s %7s %6.1f\n"
                  % (a[:18], n, models[:20], calls, _k(start), _k(cr), _k(cw), _k(o), mins))
    total = float(sum(r["input"] for r in runs) or 1)
    fams = {}
    for r in runs:
        fams[family(r["model"])] = fams.get(family(r["model"]), 0) + r["input"]
    out.write("Input by model family: %s\n" % ", ".join(
        "%s %s (%.0f%%)" % (f, _k(n), 100 * n / total)
        for f, n in sorted(fams.items(), key=lambda kv: -kv[1])))
    if args.weeks:
        out.write("\nPer week: main sessions, input (main / Opus agents / other agents), the share of main\n"
                  "input sent at > 300k context, tasks first built (the task file starts 2026-10-07;\n"
                  "that week's count includes the migration):\n")
        for wk, n, m, o, s, done, big in by_week(runs, finished_days(args.repo)):
            out.write("%s %4d sessions  main %7s (%5s/session, >300k %3.0f%%)  opus agents %7s"
                      "  other agents %7s  tasks %3d\n"
                      % (wk, n, _k(m), _k(m / float(n or 1)), 100.0 * big / (m or 1), _k(o), _k(s), done))
    if args.tail:
        out.write("\nInput from the first src / tests / patcher edit on (the build tail):\n")
        for a, n, edited, inp, share in tail_share(runs):
            out.write("%-18s %4d runs, %3d edited code  input %7s  tail %3.0f%%\n"
                      % (a[:18], n, edited, _k(inp), 100 * share))
    if args.tasks:
        out.write("\nPer task (input tokens by model family, every run naming it):\n")
        tasks = by_task(runs)
        for tid in sorted(tasks):
            t = tasks[tid]
            split = ", ".join("%s %s" % (f, _k(t[f]))
                              for f in ("opus", "sonnet", "haiku", "fable", "other") if t.get(f))
            out.write("%s %2d runs  %-30s out %6s  %s\n"
                      % (tid, t["runs"], split[:30], _k(t["output"]), ",".join(sorted(t["agents"]))))
    if args.runs:
        out.write("\n")
        for r in sorted(runs, key=lambda r: (r["agent"], r["date"])):
            top = ", ".join("%s %d" % kv for kv in
                            sorted(r["tools"].items(), key=lambda kv: -kv[1])[:4])
            desc = r["description"].encode("ascii", "replace").decode("ascii")[:34]
            out.write("%-16s %s %-14s %3d calls start %6s reads %6s out %6s %5.1f min  %-34s %s\n"
                      % (r["agent"][:16], r["date"], r["model"].replace("claude-", "")[:14],
                         r["calls"], _k(r["start"]), _k(r["cache_read"]), _k(r["output"]),
                         r["minutes"], desc, top))
    return 0


if __name__ == "__main__":
    sys.exit(main())
