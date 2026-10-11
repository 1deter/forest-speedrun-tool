"""Tests for scripts/agent-cost.py, on a transcript written in memory.

    python scripts/tests/test_agent_cost.py
"""
import importlib.util
import json
import os
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("agent_cost", os.path.join(HERE, "..", "agent-cost.py"))
A = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(A)


def reply(mid, ts, usage, tools=(), model="claude-sonnet-5-5"):
    content = [{"type": "tool_use", "name": t, "input": {}} for t in tools] or [{"type": "text", "text": "x"}]
    return json.dumps({"type": "assistant", "timestamp": ts,
                       "message": {"id": mid, "model": model, "usage": usage, "content": content}})


U1 = {"input_tokens": 3, "cache_read_input_tokens": 0, "cache_creation_input_tokens": 13000, "output_tokens": 8}
U2 = {"input_tokens": 1, "cache_read_input_tokens": 13000, "cache_creation_input_tokens": 500, "output_tokens": 50}
LINES = [
    json.dumps({"type": "user", "timestamp": "2026-10-07T04:00:00.000Z", "message": {"content": "Check T-1"}}),
    reply("m1", "2026-10-07T04:00:10.000Z", U1, ["Bash"]),
    # The same reply logged again for its second content block: counted once,
    # with the last copy's usage (the first one's output count is partial).
    reply("m1", "2026-10-07T04:00:11.000Z", dict(U1, output_tokens=200), ["Grep"]),
    reply("m2", "2026-10-07T04:01:00.000Z", U2),
    # An hour waiting on a background command: counts as 5 minutes, not 60.
    json.dumps({"type": "user", "timestamp": "2026-10-07T05:01:00.000Z", "message": {"content": "done"}}),
    "",
]


class ParseRun(unittest.TestCase):
    def setUp(self):
        self.r = A.parse_run(LINES, {"agentType": "forest-checker", "description": "Check T-1"})

    def test_usage_is_counted_once_per_reply(self):
        self.assertEqual(self.r["calls"], 2)
        self.assertEqual(self.r["cache_write"], 13500)
        self.assertEqual(self.r["cache_read"], 13000)
        self.assertEqual(self.r["output"], 250)

    def test_start_is_the_first_calls_whole_context(self):
        self.assertEqual(self.r["start"], 13003)

    def test_tools_and_meta(self):
        self.assertEqual(self.r["tools"], {"Bash": 1, "Grep": 1})
        self.assertEqual((self.r["agent"], self.r["date"], self.r["model"]),
                         ("forest-checker", "2026-10-07", "claude-sonnet-5-5"))

    def test_long_gaps_are_capped(self):
        # 10 s + 1 s + 49 s of work, then a 60 min wait capped at 5 min.
        self.assertAlmostEqual(self.r["minutes"], (60 + 300) / 60.0)

    def test_synthetic_model_is_ignored(self):
        r = A.parse_run(LINES + [reply("m3", "2026-10-07T05:02:00.000Z", U2, model="<synthetic>")])
        self.assertEqual(r["model"], "claude-sonnet-5-5")
        self.assertEqual(r["agent"], "?")


class Summary(unittest.TestCase):
    def test_means_per_agent(self):
        a = A.parse_run(LINES, {"agentType": "forest-dev"})
        b = dict(a, calls=4, output=750)
        rows = A.summarize([a, b, A.parse_run(LINES, {"agentType": "forest-qa"})])
        self.assertEqual([r[0] for r in rows], ["forest-dev", "forest-qa"])
        dev = rows[0]
        self.assertEqual((dev[1], dev[2], dev[3], dev[7]), (2, "sonnet-5-5", 3.0, 500.0))


def edit(mid, ts, usage, path):
    return json.dumps({"type": "assistant", "timestamp": ts, "message": {
        "id": mid, "model": "claude-opus-5-5", "usage": usage,
        "content": [{"type": "tool_use", "name": "Edit", "input": {"file_path": path}}]}})


