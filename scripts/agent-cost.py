"""What each project subagent costs, read from Claude Code's own transcripts.

    python scripts/agent-cost.py [--since 2026-10-07] [--agent forest-dev] [--runs]

Claude Code keeps every subagent run as
~/.claude/projects/<project>/<session>/subagents/agent-<id>.jsonl with a
.meta.json beside it (agentType, description). This reads those for this
repo (worktree sessions included) and prints, per agent type: runs, model,
API calls, the context at the first call (the cold start: system prompt,
tools, CLAUDE.md, the brief), cache reads / writes, output and active
minutes (gaps over 5 min - background waits - not counted). --runs adds one
line per run. Use it to judge a brief / model / tool-list change before and
after (T-0113; the monthly harness review, docs/harness.md 12d).
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
    calls = list(usage.values())
    first = calls[0] if calls else {}
    active = 0.0
    times = sorted(_time(s) for s in stamps)
    for a, b in zip(times, times[1:]):
        active += min((b - a).total_seconds(), GAP_CAP)
    return {
        "agent": meta.get("agentType", "?"),
        "description": meta.get("description", ""),
        "date": stamps and min(stamps)[:10] or "",
        "model": model or "?",
        "calls": len(calls),
        "start": sum(first.get(k, 0) for k in
                     ("input_tokens", "cache_read_input_tokens", "cache_creation_input_tokens")),
        "cache_read": sum(u.get("cache_read_input_tokens", 0) for u in calls),
        "cache_write": sum(u.get("cache_creation_input_tokens", 0) for u in calls),
        "output": sum(u.get("output_tokens", 0) for u in calls),
        "minutes": active / 60.0,
        "tools": tools,
    }


def load_runs(dirs):
    runs = []
    for d in dirs:
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


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--since", help="only runs on or after this date (YYYY-MM-DD)")
    ap.add_argument("--agent", help="only this agent type (forest-dev, general-purpose, ...)")
    ap.add_argument("--runs", action="store_true", help="one line per run too")
    ap.add_argument("--repo", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
    args = ap.parse_args(argv)

    runs = load_runs(project_dirs(args.repo))
    if args.since:
        runs = [r for r in runs if r["date"] >= args.since]
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
