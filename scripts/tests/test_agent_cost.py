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


if __name__ == "__main__":
    unittest.main()
