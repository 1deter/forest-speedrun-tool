"""What a spot's 3D view costs on a site: world requests and bytes on the wire
by folder, the triangles drawn, a shot per view - docs/website.md *Load size*.
Headless through Playwright (pip install playwright): Edge on Windows, the
pre-installed Chromium with software GL in a cloud session (slow: a view takes
20-120 s to settle). Shots go to scripts/site-look-shots/ (not in git).

    python scripts/site-measure.py seed [site]          a local site gets the live spot SPOT and its runners' best runs
    python scripts/site-measure.py view <tag> [desktop|phone|narrow] [views]

views: "fit" (the spot's own fit, the default) and / or "x,y,z,yaw,pitch,dist"
lookFroms (Unity coordinates, degrees), ";"-separated. phone = 375 x 812 with
touch, narrow = the same without touch.
Env: SITE (default http://localhost:5080), SPOT (default the Labskip spot),
NORUNS=1 (the runs cleared: an underground spot fades the surface), EXTRA (ms
waited after the chunks settle, default 2500), NOFAR=1 (full meshes only: the
far copies switched off in the page, the same files fetched), DELAY=<seed>
(30% of the world files held back 0.2-2 s: shows what depends on load order -
gotcha 83). "frame" = one frame of the view, drawn and waited for (20, each
followed by a pixel read back), "submitting" = the page's own time per frame,
"triangles" = what the view sends to draw.

seed: registers each runner on the board of the spot's current route and
uploads their best run with the spot's [segment] rebuilt from the live page
(the route's fingerprint comes from the local site's answer) - the 3D fit
then matches the live one. The world: aerial-upload.py --world to the same site.
"""
import collections
import glob
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request

SITE = os.environ.get("SITE", "http://localhost:5080")
SPOT = os.environ.get("SPOT", "s-82bcc808d1c3")
LIVE = "https://forest.deter.cloud"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "site-look-shots")
UA = {"User-Agent": "ForestOverlay-site-measure/1.0 (+https://github.com/1deter/forest-speedrun-tool)"}


def get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA)) as r:
        return r.read().decode("utf-8")


def post(url, body, ctype="application/json", token=None):
    req = urllib.request.Request(url, data=body.encode("utf-8"), headers=dict(UA, **{"Content-Type": ctype}), method="POST")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req) as r:
            return r.status, r.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8")


def seed(site):
    spot = json.loads(get("%s/api/spots/%s?n=%d" % (LIVE, SPOT, time.time())))
    route = spot["routes"][0]
    seg = "\n".join(["[segment]", "id       = " + SPOT, "name     = " + spot["name"], "category = " + spot["category"],
                     "spawn    = %.2f %.2f %.2f 0.00 0.00" % tuple(route["spawn"]),
                     "start    = " + route["start"]["text"], "end      = " + route["end"]["text"], "notes    = " + spot["notes"]])
    for b in route["board"]:
        run = get("%s/api/runs/%d/file" % (LIVE, b["id"]))
        st, body = post(site + "/api/register", json.dumps({"runner": b["runner"], "name": b["name"]}))
        token = json.loads(body)["token"] if st == 200 else None
        for _ in range(2):
            st, body = post(site + "/api/runs", "ForestOverlay segment 1\n\n" + seg + "\n\n[attempt]\n" + run, "text/plain", token)
            m = re.search(r"the segment is ([0-9a-f]+)", body)
            if st == 200 or not m:
                break
            run = re.sub(r"^route\|.*$", "route|" + m.group(1), run, flags=re.M)
        print(b["name"], st, body[:200])


def browser(p):
    linux = sorted(glob.glob("/opt/pw-browsers/chromium-*/chrome-linux/chrome"))
    if linux:
        return p.chromium.launch(executable_path=linux[-1], args=["--use-gl=angle", "--use-angle=swiftshader",
                                                                   "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist"])
    return p.chromium.launch(channel="msedge", headless=True, args=["--use-angle=d3d11", "--enable-gpu", "--ignore-gpu-blocklist"])


def settle(page):
    page.wait_for_timeout(1200)
    for _ in range(240):
        if not page.evaluate("(() => { const s = document.querySelector('.map3dworldnote'); return s && s.textContent; })()"):
            break
        page.wait_for_timeout(250)
    page.wait_for_timeout(int(os.environ.get("EXTRA", "2500")))


