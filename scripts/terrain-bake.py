"""Bake the game's terrain dump into the website's map files.

The dump comes from the game (v0.24.162+), over the bridge:
    call static:ForestOverlay.Game.TerrainDump Write
which writes BepInEx/config/ForestOverlay/terrain/{terrain.txt, heights.u16, alpha.u8}.

    python scripts/terrain-bake.py [dump folder]

writes site/ForestSite/wwwroot/terrain/:
    map.jpg          colour relief, heightmap resolution - 1 pixels, north up
    heights.u16      a 1025^2 height grid for the 3D view (uint16 of size.y,
                     row 0 = z min, x east along a row)
    terrain.json     where both sit in the world, the sea level

Needs numpy and pillow (pip install --user numpy pillow). Dev only.
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

SEA_LEVEL = 41.5          # Ceto Ocean.level, read live 2026-09-27
GRID_3D = 1025            # 3D heights: every second sample of the 2049^2 map
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_DUMP = os.path.join(os.environ.get("FOREST_ROOT", r"G:\SteamLibrary\steamapps\common\The Forest"),
                            "BepInEx", "config", "ForestOverlay", "terrain")
OUT = os.path.join(ROOT, "site", "ForestSite", "wwwroot", "terrain")


def read_info(path):
    info, layers = {}, []
    with open(path, encoding="utf-8") as f:
        for line in f:
            key, _, value = line.strip().partition(" = ")
            if key.startswith("layer "):
                parts = [p.strip() for p in value.split("|")]
                rgb = [int(v) for v in parts[2].split()[1].split(",")]
                layers.append({"name": parts[0], "rgb": rgb})
            elif key:
                info[key] = value
    vec = lambda s: [float(v) for v in s.split(",")]
    return {
        "size": vec(info["size"]), "position": vec(info["position"]),
        "hres": int(info["heightmapResolution"]), "ares": int(info["alphamapResolution"]),
        "layers": layers,
    }


def main():
    dump = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_DUMP
    info = read_info(os.path.join(dump, "terrain.txt"))
    size, pos, hres, ares = info["size"], info["position"], info["hres"], info["ares"]
    heights = np.fromfile(os.path.join(dump, "heights.u16"), dtype="<u2").reshape(hres, hres)
    h = heights.astype(np.float32) / 65535.0 * size[1]          # metres above the terrain's y

    # Ground colour: the splat weights x each texture's average colour.
    nl = len(info["layers"])
    if nl:
        alpha = np.fromfile(os.path.join(dump, "alpha.u8"), dtype=np.uint8).reshape(ares, ares, nl).astype(np.float32) / 255.0
        cols = np.array([l["rgb"] for l in info["layers"]], dtype=np.float32) / 255.0
        ground = alpha @ cols                                       # ares x ares x 3
        w = alpha.sum(axis=2, keepdims=True)
        ground = np.where(w > 0.01, ground / np.maximum(w, 1e-3), 0.35)
    else:
        ground = np.full((ares, ares, 3), 0.35, dtype=np.float32)
    n = hres - 1
    img = Image.fromarray((np.clip(ground, 0, 1) * 255).astype(np.uint8)).resize((n, n), Image.BICUBIC).filter(ImageFilter.GaussianBlur(3))
    colour = np.asarray(img).astype(np.float32) / 255.0
    # Lift the textures' dark averages so the map reads on a dark page.
    colour = np.clip(colour * 1.15 + 0.02, 0, 1)

    # Hillshade, sun from the north-west, 45 degrees up.
    hh = h[:n, :n]
    cell = size[0] / n
    dzdx = np.gradient(hh, cell, axis=1)
    dzdz = np.gradient(hh, cell, axis=0)
    nx, nz, ny = -dzdx, -dzdz, np.ones_like(hh)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    sun = np.array([-1.0, 1.4, 1.0]); sun /= np.linalg.norm(sun)      # x east, y up, z north
    shade = np.clip((nx * sun[0] + ny * sun[1] + nz * sun[2]) / length, 0, 1)
    rgb = colour * (0.3 + 0.7 * shade[..., None])

    # Water: below the sea, deeper = darker blue.
    world_y = hh + pos[1]
    depth = np.clip((SEA_LEVEL - world_y) / 40.0, 0, 1)[..., None]
    water = (world_y < SEA_LEVEL)[..., None]
    sea = np.array([0.10, 0.22, 0.32]) * (1 - 0.6 * depth) + 0.05 * shade[..., None]
    rgb = np.where(water, sea, rgb)

    # Row 0 of the heights is z min (south); an image's row 0 is the top (north).
    out = (np.clip(rgb, 0, 1) * 255).astype(np.uint8)[::-1]
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray(out).save(os.path.join(OUT, "map.jpg"), quality=86, optimize=True, progressive=True)

    step = (hres - 1) // (GRID_3D - 1)
    heights[::step, ::step].astype("<u2").tofile(os.path.join(OUT, "heights.u16"))

    meta = {
        "x0": pos[0], "z0": pos[2], "y0": pos[1],
        "sizeX": size[0], "sizeY": size[1], "sizeZ": size[2],
        "sea": SEA_LEVEL,
        "image": "map.jpg",
        "heights": "heights.u16", "grid": GRID_3D,
        "layers": [l["name"] for l in info["layers"]],
    }
    with open(os.path.join(OUT, "terrain.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f, indent=1)
        f.write("\n")
    print("baked", n, "px map,", GRID_3D, "^2 heights ->", OUT)


if __name__ == "__main__":
    main()
