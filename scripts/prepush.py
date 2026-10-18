"""The CI checks a push to main would fail, run before the push leaves (author 2026-10-18:
"catch this before releasing rather than GitHub catching the error and alerting me").

Called by .githooks/pre-push with the hook's stdin. For each update of refs/heads/main it
checks the pushed commit out into a throwaway worktree - the main checkout is shared with
loop sessions and usually holds someone's uncommitted edits, so the tests must see exactly
what is pushed - and runs the workflow steps the push triggers (the same commands as
.github/workflows: every build.yml step always, site.yml / bot.yml by their `paths:`).
A failure stops the push with WHAT / WHY / FIX.

    python scripts/prepush.py < <hook stdin>        # what the hook does
    python scripts/prepush.py --plan <path> ...     # which steps those paths run
"""
import os
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ZERO = "0" * 40
PY = sys.executable or "python"

# (name, path prefixes that trigger it - None = always, command). Mirrors the workflows:
# build.yml has no `paths:`, so its steps run on every push (its tests read community/,
# locations/, docs/ ... - a path filter here missed those); site.yml / bot.yml run by their
# `paths:`. scripts/tests/test_prepush.py holds these lists to the workflow files.
STEPS = [
    ("task file", None, [PY, "scripts/tasks.py", "check"]),
    ("lint", None, [PY, "scripts/lint.py"]),
    ("plugin build", None, ["dotnet", "build", "ForestOverlay.csproj", "-c", "Release", "--nologo", "-v", "q"]),
    ("plugin tests", None,
     ["dotnet", "test", "tests/ForestOverlay.Tests/ForestOverlay.Tests.csproj", "-c", "Release", "--nologo", "-v", "q"]),
    ("ILScan tests", None,
     ["dotnet", "test", "tests/ILScan.Tests/ILScan.Tests.csproj", "-c", "Release", "--nologo", "-v", "q"]),
    ("script tests", None, [PY, "-m", "unittest", "discover", "-s", "scripts/tests", "-q"]),
    ("site tests", ("site/", "community/", "src/Data/", "tests/ForestOverlay.Tests/UnityShim.cs", "nuget.config"),
     ["dotnet", "test", "site/ForestSite.Tests", "-c", "Release", "--nologo", "-v", "q"]),
    ("bot tests", ("bot/", "knowledge/", "docs/game-notes.md", "docs/savestates.md", "docs/run-mode.md",
                   "docs/fsm/", "tools/BridgeMcp/DiscordText.cs", "nuget.config"),
     ["dotnet", "test", "bot/ForestBot.Tests", "-c", "Release", "--nologo", "-v", "q"]),
]
STEP_TIMEOUT = 600


def plan(paths):
    """The steps the changed paths trigger, in run order."""
    paths = [p.replace("\\", "/") for p in paths]
    return [(name, cmd) for name, prefixes, cmd in STEPS
            if prefixes is None or any(p.startswith(prefixes) for p in paths)]


def main_updates(lines):
    """(local sha, remote sha) for each push line that updates refs/heads/main."""
    out = []
    for line in lines:
        parts = line.split()
        if len(parts) == 4 and parts[2] == "refs/heads/main" and parts[1] != ZERO:
            out.append((parts[1], parts[3]))
    return out


def git(*args):
    r = subprocess.run(["git"] + list(args), cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
                       errors="replace", stdin=subprocess.DEVNULL, timeout=60)
    return r.returncode, r.stdout.strip()


def changed(local, remote):
    base = remote
    if remote == ZERO or git("cat-file", "-e", remote + "^{commit}")[0] != 0:
        code, base = git("merge-base", "origin/main", local)
        if code != 0:
            return None   # no common base known: run everything
    code, out = git("diff", "--name-only", base, local)
    return out.splitlines() if code == 0 else None


def run_steps(sha, steps):
    tmp = tempfile.mkdtemp(prefix="forest-prepush-")
    wt = os.path.join(tmp, "wt")
    code, out = git("worktree", "add", "--detach", wt, sha)
    if code != 0:
        print("pre-push: could not check out %s into a worktree: %s" % (sha[:8], out), file=sys.stderr)
        return 1
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    env.pop("ForestManagedPath", None)   # CI builds against the stub UnityEngine, so does this
    env.pop("FOREST_MANAGED_PATH", None)
    try:
        for name, cmd in steps:
            t0 = time.time()
            try:
                r = subprocess.run(cmd, cwd=wt, capture_output=True, text=True, encoding="utf-8",
                                   errors="replace", stdin=subprocess.DEVNULL, timeout=STEP_TIMEOUT, env=env)
                ok, log = r.returncode == 0, (r.stdout + r.stderr)
            except subprocess.TimeoutExpired:
                ok, log = False, "timed out after %d s" % STEP_TIMEOUT
            print("pre-push: %-13s %s (%.0f s)" % (name, "ok" if ok else "FAILED", time.time() - t0),
                  file=sys.stderr)
            if not ok:
                tail = "\n".join(log.strip().splitlines()[-40:])
                print(tail, file=sys.stderr)
                print("\nERROR: %s fails on %s\n  WHY: CI runs the same step on main and mails the author when "
                      "it fails (author 2026-10-18)\n  FIX: fix it, commit, push again (`%s` from the repo root)"
                      % (name, sha[:8], " ".join(cmd[1:] if cmd[0] == PY else cmd)), file=sys.stderr)
                return 1
        return 0
    finally:
        git("worktree", "remove", "--force", wt)
        shutil.rmtree(tmp, ignore_errors=True)
        git("worktree", "prune")


def main(argv):
    if argv[:1] == ["--plan"]:
        for name, cmd in plan(argv[1:]):
            print("%-13s %s" % (name, " ".join(cmd)))
        return 0
    status = 0
    for local, remote in main_updates(sys.stdin.read().splitlines()):
        paths = changed(local, remote)
        steps = [(n, c) for n, _, c in STEPS] if paths is None else plan(paths)
        print("pre-push: checking %s for main (%s)" % (local[:8], ", ".join(n for n, _ in steps)), file=sys.stderr)
        status = status or run_steps(local, steps)
    return status


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
