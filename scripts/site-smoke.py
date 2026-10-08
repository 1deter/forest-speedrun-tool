"""Smoke the website in a real browser over a throwaway local copy.

    python scripts/site-smoke.py [--no-build] [--keep] [--headed]

Builds site/ForestSite (Release), starts it on a free port with an empty
temp data folder (no Discord webhook, no speedrun.com sync), seeds it over
the API - a runner, the run in site/ForestSite.Tests/smoke-run.foseg (its
test keeps it uploadable), a started attempt - then opens each page in
headless Chromium (Python Playwright): /, /about, /compare, the spot page
and the attempt page. A page passes when it renders its content (no
"Loading", no .error) with no console error, no page exception (CSP
violations are console errors) and no failed same-origin request. The API
is checked too. Exit 0 = all passed; the report names each failure.

CI: .github/workflows/site.yml runs it before Publish, so a broken page
stops the deploy (docs/harness.md 8c, T-0011). Setup once:
pip install playwright && python -m playwright install chromium.
Tests: scripts/tests/test_site_smoke.py.
"""
import argparse
import json
import os
import secrets
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SITE = os.path.join(ROOT, "site", "ForestSite")
FIXTURE = os.path.join(ROOT, "site", "ForestSite.Tests", "smoke-run.foseg")
SPOT = "s-5a0ce0000001"   # the fixture's segment
RUNNER = "r-00000000000000aa"  # the fixture run's runner (an upload must come from its owner)
WAIT_START = 90            # s for the site to answer
WAIT_PAGE = 20             # s for a page to render

# (path, text the rendered page must show); {spot} / {attempt} filled in.
PAGES = [
    ("/", "Test dash"),
    ("/about", None),
    ("/compare", None),
    ("/spot/{spot}", "Test dash"),
    ("/attempt/{attempt}", "Run attempt"),
]


def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def http(base, method, path, body=None, token=None, ctype="application/json"):
    """(status, text) - never raises on an HTTP error status."""
    data = None
    if body is not None:
        data = (json.dumps(body) if ctype == "application/json" else body).encode("utf-8")
    req = urllib.request.Request(base + path, data=data, method=method)
    if data is not None:
        req.add_header("Content-Type", ctype)
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, timeout=15) as r:
            return r.status, r.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")


def site_env(data_dir):
    env = dict(os.environ)
    for k in ("FOREST_DISCORD_WEBHOOK", "FOREST_SITE_URL", "FOREST_ORIGIN_SECRET"):
        env.pop(k, None)
    env.update({"FOREST_DATA": data_dir, "FOREST_SRC_SYNC": "off",
                "FOREST_ADMIN_TOKEN": secrets.token_hex(16), "ASPNETCORE_ENVIRONMENT": "Production",
                "Logging__LogLevel__Default": "Warning"})
    return env


def start_site(port, data_dir, log):
    dll = os.path.join(SITE, "bin", "Release", "net10.0", "ForestSite.dll")
    if not os.path.exists(dll):
        raise SystemExit("not built: " + dll + " (run without --no-build)")
    # The DLL, not `dotnet run` (no child process left behind); the
    # content root is the project folder (wwwroot). stdin closed (gotcha 95).
    return subprocess.Popen(["dotnet", dll, "--urls", "http://127.0.0.1:%d" % port], cwd=SITE,
                            env=site_env(data_dir), stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT)


def wait_up(base, proc):
    deadline = time.time() + WAIT_START
    while time.time() < deadline:
        if proc.poll() is not None:
            return "the site exited (code %s)" % proc.returncode
        try:
            if http(base, "GET", "/api/spots")[0] == 200:
                return None
        except OSError:
            pass
        time.sleep(0.5)
    return "the site did not answer in %d s" % WAIT_START


def seed(base):
    """Runner + the fixture run + a started attempt; returns (attempt id, failures)."""
    fails = []
    st, text = http(base, "POST", "/api/register", {"runner": RUNNER, "name": "Smoke"})
    if st != 200:
        return None, ["register: %d %s" % (st, text[:200])]
    token = json.loads(text)["token"]
    with open(FIXTURE, encoding="utf-8") as f:
        st, text = http(base, "POST", "/api/runs", f.read(), token, "text/plain")
    if st != 200 or not json.loads(text).get("added"):
        fails.append("upload the fixture run: %d %s" % (st, text[:200]))
    attempt = "a-" + secrets.token_hex(8)
    st, text = http(base, "POST", "/api/attempts", {"attempt": attempt, "category": "Any%", "spot": SPOT}, token)
    if st != 200:
        fails.append("start an attempt: %d %s" % (st, text[:200]))
    return attempt, fails


def check_api(base, attempt):
    fails = []
    for path, want in (("/api/spots", SPOT), ("/api/spots/" + SPOT, "Test dash"),
                       ("/api/categories.txt", None), ("/api/attempts/" + attempt, attempt),
                       ("/api/nope", None)):
        st, text = http(base, "GET", path)
        expect = 404 if path == "/api/nope" else 200
        if st != expect:
            fails.append("GET %s: %d, expected %d" % (path, st, expect))
        elif want and want not in text:
            fails.append("GET %s: no %r in the answer" % (path, want))
    return fails


