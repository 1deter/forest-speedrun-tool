"""Bake the game's aerial capture into map tiles for the website.

The capture comes from the game (v0.24.164+), over the bridge:
    call BepInEx_Manager OverlayPlugin._host._modules[8].AerialStart x0 z0 x1 z1 tile settle rangeScale sunTime
which writes BepInEx/config/ForestOverlay/aerial/{tiles.txt, canopy/, ground/}:
one <ix>_<iz>.jpg per tile (north up), tile (ix, iz) covering
x0 + ix*tile .. +tile, z0 + iz*tile .. +tile.

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
import math
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_CAPTURE = os.path.join(os.environ.get("FOREST_ROOT", r"G:\SteamLibrary\steamapps\common\The Forest"),
                               "BepInEx", "config", "ForestOverlay", "aerial")
DEFAULT_OUT = os.path.join(ROOT, "site", "aerial-out")      # not in git; uploaded to the site
WEB = 256
MAX_LEVEL = 6                  # 3500 m / (256 * 64) = 0.21 m per pixel
QUALITY = 82


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

    for layer in ("canopy", "ground"):
        src = os.path.join(capture, layer)
        if not os.path.isdir(src):
            continue
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
        json.dump({"levels": MAX_LEVEL, "tile": WEB, "layers": ["canopy", "ground"]}, f)
        f.write("\n")


if __name__ == "__main__":
    main()
