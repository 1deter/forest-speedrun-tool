"""Removes what finished work leaves behind (docs/harness.md 10a). Idempotent.

    python scripts/cleanup.py             # do it, saying each thing removed
    python scripts/cleanup.py --dry-run   # only say what it would remove

What goes (author, 2026-10-07: merged branches locally AND on origin; scratch
= whatever has no long-term use):
  - worktrees whose branch (or detached HEAD) is merged into origin/main and
    whose tree is clean - a dirty one is kept and named, and a locked one (a running
    agent's worktree; it can look merged before its first commit) is in use and left alone;
  - local branches merged into origin/main;
  - branches on origin merged into origin/main (`git push origin --delete`);
  - __pycache__ folders in the repo;
  - scratch older than --days (7): this project's session scratchpads under
    %TEMP%/claude, scripts/site-look-shots, .claude/shots.
Never: main, a branch a remaining worktree has checked out, site/aerial-out
(aerial-upload.py needs it; rebuilding it means recapturing in game).

session-start.py imports the read-only helpers below to list the same things.
"""
import argparse
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASE = "origin/main"
KEEP_BRANCHES = ("main", "HEAD")
DAYS = 7
SCRATCH_DIRS = (os.path.join("scripts", "site-look-shots"), os.path.join(".claude", "shots"))
SKIP_WALK = (".git", "node_modules", "worktrees", "bin", "obj")


# ---------------------------------------------------------------- git

def git(*args, check=False, cwd=ROOT):
    """Runs git; returns stdout stripped (or raises with git's message when check)."""
    p = subprocess.run(["git"] + list(args), cwd=cwd, capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    if check and p.returncode != 0:
        raise RuntimeError("git %s: %s" % (" ".join(args), (p.stderr or p.stdout).strip()))
    return p.stdout.strip() if p.returncode == 0 else ""


def parse_worktrees(porcelain):
    """`git worktree list --porcelain` -> [{path, head, branch (short or None), prunable, locked}]."""
    out, cur = [], None
    for line in porcelain.splitlines():
        if line.startswith("worktree "):
            cur = {"path": line[9:], "head": None, "branch": None, "prunable": False, "locked": False}
            out.append(cur)
        elif cur is None:
            continue
        elif line.startswith("HEAD "):
            cur["head"] = line[5:]
        elif line.startswith("branch "):
            cur["branch"] = re.sub(r"^refs/heads/", "", line[7:])
        elif line.startswith("prunable"):
            cur["prunable"] = True
        elif line == "locked" or line.startswith("locked "):
            cur["locked"] = True
    return out


def is_merged(rev, base=BASE):
    """True when rev is an ancestor of base (its commits are all in base)."""
    p = subprocess.run(["git", "merge-base", "--is-ancestor", rev, base], cwd=ROOT,
                       capture_output=True)
    return p.returncode == 0


def is_clean(path):
    p = subprocess.run(["git", "status", "--porcelain"], cwd=path, capture_output=True, text=True)
    return p.returncode == 0 and not p.stdout.strip()


def same_path(a, b):
    return os.path.normcase(os.path.normpath(a)) == os.path.normcase(os.path.normpath(b))


def local_to_delete(local, worktrees):
    """Local branches to delete: the merged ones survey() found plus those of the merged worktrees
    just removed (a branch is free once its worktree is gone). Each name once - survey() already
    lists a merged worktree's branch, and a second `branch -D` of it fails (T-0169)."""
    names = [name for name, merged in local if merged]
    for wt, st in worktrees:
        if st == "merged" and wt["branch"] and wt["branch"] not in KEEP_BRANCHES:
            names.append(wt["branch"])
    seen, out = set(), []
    for name in names:
        if name not in seen:
            seen.add(name)
            out.append(name)
    return out


def survey():
    """What is merged and what is live. Read-only; session-start prints it.

    Returns {worktrees: [(wt, state)], local: [(name, merged)], remote: [(name, merged)]}
    where state is 'main' | 'merged' | 'dirty' | 'live' | 'locked' | 'prunable'."""
    wts = parse_worktrees(git("worktree", "list", "--porcelain"))
    worktrees = []
    for wt in wts:
        if same_path(wt["path"], ROOT):
            state = "main"
        elif wt["locked"]:
            state = "locked"   # in use by a running agent: never removed, whatever else is true
        elif wt["prunable"] or not os.path.isdir(wt["path"]):
            state = "prunable"
        elif not is_clean(wt["path"]):
            state = "dirty"
        elif is_merged(wt["branch"] or wt["head"]):
            state = "merged"
        else:
            state = "live"
        worktrees.append((wt, state))
    # Branches a worktree we keep has checked out are never deleted.
    held = {wt["branch"] for wt, st in worktrees if wt["branch"] and st != "merged"}
    local = []
    for name in git("for-each-ref", "--format=%(refname:short)", "refs/heads").splitlines():
        if name in KEEP_BRANCHES or name in held:
            continue
        local.append((name, is_merged(name)))
    remote = []
    for ref in git("for-each-ref", "--format=%(refname:short)", "refs/remotes/origin").splitlines():
        name = ref.split("/", 1)[1] if "/" in ref else ref
        if ref == "origin" or name in KEEP_BRANCHES or name in held:
            continue
        remote.append((name, is_merged(ref)))
    return {"worktrees": worktrees, "local": local, "remote": remote}


# ---------------------------------------------------------------- files

def project_slug(root=ROOT):
    """Claude Code's folder name for a project: every non-alphanumeric char -> '-'."""
    return re.sub(r"[^A-Za-z0-9]", "-", root)


def newest_mtime(path):
    """The newest modification time of path or anything under it."""
    newest = os.path.getmtime(path)
    if os.path.isdir(path):
        for dirpath, dirnames, filenames in os.walk(path):
            for n in dirnames + filenames:
                try:
                    newest = max(newest, os.path.getmtime(os.path.join(dirpath, n)))
                except OSError:
                    pass
    return newest


def stale(entries, now, days, keep=()):
    """entries: [(path, newest mtime)] -> the paths untouched for more than days, minus keep."""
    cutoff = now - days * 86400
    keep = {os.path.normcase(k) for k in keep}
    return [p for p, m in entries if m < cutoff and os.path.normcase(os.path.basename(p)) not in keep]


def scratch_candidates(now, days, root=ROOT):
    out = []
    # This project's session scratchpads (and its worktrees' ones), never the current session.
    temp = os.path.join(tempfile.gettempdir(), "claude")
    slug = project_slug(root)
    keep = [os.environ.get("CLAUDE_CODE_SESSION_ID", "")]
    if os.path.isdir(temp):
        for proj in os.listdir(temp):
            if proj != slug and not proj.startswith(slug + "--"):
                continue
            pdir = os.path.join(temp, proj)
            if os.path.isdir(pdir):
                out += stale([(os.path.join(pdir, n), newest_mtime(os.path.join(pdir, n)))
                              for n in os.listdir(pdir)], now, days, keep)
    for rel in SCRATCH_DIRS:
        d = os.path.join(root, rel)
        if os.path.isdir(d):
            out += stale([(os.path.join(d, n), newest_mtime(os.path.join(d, n)))
                          for n in os.listdir(d)], now, days)
    return out


def pycaches(root=ROOT):
    out = []
    for dirpath, dirnames, _ in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_WALK and not d.startswith(".")]
        if "__pycache__" in dirnames:
            out.append(os.path.join(dirpath, "__pycache__"))
            dirnames.remove("__pycache__")
    return out


