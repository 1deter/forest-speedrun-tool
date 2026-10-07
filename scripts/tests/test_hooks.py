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


if __name__ == "__main__":
    unittest.main()
