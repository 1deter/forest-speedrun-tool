"""Bake the game's aerial capture into map tiles for the website.

The capture comes from the game (v0.24.164+), over the bridge:
    call BepInEx_Manager OverlayPlugin._host._modules[8].AerialStart x0 z0 x1 z1 tile settle rangeScale sunTime
which writes BepInEx/config/ForestOverlay/aerial/{tiles.txt, canopy/, ground/}:
one <ix>_<iz>.jpg per tile (north up), tile (ix, iz) covering
x0 + ix*tile .. +tile, z0 + iz*tile .. +tile. (v0.24.170-177 also wrote
canopy-dry/ ground-dry/ - ignored: the game's ocean never shows in the
capture, so those were the same pictures.)

The water is drawn here, from the terrain's heights (terrain.json +
heights.u16): "canopy" / "ground" get the sea over every pixel whose ground
is under sea level, shading with depth; "canopy-dry" / "ground-dry" are the
capture as it is (the map's Water button switches between them).

    python scripts/aerial-bake.py [capture folder] [out folder]

writes a tile pyramid: <out>/<layer>/<L>/<tx>_<ty>.jpg, 256 px each. Level L
has 2^L x 2^L tiles over the terrain's square (terrain.json: x0, z0, sizeX),
tx east from x0, ty south from the north edge. Tiles with nothing captured
are not written (the map draws the relief under them). The capture is
expected on the pyramid's own grid (tile = sizeX / 2^k, origin = the
terrain's corner) so every capture tile lands on whole web tiles; anything
else is resampled.

Needs numpy + pillow. Dev only.
"""
import json
import time
import math
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_CAPTURE = os.path.join(os.environ.get("FOREST_ROOT", r"G:\SteamLibrary\steamapps\common\The Forest"),
                               "BepInEx", "config", "ForestOverlay", "aerial")
DEFAULT_OUT = os.path.join(ROOT, "site", "aerial-out")      # not in git; uploaded to the site
WEB = 256
MAX_LEVEL = 6                  # 3500 m / (256 * 64) = 0.21 m per pixel
QUALITY = 82

# The sea from above, as the game draws it straight down at midday-ish light
# (bridge shots, 2026-10-01): a dark teal, the floor showing through only
# in the first metres.
DEEP = np.array([16, 44, 52], np.float32)
SHALLOW = np.array([64, 128, 126], np.float32)
FOAM = np.array([225, 232, 228], np.float32)


def row_steps(src, tiles):
    """Brightness steps between capture rows, as warnings. The capture holds
    one exposure, but the game's light can still change during the ~20 min
    run: v0.24.178's came out 1.6x darker from its 8th row on (taken in
    order, so a straight line across the map) and those rows were taken
    again. A step shows at a row boundary: the median, over the columns, of
    the brightness ratio across the seam (4 px strips of each tile at
    128 px; snow - clipped - and near black edges left out). Not corrected
    here: the game's tonemapping bends a light change, and a plain gain
    over- or under-shoots - retake the rows from the step on."""
    S, E = 128, 4
    edge = {}
    for ix, iz in tiles:
        p = os.path.join(src, "%d_%d.jpg" % (ix, iz))
        if not os.path.exists(p):
            continue
        a = np.asarray(Image.open(p).convert("RGB").resize((S, S), Image.BILINEAR), np.float32) + 4.0
        edge[(ix, iz)] = (a[:E].mean((0, 1)), a[-E:].mean((0, 1)))     # north (image top), south
    usable = lambda v: v.max() < 225 and v.min() > 16
    steps = []
    for iz in sorted({iz for _, iz in edge})[1:]:
        r = [np.log(edge[(ix, iz)][1] / edge[(ix, iz - 1)][0]).mean()
             for ix, z in edge if z == iz - 1 and (ix, iz) in edge
             and usable(edge[(ix, iz)][1]) and usable(edge[(ix, iz - 1)][0])]
        if len(r) >= 3 and abs(np.median(r)) > 0.12:
            steps.append((iz, float(np.exp(np.median(r)))))
    return steps