def page_problems(console, errors, failed, bad_status, answered, origin):
    """The failures for one page from what the browser reported. A request
    that got an answer is not "failed" even when Chromium says aborted (a
    204, like /aerial/aerial.json with no photo layer); its status counts."""
    out = ["console: " + m for m in console]
    out += ["exception: " + m for m in errors]
    out += ["request failed: %s (%s)" % (u, why) for u, why in failed if u.startswith(origin) and u not in answered]
    out += ["HTTP %d: %s" % (st, u) for u, st in bad_status if u.startswith(origin)]
    return out


def launch(p, headed):
    """Playwright's Chromium (CI installs it), else the machine's Edge or
    Chrome - Windows always has Edge, so no browser download locally."""
    last = None
    for channel in (None, "msedge", "chrome"):
        try:
            return p.chromium.launch(headless=not headed, channel=channel)
        except Exception as e:
            last = e
    raise SystemExit("no browser for Playwright: " + str(last).splitlines()[0] +
                     "\n(python -m playwright install chromium)")


def check_pages(base, attempt, headed):
    from playwright.sync_api import sync_playwright
    results = []
    with sync_playwright() as p:
        browser = launch(p, headed)
        for path, want in PAGES:
            url = base + path.format(spot=SPOT, attempt=attempt)
            page = browser.new_page()
            console, errors, failed, bad_status, answered = [], [], [], [], set()

            def on_response(r, bad=bad_status, seen=answered):
                seen.add(r.url)
                if r.status >= 400:
                    bad.append((r.url, r.status))
            page.on("console", lambda m, c=console: c.append(m.text) if m.type == "error" else None)
            page.on("pageerror", lambda e, c=errors: c.append(str(e)))
            page.on("requestfailed", lambda r, c=failed: c.append((r.url, r.failure or "")))
            page.on("response", on_response)
            problems = []
            try:
                resp = page.goto(url, wait_until="networkidle", timeout=WAIT_PAGE * 1000)
                if resp is None or resp.status != 200:
                    problems.append("status %s" % (resp.status if resp else "none"))
                page.wait_for_function(
                    "() => { const v = document.getElementById('view');"
                    " return v && v.children.length > 0 && !v.querySelector('.loading'); }",
                    timeout=WAIT_PAGE * 1000)
                err = page.query_selector("#view .error")
                if err:
                    problems.append("shows an error: " + err.inner_text()[:200])
                # text_content: inner_text follows CSS text-transform (the h1s are upper case)
                if want and want not in (page.text_content("#view") or ""):
                    problems.append("no %r on the page" % want)
            except Exception as e:  # a timeout names the page, the run goes on
                problems.append("did not render: " + str(e).splitlines()[0])
            problems += page_problems(console, errors, failed, bad_status, answered, base)
            results.append((path, problems))
            page.close()
        browser.close()
    return results


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--no-build", action="store_true", help="use the existing Release build")
    ap.add_argument("--keep", action="store_true", help="keep the temp data folder and the site log")
    ap.add_argument("--headed", action="store_true", help="show the browser")
    a = ap.parse_args(argv)

    if not a.no_build:
        b = subprocess.run(["dotnet", "build", SITE, "-c", "Release", "--nologo", "-v", "q"],
                           stdin=subprocess.DEVNULL, timeout=600)
        if b.returncode != 0:
            print("FAIL: the site does not build")
            return 1

    tmp = tempfile.mkdtemp(prefix="forest-site-smoke-")
    port = free_port()
    base = "http://127.0.0.1:%d" % port
    log_path = os.path.join(tmp, "site.log")
    fails, results = [], []
    with open(log_path, "w", encoding="utf-8") as log:
        proc = start_site(port, os.path.join(tmp, "data"), log)
        try:
            err = wait_up(base, proc)
            if err:
                fails.append(err)
            else:
                attempt, fails = seed(base)
                if attempt:
                    fails += check_api(base, attempt)
                    results = check_pages(base, attempt, a.headed)
        finally:
            proc.terminate()
            try:
                proc.wait(timeout=15)
            except subprocess.TimeoutExpired:
                proc.kill()

    for path, problems in results:
        print(("ok    " if not problems else "FAIL  ") + path)
        for pr in problems:
            print("        " + pr)
    for f in fails:
        print("FAIL  " + f)
    bad = len(fails) + sum(1 for _, p in results if p)
    print("site smoke: %s (%d pages, %d failure(s))" % ("passed" if bad == 0 else "FAILED", len(results), bad))
    if bad:
        with open(log_path, encoding="utf-8", errors="replace") as f:
            print("--- site log (last 30 lines) ---\n" + "".join(f.readlines()[-30:]))
    if a.keep:
        print("kept: " + tmp)
    else:
        shutil.rmtree(tmp, ignore_errors=True)
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
