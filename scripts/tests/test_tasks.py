"""Tests for scripts/tasks.py: parsing, the gates and the next-task choice.

    python scripts/tests/test_tasks.py
"""
import argparse
import os
import subprocess
import sys
import tempfile
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

    def test_plugin_checker_task_needs_review_then_in_game_evidence(self):
        ts = [task("T-0001", area="plugin", status="released", checker=True, maker="main", commits=["abc"],
                   evidence=[{"by": "main", "what": "looked fine"}])]
        with self.assertRaises(T.TaskError) as e:
            T.transition(ts, ts[0], "confirmed", by="main")
        self.assertIn("no checker review", str(e.exception))
        ts[0]["reviews"] = [{"verdict": "accept", "by": "forest-checker", "commits": ["abc"]}]
        with self.assertRaises(T.TaskError) as e:
            T.transition(ts, ts[0], "confirmed", by="main")
        self.assertIn("in-game", str(e.exception))
        ts[0]["evidence"].append({"by": "forest-checker", "what": "diff reads fine"})
        with self.assertRaises(T.TaskError):  # the checker's word is a gate, not a confirmation
            T.transition(ts, ts[0], "confirmed", by="main")
        ts[0]["evidence"].append({"by": "qa:maks", "what": "lines gone in a run"})
        T.transition(ts, ts[0], "confirmed", by="main")

    def test_scripts_only_plugin_task_confirms_on_test_evidence(self):
        ts = [task("T-0001", area="plugin", status="built", checker=True, maker="main", commits=["abc"],
                   reviews=[{"verdict": "accept", "by": "forest-checker", "commits": ["abc"]}],
                   evidence=[{"by": "forest-checker", "what": "286 tests pass"}])]
        saved = T.commit_files
        try:
            T.commit_files = lambda sha: ["scripts/loop.py", "docs/harness.md"]
            T.transition(ts, ts[0], "confirmed", by="main")
            self.assertEqual(ts[0]["status"], "confirmed")
            ts[0]["status"] = "built"
            T.commit_files = lambda sha: ["scripts/loop.py", "src/Modules/X.cs"]
            with self.assertRaises(T.TaskError) as e:
                T.transition(ts, ts[0], "confirmed", by="main")
            self.assertIn("in-game", str(e.exception))
        finally:
            T.commit_files = saved

    def test_site_checker_task_confirms_with_review_and_live_check(self):
        ts = [task("T-0001", status="built", checker=True, maker="main", commits=["abc"],
                   evidence=[{"by": "main", "what": "deploy-watch: live"}])]
        with self.assertRaises(T.TaskError):
            T.transition(ts, ts[0], "confirmed", by="main")
        ts[0]["reviews"] = [{"verdict": "accept", "by": "forest-checker", "commits": ["abc"]}]
        T.transition(ts, ts[0], "confirmed", by="main")