def load_heights(terrain):
    """The terrain's heights as world y, [z row][x column] (map.js groundAt),
    and the sea's depth there: sea level minus the ground, only where the
    water is open to the map's edge (the sinkhole and other pits below sea
    level stay dry), 0 elsewhere."""
    path = os.path.join(ROOT, "site", "ForestSite", "wwwroot", "terrain", terrain["heights"])
    g = terrain["grid"]
    h = np.fromfile(path, dtype="<u2").astype(np.float32).reshape(g, g)
    h = terrain["y0"] + h / 65535.0 * terrain["sizeY"]
    under = h < terrain["sea"]
    sea = np.zeros_like(under)
    stack = [(j, i) for j in range(g) for i in (0, g - 1)] + [(j, i) for i in range(g) for j in (0, g - 1)]
    while stack:
        j, i = stack.pop()
        if sea[j, i] or not under[j, i]:
            continue
        sea[j, i] = True
        if j > 0: stack.append((j - 1, i))
        if j < g - 1: stack.append((j + 1, i))
        if i > 0: stack.append((j, i - 1))
        if i < g - 1: stack.append((j, i + 1))
    # A sample just above the sea beside open water keeps its (negative)
    # depth, so the shore fades between samples instead of stepping.
    near = sea.copy()
    near[1:, :] |= sea[:-1, :]; near[:-1, :] |= sea[1:, :]
    near[:, 1:] |= sea[:, :-1]; near[:, :-1] |= sea[:, 1:]
    depth = np.where(near, terrain["sea"] - h, -10.0).astype(np.float32)
    return depth


def sample(grid, terrain, x, z):
    """Bilinear sample of a terrain-grid array at world x, z (arrays)."""
    n = terrain["grid"] - 1
    u = np.clip((x - terrain["x0"]) / terrain["sizeX"] * n, 0, n)
    v = np.clip((z - terrain["z0"]) / terrain["sizeZ"] * n, 0, n)
    i = np.minimum(np.floor(u).astype(np.int32), n - 1)
    j = np.minimum(np.floor(v).astype(np.int32), n - 1)
    fu, fv = u - i, v - j
    top = grid[j, i] * (1 - fu) + grid[j, i + 1] * fu
    bottom = grid[j + 1, i] * (1 - fu) + grid[j + 1, i + 1] * fu
    return top * (1 - fv) + bottom * fv


def add_water(img, sea_depth, terrain, west, north, tile):
    """The sea over a capture tile resized to img's size (west / north edge, metres)."""
    w = img.width
    step = tile / w
    xs = west + (np.arange(w, dtype=np.float32) + 0.5) * step
    zs = north - (np.arange(w, dtype=np.float32) + 0.5) * step
    depth = sample(sea_depth, terrain, xs[None, :], zs[:, None])
    if depth.max() <= 0:
        return img
    d = np.clip(depth, 0, None)[..., None]
    colour = SHALLOW + (DEEP - SHALLOW) * np.clip(d / 12.0, 0, 1)
    alpha = np.where(d > 0, 0.55 + 0.42 * (1 - np.exp(-d / 3.0)), 0)
    foam = np.where((d > 0) & (d < 0.35), 0.5 * (1 - d / 0.35), 0)
    a = np.asarray(img, np.float32)
    a = a * (1 - alpha) + colour * alpha
    a = a * (1 - foam) + FOAM * foam
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


def read_index(path):
    info, tiles = {}, []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if " = " in line:
                k, v = line.split(" = ", 1)
                info[k] = v
            elif line:
                ix, iz = line.split(",")
                tiles.append((int(ix), int(iz)))
    ox, oz = (float(v) for v in info["origin"].split(","))
    return float(info["tile"]), ox, oz, tiles


