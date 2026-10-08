"""Session start: where the project stands, in one short report (docs/harness.md 4a).

    python scripts/session-start.py              # the report
    python scripts/session-start.py --baseline   # force the local build + tests
    python scripts/session-start.py --hook       # JSON for the SessionStart hook

Run first in every session; the SessionStart hook (.claude/settings.json,
startup + /clear) does it unprompted. It reports:
  - git: fetched, ahead / behind origin/main, uncommitted files;
  - worktrees and branches, live vs merged (merged -> `python scripts/cleanup.py`);
  - the baseline: CI badges (build, site, bot) when HEAD is origin/main and the
    tree is clean, else the local build + every test suite (author,
    2026-10-07). A red baseline is the first task;
  - the latest tag's DLL attached (the asset URL - never api.github.com),
    plugin commits since it;
  - the site up, the VPS containers (ssh, skipped without the key);
  - bot feedback: new thumbs-down / partial queue items (the VPS) and new
    humans' messages in the knowledge-testing channel (Discord REST, read-only)
    since the last bot review (docs/bot-reviews/mark.json); any new = the
    review is due, skill bot-review (T-0141);
  - tasks: in progress, parked questions, what is next;
  - quality (docs/quality.md): the lowest grades, and the rows whose paths
    changed after their Reviewed date (re-grade them);
  - the weekly cleanup (docs/quality.md *Cleanup log*): due 7 days after the last
    row, skill weekly-cleanup (docs/harness.md 10e, T-0014);
  - the monthly harness review (docs/quality.md *Simplification log*): due 30 days
    after the last row; while a component is off, how many of its 5 tasks have
    finished, then "compare and decide", skill harness-review (12d).
QA messages need the MCP tool, so it ends with the reminder to run qa_read.
Every check has a timeout; one that cannot run says so and the rest go on.
"""
import argparse
import concurrent.futures
import datetime
import json
import os
import random
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.dont_write_bytecode = True   # a report must not leave __pycache__ for cleanup.py to find
import cleanup  # noqa: E402
import lint  # noqa: E402
import tasks as T  # noqa: E402

ROOT = cleanup.ROOT
REPO = "1deter/forest-speedrun-tool"
SITE = "https://forest.deter.cloud/"
WORKFLOWS = ("build", "site", "bot")
VPS = "ubuntu@141.147.101.228"
VPS_KEY = os.path.join(os.path.expanduser("~"), ".ssh", "ssh-key-2026-08-13.key")
NET_TIMEOUT = 8
# The bot review's mark (skill bot-review): the last review's date, the highest
# queue id and the last knowledge-testing message id it covered.
REVIEW_MARK = os.path.join(ROOT, "docs", "bot-reviews", "mark.json")
KNOWLEDGE_TESTING = "1555989862652313620"   # tools/BridgeMcp/Discord.cs KnowledgeTestingChannel
BOT_QUEUE_CMD = "sudo docker exec forest-bot dotnet /srv/current/forest-bot.dll queue"
# Local baseline, in order; (name, command, cwd-relative). The build gets
# ForestManagedPath when the User-scope variable exists (CLAUDE.md, Commands).
SUITES = [
    ("plugin tests", ["dotnet", "test", "tests/ForestOverlay.Tests/ForestOverlay.Tests.csproj", "-v", "q", "--nologo"]),
    ("site tests", ["dotnet", "test", "site/ForestSite.Tests", "-v", "q", "--nologo"]),
    ("bot tests", ["dotnet", "test", "bot/ForestBot.Tests", "-v", "q", "--nologo"]),
    ("task + script tests", [sys.executable, "-m", "unittest", "discover", "-s", "scripts/tests", "-q"]),
]
# The weekly cleanup and the monthly harness review (docs/harness.md 10e, 12d; T-0014).
CLEANUP_DAYS = 7
REVIEW_DAYS = 30
REVIEW_TASKS = 5
# Paths whose changes mean the plugin needs a release.
PLUGIN_PATHS = list(T.PLUGIN_PATHS)


# ---------------------------------------------------------------- pure parts (tested)

