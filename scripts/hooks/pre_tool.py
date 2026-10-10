"""PreToolUse hook: the dangerous commands, refused with the rule's reason (docs/harness.md 5b).

.claude/settings.json runs it before every Bash / PowerShell / WebFetch call
with the call as JSON on stdin. It answers with a permission decision:

- refuse a request to api.github.com (router rule 4),
- refuse a forced push to main,
- (a hand deploy into the game no longer asks: author, 2026-10-10;
  while a loop run is open (tasks/loop.jsonl) any ask is a refusal so it
  never waits on the author - Stage A, docs/harness.md 12),
- refuse a search over a whole drive, the filesystem root or the home folder
  (find / ls -R / grep -r / rg / Get-ChildItem -Recurse / dir /s / where /r /
  a Python walk, deeper than 2) and name where the files are (gotcha 99),
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


def ps_round_trip(command, tool="PowerShell"):
    # In Bash the words only run inside a powershell / pwsh call (not in a heredoc's text).
    if tool != "PowerShell" and not re.search(r"\b(powershell|pwsh)\b", command, re.I):
        return None
    if re.search(r"Get-Content\b[^;\n]*\|[^;\n]*\b(Set-Content|Out-File)\b", command, re.I):
        return ("warn", "Router rule 8 / gotcha 9: Get-Content | Set-Content in PowerShell 5.1 reads BOM-less UTF-8 "
                        "as cp1252 and garbles every em dash. Edit with the Edit tool or Python with an explicit "
                        "encoding instead.")
    return None


# A search over a whole drive or the home folder: an agent that did not know
# where a file lives ran `find / -path ...` (2026-10-07, T-0150's in-game
# check); it timed out into the background and crawled every drive for 22 min
# after the agent had finished. The refusal names where the files are.
HEREDOC = re.compile(r"<<-?\s*(['\"]?)(\w+)\1[^\n]*\n.*?\n\s*\2\b", re.S)
QUOTED = re.compile(r"\"(?:\\.|[^\"\\])*\"|'[^']*'")
SEGMENT = re.compile(r"\|\||&&|[;|\n]|\$\(|`")
SEARCHERS = re.compile(r"^(?:(?:sudo|xargs|exec|nohup|nice|time|command|env|timeout(?:\s+-\S+(?:\s+\d\S*)?)*\s+\S+)\s+|&\s*)*(find|ls|grep|egrep|rg|tree|du|Get-ChildItem|gci|dir|where(?:\.exe)?)(?=\s|$)(.*)$",
                       re.I | re.S)
SHALLOW = 2  # -maxdepth / -Depth up to this is a look, not a crawl


def home_dir():
    return norm(os.path.expanduser("~"))


def wide_path(token, home):
    """True for the filesystem root, a drive root, the home folder or C:/Users."""
    t = token.strip().strip("'\"")
    if not t.strip("\\") or t.startswith("-"):  # `\` of a `-exec ... \;`
        return False
    t = re.sub(r"^(\$\{?HOME\}?|\$env:USERPROFILE|%USERPROFILE%|~)(?=$|[/\\])", lambda m: home, t, flags=re.I)
    if t == "/":
        return True
    if re.fullmatch(r"/[a-zA-Z]/?", t):  # Git Bash's /c
        return True
    n = norm(t)
    return n == "" or re.fullmatch(r"[a-z]:", n) is not None or n == home or n == "c:/users"


def depth_of(args):
    m = re.search(r"-(?:max)?depth\s+(\d+)", args, re.I)
    return int(m.group(1)) if m else None


def known_places(root):
    home = os.path.expanduser("~")
    saves = os.path.join(home, "AppData", "LocalLow", "SKS", "TheForest")
    try:
        ids = [d for d in os.listdir(saves) if d.isdigit()]
        if ids:
            saves = os.path.join(saves, ids[0])
    except OSError:
        pass
    game = root or r"G:\SteamLibrary\steamapps\common\The Forest"
    return ("the game (FOREST_ROOT) %s; the plugin's files (segments, runs, savestates, uploads, logs) %s; "
            "the BepInEx log %s; the saves %s; testers' report zips %s; the repo %s" %
            (game, os.path.join(game, "BepInEx", "config", "ForestOverlay"), os.path.join(game, "BepInEx", "LogOutput.log"),
             os.path.join(saves, "SinglePlayer", "SlotN"), os.path.join(home, "Downloads", "qa-reports", "<tester>"),
             os.environ.get("CLAUDE_PROJECT_DIR") or os.getcwd()))


def wide_search(command, root, home=None):
    home = home if home is not None else home_dir()
    text = HEREDOC.sub("", command)
    # Quoted text out first: a `|` or `;` inside a grep pattern or a task's
    # text is not a new command ("... ; find the work" in a --behavior).
    quoted = []

    def keep(m):
        quoted.append(m.group(0))
        return " \x00%d\x00 " % (len(quoted) - 1)
    masked = QUOTED.sub(keep, text)
    for seg in SEGMENT.split(masked):
        m = SEARCHERS.match(seg.strip())
        if not m:
            continue
        word, args = m.group(1).lower(), m.group(2)
        if word in ("ls", "tree") and not re.search(r"(^|\s)-[a-zA-Z]*R", args):
            continue
        if word in ("grep", "egrep") and not re.search(r"(^|\s)(-[a-zA-Z]*[rR]|--recursive)", args):
            continue
        if word in ("get-childitem", "gci", "dir") and not re.search(r"-r(ecurse)?\b|(^|\s)/s\b", args, re.I):
            continue
        if word in ("where", "where.exe") and not re.search(r"(^|\s)/r\b", args, re.I):
            continue
        d = depth_of(args)
        if d is not None and d <= SHALLOW:
            continue
        tokens = [re.sub(r"\x00(\d+)\x00", lambda q: quoted[int(q.group(1))], t) for t in args.split()]
        hit = next((t for t in tokens if wide_path(t, home)), None)
        if hit:
            return ("deny", "A search from %s walks a whole drive or the home folder: one (`find / -path ...`, "
                            "2026-10-07) timed out into the background and crawled every drive for 22 min after "
                            "the agent had finished. Search where the file lives - %s - or cap it with "
                            "-maxdepth / -Depth %d." % (hit.strip("'\""), known_places(root), SHALLOW))
    # A Python walk usually sits in a heredoc: the whole command.
    m = re.search(r"os\.walk\(\s*(['\"])(.*?)\1", command) or re.search(r"glob\(\s*r?(['\"])(.*?)\*\*", command)
    if m and m.group(2) and wide_path(m.group(2).rstrip("/\\") or "/", home):
        return ("deny", "A Python walk from %s covers a whole drive or the home folder (the 2026-10-07 `find /` "
                        "crawled 22 min). Walk where the file lives - %s." % (m.group(2) or "/", known_places(root)))
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
    return (github_api(command) or force_push(command, cwd, branch)
            or wide_search(command, root) or ps_round_trip(command, tool))


def loop_open(path=None):
    """True while a loop run is open (tasks/loop.jsonl: a begin with no stop
    after it). Nobody is there to answer an ask then."""
    path = path or os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
                                "tasks", "loop.jsonl")
    state = False
    try:
        with open(path, encoding="utf-8") as f:
            for line in f:
                if '"begin"' in line or '"stop"' in line:
                    kind = json.loads(line).get("event")
                    if kind in ("begin", "stop"):
                        state = kind == "begin"
    except (OSError, ValueError):
        return False
    return state


def answer(decision, unattended=False):
    kind, reason = decision
    # Unattended (an open loop run), an ask would wait all night: refuse it
    # instead, with the reason, so the run parks the step and goes on.
    if kind == "ask" and unattended:
        kind, reason = "deny", reason + " (A loop run is open, so this ask is refused rather than left waiting - " \
                                         "park the step for the author.)"
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
        print(json.dumps(answer(d, d[0] == "ask" and loop_open())))
    return 0


if __name__ == "__main__":
    sys.exit(main())
