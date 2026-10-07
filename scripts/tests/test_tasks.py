"""Tests for scripts/tasks.py: parsing, the gates and the next-task choice.

    python scripts/tests/test_tasks.py
"""
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import tasks as T  # noqa: E402


def task(tid, **kw):
    t = {"id": tid, "title": "t " + tid, "area": "site", "priority": 3, "status": "todo",
         "needs": "none", "question": None, "behavior": "does x", "verify": ["check x"],
         "keep": "-", "checker": False, "commits": [], "evidence": [], "blocked_by": [], "log": []}
    t.update(kw)
    return t


class ParseDump(unittest.TestCase):
    def test_round_trip_sorted_by_number(self):
        ts = [task("T-0010"), task("T-0002")]
        text = T.dump(ts)
        self.assertEqual([t["id"] for t in T.parse(text)], ["T-0002", "T-0010"])
        self.assertTrue(text.startswith('{"id": "T-0002"'))

    def test_blank_lines_skipped_and_bad_json_named(self):
        self.assertEqual(len(T.parse('\n{"id":"T-0001"}\n\n')), 1)
        with self.assertRaises(T.TaskError) as e:
            T.parse('{"id":"T-0001"}\n<<<<<<< HEAD\n')
        self.assertIn("line 2", str(e.exception))

    def test_ids(self):
        self.assertEqual(T.new_id([]), "T-0001")
        self.assertEqual(T.new_id([task("T-0007"), task("T-0003")]), "T-0008")
        self.assertEqual(T.normalise_id("t12"), "T-0012")
        self.assertEqual(T.normalise_id("T-0012"), "T-0012")


class Validate(unittest.TestCase):
    def test_duplicate_ids_from_a_merge(self):
        with self.assertRaises(T.TaskError) as e:
            T.validate([task("T-0001"), task("T-0001")])
        self.assertIn("FIX:", str(e.exception))

    def test_unknown_status(self):
        with self.assertRaises(T.TaskError):
            T.validate([task("T-0001", status="done")])

    def test_author_decision_needs_its_question(self):
        with self.assertRaises(T.TaskError):
            T.validate([task("T-0001", needs="author-decision")])
        T.validate([task("T-0001", needs="author-decision", question="A or B?")])

    def test_confirmed_needs_evidence(self):
        with self.assertRaises(T.TaskError):
            T.validate([task("T-0001", status="confirmed")])


