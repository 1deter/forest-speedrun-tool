"""Tests for scripts/bump.py: the version edits and the release's tasks (docs/harness.md 6c).

    python scripts/tests/test_bump.py
"""
import json
import os
import shutil
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import bump as B  # noqa: E402
import tasks as T  # noqa: E402

FILES = {
    "ForestOverlay.csproj": "<Version>0.24.1</Version><AssemblyVersion>0.24.1.0</AssemblyVersion>"
                            "<FileVersion>0.24.1.0</FileVersion>\r\n",
    os.path.join("src", "Plugin.cs"): '﻿public const string PluginVersion = "0.24.1";\r\n',
    "CHANGELOG.md": "# Changelog\n\n## v0.24.1 - 2026-10-01\n\n- old\n",
}


def task(tid, **kw):
    t = {"id": tid, "title": "t", "area": "plugin", "priority": 3, "status": "built", "needs": "none",
         "question": None, "behavior": "x", "verify": ["x"], "keep": "-", "checker": True,
         "commits": ["aaa"], "evidence": [], "blocked_by": [], "log": []}
    t.update(kw)
    return t


class Bump(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()
        os.makedirs(os.path.join(self.dir, "src"))
        for name, text in FILES.items():
            with open(os.path.join(self.dir, name), "w", encoding="utf-8", newline="") as f:
                f.write(text)
        self.saved = (B.ROOT, T.TASKS, T.VIEW, T.release_plan, sys.argv)
        B.ROOT = self.dir
        T.TASKS = os.path.join(self.dir, "tasks.jsonl")
        T.VIEW = os.path.join(self.dir, "tasks.md")
        real = self.saved[3]
        T.release_plan = lambda ts, v: real(ts, v, in_head=lambda c: c == "aaa", tag_of=lambda cs: None)
        sys.argv = ["bump.py", "0.24.2", "New thing."]

    def tearDown(self):
        B.ROOT, T.TASKS, T.VIEW, T.release_plan, sys.argv = self.saved
        shutil.rmtree(self.dir)

    def write_tasks(self, ts):
        with open(T.TASKS, "w", encoding="utf-8") as f:
            f.write(T.dump(ts))

    def read(self, name):
        return open(os.path.join(self.dir, name), "rb").read()

    def test_marks_the_releases_tasks(self):
        self.write_tasks([task("T-0001", reviews=[{"verdict": "accept", "commits": ["aaa"]}]),
                          task("T-0002", commits=["zzz"])])
        B.main()
        ts = {t["id"]: t for t in T.load(T.TASKS)}
        self.assertEqual((ts["T-0001"]["status"], ts["T-0001"]["release"]), ("released", "v0.24.2"))
        self.assertEqual(ts["T-0002"]["status"], "built")
        self.assertIn(b'PluginVersion = "0.24.2"', self.read(os.path.join("src", "Plugin.cs")))
        self.assertTrue(self.read(os.path.join("src", "Plugin.cs")).startswith(b"\xef\xbb\xbf"))
        self.assertIn(b"## v0.24.2", self.read("CHANGELOG.md"))

    def test_refuses_without_an_accept_and_touches_nothing(self):
        self.write_tasks([task("T-0001")])
        before = {n: self.read(n) for n in FILES}
        tasks_before = self.read("tasks.jsonl")
        with self.assertRaises(SystemExit) as e:
            B.main()
        self.assertIn("T-0001", str(e.exception.code))
        self.assertEqual({n: self.read(n) for n in FILES}, before)
        self.assertEqual(self.read("tasks.jsonl"), tasks_before)
        self.assertFalse(os.path.exists(T.VIEW))


if __name__ == "__main__":
    unittest.main()
