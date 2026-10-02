"""Headless look at a spot page of a local (or the live) site - docs/website.md
*Looking at a spot*. Edge through Playwright (pip install playwright), the CSP
bypassed so page scripts can be evaluated; shots go to scripts/site-look-shots/
(not in git). Prints page errors at the end.

    python scripts/site-look.py <scenario> [name] [args]

Scenarios (Unity coordinates; yaw in degrees for lookFrom, radians for orbit):
  2d                        the 2D map: island view, zoom-at-pointer and drag checks
  3dfit                     the 3D view's own fit, then the whole island
  view                      the page's 3D view and four turns (KEEPRUNS=1 keeps the runs)
  edge                      the orbit's centre past the map's edges: counts patch rebuilds
  pan                       the camera panned along the coast: counts rebuilds
  frames x y z dist yaw pitch step   six frames, the orbit moved step m east each
  lake x y z yaw pitch dist  lookFrom; shots with the lakes' ground clip on / off, Water off
  orbit x y z dist yaw pitch the orbit itself; JS="expr;;expr" shot after each,
                            MODELSOFF=1 shots with each kind off (Trees / Rocks / Props / Pickups) and all, HIDE=model ids
  eval x y z yaw pitch dist "<js>"   lookFrom, settle, print the expression (ORBIT=x,y,z,dist,yaw,pitch;
                            SHOT=1 a shot after it)

Env: SITE (default http://localhost:5080), SPOT (a spot id; default the Labskip
test spot), KEEPRUNS=1 (the spot's runs stay - an underground spot fades the
terrain), COARSE=1 (the detail patch pinned in a far corner: the island mesh).
"""
import sys, os, time, json
from playwright.sync_api import sync_playwright

SITE = os.environ.get("SITE", "http://localhost:5080")
SPOT = "/spot/" + os.environ.get("SPOT", "s-82bcc808d1c3")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "site-look-shots")
os.makedirs(OUT, exist_ok=True)


def shot(page, name, el="canvas:visible"):
    path = os.path.join(OUT, name + ".png")
    page.locator(el).first.screenshot(path=path)
    print("shot", path)


def open3d(page):
    page.goto(SITE + SPOT)
    page.wait_for_selector("canvas")
    page.get_by_role("button", name="3D", exact=True).click()
    page.wait_for_function("window.forest3d && window.forest3d.coarse")
    if not os.environ.get("KEEPRUNS"):
        page.evaluate("forest3d.setRuns([], true)")   # the test spot is underground: no fade
    page.wait_for_timeout(1500)


def settle(page, extra=1500):
    # no chunk loading, then a moment for textures
    for _ in range(120):
        busy = page.evaluate("(() => { const s = document.querySelector('.map3dworldnote'); return s && s.textContent; })()")
        if not busy:
            break
        page.wait_for_timeout(250)
    page.wait_for_timeout(extra)


def count_regions(page):
    page.evaluate("""(() => { const v = window.forest3d; if (v._counted) return; v._counted = true; v.regions = 0;
      const o = v.setRegion.bind(v); v.setRegion = r => { v.regions++; o(r); };
      v.islands = 0; if (v.textures && !v.islandTexture) { const ot = v.textures.bind(v); v.textures = () => { v.islands++; ot(); }; } if (v.islandTexture) { const oi = v.islandTexture.bind(v); v.islandTexture = () => { v.islands++; oi(); }; } })()""")


