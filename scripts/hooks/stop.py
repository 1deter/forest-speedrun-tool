"""Stop hook: what is unfinished, shown to the model before it stops (docs/harness.md 7b).

.claude/settings.json runs it when a turn ends. It lists:
- uncommitted changes, and commits not pushed,
- a csproj version with no tag, or a tag not pushed (a bump left half-done),
- tasks in progress with no running note (tasks.py note) for the next session,
- a changelog line not yet pushed that claims a number with no measurement (gotcha 44).

With anything to say it blocks the stop once: the model reads the list and
finishes or says why it stays (waiting on the author is a fine reason).
The second time (stop_hook_active) it only shows the list to the author, so
it can never loop. Any error means silence - the hook must not get in the way.
"""
import json
import os
import re
import subprocess
import sys

CLAIM = re.compile(r"\d+(\.\d+)?\s*(%|x\b|times\b|ms\b|fps\b|[KMG]B\b)|\bless garbage\b", re.I)


def git(args, cwd, timeout=10):
    r = subprocess.run(["git"] + args, cwd=cwd, capture_output=True, text=True, encoding="utf-8",
                       errors="replace", timeout=timeout)
    if r.returncode != 0:
        raise RuntimeError(r.stderr.strip())
    return r.stdout


def try_git(args, cwd, timeout=10):
    try:
        return git(args, cwd, timeout)
    except Exception:
        return None


def csproj_version(root):
    try:
        with open(os.path.join(root, "ForestOverlay.csproj"), encoding="utf-8-sig") as f:
            m = re.search(r"<Version>([^<]+)</Version>", f.read())
        return m.group(1).strip() if m else None
    except OSError:
        return None


def unnoted_tasks(root):
    """In-progress tasks without a tasks/notes/<id>.md for the next session."""
    path = os.path.join(root, "tasks", "tasks.jsonl")
    out = []
    try:
        with open(path, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                t = json.loads(line)
                if t.get("status") == "in-progress" and \
                        not os.path.exists(os.path.join(root, "tasks", "notes", t["id"] + ".md")):
                    out.append(t["id"])
    except (OSError, ValueError):
        pass
    return out


def claims(diff):
    """Added changelog lines that carry a number like a measurement, unless they say it was measured."""
    out = []
    for line in diff.splitlines():
        if line.startswith("+") and not line.startswith("+++"):
            text = line[1:].strip()
            if CLAIM.search(text) and "measured" not in text.lower():
                out.append(text)
    return out


def findings(root, status, ahead, version, local_tag, remote_tag, tasks, claim_lines):
    """The list shown to the model; every input is already fetched, so it is testable."""
    out = []
    if status:
        n = len(status)
        out.append("%d uncommitted change(s) (%s%s) - commit them or say why they stay"
                   % (n, ", ".join(s[3:] for s in status[:4]), ", ..." if n > 4 else ""))
    if ahead:
        out.append("%d commit(s) not pushed - git push (memory: keep the repo in sync)" % ahead)
    if version and not local_tag:
        out.append("ForestOverlay.csproj is at %s but there is no tag v%s - finish the release (skill `release`)"
                   % (version, version))
    elif version and local_tag and remote_tag is False:
        out.append("tag v%s is not pushed - git push origin v%s (CI publishes the DLL from it)" % (version, version))
    for tid in tasks:
        out.append("%s is in progress with no running note - python scripts/tasks.py note %s \"where it stands\""
                   % (tid, tid))
    for c in claim_lines:
        out.append("changelog claims a number with no measurement (gotcha 44): \"%s\" - measure the released build, "
                   "or say what changed without the figure" % c[:120])
    return out


def collect(cwd):
    root = (try_git(["rev-parse", "--show-toplevel"], cwd) or "").strip()
    if not root:
        return []
    status = [s for s in (try_git(["status", "--porcelain"], root) or "").splitlines() if s.strip()]
    upstream = (try_git(["rev-parse", "--abbrev-ref", "@{u}"], root) or "").strip()
    ahead = int((try_git(["rev-list", "--count", "@{u}..HEAD"], root) or "0").strip() or 0) if upstream else 0
    version = csproj_version(root)
    local_tag = remote_tag = None
    if version:
        local_tag = bool((try_git(["tag", "-l", "v" + version], root) or "").strip())
        if local_tag:
            out = try_git(["ls-remote", "--tags", "origin", "refs/tags/v" + version], root, timeout=8)
            remote_tag = None if out is None else bool(out.strip())
    diff = try_git(["diff", upstream or "HEAD", "--", "CHANGELOG.md"], root) or ""
    return findings(root, status, ahead, version, local_tag, remote_tag, unnoted_tasks(root), claims(diff))


def main():
    try:
        payload = json.load(sys.stdin)
        items = collect(payload.get("cwd") or os.getcwd())
    except Exception:
        return 0
    if not items:
        return 0
    text = "Unfinished (Stop hook, docs/harness.md 7b):\n- " + "\n- ".join(items)
    if payload.get("stop_hook_active"):
        print(json.dumps({"systemMessage": text}))
    else:
        print(json.dumps({"decision": "block",
                          "reason": text + "\nFinish these, or end the turn saying why each one stays."}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
