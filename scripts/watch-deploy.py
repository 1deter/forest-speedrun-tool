"""Watch a push deploy reach the VPS: the site and / or the bot (skill deploy-watch).

    python scripts/watch-deploy.py              # every target with a commit newer than its restart
    python scripts/watch-deploy.py site bot     # these, regardless of paths
    python scripts/watch-deploy.py --timeout 900 --since 1760000000

Deployed = the container was restarted after the last commit on origin/main
touching its paths (each deploy.sh ends with `docker restart`), then the
live check answers and its CI badge reads passing. The site is polled with
a new query string each time (Cloudflare caches a ?v= URL); never
api.github.com (60 calls an hour per IP, shared with the author's game).
Exit 0 = every target deployed, 1 = failed or timed out.
"""
import argparse
import calendar
import importlib.util
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.dont_write_bytecode = True
_spec = importlib.util.spec_from_file_location("session_start", os.path.join(HERE, "session-start.py"))
S = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(S)

# Mirrors the `paths:` of .github/workflows/site.yml and bot.yml.
TARGETS = {
    "site": {
        "container": "forest-site",
        "workflow": "site",
        "paths": ["site", "community", "src/Data", "tests/ForestOverlay.Tests/UnityShim.cs",
                  "scripts/site-smoke.py", "scripts/tests/test_site_smoke.py", ".github/workflows/site.yml"],
        "live": S.SITE + "api/spots",
    },
    "bot": {
        "container": "forest-bot",
        "workflow": "bot",
        "paths": ["bot", "knowledge", "docs/game-notes.md", "docs/savestates.md", "docs/run-mode.md",
                  "docs/fsm", "tools/BridgeMcp/DiscordText.cs", ".github/workflows/bot.yml"],
        "live": None,   # no public endpoint: the container being Up is the check
    },
}
POLL = 20


def parse_started(text):
    """docker's `2026-10-07T12:34:56.123456789Z` -> epoch seconds, None if unparsable."""
    try:
        return calendar.timegm(time.strptime(text.strip()[:19], "%Y-%m-%dT%H:%M:%S"))
    except ValueError:
        return None


def last_push(paths):
    out = S.cleanup.git("log", "-1", "--format=%ct", "origin/main", "--", *paths).strip()
    return int(out) if out else 0


def container_state(name):
    """(started epoch or None, running bool, error text)."""
    try:
        p = subprocess.run(["ssh", "-i", S.VPS_KEY, "-o", "BatchMode=yes", "-o", "ConnectTimeout=6", S.VPS,
                            "sudo docker inspect -f '{{.State.StartedAt}} {{.State.Running}}' " + name],
                           capture_output=True, text=True, timeout=20)
    except subprocess.TimeoutExpired:
        return None, False, "ssh timed out"
    if p.returncode != 0:
        return None, False, (p.stderr.strip().splitlines() or ["exit %d" % p.returncode])[-1]
    parts = p.stdout.split()
    return parse_started(parts[0]) if parts else None, len(parts) > 1 and parts[1] == "true", ""


def badge(workflow):
    status, body, _ = S.http("https://github.com/%s/actions/workflows/%s.yml/badge.svg?branch=main&v=%s"
                             % (S.REPO, workflow, S.bust()))
    return S.badge_state(body) if status == 200 else "unreachable"


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("targets", nargs="*", help="site / bot (default: the ones not deployed yet)")
    ap.add_argument("--timeout", type=int, default=900, help="seconds (default 900)")
    ap.add_argument("--since", type=int, help="epoch seconds the restart must be after (default: last push)")
    a = ap.parse_args()

    if not os.path.exists(S.VPS_KEY):
        print("no VPS ssh key here (%s) - cannot watch" % S.VPS_KEY)
        return 1
    bad = [t for t in a.targets if t not in TARGETS]
    if bad:
        ap.error("unknown target %s (site / bot)" % ", ".join(bad))
    S.cleanup.git("fetch", "-q", "origin")
    targets = a.targets
    if not targets:
        for t, c in TARGETS.items():
            started, _, err = container_state(c["container"])
            if err or started is None or started < last_push(c["paths"]):
                targets.append(t)
            else:
                print("%s: up to date (restarted %s, after its last commit)"
                      % (t, time.strftime("%Y-%m-%d %H:%M", time.localtime(started))))
        if not targets:
            return 0

    deadline = time.time() + a.timeout
    pending = {}
    for t in targets:
        since = a.since or last_push(TARGETS[t]["paths"])
        pending[t] = since
        print("%s: waiting for a restart after %s" % (t, time.strftime("%H:%M:%S", time.localtime(since))))

    failed = []
    while pending and time.time() < deadline:
        for t in list(pending):
            c = TARGETS[t]
            started, running, err = container_state(c["container"])
            if err:
                print("%s: %s" % (t, err))
                continue
            if started is None or started < pending[t] or not running:
                continue
            if c["live"]:
                status, _, ms = S.http(c["live"] + "?v=" + S.bust())
                if status != 200:
                    print("%s: restarted, live check %s - retrying" % (t, status or "no answer"))
                    continue
            b = badge(c["workflow"])
            if b == "failing":
                print("%s: restarted but the %s badge reads failing - look at the run" % (t, c["workflow"]))
                failed.append(t)
            else:
                print("%s: deployed - restarted %s, badge %s"
                      % (t, time.strftime("%H:%M:%S", time.localtime(started)), b))
            del pending[t]
        if pending:
            time.sleep(POLL)

    for t in pending:
        print("%s: NOT deployed within %d s - check the %s workflow run (badge: %s)"
              % (t, a.timeout, TARGETS[t]["workflow"], badge(TARGETS[t]["workflow"])))
    return 1 if pending or failed else 0


if __name__ == "__main__":
    sys.exit(main())