class Gates(unittest.TestCase):
    def test_start_needs_a_worker_and_a_contract(self):
        ts = [task("T-0001", keep=None)]
        with self.assertRaises(T.TaskError):
            T.transition(ts, ts[0], "in-progress", by=None)
        with self.assertRaises(T.TaskError) as e:
            T.transition(ts, ts[0], "in-progress", by="main")
        self.assertIn("--keep", str(e.exception))
        ts[0]["keep"] = "-"
        T.transition(ts, ts[0], "in-progress", by="main")
        self.assertEqual((ts[0]["status"], ts[0]["owner"]), ("in-progress", "main"))

    def test_open_question_blocks_start(self):
        ts = [task("T-0001", question="which word?")]
        with self.assertRaises(T.TaskError):
            T.transition(ts, ts[0], "in-progress", by="main")

    def test_wip_one_per_worker(self):
        ts = [task("T-0001", status="in-progress", owner="main"), task("T-0002")]
        with self.assertRaises(T.TaskError):
            T.transition(ts, ts[1], "in-progress", by="main")
        T.transition(ts, ts[1], "in-progress", by="forest-dev")

    def test_dependency_must_be_built(self):
        ts = [task("T-0001"), task("T-0002", blocked_by=["T-0001"])]
        with self.assertRaises(T.TaskError):
            T.transition(ts, ts[1], "in-progress", by="main")
        ts[0]["status"] = "built"
        T.transition(ts, ts[1], "in-progress", by="main")

    def test_built_needs_a_commit_and_records_the_maker(self):
        ts = [task("T-0001", status="in-progress", owner="main")]
        with self.assertRaises(T.TaskError):
            T.transition(ts, ts[0], "built", by="main")
        ts[0]["commits"] = ["abc1234"]
        T.transition(ts, ts[0], "built", by="main")
        self.assertEqual(ts[0]["maker"], "main")
        self.assertNotIn("owner", ts[0])

    def test_confirmed_needs_evidence(self):
        ts = [task("T-0001", status="built")]
        with self.assertRaises(T.TaskError):
            T.transition(ts, ts[0], "confirmed", by="main")
        ts[0]["evidence"] = [{"by": "main", "what": "test passes"}]
        T.transition(ts, ts[0], "confirmed", by="main")

    def test_checker_task_needs_evidence_from_someone_else(self):
        ts = [task("T-0001", status="released", checker=True, maker="main",
                   evidence=[{"by": "main", "what": "looked fine"}])]
        with self.assertRaises(T.TaskError) as e:
            T.transition(ts, ts[0], "confirmed", by="main")
        self.assertIn("fresh-context", str(e.exception))
        ts[0]["evidence"].append({"by": "forest-tester", "what": "Restart line seen"})
        T.transition(ts, ts[0], "confirmed", by="forest-tester")

    def test_confirmed_never_goes_back(self):
        ts = [task("T-0001", status="confirmed", evidence=[{"by": "x", "what": "y"}])]
        with self.assertRaises(T.TaskError) as e:
            T.transition(ts, ts[0], "todo")
        self.assertIn("Regression", str(e.exception))

    def test_blocked_and_wontfix_need_a_reason(self):
        ts = [task("T-0001")]
        with self.assertRaises(T.TaskError):
            T.transition(ts, ts[0], "blocked")
        with self.assertRaises(T.TaskError):
            T.transition(ts, ts[0], "wontfix")
        T.transition(ts, ts[0], "wontfix", note="superseded by T-0002")

    def test_transitions_are_logged(self):
        ts = [task("T-0001")]
        T.transition(ts, ts[0], "in-progress", by="main")
        self.assertEqual(ts[0]["log"][-1]["from"], "todo")
        self.assertEqual(ts[0]["log"][-1]["by"], "main")


class Next(unittest.TestCase):
    def test_priority_then_id(self):
        ts = [task("T-0003", priority=2), task("T-0001", priority=3), task("T-0002", priority=2)]
        self.assertEqual(T.pick_next(ts)["id"], "T-0002")

    def test_skips_what_an_agent_cannot_do_alone(self):
        ts = [task("T-0001", priority=1, needs="author-eyes"),
              task("T-0002", priority=1, needs="author-decision", question="?"),
              task("T-0003", priority=1, needs="bridge"),
              task("T-0004", priority=1, question="unanswered"),
              task("T-0005", priority=1, status="built"),
              task("T-0006", priority=4)]
        self.assertEqual(T.pick_next(ts)["id"], "T-0006")
        self.assertEqual(T.pick_next(ts, bridge=True)["id"], "T-0003")

    def test_skips_waiting_dependencies(self):
        ts = [task("T-0001", priority=4), task("T-0002", priority=1, blocked_by=["T-0001"])]
        self.assertEqual(T.pick_next(ts)["id"], "T-0001")

    def test_worker_gets_its_own_task_back(self):
        ts = [task("T-0001", priority=1), task("T-0002", status="in-progress", owner="main")]
        self.assertEqual(T.pick_next(ts, by="main")["id"], "T-0002")
        self.assertEqual(T.pick_next(ts, by="other")["id"], "T-0001")

    def test_nothing(self):
        self.assertIsNone(T.pick_next([task("T-0001", status="confirmed")]))


class Views(unittest.TestCase):
    def test_render_lists_questions_first_and_counts(self):
        ts = [task("T-0001", question="A or B?", needs="author-decision"), task("T-0002"),
              task("T-0003", status="confirmed", evidence=[{"by": "x", "what": "y"}])]
        md = T.render(ts)
        self.assertLess(md.index("Questions for the author"), md.index("## To do"))
        self.assertIn("2 open, 1 done", md)

    def test_stats_counts_layers(self):
        s = T.stats([task("T-0001", layer="spec"), task("T-0002")])
        self.assertIn("spec 1", s)


if __name__ == "__main__":
    unittest.main()