class Checker(unittest.TestCase):
    SCORES = "correctness=2,verification=2,scope=2,restart=n/a,legible=1,handoff=2"

    def review(self, ts, verdict, by="forest-checker", scores=None, faults=None):
        a = argparse.Namespace(id="T-0001", verdict=verdict, by=by, scores=scores or self.SCORES, faults=faults)
        return T.cmd_review(ts, a)

    def built(self, **kw):
        return [task("T-0001", area="plugin", status="built", checker=True, maker="main", commits=["abc"], **kw)]

    def test_release_needs_an_accept_covering_every_commit(self):
        ts = self.built(release="v1")
        with self.assertRaises(T.TaskError):
            T.transition(ts, ts[0], "released", by="main")
        self.review(ts, "accept")
        ts[0]["commits"].append("def")
        with self.assertRaises(T.TaskError) as e:
            T.transition(ts, ts[0], "released", by="main")
        self.assertIn("did not see", str(e.exception))
        self.review(ts, "accept")
        T.transition(ts, ts[0], "released", by="main")

    def test_maker_cannot_review(self):
        with self.assertRaises(T.TaskError):
            self.review(self.built(), "accept", by="main")

    def test_rubric_must_be_full_and_match_the_verdict(self):
        with self.assertRaises(T.TaskError):
            self.review(self.built(), "accept", scores="correctness=2")
        with self.assertRaises(T.TaskError):
            self.review(self.built(), "accept", scores=self.SCORES.replace("legible=1", "legible=0"))
        with self.assertRaises(T.TaskError):
            self.review(self.built(), "revise")  # no faults

    def test_revise_hands_it_back_to_the_maker(self):
        ts = self.built()
        self.review(ts, "revise", faults=["src/X.cs:10 - null when no marker"])
        self.assertEqual((ts[0]["status"], ts[0]["owner"]), ("in-progress", "main"))
        self.assertEqual(ts[0]["reviews"][-1]["faults"], ["src/X.cs:10 - null when no marker"])

    def test_revise_when_the_maker_is_busy_goes_back_on_the_list(self):
        ts = self.built() + [task("T-0002", status="in-progress", owner="main")]
        self.review(ts, "revise", faults=["x"])
        self.assertEqual(ts[0]["status"], "todo")

    def test_block_parks_a_question_for_the_author(self):
        ts = self.built()
        self.review(ts, "block", faults=["the label wording is a runner-facing choice"])
        self.assertEqual((ts[0]["status"], ts[0]["needs"]), ("built", "author-decision"))
        self.assertIn("label wording", ts[0]["question"])
        T.validate(ts)

    def test_brief_has_contract_suites_and_cut_diff(self):
        calls = []

        def show(args):
            calls.append(args)
            if "--name-only" in args:
                return "src/Modules/Runs.cs\nscripts/tasks.py\n"
            return "diff " + "x" * 500

        text = T.brief(self.built()[0], show=show, limit=100)
        self.assertIn("behavior: does x", text)
        self.assertIn("ForestOverlay.Tests", text)
        self.assertIn("test_tasks.py", text)
        self.assertNotIn("ForestSite.Tests", text)
        self.assertIn("cut at 100", text)
        self.assertTrue(all(":(exclude)tasks/tasks.jsonl" in c for c in calls))

    def test_brief_without_commits_says_so(self):
        with self.assertRaises(T.TaskError):
            T.brief(task("T-0001"), show=lambda a: "")

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

    def test_stats_window_counts_tasks_finished_in_it(self):
        def done(tid, day, **kw):
            return task(tid, status="confirmed", log=[{"at": "2026-10-01", "from": "todo", "to": "in-progress"},
                                                      {"at": day, "from": "in-progress", "to": "built"},
                                                      {"at": "2026-10-20", "from": "built", "to": "confirmed"}], **kw)
        ts = [done("T-0001", "2026-10-02"), done("T-0002", "2026-10-05", layer="spec"),
              done("T-0003", "2026-10-09"), task("T-0004")]
        self.assertEqual(T.finished_on(ts[0]), "2026-10-02")   # first built, not the later confirm
        self.assertIsNone(T.finished_on(ts[3]))
        s = T.stats(ts, since="2026-10-03", until="2026-10-09", events=[
            {"at": "2026-10-01T10:00:00", "event": "begin", "run": "R-0001"},
            {"at": "2026-10-04T10:00:00", "event": "begin", "run": "R-0002"}])
        self.assertIn("window: 2026-10-03..2026-10-09, 2 task(s) finished", s)
        self.assertIn("spec 1", s)
        self.assertIn("loop: 1 run(s)", s)                       # R-0001 began before the window
        self.assertNotIn("window", T.stats(ts, events=[]))      # no flags: today's output


ACCEPT = {"verdict": "accept", "commits": ["aaa"]}