def main():
    sc = sys.argv[1]
    pre = sys.argv[2] if len(sys.argv) > 2 else sc
    with sync_playwright() as p:
        b = p.chromium.launch(channel="msedge", headless=True, args=["--use-angle=d3d11", "--enable-gpu", "--ignore-gpu-blocklist"])
        page = b.new_context(bypass_csp=True, viewport={"width": 1400, "height": 1000}).new_page()
        errors = []
        page.on("pageerror", lambda e: errors.append(str(e)))
        page.on("console", lambda m: errors.append("console " + m.type + ": " + m.text) if m.type in ("error", "warning") else None)
        if sc == "2d":
            page.goto(SITE + SPOT)
            page.wait_for_selector("canvas")
            page.wait_for_timeout(2000)
            page.evaluate("(() => { const m = [...RunMap.terrain.maps][0]; window.m2 = m; m.setRuns([]); m.view = { cx: 0, cz: 0, scale: 0.26 }; m.draw(); })()")
            page.wait_for_timeout(3000)
            shot(page, pre + "-island")
            c = page.locator("canvas:visible").first
            box = c.bounding_box()
            # zoom at a point: the world point under the pointer must stay
            px, py = 250, 150
            before = page.evaluate(f"m2.fromScreen({px}, {py})")
            page.mouse.move(box["x"] + px, box["y"] + py)
            for _ in range(4):
                page.mouse.wheel(0, -300)
                page.wait_for_timeout(60)
            after = page.evaluate(f"m2.fromScreen({px}, {py})")
            print("zoom-at point world before", before, "after", after, "scale", page.evaluate("m2.view.scale"))
            # drag: the ground under the pointer moves with it
            a = page.evaluate(f"m2.fromScreen({px}, {py})")
            page.mouse.move(box["x"] + px, box["y"] + py); page.mouse.down(); page.mouse.move(box["x"] + px + 200, box["y"] + py + 100, steps=8); page.mouse.up()
            b2 = page.evaluate(f"m2.fromScreen({px + 200}, {py + 100})")
            print("drag: world under pointer before", a, "after (moved point)", b2)
            page.wait_for_timeout(2500)
            shot(page, pre + "-zoomed")
            for lx, lz in [(-1300, -1300), (0, 0)]:
                pass
        elif sc == "3dfit":
            open3d(page)
            settle(page)
            shot(page, pre + "-fit")
            page.evaluate("(() => { const o = forest3d.orbit; o.target.set(0, 50, 0); o.dist = 4200; o.pitch = 1.1; forest3d.dirty = true; })()")
            settle(page, 4000)
            shot(page, pre + "-island")
        elif sc == "view":
            # the page's own view (KEEPRUNS=1), then a few turns around the orbit's centre
            open3d(page)
            settle(page, 5000)
            shot(page, pre + "-0")
            for k in range(1, 5):
                page.evaluate("(() => { forest3d.orbit.yaw += 0.5; forest3d.dirty = true; })()")
                settle(page, 2500)
                shot(page, pre + "-" + str(k))
            print(page.evaluate("[forest3d.orbit.target.x, forest3d.orbit.target.y, -forest3d.orbit.target.z, forest3d.orbit.dist, forest3d.region]"))
        elif sc == "orbit":
            # x y z dist yaw pitch: the orbit itself (Unity coordinates); WATERHIDE=model ids to hide
            x, y, z, dist, yaw, pitch = map(float, sys.argv[3:9])
            open3d(page)
            page.evaluate(f"(() => {{ const o = forest3d.orbit; o.target.set({x}, {y}, {-z}); o.dist = {dist}; o.yaw = {yaw}; o.pitch = {pitch}; forest3d.dirty = true; }})()")
            settle(page, 5000)
            shot(page, pre + "-a")
            for k, js in enumerate(os.environ.get("JS", "").split(";;")):
                if not js:
                    continue
                print("js", k, page.evaluate(js))
                page.wait_for_timeout(1500)
                shot(page, pre + "-js" + str(k))
            if os.environ.get("MODELSOFF"):
                # each kind off in turn (world3d.js KINDS), then all four
                kinds = ["Trees", "Rocks", "Props", "Pickups"]
                for k in kinds:
                    page.get_by_role("button", name=k, exact=True).click()
                    page.wait_for_timeout(1500)
                    shot(page, pre + "-no" + k.lower())
                    page.get_by_role("button", name=k, exact=True).click()
                    page.wait_for_timeout(300)
                print("kinds", page.evaluate("(() => { const w = forest3d.world, c = {}; for (const [mi] of w.drawn) { const k = w.kinds[mi]; c[k] = (c[k] || 0) + 1; } return c; })()"))
                for k in kinds:
                    page.get_by_role("button", name=k, exact=True).click()
                page.wait_for_timeout(1500)
                shot(page, pre + "-nomodels")
                for k in kinds:
                    page.get_by_role("button", name=k, exact=True).click()
                page.wait_for_timeout(500)
            if os.environ.get("HIDE"):
                page.evaluate("(() => { const w = forest3d.world; for (const id of '" + os.environ["HIDE"] + "'.split(',')) { const m = w.drawn.get(+id); if (m) m.visible = false; } forest3d.dirty = true; })()")
                page.wait_for_timeout(800)
                shot(page, pre + "-hidden")
            page.evaluate("forest3d.world.ground.groundOn.value = 0; forest3d.dirty = true")
            page.wait_for_timeout(800)
            shot(page, pre + "-noclip")
        elif sc == "frames":
            # x y z dist yaw pitch step: the orbit moved by step metres east per frame, a shot each
            x, y, z, dist, yaw, pitch, step = map(float, sys.argv[3:10])
            open3d(page)
            page.evaluate(f"(() => {{ const o = forest3d.orbit; o.target.set({x}, {y}, {-z}); o.dist = {dist}; o.yaw = {yaw}; o.pitch = {pitch}; forest3d.dirty = true; }})()")
            settle(page, 5000)
            count_regions(page)
            for k in range(6):
                page.evaluate(f"(() => {{ forest3d.orbit.target.x += {step}; forest3d.dirty = true; }})()")
                page.wait_for_timeout(120)
                shot(page, f"{pre}-{k}")
            print("rebuilds while moving", page.evaluate("[forest3d.regions, forest3d.islands]"))
        elif sc == "edge":
            # Centre past the map's east edge, close: the patch must not be rebuilt in a loop.
            open3d(page)
            count_regions(page)
            for i, (x, y, z, yaw, pitch, dist) in enumerate([(2100, 300, 300, 270, 30, 300), (1900, 300, -1900, 315, 30, 300), (300, 300, 2100, 180, 30, 300)]):
                page.evaluate(f"forest3d.lookFrom({x}, {y}, {z}, {yaw}, {pitch}, {dist})")
                page.wait_for_timeout(1500)
                r0 = page.evaluate("[forest3d.regions, forest3d.islands]")
                page.wait_for_timeout(4000)
                r1 = page.evaluate("[forest3d.regions, forest3d.islands, !!forest3d.looking]")
                print(f"edge {i}: setRegion / islandTexture after 1.5 s {r0}, after 5.5 s {r1}")
                shot(page, f"{pre}-{i}")
        elif sc == "pan":
            # Pan along the coast in steps (a camera moving): count rebuilds.
            open3d(page)
            count_regions(page)
            page.evaluate("forest3d.lookFrom(2100, 300, 600, 270, 30, 300)")
            page.wait_for_timeout(3000)
            n0 = page.evaluate("forest3d.regions")
            for k in range(30):
                page.evaluate(f"(() => {{ forest3d.orbit.target.z -= 10; forest3d.dirty = true; }})()")
                page.wait_for_timeout(100)
            page.wait_for_timeout(2000)
            print("pan: rebuilds", n0, "->", page.evaluate("forest3d.regions"), "islands", page.evaluate("forest3d.islands"))
            shot(page, pre)
        elif sc == "lake":
            # args: x y z yaw pitch dist
            x, y, z, yaw, pitch, dist = map(float, sys.argv[3:9])
            open3d(page)
            if os.environ.get("COARSE"):
                # the lakes on the coarse mesh: the patch pinned in a far corner
                page.evaluate("(() => { forest3d.lookRegion = () => {}; forest3d.setRegion(forest3d.regionAt(60, 60, 256)); })()")
            if os.environ.get("NOMODELS"):
                page.evaluate("(() => { const w = forest3d.world; for (const [mi, mesh] of w.drawn) { const mats = [].concat(mesh.material); if (!mats.some(m => w.waterMats.has(m))) mesh.visible = false; } w.wanted = mo => false; })()")
            page.evaluate(f"forest3d.lookFrom({x}, {y}, {z}, {yaw}, {pitch}, {dist})")
            settle(page, 3500)
            if os.environ.get("NOMODELS"):
                page.evaluate("(() => { const w = forest3d.world; for (const [mi, mesh] of w.drawn) { const mats = [].concat(mesh.material); mesh.visible = mats.some(m => w.waterMats.has(m)); } forest3d.dirty = true; })()")
                page.wait_for_timeout(500)
            shot(page, pre + "-clip")
            page.evaluate("forest3d.world.ground.groundOn.value = 0; forest3d.dirty = true")
            page.wait_for_timeout(800)
            shot(page, pre + "-noclip")
            page.evaluate("forest3d.world.ground.groundOn.value = 1; forest3d.dirty = true")
            page.get_by_role("button", name="Water", exact=True).click()
            page.wait_for_timeout(3500)
            print("water off: sea visible", page.evaluate("forest3d.sea.visible"), "layer", page.evaluate("forest3d.layer"))
            shot(page, pre + "-dry")
            page.get_by_role("button", name="Water", exact=True).click()
            page.wait_for_timeout(500)
        if sc == "eval":
            x, y, z, yaw, pitch, dist = map(float, sys.argv[3:9])
            open3d(page)
            page.evaluate(f"forest3d.lookFrom({x}, {y}, {z}, {yaw}, {pitch}, {dist})")
            settle(page, 3000)
            if os.environ.get("ORBIT"):
                page.evaluate("(() => { const a = '" + os.environ["ORBIT"] + "'.split(',').map(Number); const o = forest3d.orbit; o.target.set(a[0], a[1], -a[2]); o.dist = a[3]; o.yaw = a[4]; o.pitch = a[5]; forest3d.dirty = true; })()")
                settle(page, 4000)
            print(json.dumps(page.evaluate(sys.argv[9]), indent=1)[:4000])
            if os.environ.get("SHOT"):
                # SHOT=1: a shot after the expression (e.g. the game's FOV, 95)
                page.evaluate("forest3d.dirty = true")
                page.wait_for_timeout(1500)
                shot(page, pre)
        print("errors:", errors[:10])
        b.close()


main()
