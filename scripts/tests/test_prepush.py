"""The commit / push gates that keep CI failures off main (author 2026-10-18): scripts/prepush.py
runs the workflow steps a push triggers, tasks.py check --staged stages the rendered task view.

    python scripts/tests/test_prepush.py
"""
import os
import re
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
import prepush as P  # noqa: E402
import tasks as T  # noqa: E402

WORKFLOWS = os.path.join(HERE, "..", "..", ".github", "workflows")


def names(paths):
    return [n for n, _ in P.plan(paths)]


def push_paths(workflow):
    """The `paths:` list under `on: push:` of a workflow, as plain prefixes."""
    with open(os.path.join(WORKFLOWS, workflow), encoding="utf-8") as f:
        text = f.read()
    block = re.search(r"\n  push:\n(?:    .*\n)*?    paths:\n((?:      - .*\n)+)", text).group(1)
    return [re.sub(r"\*\*$", "", re.search(r"'([^']+)'", line).group(1)) for line in block.splitlines()]


BUILD_STEPS = ["task file", "lint", "plugin build", "plugin tests", "ILScan tests", "script tests"]


def build_targets():
    """What build.yml's run lines build, test or run: project paths and script names."""
    with open(os.path.join(WORKFLOWS, "build.yml"), encoding="utf-8") as f:
        text = f.read()
    return (re.findall(r"dotnet (?:build|test) (\S+)", text)
            + re.findall(r"python3? (scripts/\S+\.py|-m unittest discover)", text))


class Plan(unittest.TestCase):
    def test_build_yml_has_no_path_filter(self):
        # if it gains one, the always-run steps below must follow it
        with open(os.path.join(WORKFLOWS, "build.yml"), encoding="utf-8") as f:
            self.assertNotIn("paths:", f.read())

    def test_every_build_yml_step_runs_on_any_push(self):
        for paths in (["tasks/tasks.jsonl"], ["community/index.json"], ["docs/x.md"]):
            self.assertEqual(names(paths)[:len(BUILD_STEPS)], BUILD_STEPS, paths)

    def test_every_build_yml_command_is_mirrored(self):
        ours = " | ".join(" ".join(c) for _, c in P.plan([]))
        targets = build_targets()
        self.assertGreaterEqual(len(targets), 6)
        for target in targets:
            self.assertIn(target, ours, "build.yml runs %r, prepush.py does not" % target)

    def test_a_task_file_push_skips_site_and_bot(self):
        got = names(["tasks/tasks.jsonl", "docs/tasks.md", "tasks/loop.jsonl"])
        self.assertNotIn("site tests", got)
        self.assertNotIn("bot tests", got)

    def test_src_data_also_runs_the_site(self):
        self.assertIn("site tests", names(["src/Data/RunReport.cs"]))

    def test_windows_separators(self):
        self.assertIn("bot tests", names(["knowledge\\cards\\x.md"]))

    def test_every_site_and_bot_workflow_path_triggers_its_step(self):
        for workflow, step in (("site.yml", "site tests"), ("bot.yml", "bot tests")):
            for prefix in push_paths(workflow):
                if prefix.startswith((".github/", "scripts/")):
                    continue   # the workflow file itself / the browser smoke, not a dotnet suite
                self.assertIn(step, names([prefix + ("x" if prefix.endswith("/") else "")]),
                              "%s path %s does not run %s" % (workflow, prefix, step))


class MainUpdates(unittest.TestCase):
    def test_only_pushes_to_main_count(self):
        a, b = "a" * 40, "b" * 40
        lines = ["refs/heads/main %s refs/heads/main %s" % (a, b),
                 "refs/tags/v1 %s refs/tags/v1 %s" % (a, P.ZERO),
                 "refs/heads/x %s refs/heads/x %s" % (a, b),
                 "(delete) %s refs/heads/main %s" % (P.ZERO, b)]
        self.assertEqual(P.main_updates(lines), [(a, b)])


class StagedTaskView(unittest.TestCase):
    def fake(self, index, staged):
        calls = []

        def git(args, stdin=None):
            calls.append(args)
            if args[:2] == ["diff", "--cached"]:
                return 0, "\n".join(staged)
            if args[0] == "show":
                path = args[1][1:]
                return (0, index[path]) if path in index else (128, "")
            if args[0] == "hash-object":
                index["blob"] = stdin.decode("utf-8")
                return 0, "f" * 40 + "\n"
            if args[0] == "update-index":
                index["docs/tasks.md"] = index["blob"]
                return 0, ""
            raise AssertionError(args)
        return git, calls

    def test_nothing_staged(self):
        git, _ = self.fake({}, ["src/x.cs"])
        self.assertEqual(T.check_staged(git), "tasks: nothing staged")

    def test_a_stale_view_is_rendered_from_the_index_and_staged(self):
        with open(T.TASKS, encoding="utf-8") as f:
            text = f.read()
        index = {"tasks/tasks.jsonl": text, "docs/tasks.md": "stale\n"}
        git, calls = self.fake(index, ["tasks/tasks.jsonl"])
        self.assertIn("re-rendered", T.check_staged(git))
        self.assertEqual(index["docs/tasks.md"], T.render(T.parse(text)))
        self.assertTrue(any(c[0] == "update-index" for c in calls))

    def test_a_matching_view_passes_untouched(self):
        with open(T.TASKS, encoding="utf-8") as f:
            text = f.read()
        view = T.render(T.parse(text)).replace("\n", "\r\n")   # CRLF in the index still matches
        git, calls = self.fake({"tasks/tasks.jsonl": text, "docs/tasks.md": view}, ["tasks/tasks.jsonl"])
        self.assertIn("ok (staged)", T.check_staged(git))
        self.assertFalse(any(c[0] == "update-index" for c in calls))


if __name__ == "__main__":
    unittest.main()