class Tail(unittest.TestCase):
    def test_tail_starts_at_the_first_code_edit(self):
        lines = [reply("m1", "2026-10-07T04:00:00.000Z", U1, ["Bash"]),
                 edit("m2", "2026-10-07T04:01:00.000Z", U2, r"C:\repo\docs\game-notes.md"),
                 edit("m3", "2026-10-07T04:02:00.000Z", U2, r"C:\repo\src\Modules\X.cs"),
                 reply("m4", "2026-10-07T04:03:00.000Z", U2, ["Bash"])]
        r = A.parse_run(lines, {"agentType": "forest-researcher"})
        self.assertEqual(r["input"], 13003 + 3 * 13501)
        self.assertEqual(r["tail"], 2 * 13501)  # the docs edit does not start it
        (row,) = A.tail_share([r])
        self.assertEqual(row[:3], ("forest-researcher", 1, 1))

    def test_script_tests_are_not_plugin_code(self):
        lines = [edit("m1", "2026-10-07T04:00:00.000Z", U2, "C:/repo/scripts/tests/test_x.py")]
        self.assertEqual(A.parse_run(lines)["tail"], 0)

    def test_big_counts_calls_over_300k(self):
        big = {"input_tokens": 1, "cache_read_input_tokens": 300000, "cache_creation_input_tokens": 0}
        r = A.parse_run(LINES + [reply("m9", "2026-10-07T05:02:00.000Z", big)])
        self.assertEqual(r["big"], 300001)

    def test_no_code_edit_no_tail(self):
        self.assertEqual(A.parse_run(LINES)["tail"], 0)


class Grouping(unittest.TestCase):
    def run_(self, agent, model, date, desc="", inp=100):
        return {"agent": agent, "model": model, "date": date, "description": desc,
                "input": inp, "output": 1}

    def test_by_task_sums_by_model_family(self):
        t = A.by_task([self.run_("forest-researcher", "claude-opus-5-5", "2026-10-11", "Research T-0311 x"),
                       self.run_("forest-dev", "claude-sonnet-5-5", "2026-10-11", "Build T-0311", 40),
                       self.run_("forest-dev", "claude-sonnet-5-5", "2026-10-11", "no task")])
        self.assertEqual(list(t), ["T-0311"])
        self.assertEqual((t["T-0311"]["opus"], t["T-0311"]["sonnet"], t["T-0311"]["runs"]), (100, 40, 2))

    def test_by_tier_joins_runs_with_tiers_and_outcomes(self):
        runs = [dict(self.run_("forest-researcher", "claude-opus-5-5", "2026-10-11", "Research T-0001"), effort="high"),
                dict(self.run_("forest-dev", "claude-sonnet-5-5", "2026-10-11", "Build T-0002", 40), effort="low"),
                dict(self.run_("forest-checker", "claude-sonnet-5-5", "2026-10-11", "Check T-0002", 10), effort="high"),
                dict(self.run_("forest-dev", "claude-sonnet-5-5", "2026-10-11", "Build T-0003", 7), effort="medium"),
                dict(self.run_("forest-dev", "claude-sonnet-5-5", "2026-10-11", "Build T-9999"), effort="low")]
        tasks = [{"id": "T-0001", "tier": "hard", "reviews": [{"verdict": "revise"}, {"verdict": "accept"}]},
                 {"id": "T-0002", "tier": "easy", "reviews": [{"verdict": "accept"}]},
                 {"id": "T-0003"}]
        loop = [{"event": "round", "run": "R-1", "round": 1, "task": "T-0001"},
                {"event": "intervene", "run": "R-1", "round": 1, "what": "x"},
                {"event": "intervene", "run": "R-1", "round": None, "what": "between rounds"}]
        routes, outcomes = A.by_tier(runs, tasks, loop)
        self.assertEqual(routes, [("easy", "forest-checker", "sonnet", "high", 1, 1, 10),
                                  ("easy", "forest-dev", "sonnet", "low", 1, 1, 40),
                                  ("hard", "forest-researcher", "opus", "high", 1, 1, 100),
                                  ("unset", "forest-dev", "sonnet", "medium", 1, 1, 7)])
        self.assertEqual(outcomes, [("easy", 1, 1, 1, 0, 0), ("hard", 1, 1, 0, 1, 1), ("unset", 1, 0, 0, 0, 0)])

    def test_effort_is_read_per_reply(self):
        lines = [json.dumps(dict(json.loads(reply("m%d" % i, "2026-10-07T04:00:0%d.000Z" % i, U1)), effort=e))
                 for i, e in enumerate(["high", "high", "low"])]
        self.assertEqual(A.parse_run(lines)["effort"], "high")
        self.assertEqual(A.parse_run(LINES)["effort"], "?")

    def test_by_week_splits_main_from_agents(self):
        rows = A.by_week([self.run_("main", "claude-opus-5-5", "2026-10-04"),
                          self.run_("main", "claude-opus-5-5", "2026-10-06", inp=50),
                          self.run_("forest-researcher", "claude-opus-5-5", "2026-10-06", inp=7),
                          self.run_("forest-checker", "claude-sonnet-5-5", "2026-10-06", inp=3)],
                         ["2026-10-06", "2026-10-07", "2026-01-01"])
        self.assertEqual(rows, [("2026-W40", 1, 100, 0, 0, 0, 0), ("2026-W41", 1, 50, 7, 3, 2, 0)])


