"""PreToolUse hook: the dangerous commands, refused with the rule's reason (docs/harness.md 5b).

.claude/settings.json runs it before every Bash / PowerShell / WebFetch call
with the call as JSON on stdin. It answers with a permission decision:

- refuse a request to api.github.com (router rule 4),
- refuse a forced push to main,
- ask before a DLL deploy into the author's game install (router rule 2;
  author, 2026-10-07: "just ask me"). Bridge tests install releases with
  update_game and never come here; an unattended loop must turn this ask
  into a refusal so it never waits on the author (Stage A, docs/harness.md 12),
- warn on Get-Content | Set-Content (router rule 8, gotcha 9).

Anything else passes silently. A crash here must never block work, so
every error falls through to "no opinion".
"""
import json
import os
import re
import subprocess
import sys

FETCHERS = re.compile(r"\b(curl|wget|Invoke-WebRequest|Invoke-RestMethod|iwr|irm|urllib|requests\.|http\.client|"
                      r"WebClient|HttpClient|fetch\()", re.I)
COPIERS = re.compile(r"\b(cp|copy|Copy-Item|xcopy|robocopy|mv|move|Move-Item|shutil\.copy\w*|install)\b|>", re.I)


def install_root():
    """The author's game install: FOREST_ROOT (User scope on this machine, so read the registry too)."""
    root = os.environ.get("FOREST_ROOT")
    if not root and sys.platform == "win32":
        try:
            import winreg
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k:
                root = winreg.QueryValueEx(k, "FOREST_ROOT")[0]
        except OSError:
            root = None
    return root


def norm(path):
    """Lower case, forward slashes, Git Bash's /g/... as g:/..., no trailing slash."""
    p = path.strip().strip("'\"").replace("\\", "/").lower()
    m = re.match(r"^/([a-z])/(.*)$", p)
    if m:
        p = "%s:/%s" % (m.group(1), m.group(2))
    return p.rstrip("/")


def mentions(command, root):
    if not root:
        return False
    text = command.replace("\\", "/").lower()
    text = re.sub(r"(^|[\s\"'=])/([a-z])/", r"\1\2:/", text)  # Git Bash's /g/... inside the command
    return norm(root) in text


# ---------------------------------------------------------------- the rules

def github_api(command):
    if "api.github.com" in command.lower() and FETCHERS.search(command):
        return ("deny", "Router rule 4: never poll api.github.com - it allows 60 calls an hour per IP, shared with "
                        "the author's game (its update check was locked out once). Poll the release asset URL instead: "
                        "curl -s -o /dev/null -w '%{http_code}' -L "
                        "https://github.com/1deter/forest-speedrun-tool/releases/download/vX.Y.Z/ForestOverlay.dll "
                        "(or `gh`, which is authenticated).")
    return None


def push_targets(command):
    """Each `git push` in the command: (forced, [refspecs])."""
    out = []
    for m in re.finditer(r"\bgit\s+push\b([^;&|\n]*)", command):
        args = m.group(1).split()
        forced = any(a in ("-f", "--force", "--force-with-lease", "--force-if-includes") or
                     a.startswith("--force-with-lease=") or (re.match(r"^-[a-zA-Z]*f[a-zA-Z]*$", a) is not None)
                     for a in args)
        pos = [a for a in args if not a.startswith("-")]
        refspecs = pos[1:]  # pos[0] is the remote
        forced = forced or any(r.startswith("+") for r in refspecs)
        out.append((forced, refspecs))
    return out


def current_branch(cwd):
    try:
        return subprocess.run(["git", "rev-parse", "--abbrev-ref", "HEAD"], cwd=cwd or None,
                              capture_output=True, text=True, timeout=5).stdout.strip()
    except Exception:
        return ""


def force_push(command, cwd, branch=None):
    for forced, refspecs in push_targets(command):
        if not forced:
            continue
        if refspecs:
            hits_main = any(re.search(r"(^\+?|:)(refs/heads/)?main$", r) for r in refspecs)
        else:
            hits_main = (branch if branch is not None else current_branch(cwd)) == "main"
        if hits_main:
            return ("deny", "A forced push to main rewrites the history every other session, worktree and CI run "
                            "builds on (docs/harness.md 5b). Pull and merge instead (git pull --no-rebase, "
                            "scripts/merge-keepboth.py for add/add conflicts), or revert with a new commit.")
    return None


def game_root_arg(command):
    m = re.search(r"-GameRoot\s+(\"[^\"]*\"|'[^']*'|\S+)", command, re.I)
    return m.group(1) if m else None


def dll_deploy(command, root):
    reason = ("Router rule 2: never deploy a DLL into the author's game install by hand - it updates through the "
              "real release path (update_game installs a release). Approve only if the author asked for this deploy.")
    if re.search(r"deploy\.ps1", command, re.I):
        arg = game_root_arg(command)
        if arg is None or "forest_root" in arg.lower() or (root and norm(arg).startswith(norm(root))):
            return ("ask", reason)
        return None
    # Reading the install's logs is fine; writing into its plugin / patcher folders is a deploy.
    if mentions(command, root) and re.search(r"bepinex[/\\]+(plugins|patchers)", command, re.I) \
            and COPIERS.search(command):
        return ("ask", reason)
    return None


def ps_round_trip(command, tool="PowerShell"):
    # In Bash the words only run inside a powershell / pwsh call (not in a heredoc's text).
    if tool != "PowerShell" and not re.search(r"\b(powershell|pwsh)\b", command, re.I):
        return None
    if re.search(r"Get-Content\b[^;\n]*\|[^;\n]*\b(Set-Content|Out-File)\b", command, re.I):
        return ("warn", "Router rule 8 / gotcha 9: Get-Content | Set-Content in PowerShell 5.1 reads BOM-less UTF-8 "
                        "as cp1252 and garbles every em dash. Edit with the Edit tool or Python with an explicit "
                        "encoding instead.")
    return None


def decide(payload, root=None, branch=None):
    """(decision, reason) or None. decision: deny | ask | warn."""
    tool = payload.get("tool_name", "")
    inp = payload.get("tool_input") or {}
    if tool == "WebFetch":
        if "api.github.com" in (inp.get("url") or "").lower():
            return github_api("curl " + inp["url"])
        return None
    command = inp.get("command") or ""
    if not command:
        return None
    cwd = payload.get("cwd")
    return (github_api(command) or force_push(command, cwd, branch) or dll_deploy(command, root)
            or ps_round_trip(command, tool))


def answer(decision):
    kind, reason = decision
    if kind == "warn":
        return {"systemMessage": "Warning: " + reason,
                "hookSpecificOutput": {"hookEventName": "PreToolUse", "additionalContext": "Warning: " + reason}}
    return {"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": kind,
                                   "permissionDecisionReason": reason}}


def main():
    try:
        payload = json.load(sys.stdin)
        d = decide(payload, install_root())
    except Exception:
        return 0
    if d:
        print(json.dumps(answer(d)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
