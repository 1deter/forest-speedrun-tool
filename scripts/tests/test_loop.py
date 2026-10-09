"""Tests for scripts/loop.py: the assisted loop's rounds, actions and stop conditions.

    python scripts/tests/test_loop.py
"""
import os
import shutil
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import loop as L  # noqa: E402
import tasks as T  # noqa: E402

REAL_CONTEXT = L.context_tokens
L.context_tokens = lambda *a, **k: None  # the tests never read this machine's own session

SUMMARY = "Built the thing, the suite proves it, nothing is left for the next round."


def task(tid, **kw):
    t = {"id": tid, "title": "t " + tid, "area": "harness", "priority": 3, "status": "todo",
         "needs": "none", "question": None, "behavior": "does x", "verify": ["check x"],
         "keep": "-", "checker": False, "commits": [], "evidence": [], "blocked_by": [], "log": []}
    t.update(kw)
    return t


def review(verdict, commits=("abc",), faults=("f.py:1 - wrong",)):
    return {"at": "2026-10-07", "by": "forest-checker", "verdict": verdict, "scores": {},
            "faults": list(faults), "commits": list(commits)}


def run(events, fn, *args):
    """Calls a command and appends its events, as main does."""
    new, text, code = fn(events, *args)
    events.extend(new)
    return text, code


class Rounds(unittest.TestCase):
    def test_begin_once_and_lists_parked_questions(self):
        ev = []
        text, _ = run(ev, L.cmd_begin, [task("T-0001", question="A or B?", needs="author-decision")])
        self.assertIn("R-0001", text)
        self.assertIn("A or B?", text)
        with self.assertRaises(L.LoopError):
            L.cmd_begin(ev, [])

    def test_next_opens_a_round_on_the_pick_and_repeats_its_action(self):
        ts = [task("T-0001", priority=3), task("T-0002", priority=2), task("T-0003", needs="author-present")]
        ev = []
        run(ev, L.cmd_begin, ts)
        text, code = run(ev, L.cmd_next, ts)
        self.assertEqual(code, 0)
        self.assertIn("round 1 of 5", text)
        self.assertIn("T-0002", text)
        self.assertIn("contract", text)
        n = len(ev)
        text, _ = run(ev, L.cmd_next, ts)
        self.assertEqual(len(ev), n)  # the open round's action, nothing recorded
        self.assertIn("round 1: T-0002", text)

    def test_a_task_taken_this_run_is_not_picked_again(self):
        ts = [task("T-0001", priority=2), task("T-0002", priority=3)]
        ev = []
        run(ev, L.cmd_begin, ts)
        run(ev, L.cmd_next, ts)
        ts[0]["status"] = "todo"  # given back, unfinished
        ts[0]["notes"] = "gave up"
        run(ev, L.cmd_end, ts, SUMMARY)
        text, _ = run(ev, L.cmd_next, ts)
        self.assertIn("round 2 of 5: T-0002", text)

    def test_skip_keeps_the_wip_rule(self):
        ts = [task("T-0001", status="in-progress", owner="main"), task("T-0002")]
        self.assertEqual(T.pick_next(ts, by="main")["id"], "T-0001")
        self.assertEqual(T.pick_next(ts, by="main", skip={"T-0001"})["id"], "T-0002")

    def test_bridge_runs_take_bridge_tasks(self):
        ts = [task("T-0001", needs="bridge")]
        ev = []
        run(ev, L.cmd_begin, ts, 5, True)
        text, code = run(ev, L.cmd_next, ts)
        self.assertIn("T-0001", text)
        ev2 = []
        run(ev2, L.cmd_begin, ts)
        text, code = run(ev2, L.cmd_next, ts)
        self.assertEqual(code, L.EXIT_STOP)
        self.assertIn("nothing left", text)