def parse_changes(log):
    """[(YYYY-MM-DD, path)] from `git log --format=@%cs --name-only`."""
    out, day = [], None
    for l in log.splitlines():
        if l.startswith("@"):
            day = l[1:].strip()
        elif l.strip() and day:
            out.append((day, l.strip()))
    return out


def quality_line(rows, stale):
    worst = max(r["grade"] for r in rows)
    s = "quality: lowest %s - %s" % (worst, ", ".join(r["area"] for r in rows if r["grade"] == worst))
    if stale:
        return s + "; changed since their review: %s -> re-grade in docs/quality.md" % ", ".join(
            "%s (%s)" % (a, plural(n, "file")) for a, n in stale)
    return s + "; every row reviewed since its area last changed"


def log_table(text, heading):
    """The data rows (cell lists) of the table under `## heading` in docs/quality.md."""
    parts = re.split(r"^## ", text or "", flags=re.M)
    sec = next((p for p in parts if p.startswith(heading + "\n")), None)
    if sec is None:
        return None
    rows = []
    for l in sec.splitlines():
        if l.startswith("|"):
            cells = [c.strip() for c in l.strip().strip("|").split("|")]
            if re.match(r"^\d{4}-\d{2}-\d{2}$", cells[0]):
                rows.append(cells)
    return rows


def days_between(a, b):
    return (datetime.date.fromisoformat(b) - datetime.date.fromisoformat(a)).days


def cleanup_line(rows, today):
    """(line, due) for the weekly cleanup: due with no row or the last one CLEANUP_DAYS old."""
    if rows is None:
        return "cleanup: no Cleanup log in docs/quality.md", False
    if not rows:
        return "cleanup: never run -> due, skill weekly-cleanup", True
    last = max(r[0] for r in rows)
    if days_between(last, today) >= CLEANUP_DAYS:
        return "cleanup: last run %s -> due, skill weekly-cleanup" % last, True
    nxt = (datetime.date.fromisoformat(last) + datetime.timedelta(days=CLEANUP_DAYS)).isoformat()
    return "cleanup: last run %s, next due %s" % (last, nxt), False


def review_line(rows, finished_since, today):
    """(line, due) for the monthly harness review. rows: the Simplification log (date, component,
    how to switch it back, outcome, decision); finished_since(date) -> tasks finished from that day."""
    if rows is None:
        return "harness review: no Simplification log in docs/quality.md", False
    open_ = [r for r in rows if len(r) >= 5 and r[4].lower() == "open"]
    if open_:
        r = open_[-1]
        n = finished_since(r[0])
        s = "harness review: %s off since %s, %d/%d tasks finished" % (r[1], r[0], min(n, REVIEW_TASKS), REVIEW_TASKS)
        if n >= REVIEW_TASKS:
            return s + " -> compare and decide, skill harness-review", True
        return s, False
    if not rows:
        return "harness review: never run -> due, skill harness-review", True
    last = max(r[0] for r in rows)
    if days_between(last, today) >= REVIEW_DAYS:
        return "harness review: last %s -> due, skill harness-review" % last, True
    nxt = (datetime.date.fromisoformat(last) + datetime.timedelta(days=REVIEW_DAYS)).isoformat()
    return "harness review: last %s, next due %s" % (last, nxt), False


def badge_state(svg):
    """A GitHub Actions badge SVG -> 'passing' | 'failing' | whatever its title says."""
    m = re.search(r"<title>[^<]*?-\s*([^<]+)</title>", svg or "")
    return m.group(1).strip().lower() if m else "unknown"


def ahead_behind(counts):
    """`git rev-list --left-right --count HEAD...origin/main` -> (ahead, behind)."""
    parts = (counts or "").split()
    return (int(parts[0]), int(parts[1])) if len(parts) == 2 else (0, 0)


def test_summary(output):
    """A dotnet test or unittest run -> '909 passed' / '2 of 909 FAILED'."""
    text = output or ""
    m = re.search(r"Failed:\s*(\d+),\s*Passed:\s*(\d+),.*?Total:\s*(\d+)", text)
    if m:
        failed, total = int(m.group(1)), int(m.group(3))
        return "%d passed" % total if not failed else "%d of %d FAILED" % (failed, total)
    m = re.search(r"Ran (\d+) tests?", text)
    if m:
        f = re.search(r"FAILED \((?:failures|errors)=(\d+)", text)
        return "%s passed" % m.group(1) if not f else "%s of %s FAILED" % (f.group(1), m.group(1))
    lines = [l.strip() for l in text.splitlines() if l.strip()]
    return lines[-1] if lines else "no output"


