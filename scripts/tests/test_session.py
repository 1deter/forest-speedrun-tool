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


QUALITY = """# Quality

## Cleanup log

| Date | Findings | Filed | Ignored | Re-graded |
|---|---|---|---|---|
%s
## Simplification log

| Date | Component switched off | How to switch it back | Outcome | Decision |
|---|---|---|---|---|
%s
## Change history
"""


class CleanupAndReview(unittest.TestCase):
    """The weekly cleanup and monthly harness review lines (docs/harness.md 10e, 12d; T-0014)."""

    def test_log_table_reads_dated_rows_only(self):
        text = QUALITY % ("| 2026-10-07 | 9 | 4 | 3 | none |\n", "")
        self.assertEqual(S.log_table(text, "Cleanup log"), [["2026-10-07", "9", "4", "3", "none"]])
        self.assertEqual(S.log_table(text, "Simplification log"), [])
        self.assertIsNone(S.log_table("# nothing", "Cleanup log"))

    def test_cleanup_due_after_seven_days(self):
        self.assertTrue(S.cleanup_line([], "2026-10-07")[1])
        line, due = S.cleanup_line([["2026-10-01"], ["2026-10-07"]], "2026-10-13")
        self.assertFalse(due)
        self.assertIn("next due 2026-10-14", line)
        self.assertTrue(S.cleanup_line([["2026-10-07"]], "2026-10-14")[1])
        self.assertFalse(S.cleanup_line(None, "2026-10-14")[1])

    def test_review_due_after_thirty_days(self):
        row = ["2026-10-07", "the Stop hook", "settings.json", "kept: rounds 1.2 -> 1.9", "keep"]
        self.assertTrue(S.review_line([], lambda d: 0, "2026-10-07")[1])
        self.assertFalse(S.review_line([row], lambda d: 0, "2026-11-05")[1])
        self.assertTrue(S.review_line([row], lambda d: 0, "2026-11-06")[1])

    def test_open_review_counts_its_tasks(self):
        row = ["2026-10-07", "lint UI heuristics", "lint.py ui check back on", "-", "open"]
        seen = []
        line, due = S.review_line([row], lambda d: seen.append(d) or 3, "2026-12-30")
        self.assertEqual(seen, ["2026-10-07"])
        self.assertFalse(due)                     # open: never "due" by age, only by its tasks
        self.assertIn("lint UI heuristics off since 2026-10-07, 3/5 tasks finished", line)
        line, due = S.review_line([row], lambda d: 7, "2026-10-09")
        self.assertTrue(due)
        self.assertIn("5/5 tasks finished -> compare and decide", line)


class Badges(unittest.TestCase):
    def test_states(self):
        self.assertEqual(S.badge_state('<svg><title>build - passing</title></svg>'), "passing")
        self.assertEqual(S.badge_state('<svg><title>site - failing</title></svg>'), "failing")
        self.assertEqual(S.badge_state('<svg><title>bot - no status</title></svg>'), "no status")
        self.assertEqual(S.badge_state(""), "unknown")