class Actions(unittest.TestCase):
    RND = {"start": {"reviews_before": 0}}

    def act(self, **kw):
        return L.action(task("T-0001", **kw), self.RND)

    def test_each_state_names_one_action(self):
        self.assertIn("tasks.py start T-0001", self.act())
        self.assertIn("forest-dev", self.act(status="in-progress", area="plugin"))
        self.assertIn("forest-site", self.act(status="in-progress", area="site"))
        self.assertIn("main itself", self.act(status="in-progress", area="docs"))
        self.assertIn("forest-checker", self.act(status="built", area="plugin", checker=True, commits=["abc"]))
        acc = dict(checker=True, commits=["abc"], reviews=[review("accept")])
        self.assertIn("skill release", self.act(status="built", area="plugin", **acc))
        self.assertIn("deploy-watch", self.act(status="built", area="site", **acc))
        self.assertIn("tasks.py evidence", self.act(status="built", area="harness", commits=["abc"]))
        self.assertIn("e2e.py", self.act(status="released", area="plugin", **acc))
        self.assertIn("loop.py end", self.act(status="confirmed"))
        self.assertIn("parked", self.act(status="built", question="Which?", needs="author-decision"))

    def test_revise_goes_back_to_the_maker(self):
        rv = dict(checker=True, commits=["abc"], reviews=[review("revise")])
        self.assertIn("revise 1 of 2", self.act(status="in-progress", **rv))
        self.assertIn("tasks.py start", self.act(status="todo", **rv))
        # a review from before the round does not count
        self.assertIn("do the work", L.action(task("T-0001", status="in-progress", **rv),
                                              {"start": {"reviews_before": 1}}))


class ReleaseStep(unittest.TestCase):
    """A plugin-area task whose commits touch no plugin path has nothing to release (T-0154)."""
    RND = {"start": {"reviews_before": 0}}

    def built(self, files_of, **kw):
        t = task("T-0001", status="built", area="plugin", checker=True, commits=["abc"],
                 reviews=[review("accept")], **kw)
        return t, L.needs_release(t, files_of)

    def test_plugin_path_releases(self):
        for path in ("src/Core/Host.cs", "patcher/P.cs", "locations/a.json", "collectibles/b.json", "qa/x"):
            self.assertTrue(self.built(lambda c, p=path: ["docs/a.md", p])[1], path)

    def test_scripts_and_docs_only_do_not(self):
        t, rel = self.built(lambda c: ["scripts/loop.py", "docs/areas/workflow.md", "tests/x.cs"])
        self.assertFalse(rel)
        real, L.commit_files = L.commit_files, lambda c: ["scripts/loop.py"]
        try:
            act = L.action(t, self.RND)
        finally:
            L.commit_files = real
        self.assertNotIn("skill release", act)
        self.assertIn("tasks.py evidence", act)

    def test_any_plugin_commit_among_several_releases(self):
        files = {"a": ["scripts/x.py"], "b": ["src/Plugin.cs"]}
        t = task("T-0001", area="plugin", commits=["a", "b"])
        self.assertTrue(L.needs_release(t, files.get))

    def test_unknown_commit_or_none_still_releases(self):
        self.assertTrue(self.built(lambda c: None)[1])
        self.assertTrue(L.needs_release(task("T-0001", area="plugin", commits=[]), lambda c: []))

    def test_other_areas_never_release(self):
        self.assertFalse(L.needs_release(task("T-0001", area="site", commits=["a"]), lambda c: ["src/x.cs"]))

    def test_real_git_resolves_a_commit(self):
        sha = L.subprocess.run(["git", "log", "-1", "--format=%H", "--", "scripts/loop.py"], cwd=T.ROOT,
                               capture_output=True, text=True).stdout.strip()
        if sha:
            self.assertIn("scripts/loop.py", L.commit_files(sha))
        self.assertIsNone(L.commit_files("0" * 40))


class End(unittest.TestCase):
    def setUp(self):
        self.ts = [task("T-0001", area="site", checker=True), task("T-0002")]
        self.ev = []
        run(self.ev, L.cmd_begin, self.ts)
        run(self.ev, L.cmd_next, self.ts)

    def test_in_progress_is_refused(self):
        self.ts[0]["status"] = "in-progress"
        with self.assertRaises(L.LoopError) as e:
            L.cmd_end(self.ev, self.ts, SUMMARY)
        self.assertIn("FIX:", str(e.exception))

    def test_built_behaviour_change_needs_its_accept(self):
        self.ts[0].update(status="built", commits=["abc"])
        with self.assertRaises(L.LoopError):
            L.cmd_end(self.ev, self.ts, SUMMARY)
        self.ts[0]["reviews"] = [review("accept")]
        text, _ = run(self.ev, L.cmd_end, self.ts, SUMMARY)
        self.assertIn("(progress)", text)

    def test_checker_block_ends_parked(self):
        self.ts[0].update(status="built", commits=["abc"], reviews=[review("block")],
                          question="Checker blocked it: x", needs="author-decision")
        run(self.ev, L.cmd_end, self.ts, SUMMARY)
        self.assertEqual(self.ev[-1]["progress"], False)
        self.assertEqual(self.ev[-1]["parked"], True)

    def test_summary_is_one_real_paragraph(self):
        self.ts[0].update(status="wontfix", notes="obsolete")
        for bad in ("done", SUMMARY + "\n\n" + SUMMARY):
            with self.assertRaises(L.LoopError):
                L.cmd_end(self.ev, self.ts, bad)
        run(self.ev, L.cmd_end, self.ts, SUMMARY)
        self.assertEqual(self.ev[-1]["progress"], True)

    def test_wrong_id_and_no_round(self):
        self.ts[0].update(status="confirmed", evidence=[{"by": "x", "what": "y"}])
        with self.assertRaises(L.LoopError):
            L.cmd_end(self.ev, self.ts, SUMMARY, "T-0002")
        run(self.ev, L.cmd_end, self.ts, SUMMARY, "t1")
        with self.assertRaises(L.LoopError):
            L.cmd_end(self.ev, self.ts, SUMMARY)