def parse_queue_ids(text):
    """Ids of the open items in `forest-bot queue` output (lines `#12 2026-10-07 ...`)."""
    return [int(m.group(1)) for m in re.finditer(r"^#(\d+) ", text or "", re.M)]


def new_messages(msgs, after_id, bot_id=None):
    """Humans' messages newer than the mark: Discord messages (dicts), snowflake ids compared as ints."""
    out = []
    for m in msgs or []:
        a = m.get("author") or {}
        if a.get("bot") or (bot_id and a.get("id") == bot_id):
            continue
        if int(m["id"]) > int(after_id or 0):
            out.append(m)
    return out


def read_mark(text):
    """The mark file -> dict with date / queue_id / message_id, or None when absent or unreadable."""
    try:
        d = json.loads(text)
        return {"date": str(d["date"]), "queue_id": int(d.get("queue_id", 0)), "message_id": str(d.get("message_id", "0"))}
    except (TypeError, ValueError, KeyError):
        return None


def feedback_line(mark, queue_new, msgs_new, notes=()):
    """(line, due): queue_new / msgs_new are counts, or None when that source could not be read."""
    if mark is None:
        return ("bot feedback: no review mark (docs/bot-reviews/mark.json) - the first review is due, skill bot-review", True)
    parts = []
    for n, what in ((queue_new, "new thumbs-down / partial queue items"), (msgs_new, "new knowledge-testing messages")):
        parts.append("%s unknown" % what if n is None else "%d %s" % (n, what))
    due = bool((queue_new or 0) + (msgs_new or 0))
    s = "bot feedback since the review of %s: %s" % (mark["date"], ", ".join(parts))
    s += " -> review due, skill bot-review" if due else " (no review due)"
    if notes:
        s += " [%s]" % "; ".join(notes)
    return s, due


def use_badges(head, origin, dirty, forced):
    """Badges stand for the baseline only when HEAD is what CI built and nothing is local."""
    return not forced and head and head == origin and not dirty


def plural(n, word):
    return "%d %s%s" % (n, word, "" if n == 1 else "s")


# ---------------------------------------------------------------- checks

def http(url, method="GET"):
    """(status, body, ms); status 0 with the error text when it fails."""
    t = time.time()
    req = urllib.request.Request(url, method=method, headers={"User-Agent": "forest-session-start"})
    try:
        with urllib.request.urlopen(req, timeout=NET_TIMEOUT) as r:
            body = r.read().decode("utf-8", "replace") if method == "GET" else ""
            return r.status, body, int((time.time() - t) * 1000)
    except urllib.error.HTTPError as e:
        return e.code, "", int((time.time() - t) * 1000)
    except Exception as e:  # timeout, DNS, TLS
        return 0, str(e), int((time.time() - t) * 1000)


def bust():
    return "%d%04d" % (time.time(), random.randint(0, 9999))


def check_badges():
    out = {}
    for w in WORKFLOWS:
        status, body, _ = http("https://github.com/%s/actions/workflows/%s.yml/badge.svg?branch=main&v=%s"
                               % (REPO, w, bust()))
        out[w] = badge_state(body) if status == 200 else "unreachable (%s)" % (status or body)
    return out


def check_release():
    tag = cleanup.git("tag", "-l", "v*", "--sort=-v:refname").split("\n")[0].strip()
    if not tag:
        return "no v* tag found"
    status, _, _ = http("https://github.com/%s/releases/download/%s/ForestOverlay.dll" % (REPO, tag), "HEAD")
    attached = {200: "DLL attached"}.get(status, "DLL NOT attached yet (%s)" % status)
    pending = cleanup.git("rev-list", "--count", "%s..%s" % (tag, cleanup.BASE), "--", *PLUGIN_PATHS)
    note = ""
    if pending and pending != "0":
        note = "; %s since it touch the plugin (unreleased)" % plural(int(pending), "commit")
    return "%s: %s%s" % (tag, attached, note)