class Release(unittest.TestCase):
    """bump.py's half of a release (docs/harness.md 6c)."""

    def plan(self, ts, head=("aaa", "bbb"), tags=None):
        return T.release_plan(ts, "0.24.9", in_head=lambda c: c in head,
                              tag_of=lambda cs: (tags or {}).get(cs[0]))

    def test_built_plugin_task_in_head_is_released_with_the_new_version(self):
        ts = [task("T-0001", area="plugin", status="built", commits=["aaa"], checker=True, reviews=[ACCEPT])]
        plan = self.plan(ts)
        self.assertEqual([(t["id"], r) for t, r in plan], [("T-0001", "v0.24.9")])
        T.mark_released(ts, plan)
        self.assertEqual((ts[0]["status"], ts[0]["release"]), ("released", "v0.24.9"))

    def test_a_commit_already_tagged_keeps_its_own_release(self):
        ts = [task("T-0001", area="plugin", status="built", commits=["aaa"])]
        self.assertEqual(self.plan(ts, tags={"aaa": "v0.24.5"})[0][1], "v0.24.5")

    def test_left_out_off_head_other_areas_and_not_built(self):
        ts = [task("T-0001", area="plugin", status="built", commits=["zzz"]),      # another branch
              task("T-0002", area="plugin", status="built", commits=["aaa", "zzz"]),
              task("T-0003", area="site", status="built", commits=["aaa"]),        # deploys on push
              task("T-0004", area="plugin", status="todo", commits=["aaa"]),
              task("T-0005", area="plugin", status="built", commits=[])]
        self.assertEqual(self.plan(ts), [])

    def test_refuses_without_an_accept(self):
        ts = [task("T-0001", area="plugin", status="built", commits=["aaa"], checker=True),
              task("T-0002", area="plugin", status="built", commits=["bbb"], checker=True,
                   reviews=[{"verdict": "accept", "commits": ["bbb"]}])]
        with self.assertRaises(T.TaskError) as e:
            self.plan(ts)
        self.assertIn("T-0001", str(e.exception))
        self.assertNotIn("T-0002", str(e.exception).split("\n")[0])
        self.assertEqual(ts[0]["status"], "built")


class ReleaseFiles(unittest.TestCase):
    """commit_files / release_plan against a temp git repo (T-0196)."""

    def setUp(self):
        import subprocess
        import tempfile
        self.tmp = tempfile.mkdtemp()
        self.saved_root = T.ROOT
        T.ROOT = self.tmp
        self.sp = subprocess
        self.git("init", "-q", "-b", "main")
        self.git("config", "user.email", "t@t")
        self.git("config", "user.name", "t")
        self.git("config", "commit.gpgsign", "false")
        self.commit("README.md", "base")

    def tearDown(self):
        import shutil
        T.ROOT = self.saved_root
        shutil.rmtree(self.tmp, ignore_errors=True)

    def git(self, *a):
        return self.sp.run(["git"] + list(a), cwd=self.tmp, capture_output=True, text=True, check=True).stdout.strip()

    def commit(self, path, msg):
        import os
        full = os.path.join(self.tmp, path)
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "a") as f:
            f.write(msg + "\n")
        self.git("add", "-A")
        self.git("commit", "-q", "-m", msg)
        return self.git("rev-parse", "HEAD")

    def merge(self, path):
        self.git("checkout", "-q", "-b", "side")
        self.commit(path, "side work")
        self.git("checkout", "-q", "main")
        self.commit("docs/other.md", "main moves")
        self.git("merge", "-q", "--no-ff", "-m", "merge side", "side")
        return self.git("rev-parse", "HEAD")

    def test_plain_commit_and_paths_with_spaces(self):
        sha = self.commit("src/My Mod/A File.cs", "spaced")
        self.assertEqual(T.commit_files(sha), ["src/My Mod/A File.cs"])
        self.assertTrue(T.touches_plugin([sha]))

    def test_merge_counts_against_its_first_parent(self):
        sha = self.merge("src/Core/X.cs")
        self.assertEqual(T.commit_files(sha), ["src/Core/X.cs"])
        self.assertTrue(T.touches_plugin([sha]))

    def test_merge_of_scripts_only_is_not_a_plugin_change(self):
        sha = self.merge("scripts/x.py")
        self.assertEqual(T.commit_files(sha), ["scripts/x.py"])
        self.assertFalse(T.touches_plugin([sha]))

    def test_unknown_commit_counts_as_plugin(self):
        self.assertIsNone(T.commit_files("0" * 40))
        self.assertTrue(T.touches_plugin(["0" * 40]))

    def test_release_plan_skips_script_only_tasks_like_needs_release(self):
        ts = [task("T-0001", area="plugin", status="built", commits=["aaa"]),
              task("T-0002", area="plugin", status="built", commits=["bbb"])]
        files = {"aaa": ["scripts/x.py"], "bbb": ["src/A.cs"]}
        plan = T.release_plan(ts, "0.24.9", in_head=lambda c: True, tag_of=lambda cs: None, files_of=files.get)
        self.assertEqual([t["id"] for t, r in plan], ["T-0002"])

    def test_release_plan_counts_a_real_merge(self):
        sha = self.merge("src/Core/X.cs")
        ts = [task("T-0001", area="plugin", status="built", commits=[sha])]
        plan = T.release_plan(ts, "0.24.9", tag_of=lambda cs: None)
        self.assertEqual([t["id"] for t, r in plan], ["T-0001"])