class Stops(unittest.TestCase):
    def finish(self, ev, ts, status):
        text, code = run(ev, L.cmd_next, ts)
        if code == L.EXIT_STOP:
            return text, code
        t = T.find(ts, ev[-1]["task"])
        t["status"] = status
        t["notes"] = "n"
        if status == "confirmed":
            t["evidence"] = [{"by": "x", "what": "y"}]
        run(ev, L.cmd_end, ts, SUMMARY)
        return text, code

    def test_max_rounds(self):
        ts = [task("T-%04d" % i) for i in range(1, 5)]
        ev = []
        run(ev, L.cmd_begin, ts, 2)
        self.finish(ev, ts, "confirmed")
        self.finish(ev, ts, "confirmed")
        text, code = run(ev, L.cmd_next, ts)
        self.assertEqual(code, L.EXIT_STOP)
        self.assertIn("2 rounds done", text)
        self.assertIsNone(L.open_run(ev))

    def test_three_rounds_without_progress(self):
        ts = [task("T-%04d" % i) for i in range(1, 6)]
        ev = []
        run(ev, L.cmd_begin, ts)
        self.finish(ev, ts, "confirmed")
        for _ in range(3):
            self.finish(ev, ts, "todo")
        text, code = run(ev, L.cmd_next, ts)
        self.assertEqual(code, L.EXIT_STOP)
        self.assertIn("no progress for 3 rounds", text)

    def test_stop_waits_for_the_round(self):
        ts = [task("T-0001")]
        ev = []
        run(ev, L.cmd_begin, ts)
        run(ev, L.cmd_next, ts)
        with self.assertRaises(L.LoopError):
            L.cmd_stop(ev, "author stops it")
        ts[0].update(status="wontfix", notes="n")
        run(ev, L.cmd_end, ts, SUMMARY)
        run(ev, L.cmd_stop, "author stops it")
        self.assertIsNone(L.open_run(ev))
        run(ev, L.cmd_begin, ts)
        self.assertEqual(L.open_run(ev)["id"], "R-0002")


class Context(unittest.TestCase):
    def tearDown(self):
        L.context_tokens = lambda *a, **k: None

    def test_reads_the_newest_main_transcripts_last_call(self):
        import json
        import time
        home = tempfile.mkdtemp()
        repo = os.path.join(home, "repo")
        slug = __import__("re").sub(r"[^A-Za-z0-9]", "-", os.path.abspath(repo))
        d = os.path.join(home, ".claude", "projects", slug)
        os.makedirs(d)
        old = os.path.join(d, "old.jsonl")
        with open(old, "w") as f:
            f.write(json.dumps({"type": "assistant", "message": {"usage": {"input_tokens": 999999}}}) + "\n")
        time.sleep(0.05)
        with open(os.path.join(d, "new.jsonl"), "w") as f:
            for n in (1000, 2000):
                f.write(json.dumps({"type": "assistant", "message": {"usage": {
                    "input_tokens": 1, "cache_read_input_tokens": n, "cache_creation_input_tokens": 10}}}) + "\n")
            f.write(json.dumps({"type": "user", "message": {"content": "hi"}}) + "\n")
        os.utime(old, (1, 1))
        self.assertEqual(REAL_CONTEXT(repo, home), 2011)
        self.assertIsNone(REAL_CONTEXT(os.path.join(home, "other"), home))
        shutil.rmtree(home)

    def test_a_big_context_stops_the_run_and_refuses_a_new_one(self):
        ev = []
        run(ev, L.cmd_begin, [task("T-0001")])
        L.context_tokens = lambda *a, **k: 250000
        text, code = run(ev, L.cmd_next, [task("T-0001")])
        self.assertEqual(code, L.EXIT_STOP)
        self.assertIn("250k", text)
        with self.assertRaises(L.LoopError):
            L.cmd_begin(ev, [task("T-0001")])
        L.context_tokens = lambda *a, **k: 150000
        self.assertEqual(run(ev, L.cmd_begin, [task("T-0001")])[1], 0)