def view(tag, mode, views):
    from playwright.sync_api import sync_playwright
    os.makedirs(OUT, exist_ok=True)
    with sync_playwright() as p:
        b = browser(p)
        size = {"width": 375, "height": 812} if mode in ("phone", "narrow") else {"width": 1400, "height": 900}
        ctx = b.new_context(viewport=size, device_scale_factor=2 if mode != "desktop" else 1,
                            is_mobile=mode == "phone", has_touch=mode == "phone", bypass_csp=True)
        page = ctx.new_page()
        errs, done = [], []
        page.on("pageerror", lambda e: errs.append(str(e)))
        page.on("requestfinished", lambda r: done.append(r))
        if os.environ.get("DELAY"):
            import random
            rnd = random.Random(int(os.environ["DELAY"]))

            def slow(route):
                if rnd.random() < 0.3:
                    time.sleep(rnd.uniform(0.2, 2.0))
                route.continue_()
            page.route("**/world/[pqcb]/*", slow)
        page.goto(SITE + "/spot/" + SPOT)
        page.wait_for_selector("canvas")
        page.get_by_role("button", name="3D", exact=True).click()
        page.wait_for_function("window.forest3d && window.forest3d.coarse && window.forest3d.world && window.forest3d.world.meta")
        if os.environ.get("NORUNS"):
            page.evaluate("forest3d.setRuns([], true)")
        if os.environ.get("NOFAR"):
            page.evaluate("forest3d.world.setFar(0)")
        t0 = time.time()
        for k, v in enumerate(views.split(";")):
            if v != "fit":
                page.evaluate("forest3d.lookFrom(%s)" % ",".join(str(float(a)) for a in v.split(",")))
            settle(page)
            path = os.path.join(OUT, "%s-%s-%d.png" % (tag, mode, k))
            page.screenshot(path=path, clip=page.locator("canvas:visible").first.bounding_box(), timeout=180000)
            print("shot", path)
        by, size = collections.Counter(), collections.Counter()
        for r in done:
            if "/world/" not in r.url:
                continue
            name = r.url.split("/world/")[1].split("?")[0]
            folder = name.split("/")[0] if "/" in name else name
            by[folder] += 1
            try:
                size[folder] += r.sizes()["responseBodySize"]
            except Exception:
                pass
        info = page.evaluate("""(() => { const w = forest3d.world; let tris = 0;
          // A far model is a group of InstancedMeshes (one per copy), collision a group of two.
          for (const [, m] of w.drawn) m.traverseVisible(o => { if (o.isInstancedMesh && o.geometry.index) tris += o.geometry.index.count / 3 * o.count; });
          // A pixel read back waits for every frame before it (gl.finish does
          // not, in Chrome: 101M triangles "took" 2 ms).
          const gl = forest3d.renderer.getContext(), px = new Uint8Array(4);
          const sync = () => gl.readPixels(0, 0, 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, px);
          forest3d.render(); sync();
          const t0 = performance.now();
          for (let i = 0; i < 20; i++) { forest3d.render(); sync(); }
          const frame = (performance.now() - t0) / 20, calls = forest3d.renderer.info.render.calls;
          const c0 = performance.now();
          for (let i = 0; i < 20; i++) forest3d.render();
          const cpu = (performance.now() - c0) / 20;
          sync();
          return { chunks: [...w.chunks.values()].filter(c => c.state === 'ready').length, models: w.drawn.size, tris,
                   frame, calls, cpu }; })()""")
        print("%s %s: %d world requests, %.1f MB on the wire, %.0f s; %s" % (tag, mode, sum(by.values()), sum(size.values()) / 1e6,
              time.time() - t0, dict(sorted(by.items()))))
        print("  drawn: %d chunks, %d models, %.1fM triangles, %d draw calls, frame %.1f ms (%.1f ms submitting)" % (
            info["chunks"], info["models"], info["tris"] / 1e6, info["calls"], info["frame"], info["cpu"]))
        if errs:
            print("  page errors:", errs[:5])
        b.close()


if __name__ == "__main__":
    a = sys.argv[1:]
    if a and a[0] == "seed":
        seed(a[1] if len(a) > 1 else SITE)
    elif len(a) > 1 and a[0] == "view":
        view(a[1], a[2] if len(a) > 2 else "desktop", a[3] if len(a) > 3 else "fit")
    else:
        print(__doc__)