def check_site():
    status, _, ms = http(SITE + "?v=" + bust())
    return "up (%d ms)" % ms if status == 200 else "DOWN or erroring: %s" % (status or "no answer")


def check_vps():
    if not os.path.exists(VPS_KEY) or not shutil.which("ssh"):
        return "skipped (no ssh key here)"
    try:
        p = subprocess.run(["ssh", "-i", VPS_KEY, "-o", "BatchMode=yes", "-o", "ConnectTimeout=6", VPS,
                            "sudo docker ps -a --format '{{.Names}}: {{.Status}}'"],
                           capture_output=True, text=True, timeout=20)
    except subprocess.TimeoutExpired:
        return "ssh timed out"
    if p.returncode != 0:
        return "ssh failed: %s" % (p.stderr.strip().splitlines() or ["exit %d" % p.returncode])[-1]
    # Only the project's containers; the VPS runs the author's other services too.
    rows = [r for r in p.stdout.strip().splitlines() if r.startswith("forest")]
    bad = [r for r in rows if not r.split(": ", 1)[-1].startswith("Up")]
    return ("; ".join(rows) or "no containers") + ("  <- NOT UP" if bad else "")


def user_env(name):
    """An environment variable, then the Windows User scope (tooling shells do not inherit it)."""
    v = os.environ.get(name)
    if v or os.name != "nt":
        return v
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k:
            return winreg.QueryValueEx(k, name)[0]
    except OSError:
        return None


def check_queue_ids():
    """(ids, note): the open thumbs-down / partial queue ids from the VPS; ids None when it cannot be read."""
    if not os.path.exists(VPS_KEY) or not shutil.which("ssh"):
        return None, "queue skipped (no ssh key here)"
    try:
        p = subprocess.run(["ssh", "-i", VPS_KEY, "-o", "BatchMode=yes", "-o", "ConnectTimeout=6", VPS, BOT_QUEUE_CMD],
                           capture_output=True, text=True, timeout=30)
    except subprocess.TimeoutExpired:
        return None, "queue: ssh timed out"
    if p.returncode != 0:
        return None, "queue: ssh failed (exit %d)" % p.returncode
    return parse_queue_ids(p.stdout), None


def check_testing_messages(after_id):
    """(humans' messages since after_id, note) from the knowledge-testing channel, read-only REST.
    The QA bot token is read from the environment and never printed or put in a message."""
    token = user_env("FOREST_QA_BOT_TOKEN")
    if not token:
        return None, "channel skipped (no FOREST_QA_BOT_TOKEN)"
    url = "https://discord.com/api/v10/channels/%s/messages?limit=100&after=%s" % (KNOWLEDGE_TESTING, after_id or "0")
    req = urllib.request.Request(url, headers={
        "Authorization": "Bot " + token,
        "User-Agent": "DiscordBot (https://github.com/1deter/forest-speedrun-tool, 1.0)"})
    try:
        with urllib.request.urlopen(req, timeout=NET_TIMEOUT) as r:
            msgs = json.loads(r.read().decode("utf-8", "replace"))
    except urllib.error.HTTPError as e:
        return None, "channel: Discord answered %d" % e.code
    except Exception as e:
        return None, "channel: %s" % type(e).__name__
    return new_messages(msgs, after_id), None


def check_bot_feedback():
    """The 'bot feedback' line and whether the review is due. Read-only; never raises."""
    try:
        with open(REVIEW_MARK, encoding="utf-8") as f:
            mark = read_mark(f.read())
    except OSError:
        mark = None
    if mark is None:
        return feedback_line(None, None, None)
    notes = []
    ids, note = check_queue_ids()
    if note:
        notes.append(note)
    msgs, note = check_testing_messages(mark["message_id"])
    if note:
        notes.append(note)
    q_new = None if ids is None else len([i for i in ids if i > mark["queue_id"]])
    return feedback_line(mark, q_new, None if msgs is None else len(msgs), notes)


def managed_path():
    v = os.environ.get("FOREST_MANAGED_PATH")
    if v or os.name != "nt":
        return v
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k:
            return winreg.QueryValueEx(k, "FOREST_MANAGED_PATH")[0]
    except OSError:
        return None