class QaTodo(unittest.TestCase):
    """The #qa-todo-list message, rendered from the tasks (docs/harness.md 6d)."""

    def test_only_open_tester_items_with_qa_lines_in_priority_order(self):
        ts = [task("T-0001", needs="tester", qa="maks: do b - see c: link2", priority=3),
              task("T-0002", needs="tester", qa="Anyone: do a - see b: link1", priority=2),
              task("T-0003", needs="tester", qa="old", status="confirmed", evidence=[{"by": "x", "what": "y"}]),
              task("T-0004", needs="tester", qa="paused", status="blocked", notes="waits on Tom"),
              task("T-0005", needs="bridge", qa="a session checks it")]
        md = T.qa_todo(ts, "v0.24.9", day="2026-10-07")
        self.assertEqual(md, "**ForestOverlay - QA to-do** (2026-10-07, v0.24.9)\n\n**Please test**\n"
                             "- Anyone: do a - see b: link1\n- maks: do b - see c: link2")

    def test_nothing_to_test(self):
        self.assertIn("Nothing to test right now.", T.qa_todo([], "v1", day="d"))

    def test_open_tester_task_needs_a_qa_line(self):
        with self.assertRaises(T.TaskError) as e:
            T.validate([task("T-0001", needs="tester")])
        self.assertIn("--qa", str(e.exception))
        T.validate([task("T-0001", needs="tester", status="blocked", notes="paused")])
        T.validate([task("T-0001", needs="tester", qa="Anyone: x - y: link")])


WRITER = """
import sys, time
sys.path.insert(0, %r)
import tasks as T
path, tid = sys.argv[1], sys.argv[2]
for i in range(8):
    with T.locked(path):
        ts = T.load(path)
        time.sleep(0.005)  # widens the read-modify-write window
        ts.append(%s)
        T.save(ts, path, None)
"""