class Report(unittest.TestCase):
    def test_report_and_stats(self):
        ts = [task("T-0001"), task("T-0002")]
        ev = []
        self.assertEqual(L.stats_line(ev), "loop: no runs yet")
        run(ev, L.cmd_begin, ts)
        run(ev, L.cmd_next, ts)
        run(ev, L.cmd_intervene, "author fixed the contract")
        ts[0].update(status="confirmed", evidence=[{"by": "x", "what": "y"}])
        run(ev, L.cmd_end, ts, SUMMARY)
        run(ev, L.cmd_next, ts)
        ts[1].update(status="built", question="A or B?", needs="author-decision")
        run(ev, L.cmd_end, ts, SUMMARY)
        rep = L.report(ev)
        self.assertIn("2 round(s), 1 progressed, 1 without progress (1 parked), 1 intervention(s) - still open", rep)
        self.assertIn("1. T-0001 -> confirmed: " + SUMMARY, rep)
        self.assertIn("2. T-0002 -> built (no progress)", rep)
        self.assertIn("intervention (round 1): author fixed the contract", rep)
        self.assertEqual(L.stats_line(ev), "loop: 1 run(s), 2 round(s), 1 progressed, 1 without progress, "
                                           "1 intervention(s), 2.0 rounds per run")
        self.assertEqual(L.parse(T.dump([]) + "\n".join('{"event": "x"}' for _ in range(2))),
                         [{"event": "x"}] * 2)


class ThroughMain(unittest.TestCase):
    """The CLI on temp files: the third revise parks the task in tasks.jsonl."""

    def setUp(self):
        self.dir = tempfile.mkdtemp()
        self.loop = os.path.join(self.dir, "loop.jsonl")
        self.tasks = os.path.join(self.dir, "tasks.jsonl")

    def tearDown(self):
        shutil.rmtree(self.dir)

    def write(self, ts):
        T.save(ts, self.tasks, None)

    def main(self, *argv):
        return L.main(list(argv), loop_path=self.loop, tasks_path=self.tasks)

    def test_third_revise_parks_and_moves_on(self):
        self.write([task("T-0001", area="plugin", checker=True, priority=1), task("T-0002")])
        self.assertEqual(self.main("begin"), 0)
        self.assertEqual(self.main("next"), 0)
        ts = T.load(self.tasks)
        ts[0].update(status="in-progress", owner="main", commits=["abc"],
                     reviews=[review("revise"), review("revise")])
        self.write(ts)
        self.assertEqual(self.main("next"), 0)  # two revises: still the maker's
        self.assertEqual(T.load(self.tasks)[0]["status"], "in-progress")
        ts[0]["reviews"].append(review("revise", faults=["x.cs:9 - still wrong"]))
        self.write(ts)
        self.assertEqual(self.main("next"), 0)
        t = T.load(self.tasks)[0]
        self.assertEqual((t["status"], t["needs"]), ("blocked", "author-decision"))
        self.assertIn("x.cs:9 - still wrong", t["question"])
        ev = L.load(self.loop)
        self.assertEqual([e["event"] for e in ev], ["begin", "round", "park", "end", "round"])
        self.assertEqual(ev[-1]["task"], "T-0002")

    def test_peek_writes_nothing_and_errors_exit_1(self):
        self.write([task("T-0001")])
        self.assertEqual(self.main("next"), 1)
        self.main("begin")
        before = L.load(self.loop)
        self.assertEqual(self.main("peek"), 0)
        self.assertEqual(L.load(self.loop), before)
        self.assertEqual(self.main("begin", "--max-rounds", "0"), 1)


if __name__ == "__main__":
    unittest.main()