def run_local():
    """Build + every suite; [(name, ok, summary)]. Stops nothing on a failure."""
    if not shutil.which("dotnet"):
        return [("build", False, "dotnet not found on PATH")]
    results = []
    build = ["dotnet", "build", "ForestOverlay.csproj", "-c", "Release", "-v", "q", "-nologo"]
    mp = managed_path()
    if mp:
        build.append("-p:ForestManagedPath=" + mp)
    for name, cmd in [("plugin build", build)] + SUITES:
        try:
            p = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, encoding="utf-8",
                               errors="replace", timeout=240)
            out = p.stdout + p.stderr
            ok = p.returncode == 0
            summary = test_summary(out) if "test" in name else (
                "ok" if ok else next((l.strip() for l in out.splitlines() if "error" in l.lower()), "failed"))
        except subprocess.TimeoutExpired:
            ok, summary = False, "timed out after 240 s"
        results.append((name, ok, summary))
    return results


# ---------------------------------------------------------------- report

def report(force_local=False):
    t0 = time.time()
    lines, problems = [], []

    fetched = subprocess.run(["git", "fetch", "-q"], cwd=ROOT, capture_output=True, text=True, timeout=30)
    head = cleanup.git("rev-parse", "HEAD")
    origin = cleanup.git("rev-parse", cleanup.BASE)
    branch = cleanup.git("rev-parse", "--abbrev-ref", "HEAD")
    ahead, behind = ahead_behind(cleanup.git("rev-list", "--left-right", "--count", "HEAD..." + cleanup.BASE))
    dirty = [l for l in cleanup.git("status", "--porcelain").splitlines() if l.strip()]

    pool = concurrent.futures.ThreadPoolExecutor(max_workers=6)
    badges = use_badges(head, origin, dirty, force_local)
    f_base = pool.submit(check_badges if badges else run_local)
    f_rel = pool.submit(check_release)
    f_site = pool.submit(check_site)
    f_vps = pool.submit(check_vps)
    f_survey = pool.submit(cleanup.survey)
    f_feedback = pool.submit(check_bot_feedback)

    g = "git: %s at %s" % (branch, head[:7])
    g += ", fetched" if fetched.returncode == 0 else ", FETCH FAILED (%s)" % fetched.stderr.strip()[:80]
    if ahead or behind:
        g += ", %d ahead / %d behind origin/main" % (ahead, behind)
        if behind:
            problems.append("behind origin/main by %s - pull before working" % plural(behind, "commit"))
    else:
        g += ", even with origin/main"
    if dirty:
        g += ", %s uncommitted" % plural(len(dirty), "file")
    lines.append(g)
    # The lints' git hooks (docs/harness.md 5a) live in .githooks/; a fresh clone needs them turned on.
    if cleanup.git("config", "--get", "core.hooksPath") != ".githooks":
        subprocess.run(["git", "config", "core.hooksPath", ".githooks"], cwd=ROOT, capture_output=True)
        lines.append("git hooks: turned on (core.hooksPath .githooks - lints before commit, tag check before push)")

    s = f_survey.result()
    live = [wt for wt, st in s["worktrees"] if st in ("live", "dirty", "locked")]
    merged_wt = [wt for wt, st in s["worktrees"] if st in ("merged", "prunable")]
    merged_br = [n for n, m in s["local"] if m] + ["origin/" + n for n, m in s["remote"] if m]
    unmerged_br = [n for n, m in s["local"] if not m] + ["origin/" + n for n, m in s["remote"] if not m]
    wl = "worktrees: %s" % (", ".join("%s (%s%s)" % (os.path.basename(wt["path"]), wt["branch"] or "detached",
                                                     ", dirty" if st == "dirty" else ", in use" if st == "locked" else "")
                                      for wt, st in s["worktrees"] if st in ("live", "dirty", "locked")) or "none besides main")
    lines.append(wl)
    if unmerged_br:
        lines.append("unmerged branches: " + ", ".join(unmerged_br))
    if merged_wt or merged_br:
        lines.append("merged, to clean: %s -> `python scripts/cleanup.py`"
                     % ", ".join([os.path.basename(w["path"]) for w in merged_wt] + merged_br))

    base = f_base.result()
    if badges:
        red = [w for w, st in base.items() if st != "passing"]
        lines.append("baseline (CI badges = last finished run on main; HEAD = origin/main): " + ", ".join("%s %s" % kv for kv in base.items()))
        if red:
            problems.append("CI not passing for %s - check the Actions page, or run "
                            "`python scripts/session-start.py --baseline` to test locally" % ", ".join(red))
    else:
        why = "forced" if force_local else ("local changes" if dirty else "HEAD differs from origin/main")
        lines.append("baseline (local: %s): %s" % (why, "; ".join(
            "%s %s" % (n, s_ if ok else "FAILED (" + s_ + ")") for n, ok, s_ in base)))
        red = [n for n, ok, _ in base if not ok]
        if red:
            problems.append("RED BASELINE: %s failed - fix this first (docs/harness.md 4a)" % ", ".join(red))

    lines.append("release: " + f_rel.result())
    lines.append("site: " + f_site.result())
    lines.append("VPS: " + f_vps.result())
    pool.shutdown(wait=False)

    try:
        ts = T.load()
        counts = {}
        for t in ts:
            counts[t["status"]] = counts.get(t["status"], 0) + 1
        lines.append("tasks: " + ", ".join("%d %s" % (counts[s_], s_) for s_ in T.STATUSES if counts.get(s_)))
        for t in ts:
            if t["status"] == "in-progress":
                lines.append("  in progress (%s): %s" % (t.get("owner", "?"), T.line(t)))
        parked = [t for t in ts if t["status"] not in T.DONE and (t.get("question") or t["needs"] == "author-decision")]
        if parked:
            lines.append("  parked questions for the author: " + ", ".join(t["id"] for t in parked)
                         + " (`tasks.py list --needs author-decision`)")
        nxt = T.pick_next(ts)
        nxt_b = T.pick_next(ts, bridge=True)
        if nxt:
            lines.append("  next: " + T.line(nxt))
        if nxt_b and nxt_b is not nxt:
            lines.append("  next with the game up: " + T.line(nxt_b))
    except T.TaskError as e:
        problems.append("tasks file: " + str(e).replace("\n", " "))

    try:
        qtext = lint.read(lint.QUALITY)
        rows, paths = lint.quality_doc(qtext)
        log = cleanup.git("log", "--since=" + min(r["reviewed"] for r in rows), "--format=@%cs", "--name-only")
        lines.append(quality_line(rows, lint.stale_areas(rows, paths, parse_changes(log))))
        today = datetime.date.today().isoformat()
        lines.append(cleanup_line(log_table(qtext, "Cleanup log"), today)[0])
        done = T.load()
        lines.append(review_line(log_table(qtext, "Simplification log"),
                                 lambda d: len(T.finished_between(done, since=d)), today)[0])
    except (OSError, ValueError, T.TaskError) as e:
        problems.append("docs/quality.md: %s - `python scripts/lint.py` says what is wrong" % e)

    try:
        fb_line, fb_due = f_feedback.result(timeout=60)
        lines.append(fb_line)
        if fb_due:
            problems.append("bot review due - skill bot-review (new queue items / knowledge-testing messages)")
    except Exception as e:
        lines.append("bot feedback: check failed (%s)" % type(e).__name__)

    lines.append("QA: run `qa_read new_only` (forest MCP) and file each new message as a task")
    head_line = "Session start (%.1f s)%s" % (time.time() - t0, "" if not problems else " - %s" % plural(len(problems), "problem"))
    return head_line, ["! " + p for p in problems] + lines


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--baseline", action="store_true", help="run the local build + tests even when CI covers HEAD")
    p.add_argument("--hook", action="store_true", help="print the SessionStart hook's JSON")
    a = p.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    try:
        title, lines = report(a.baseline)
    except Exception as e:  # the hook must never block a session start
        title, lines = "Session start failed", ["%s: %s" % (type(e).__name__, e),
                                                "run `python scripts/session-start.py` by hand to see it"]
    text = title + "\n" + "\n".join(lines)
    if a.hook:
        problems = [l for l in lines if l.startswith("! ")]
        print(json.dumps({
            "systemMessage": title + (": " + problems[0][2:] if problems else ""),
            "hookSpecificOutput": {"hookEventName": "SessionStart", "additionalContext": text},
        }))
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