class LocalBranchesToDelete(unittest.TestCase):
    """cleanup.py lists each merged branch once (T-0169)."""
    def wt(self, branch, state):
        return ({"path": "/w/" + str(branch), "branch": branch}, state)

    def test_a_merged_worktree_branch_is_listed_once(self):
        local = [("agent-a", True), ("wip", False)]
        self.assertEqual(C.local_to_delete(local, [self.wt("agent-a", "merged")]), ["agent-a"])

    def test_branch_only_known_from_its_worktree_is_added(self):
        self.assertEqual(C.local_to_delete([("old", True)], [self.wt("agent-b", "merged")]), ["old", "agent-b"])

    def test_unmerged_live_dirty_detached_and_kept_are_skipped(self):
        wts = [self.wt("a", "live"), self.wt("b", "dirty"), self.wt(None, "merged"), self.wt("main", "merged")]
        self.assertEqual(C.local_to_delete([("a", False)], wts), [])

    def test_two_worktrees_on_one_branch_do_not_repeat_it(self):
        wts = [self.wt("x", "merged"), self.wt("x", "merged")]
        self.assertEqual(C.local_to_delete([], wts), ["x"])


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
        self.assertEqual([w["locked"] for w in wts], [False, False, False])

    def test_parse_worktrees_locked_with_and_without_reason(self):
        text = ("worktree C:/r\nHEAD aaa\nbranch refs/heads/main\n\n"
                "worktree C:/r/w1\nHEAD bbb\nbranch refs/heads/a\nlocked\n\n"
                "worktree C:/r/w2\nHEAD ccc\nbranch refs/heads/b\nlocked claude agent agent-1 (pid 7)\n")
        self.assertEqual([w["locked"] for w in C.parse_worktrees(text)], [False, True, True])

    def test_survey_leaves_a_locked_worktree_that_looks_merged(self):
        import tempfile
        from unittest import mock
        with tempfile.TemporaryDirectory() as d:
            wt = os.path.join(d, "agent")
            os.mkdir(wt)
            porcelain = ("worktree %s\nHEAD aaa\nbranch refs/heads/main\n\n"
                         "worktree %s\nHEAD bbb\nbranch refs/heads/agent-x\nlocked\n") % (C.ROOT, wt)
            def fake_git(*args, **kw):
                return porcelain if args[:2] == ("worktree", "list") else ""
            with mock.patch.object(C, "git", fake_git), mock.patch.object(C, "is_merged", lambda r, base=C.BASE: True), \
                    mock.patch.object(C, "is_clean", lambda p: True):
                s = C.survey()
            self.assertEqual([st for _, st in s["worktrees"]], ["main", "locked"])
            calls = []
            with mock.patch.object(C, "git", lambda *a, **k: calls.append(a) or (porcelain if a[:2] == ("worktree", "list") else "")), \
                    mock.patch.object(C, "is_merged", lambda r, base=C.BASE: True), \
                    mock.patch.object(C, "is_clean", lambda p: True), \
                    mock.patch.object(C, "pycaches", lambda: []), mock.patch.object(C, "scratch_candidates", lambda n, d: []):
                C.main([])
            self.assertFalse([c for c in calls if c[:2] == ("worktree", "remove")])
            self.assertFalse([c for c in calls if c[:1] == ("branch",)])


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
    MARK = {"date": "2026-10-07", "queue_id": 12, "message_id": "1000", "eval": "2026-10-07"}

    def test_queue_ids(self):
        out = "#12 2026-10-06 thumbs-down (answer #3): q\n    detail\n#15 2026-10-07 partial (answer #9): q2\n    #99 not an item\n"
        self.assertEqual(S.parse_queue_ids(out), [12, 15])
        self.assertEqual(S.parse_queue_ids(""), [])

    def test_bot_use_only_counts_messages_involving_the_bot(self):
        bot = {"id": "9", "bot": True}
        msgs = [{"id": "999", "author": {"id": "1"}, "mentions": [bot]},                 # before the mark
                {"id": "1001", "author": {"id": "2"}},                                    # runners chatting
                {"id": "1002", "author": {"id": "3"}, "mentions": [{"id": "2"}]},         # mentions a person
                {"id": "1003", "author": {"id": "3"}, "mentions": [bot]},                 # @bot question
                {"id": "1004", "author": bot, "interaction_metadata": {"id": "5"}},       # /ask answer
                {"id": "1005", "author": bot},                                            # the bot's own follow-up
                {"id": "1006", "author": {"id": "2"}, "referenced_message": {"author": bot}},  # reply to an answer
                {"id": "10000", "author": {"id": "4"}, "mentions": [bot]}]               # compared as ints, not strings
        self.assertEqual([m["id"] for m in S.bot_use(msgs, "1000")], ["1003", "1004", "1006", "10000"])

    def test_mark(self):
        self.assertEqual(S.read_mark('{"date":"2026-10-07","queue_id":12,"message_id":"1000"}'), self.MARK)
        self.assertIsNone(S.read_mark("not json"))
        self.assertIsNone(S.read_mark('{"queue_id":1}'))

    def test_line_due_only_when_something_is_new(self):
        line, due = S.feedback_line(self.MARK, 0, 0)
        self.assertFalse(due)
        self.assertIn("nothing new", line)
        line, due = S.feedback_line(self.MARK, 2, 0)
        self.assertTrue(due)
        self.assertIn("2 new thumbs-down", line)
        self.assertTrue(S.feedback_line(self.MARK, 0, 5)[1])

    def test_eval_due_only_after_a_change_and_a_week(self):
        m = dict(self.MARK, eval="2026-10-01")
        self.assertFalse(S.eval_due(m, "2026-10-09", ""))
        self.assertFalse(S.eval_due(m, "2026-10-05", "abc123"))
        self.assertTrue(S.eval_due(m, "2026-10-09", "abc123"))
        self.assertIn("full eval due", S.feedback_line(m, 0, 0, (), True)[0])

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
            self.assertIn("2 new thumbs-down / partial queue items, 1 new uses of the bot in knowledge-testing", line)
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
