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


class BotFeedback(unittest.TestCase):
    MARK = {"date": "2026-10-07", "queue_id": 12, "message_id": "1000"}

    def test_queue_ids(self):
        out = "#12 2026-10-06 thumbs-down (answer #3): q\n    detail\n#15 2026-10-07 partial (answer #9): q2\n    #99 not an item\n"
        self.assertEqual(S.parse_queue_ids(out), [12, 15])
        self.assertEqual(S.parse_queue_ids(""), [])

    def test_new_messages_are_humans_after_the_mark(self):
        msgs = [{"id": "999", "author": {"id": "1"}},
                {"id": "1001", "author": {"id": "2", "bot": True}},
                {"id": "1002", "author": {"id": "3"}},
                {"id": "10000", "author": {"id": "4"}}]   # compared as ints, not strings
        self.assertEqual([m["id"] for m in S.new_messages(msgs, "1000")], ["1002", "10000"])
        self.assertEqual(S.new_messages(msgs, "1000", bot_id="3")[0]["id"], "10000")

    def test_mark(self):
        self.assertEqual(S.read_mark('{"date":"2026-10-07","queue_id":12,"message_id":"1000"}'), self.MARK)
        self.assertIsNone(S.read_mark("not json"))
        self.assertIsNone(S.read_mark('{"queue_id":1}'))

    def test_line_due_only_when_something_is_new(self):
        line, due = S.feedback_line(self.MARK, 0, 0)
        self.assertFalse(due)
        self.assertIn("no review due", line)
        line, due = S.feedback_line(self.MARK, 2, 0)
        self.assertTrue(due)
        self.assertIn("2 new thumbs-down", line)
        self.assertTrue(S.feedback_line(self.MARK, 0, 5)[1])

    def test_unreadable_source_is_said_not_counted(self):
        line, due = S.feedback_line(self.MARK, None, 0, ["queue skipped (no ssh key here)"])
        self.assertFalse(due)
        self.assertIn("unknown", line)
        self.assertIn("no ssh key", line)

    def test_no_mark_means_first_review_due(self):
        line, due = S.feedback_line(None, None, None)
        self.assertTrue(due)
        self.assertIn("first review", line)

    def test_check_with_fake_sources(self):
        import tempfile
        d = tempfile.mkdtemp()
        mark = os.path.join(d, "mark.json")
        with open(mark, "w") as f:
            f.write('{"date":"2026-10-07","queue_id":12,"message_id":"1000"}')
        saved = (S.REVIEW_MARK, S.check_queue_ids, S.check_testing_messages)
        try:
            S.REVIEW_MARK = mark
            S.check_queue_ids = lambda: ([11, 12, 13, 14], None)
            S.check_testing_messages = lambda after: ([{"id": "1001"}], None)
            line, due = S.check_bot_feedback()
            self.assertTrue(due)
            self.assertIn("2 new thumbs-down / partial queue items, 1 new knowledge-testing messages", line)
        finally:
            S.REVIEW_MARK, S.check_queue_ids, S.check_testing_messages = saved


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
