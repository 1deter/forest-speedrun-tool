"""Tests for the Claude Code hooks in scripts/hooks/: each blocked command is
refused with its rule's reason, and the Stop hook reports unfinished work.

    python scripts/tests/test_hooks.py
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "hooks"))
import pre_tool as P  # noqa: E402
import stop as S  # noqa: E402

ROOT = r"G:\SteamLibrary\steamapps\common\The Forest"


def bash(command):
    return {"tool_name": "Bash", "tool_input": {"command": command}, "cwd": "."}


def decide(command, branch="feature"):
    return P.decide(bash(command), root=ROOT, branch=branch)


class GithubApi(unittest.TestCase):
    def test_fetching_is_refused_with_the_rule(self):
        for cmd in ("curl -s https://api.github.com/repos/1deter/forest-speedrun-tool/releases/latest",
                    "Invoke-RestMethod https://api.github.com/repos/x/y",
                    "python -c \"import urllib.request; urllib.request.urlopen('https://api.github.com/x')\""):
            kind, reason = decide(cmd)
            self.assertEqual(kind, "deny", cmd)
            self.assertIn("Router rule 4", reason)
            self.assertIn("releases/download", reason)

    def test_webfetch_is_refused(self):
        d = P.decide({"tool_name": "WebFetch", "tool_input": {"url": "https://api.github.com/repos/x"}})
        self.assertEqual(d[0], "deny")

    def test_reading_about_it_passes(self):
        self.assertIsNone(decide('git grep -n "api.github.com" -- scripts'))
        self.assertIsNone(decide("gh run list --limit 3"))
        self.assertIsNone(P.decide({"tool_name": "WebFetch", "tool_input": {"url": "https://github.com/x"}}))


class ForcePush(unittest.TestCase):
    def test_forced_push_to_main_is_refused(self):
        for cmd in ("git push --force origin main", "git push -f origin main", "git push origin +main",
                    "git push --force-with-lease origin HEAD:main", "git fetch && git push -f origin main"):
            kind, reason = decide(cmd)
            self.assertEqual(kind, "deny", cmd)
            self.assertIn("rewrites the history", reason)

    def test_forced_push_with_no_refspec_on_main(self):
        self.assertEqual(decide("git push --force", branch="main")[0], "deny")
        self.assertIsNone(decide("git push --force", branch="ui-redesign"))

    def test_normal_pushes_pass(self):
        for cmd in ("git push", "git push origin main", "git push origin v0.24.249", "git push -u origin topic",
                    "git push --force origin ui-redesign", "git push --follow-tags origin main"):
            self.assertIsNone(decide(cmd, branch="main"), cmd)


class Deploy(unittest.TestCase):
    def test_deploy_into_the_install_asks(self):
        for cmd in ("./scripts/deploy.ps1",
                    "./scripts/deploy.ps1 -GameRoot $env:FOREST_ROOT",
                    './scripts/deploy.ps1 -GameRoot "G:\\SteamLibrary\\steamapps\\common\\The Forest"',
                    'cp bin/Release/net35/ForestOverlay.dll "/g/SteamLibrary/steamapps/common/The Forest/BepInEx/plugins/"',
                    'Copy-Item bin\\Release\\net35\\ForestOverlay.dll "G:\\SteamLibrary\\steamapps\\common\\The Forest\\BepInEx\\plugins"'):
            kind, reason = decide(cmd)
            self.assertEqual(kind, "ask", cmd)
            self.assertIn("Router rule 2", reason)

    def test_deploy_name_in_quoted_text_passes(self):
        self.assertIsNone(decide('grep -rn -E "branch build|deploy.ps1" docs/areas/release.md'))
        self.assertIsNone(decide("git commit -m 'deploy.ps1: a note'"))
        kind, _ = decide('& "./scripts/deploy.ps1"')
        self.assertEqual(kind, "ask")

    def test_an_open_loop_run_refuses_the_ask(self):
        d = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, d)
        p = os.path.join(d, "loop.jsonl")
        with open(p, "w", encoding="utf-8") as f:
            f.write('{"event": "begin", "run": "R-1"}\n')
        self.assertTrue(P.loop_open(p))
        with open(p, "a", encoding="utf-8") as f:
            f.write('{"event": "stop", "run": "R-1"}\n')
        self.assertFalse(P.loop_open(p))
        self.assertFalse(P.loop_open(os.path.join(d, "none.jsonl")))
        out = P.answer(("ask", "why"), unattended=True)["hookSpecificOutput"]
        self.assertEqual(out["permissionDecision"], "deny")
        self.assertIn("loop run is open", out["permissionDecisionReason"])
        self.assertEqual(P.answer(("ask", "why"))["hookSpecificOutput"]["permissionDecision"], "ask")

    def test_test_install_and_reading_logs_pass(self):
        self.assertIsNone(decide('./scripts/deploy.ps1 -GameRoot "D:\\ForestTest"'))
        self.assertIsNone(decide('cat "/g/SteamLibrary/steamapps/common/The Forest/BepInEx/LogOutput.log"'))
        self.assertIsNone(decide('ls "G:\\SteamLibrary\\steamapps\\common\\The Forest\\BepInEx\\plugins"'))


class PowerShell(unittest.TestCase):
    def test_round_trip_warns(self):
        d = P.decide({"tool_name": "PowerShell", "tool_input": {"command": "Get-Content a.md | Set-Content a.md"}})
        self.assertEqual(d[0], "warn")
        self.assertIsNone(decide("python - <<'EOF'\nnote = 'Get-Content | Set-Content'\nEOF"))
        kind, reason = decide('powershell -c "Get-Content a.md | Set-Content a.md"')
        self.assertEqual(kind, "warn")
        self.assertIn("gotcha 9", reason)
        out = P.answer((kind, reason))
        self.assertNotIn("permissionDecision", out["hookSpecificOutput"])

    def test_plain_read_passes(self):
        self.assertIsNone(decide("Get-Content a.md -TotalCount 5"))


class WideSearch(unittest.TestCase):
    HOME = "c:/users/deter"

    def wide(self, cmd):
        return P.wide_search(cmd, ROOT, home=self.HOME)

    def test_a_crawl_of_a_drive_or_the_home_folder_is_refused_with_the_places(self):
        for cmd in ('python scripts/tasks.py show T-0150 | head -40; find / -path "*BepInEx/config/x.txt" 2>/dev/null',
                    "find /c -name x", "find /g/ -iname '*.foseg'", "find ~ -name x", "find $HOME -name x",
                    'find /c/Users/deter -maxdepth 4 -iname "quality-document*"', "ls -R /", "grep -rn Slot1 ~",
                    'Get-ChildItem C:\\ -Recurse -Filter my-segments.txt', "gci -r $env:USERPROFILE -Filter *.run",
                    "Get-ChildItem -Path G:\\ -Recurse", "dir /s C:\\", "where /r C:\\ ForestOverlay.dll",
                    "rg --files / | grep x", "cd x && find \"/\" -name y", "timeout 5 find / -name x",
                    "timeout -k 2 30 find /c -name x", "nohup find ~ -name x &", "xargs find / -name",
                    "python - <<'EOF'\nimport os\nfor d, _, f in os.walk('/'): pass\nEOF"):
            d = self.wide(cmd)
            self.assertIsNotNone(d, cmd)
            self.assertEqual(d[0], "deny", cmd)
            self.assertIn("22 min", d[1])
            self.assertIn("ForestOverlay", d[1])  # where the plugin's files are
        self.assertEqual(decide("find / -name x")[0], "deny")

    def test_searches_where_the_file_lives_pass(self):
        for cmd in ("find . -name x", "find src -name '*.cs'", "find ~/Downloads/qa-reports -name '*.zip'",
                    "find / -maxdepth 0", "find /c/Users/deter -maxdepth 2 -name x", "Get-ChildItem C:\\",
                    "find /c/Users/deter/AppData/Local/Temp -maxdepth 6 -iname x",
                    "Get-ChildItem (Join-Path $root 'BepInEx\\plugins') -Recurse -File", "gci -r ~ -Depth 1",
                    'find "/g/SteamLibrary/steamapps/common/The Forest/BepInEx/config/ForestOverlay" -name x',
                    "grep -rn x src", "grep -n x / 2>/dev/null", "ls ~", "find . -name x -exec cat {} \\;",
                    # quoted text is not a command
                    'python scripts/tasks.py add "t" --behavior "lines; find / anything | find ~ else"',
                    'grep -rn "a\\|b" --include=*.cs . | grep -v x',
                    "cat > f.md <<'EOF'\nfind / inspect / get\nEOF",
                    "Get-Process | Where-Object { $_.Name -eq 'find' }",
                    "python -c \"import glob; glob.glob('**/*.cs', recursive=True)\""):
            self.assertIsNone(self.wide(cmd), cmd)


class HookProcess(unittest.TestCase):
    def run_hook(self, script, payload):
        p = subprocess.run([sys.executable, os.path.join(HERE, "..", "hooks", script)],
                           input=json.dumps(payload), capture_output=True, text=True, timeout=60)
        self.assertEqual(p.returncode, 0, p.stderr)
        return json.loads(p.stdout) if p.stdout.strip() else None

    def test_pre_tool_answers_json(self):
        out = self.run_hook("pre_tool.py", bash("curl https://api.github.com/x"))
        self.assertEqual(out["hookSpecificOutput"]["permissionDecision"], "deny")
        self.assertIsNone(self.run_hook("pre_tool.py", bash("ls")))

    def test_bad_input_is_silent(self):
        p = subprocess.run([sys.executable, os.path.join(HERE, "..", "hooks", "pre_tool.py")],
                           input="not json", capture_output=True, text=True, timeout=60)
        self.assertEqual((p.returncode, p.stdout), (0, ""))

    def test_stop_on_a_dirty_tree_blocks_once(self):
        d = tempfile.mkdtemp()
        try:
            g = lambda *a: subprocess.run(["git"] + list(a), cwd=d, capture_output=True, check=True)
            g("init", "-q")
            g("config", "user.email", "t@t")
            g("config", "user.name", "t")
            with open(os.path.join(d, "a.txt"), "w") as f:
                f.write("x")
            g("add", ".")
            g("commit", "-qm", "a")
            with open(os.path.join(d, "a.txt"), "w") as f:
                f.write("y")
            out = self.run_hook("stop.py", {"cwd": d, "stop_hook_active": False})
            self.assertEqual(out["decision"], "block")
            self.assertIn("1 uncommitted change(s) (a.txt)", out["reason"])
            again = self.run_hook("stop.py", {"cwd": d, "stop_hook_active": True})
            self.assertNotIn("decision", again)
            self.assertIn("uncommitted", again["systemMessage"])
            g("commit", "-qam", "b")
            self.assertIsNone(self.run_hook("stop.py", {"cwd": d, "stop_hook_active": False}))
        finally:
            shutil.rmtree(d, ignore_errors=True)


class StopFindings(unittest.TestCase):
    def test_each_finding(self):
        out = S.findings("r", [" M src/A.cs"], 2, "0.24.9", False, None, ["T-0005"], ["about 60% less garbage"])
        joined = "\n".join(out)
        self.assertIn("1 uncommitted change(s) (src/A.cs)", joined)
        self.assertIn("2 commit(s) not pushed", joined)
        self.assertIn("no tag v0.24.9", joined)
        self.assertIn("T-0005 is in progress with no running note", joined)
        self.assertIn("gotcha 44", joined)

    def test_tag_not_pushed(self):
        out = S.findings("r", [], 0, "0.24.9", True, False, [], [])
        self.assertEqual(out, ["tag v0.24.9 is not pushed - git push origin v0.24.9 (CI publishes the DLL from it)"])
        self.assertEqual(S.findings("r", [], 0, "0.24.9", True, True, [], []), [])
        self.assertEqual(S.findings("r", [], 0, "0.24.9", True, None, [], []), [])  # offline: no claim

    def test_claims(self):
        diff = ("+++ b/CHANGELOG.md\n+- Loads about 40% faster.\n+- Measured: 12 ms less per frame (measured A/B).\n"
                "+- Restores no longer hang.\n- - an old 50% line\n+- 3x less garbage\n")
        self.assertEqual(S.claims(diff), ["- Loads about 40% faster.", "- 3x less garbage"])

    def test_unnoted_tasks(self):
        d = tempfile.mkdtemp()
        try:
            os.makedirs(os.path.join(d, "tasks", "notes"))
            with open(os.path.join(d, "tasks", "tasks.jsonl"), "w") as f:
                f.write('{"id":"T-0001","status":"in-progress"}\n{"id":"T-0002","status":"in-progress"}\n'
                        '{"id":"T-0003","status":"todo"}\n')
            open(os.path.join(d, "tasks", "notes", "T-0002.md"), "w").close()
            self.assertEqual(S.unnoted_tasks(d), ["T-0001"])
        finally:
            shutil.rmtree(d)

    def test_unreviewed_tasks_with_unpushed_commits(self):
        d = tempfile.mkdtemp()
        try:
            os.makedirs(os.path.join(d, "tasks"))
            rows = [{"id": "T-0001", "status": "built", "checker": True, "commits": ["abc1234"]},
                    {"id": "T-0002", "status": "built", "checker": True, "commits": ["abc1234"],
                     "reviews": [{"verdict": "accept", "commits": ["abc1234"]}]},
                    {"id": "T-0003", "status": "built", "checker": True, "commits": ["fff0000"]},
                    {"id": "T-0004", "status": "built", "checker": False, "commits": ["abc1234"]},
                    {"id": "T-0005", "status": "built", "checker": True, "commits": ["abc1234", "abc9999"],
                     "reviews": [{"verdict": "accept", "commits": ["abc1234"]}]}]
            with open(os.path.join(d, "tasks", "tasks.jsonl"), "w") as f:
                f.write("\n".join(json.dumps(r) for r in rows) + "\n")
            self.assertEqual(S.unreviewed_tasks(d, ["abc1234deadbeef"]), ["T-0001", "T-0005"])
            self.assertEqual(S.unreviewed_tasks(d, []), [])
        finally:
            shutil.rmtree(d)

    def test_unreviewed_finding_names_the_checker(self):
        out = S.findings("r", [], 0, None, None, None, [], [], ["T-0009"])
        self.assertEqual(len(out), 1)
        self.assertIn("forest-checker", out[0])


if __name__ == "__main__":
    unittest.main()