def main():
    capture = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_CAPTURE
    out = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_OUT
    with open(os.path.join(ROOT, "site", "ForestSite", "wwwroot", "terrain", "terrain.json"), encoding="utf-8") as f:
        terrain = json.load(f)
    tx0, tz0, size = terrain["x0"], terrain["z0"], terrain["sizeX"]
    top_z = tz0 + size
    tile, ox, oz, tiles = read_index(os.path.join(capture, "tiles.txt"))
    n = 2 ** MAX_LEVEL
    mpp = size / (n * WEB)              # metres per pixel at the top level
    # Where a parent has no captured child (open sea), the relief fills in.
    relief = Image.open(os.path.join(ROOT, "site", "ForestSite", "wwwroot", "terrain", terrain["image"])).convert("RGB")

    def relief_tile(level, tx, ty):
        k = relief.width / 2 ** level
        return relief.crop((round(tx * k), round(ty * k), round((tx + 1) * k), round((ty + 1) * k))).resize((WEB, WEB), Image.BILINEAR)

    sea_depth = load_heights(terrain)
    for iz, k in row_steps(os.path.join(capture, "canopy"), tiles):
        print("WARNING: capture row %d is %.2fx as bright as row %d below it - the light changed mid-capture; "
              "retake rows %d+ (docs/website.md, The photo map)" % (iz, k, iz - 1, iz))
    layers = []
    for layer in ("canopy", "ground", "canopy-dry", "ground-dry"):
        dry = layer.endswith("-dry")
        src = os.path.join(capture, layer[:-4] if dry else layer)
        if not os.path.isdir(src) or not os.listdir(src):
            continue
        layers.append(layer)
        written = set()
        # Top level: each capture tile resized to its size at mpp, cut into web tiles.
        for ix, iz in tiles:
            path = os.path.join(src, "%d_%d.jpg" % (ix, iz))
            if not os.path.exists(path):
                continue
            cx0, cz1 = ox + ix * tile, oz + (iz + 1) * tile          # west, north edges
            px = tile / mpp
            left, top = (cx0 - tx0) / mpp, (top_z - cz1) / mpp        # in top-level pixels
            img = Image.open(path).convert("RGB").resize((round(px), round(px)), Image.LANCZOS)
            if not dry:
                img = add_water(img, sea_depth, terrain, cx0, cz1, tile)
            for tx in range(int(left // WEB), int(math.ceil((left + px) / WEB))):
                for ty in range(int(top // WEB), int(math.ceil((top + px) / WEB))):
                    if not (0 <= tx < n and 0 <= ty < n):
                        continue
                    key = (tx, ty)
                    tpath = os.path.join(out, layer, str(MAX_LEVEL), "%d_%d.jpg" % key)
                    web = Image.open(tpath).convert("RGB") if key in written else Image.new("RGB", (WEB, WEB), (0, 0, 0))
                    web.paste(img, (round(left - tx * WEB), round(top - ty * WEB)))
                    os.makedirs(os.path.dirname(tpath), exist_ok=True)
                    web.save(tpath, quality=QUALITY)
                    written.add(key)
        # Lower levels: 2 x 2 children -> one parent, halved.
        have = written
        for level in range(MAX_LEVEL - 1, -1, -1):
            parents = {(tx // 2, ty // 2) for tx, ty in have}
            for px_, py_ in parents:
                canvas = Image.new("RGB", (WEB * 2, WEB * 2), (0, 0, 0))
                for dx in (0, 1):
                    for dy in (0, 1):
                        c = (px_ * 2 + dx, py_ * 2 + dy)
                        child = Image.open(os.path.join(out, layer, str(level + 1), "%d_%d.jpg" % c)) if c in have                             else relief_tile(level + 1, *c)
                        canvas.paste(child, (dx * WEB, dy * WEB))
                p = os.path.join(out, layer, str(level), "%d_%d.jpg" % (px_, py_))
                os.makedirs(os.path.dirname(p), exist_ok=True)
                canvas.resize((WEB, WEB), Image.LANCZOS).save(p, quality=QUALITY)
            have = parents
        print(layer, len(written), "top-level tiles")

    with open(os.path.join(out, "aerial.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({"levels": MAX_LEVEL, "tile": WEB, "layers": layers, "build": int(time.time())}, f)   # build: the tiles' ?v=
        f.write("\n")


if __name__ == "__main__":
    main()
