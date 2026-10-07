"""Tests for the pure parts of scripts/session-start.py and scripts/cleanup.py.

    python scripts/tests/test_session.py
"""
import importlib.util
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
import cleanup as C  # noqa: E402

_spec = importlib.util.spec_from_file_location("session_start", os.path.join(HERE, "..", "session-start.py"))
S = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(S)


class Badges(unittest.TestCase):
    def test_states(self):
        self.assertEqual(S.badge_state('<svg><title>build - passing</title></svg>'), "passing")
        self.assertEqual(S.badge_state('<svg><title>site - failing</title></svg>'), "failing")
        self.assertEqual(S.badge_state('<svg><title>bot - no status</title></svg>'), "no status")
        self.assertEqual(S.badge_state(""), "unknown")


class Git(unittest.TestCase):
    def test_ahead_behind(self):
        self.assertEqual(S.ahead_behind("2\t5"), (2, 5))
        self.assertEqual(S.ahead_behind(""), (0, 0))

    def test_badges_only_for_what_ci_built(self):
        self.assertTrue(S.use_badges("abc", "abc", [], False))
        self.assertFalse(S.use_badges("abc", "abd", [], False))      # unpushed or behind
        self.assertFalse(S.use_badges("abc", "abc", [" M x"], False))  # local changes
        self.assertFalse(S.use_badges("abc", "abc", [], True))        # --baseline
        self.assertFalse(S.use_badges("", "", [], False))             # git failed

    def test_parse_worktrees(self):
        text = ("worktree C:/r\nHEAD aaa\nbranch refs/heads/main\n\n"
                "worktree C:/r/.claude/worktrees/x\nHEAD bbb\nbranch refs/heads/ui-redesign\n\n"
                "worktree C:/r/.claude/worktrees/y\nHEAD ccc\ndetached\nprunable gitdir file points to non-existent location\n")
        wts = C.parse_worktrees(text)
        self.assertEqual([w["branch"] for w in wts], ["main", "ui-redesign", None])
        self.assertEqual(wts[2]["head"], "ccc")
        self.assertEqual([w["prunable"] for w in wts], [False, False, True])


class Quality(unittest.TestCase):
    ROWS = [{"area": "Saves", "grade": "C"}, {"area": "Site", "grade": "A"}, {"area": "Bot", "grade": "C"}]

    def test_parse_changes(self):
        log = "@2026-10-08\n\nsrc/a.cs\ndocs/b.md\n@2026-10-07\n\nsite/c.js\n"
        self.assertEqual(S.parse_changes(log), [("2026-10-08", "src/a.cs"), ("2026-10-08", "docs/b.md"),
                                                ("2026-10-07", "site/c.js")])

    def test_line(self):
        self.assertEqual(S.quality_line(self.ROWS, []),
                         "quality: lowest C - Saves, Bot; every row reviewed since its area last changed")
        self.assertIn("changed since their review: Saves (2 files) -> re-grade", S.quality_line(self.ROWS, [("Saves", 2)]))


class Tests(unittest.TestCase):
    def test_dotnet(self):
        ok = "Passed!  - Failed:     0, Passed:   909, Skipped:     0, Total:   909, Duration: 80 ms - X.dll (net8.0)"
        bad = "Failed!  - Failed:     2, Passed:   907, Skipped:     0, Total:   909, Duration: 80 ms - X.dll"
        self.assertEqual(S.test_summary("noise\n" + ok), "909 passed")
        self.assertEqual(S.test_summary(bad), "2 of 909 FAILED")

    def test_unittest(self):
        self.assertEqual(S.test_summary("....\nRan 24 tests in 0.01s\n\nOK\n"), "24 passed")
        self.assertEqual(S.test_summary("Ran 24 tests in 0.01s\n\nFAILED (failures=3)\n"), "3 of 24 FAILED")

    def test_unknown_output_shows_its_last_line(self):
        self.assertEqual(S.test_summary("error CS1002: ; expected\n\n"), "error CS1002: ; expected")
        self.assertEqual(S.test_summary(""), "no output")


class Scratch(unittest.TestCase):
    def test_slug_matches_claude_code(self):
        self.assertEqual(C.project_slug(r"C:\Users\deter\Desktop\forest-overlay"),
                         "C--Users-deter-Desktop-forest-overlay")

    def test_stale_by_age_and_keep(self):
        day = 86400
        now = 100 * day
        entries = [("/t/old", now - 8 * day), ("/t/new", now - 1 * day), ("/t/current", now - 30 * day)]
        self.assertEqual(C.stale(entries, now, 7, keep=["current"]), ["/t/old"])
        self.assertEqual(C.stale(entries, now, 0.5), ["/t/old", "/t/new", "/t/current"])


if __name__ == "__main__":
    unittest.main()