def remove(path):
    if os.path.isdir(path) and not os.path.islink(path):
        shutil.rmtree(path, ignore_errors=True)
    else:
        try:
            os.remove(path)
        except OSError:
            pass


def size_of(paths):
    total = 0
    for p in paths:
        if os.path.isfile(p):
            total += os.path.getsize(p)
            continue
        for dirpath, _, filenames in os.walk(p):
            for n in filenames:
                try:
                    total += os.path.getsize(os.path.join(dirpath, n))
                except OSError:
                    pass
    return total


# ---------------------------------------------------------------- main

def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--dry-run", action="store_true")
    p.add_argument("--days", type=int, default=DAYS)
    a = p.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    verb = "would remove" if a.dry_run else "removed"
    did = 0

    if not a.dry_run:
        git("fetch", "--prune", "-q")
        git("worktree", "prune")
    s = survey()

    for wt, state in s["worktrees"]:
        if state == "merged":
            did += 1
            if not a.dry_run:
                git("worktree", "remove", wt["path"], check=True)
            print("%s worktree %s (%s, merged)" % (verb, wt["path"], wt["branch"] or "detached"))
        elif state == "dirty":
            print("kept worktree %s (%s): uncommitted changes" % (wt["path"], wt["branch"] or "detached"))
        elif state == "locked":
            print("kept worktree %s (%s): locked, in use" % (wt["path"], wt["branch"] or "detached"))
    # A branch a just-removed worktree held is free now.
    for name in local_to_delete(s["local"], s["worktrees"]):
        did += 1
        if not a.dry_run:
            git("branch", "-D", name, check=True)   # merged into origin/main, checked above
        print("%s local branch %s (merged)" % (verb, name))
    gone = [name for name, merged in s["remote"] if merged]
    if gone:
        did += len(gone)
        if not a.dry_run:
            git("push", "origin", "--delete", *gone, check=True)
        print("%s origin branches: %s (merged)" % (verb, ", ".join(gone)))

    files = pycaches() + scratch_candidates(time.time(), a.days)
    if files:
        did += len(files)
        mb = size_of(files) / 1e6
        if not a.dry_run:
            for f in files:
                remove(f)
        print("%s %d scratch / cache item(s), %.0f MB (older than %d days, and __pycache__)"
              % (verb, len(files), mb, a.days))
    if not did:
        print("nothing to clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