class Locking(unittest.TestCase):
    def test_two_writer_processes_both_keep_every_change(self):
        here = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
        # one writer owns the T-1xxx ids, the other T-2xxx
        code = WRITER % (here, "{'id': 'T-%d%03d' % (int(tid), i), 'title': 'x', 'area': 'site', 'priority': 3,"
                         " 'status': 'todo', 'needs': 'none', 'behavior': 'b', 'verify': ['v'], 'checker': False,"
                         " 'commits': [], 'evidence': [], 'blocked_by': [], 'log': []}")
        for _ in range(3):  # a few rounds: a race is luck, not a certainty
            with tempfile.TemporaryDirectory() as d:
                path = os.path.join(d, "tasks.jsonl")
                procs = [subprocess.Popen([sys.executable, "-c", code, path, str(n)]) for n in (1, 2)]
                self.assertEqual([p.wait() for p in procs], [0, 0])
                self.assertEqual(len(T.load(path)), 16)  # 2 writers x 8 tasks, none lost
                self.assertFalse(os.path.exists(path + ".lock"))

    def test_a_held_lock_times_out_with_what_why_fix(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "tasks.jsonl")
            with T.locked(path):
                with self.assertRaises(T.TaskError) as e:
                    with T.locked(path, timeout=0.1):
                        pass
            self.assertIn("FIX:", str(e.exception))
            self.assertFalse(os.path.exists(path + ".lock"))

    def test_a_stale_lock_is_taken_over(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "tasks.jsonl")
            with open(path + ".lock", "w") as f:
                f.write("99999")
            old = os.path.getmtime(path + ".lock") - 120
            os.utime(path + ".lock", (old, old))
            with T.locked(path, timeout=1):
                pass
            self.assertFalse(os.path.exists(path + ".lock"))

    def test_a_fresh_lock_found_during_takeover_is_kept(self):
        # the lock looked stale when we checked, but a waiter replaced it before the rename
        with tempfile.TemporaryDirectory() as d:
            lock = os.path.join(d, "tasks.jsonl.lock")
            with open(lock, "w") as f:
                f.write("4242")  # fresh mtime
            T._take_over_stale(lock, 60)
            self.assertTrue(os.path.exists(lock))
            self.assertEqual(open(lock).read(), "4242")
            self.assertEqual(os.listdir(d), ["tasks.jsonl.lock"])  # no renamed leftovers

    def test_takeover_removes_a_really_stale_lock_and_leaves_no_leftovers(self):
        with tempfile.TemporaryDirectory() as d:
            lock = os.path.join(d, "tasks.jsonl.lock")
            with open(lock, "w") as f:
                f.write("1")
            os.utime(lock, (1, 1))
            T._take_over_stale(lock, 60)
            self.assertEqual(os.listdir(d), [])
            T._take_over_stale(lock, 60)  # already gone: no error

    def test_a_lock_that_cannot_be_made_is_a_task_error_not_an_oserror(self):
        with tempfile.TemporaryDirectory() as d:
            blocker = os.path.join(d, "file")
            open(blocker, "w").close()
            path = os.path.join(blocker, "sub", "tasks.jsonl")  # a folder cannot be made under a file
            with self.assertRaises(T.TaskError) as e:
                with T.locked(path, timeout=0.1):
                    pass
            for part in ("ERROR", "WHY", "FIX"):
                self.assertIn(part + ":", str(e.exception))
            path = os.path.join(d, "ghost", "tasks.jsonl")
            os.makedirs(os.path.dirname(path))
            os.chmod(os.path.dirname(path), 0o500)  # no write: the lock file cannot be created
            try:
                if os.access(os.path.dirname(path), os.W_OK):
                    return  # running as root: permissions do not apply
                with self.assertRaises(T.TaskError) as e:
                    with T.locked(path, timeout=0.1):
                        pass
                self.assertIn("FIX:", str(e.exception))
            finally:
                os.chmod(os.path.dirname(path), 0o700)


READER = """
import sys, time
sys.path.insert(0, %r)
import tasks as T
path = sys.argv[1]
end = time.time() + float(sys.argv[2])
n = 0
while time.time() < end:
    T.load(path)  # parse() raises on a half-written line
    n += 1
print(n)
"""


class AtomicSave(unittest.TestCase):
    def test_a_reader_never_sees_a_half_written_file(self):
        here = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "tasks.jsonl")
            ts = [task("T-%04d" % i, title="x" * 400) for i in range(1, 300)]
            T.save(ts, path, None)
            reader = subprocess.Popen([sys.executable, "-c", READER % here, path, "1.5"],
                                      stdout=subprocess.PIPE)
            for i in range(40):
                ts[0]["title"] = "y" * (i + 1)
                T.save(ts, path, None)
            out, _ = reader.communicate()
            self.assertEqual(reader.returncode, 0)
            self.assertGreater(int(out), 0)
            self.assertEqual(os.listdir(d), ["tasks.jsonl"])  # no temp files left

    def test_a_failed_write_leaves_the_old_file_and_no_temp(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "tasks.jsonl")
            T.save([task("T-0001")], path, None)
            with open(path, encoding="utf-8") as f:
                before = f.read()
            with self.assertRaises(T.TaskError):
                T.save([task("T-0001"), task("T-0001")], path, None)  # duplicate id: validate refuses
            with open(path, encoding="utf-8") as f:
                self.assertEqual(f.read(), before)
            self.assertEqual(os.listdir(d), ["tasks.jsonl"])

    def test_replace_is_retried_on_a_permission_error(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "f.txt")
            real, calls = os.replace, []

            def flaky(a, b):
                calls.append(1)
                if len(calls) < 3:
                    raise PermissionError("in use")
                real(a, b)
            os.replace = flaky
            try:
                T.atomic_write(path, "hi\n")
            finally:
                os.replace = real
            self.assertEqual(len(calls), 3)
            self.assertEqual(open(path).read(), "hi\n")
            self.assertEqual(os.listdir(d), ["f.txt"])


if __name__ == "__main__":
    unittest.main()
