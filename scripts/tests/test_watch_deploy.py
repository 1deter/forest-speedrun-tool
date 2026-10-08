"""Tests for the pure parts of scripts/watch-deploy.py.

    python scripts/tests/test_watch_deploy.py
"""
import importlib.util
import os
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("watch_deploy", os.path.join(HERE, "..", "watch-deploy.py"))
W = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(W)


class Started(unittest.TestCase):
    def test_docker_time(self):
        self.assertEqual(W.parse_started("2026-10-07T04:31:02.123456789Z\n"), 1791347462)

    def test_junk(self):
        self.assertIsNone(W.parse_started("junk"))


class Paths(unittest.TestCase):
    def test_match_workflows(self):
        # The watcher's paths mirror the workflows' push paths.
        for name, c in W.TARGETS.items():
            wf = open(os.path.join(HERE, "..", "..", ".github", "workflows", name + ".yml"), encoding="utf-8").read()
            push = wf.split("pull_request:")[0]
            listed = [l.strip().strip("-' ").rstrip("/*") for l in push.splitlines() if l.strip().startswith("- '")]
            self.assertEqual(sorted(listed), sorted(c["paths"]), name)


if __name__ == "__main__":
    unittest.main()