class Folders(unittest.TestCase):
    def test_finds_the_repo_and_its_worktrees_only(self):
        with tempfile.TemporaryDirectory() as home:
            repo = os.path.join(home, "work", "forest-overlay")
            slug = A.re.sub(r"[^A-Za-z0-9]", "-", os.path.abspath(repo))
            projects = os.path.join(home, ".claude", "projects")
            for name in (slug, slug + "--claude-worktrees-x", "C--other"):
                os.makedirs(os.path.join(projects, name, "s1", "subagents"))
            with open(os.path.join(projects, slug, "s1", "subagents", "agent-a.jsonl"), "w",
                      encoding="utf-8") as f:
                f.write("\n".join(LINES))
            with open(os.path.join(projects, slug, "s1", "subagents", "agent-a.meta.json"), "w",
                      encoding="utf-8") as f:
                json.dump({"agentType": "forest-site"}, f)
            dirs = A.project_dirs(repo, home)
            self.assertEqual(sorted(os.path.basename(d) for d in dirs), [slug, slug + "--claude-worktrees-x"])
            runs = A.load_runs(dirs)
            self.assertEqual([r["agent"] for r in runs], ["forest-site"])


def call(mid, context, out, tools=()):
    content = [{"type": "tool_use", "id": i, "name": n, "input": inp} for i, n, inp in tools]
    return json.dumps({"type": "assistant", "message": {"id": mid, "usage": {
        "input_tokens": 0, "cache_read_input_tokens": context, "cache_creation_input_tokens": 0,
        "output_tokens": out}, "content": content}})


def result(tid, text):
    return json.dumps({"type": "user", "message": {"content": [
        {"type": "tool_result", "tool_use_id": tid, "content": text}]}})


class Levers(unittest.TestCase):
    def test_bash_kind(self):
        self.assertEqual(A.bash_kind("cd /c/x && python scripts/tasks.py list --open"), "tasks.py list")
        self.assertEqual(A.bash_kind("python scripts/lint.py"), "lint.py")
        self.assertEqual(A.bash_kind("python - <<'EOF'"), "python inline")
        self.assertEqual(A.bash_kind("git diff HEAD"), "git diff")
        self.assertEqual(A.bash_kind("sed -n 1,40p a.md"), "sed")

    def test_carried(self):
        lines = [
            call("m1", 1000, 10, [("t1", "Bash", {"command": "git log"}), ("t2", "Read", {})]),
            result("t1", "a" * 30), result("t2", "b" * 10),
            call("m2", 1050, 5),  # grew 50: 10 own output, 40 split 3:1 over the two results
            call("m3", 1055, 0),  # grew 5: all own output of m2
            call("m4", 200, 0),   # a compaction: a new base, nothing carried across it
        ]
        c = A.carried(lines)
        self.assertEqual(c["start"], 3 * 1000)
        self.assertEqual(c["compaction summary"], 200)
        self.assertAlmostEqual(c["own output"], 10 * 2 + 5 * 1)
        self.assertAlmostEqual(c["bash: git log"], 30 * 2)
        self.assertAlmostEqual(c["tool: Read"], 10 * 2)
        self.assertAlmostEqual(sum(c.values()), 1000 + 1050 + 1055 + 200)


if __name__ == "__main__":
    unittest.main()
