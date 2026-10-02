"""Bakes the caves' floor plan for the 2D map (site/ForestSite/wwwroot/terrain/
caves.webp + caves.json): the 3D world's cave and endgame collision meshes
(scripts/world-extract.py export, version 3) rasterised from above.

A floor is an up-facing collision face whose next face straight above it is
a down-facing one (a ceiling seen from below) at least CLEAR metres up (the
collision pieces are open surfaces, stacked: a floor over the top of a
lower one). The pieces overlap a lot, leaving air slivers between them that
pass that test (a 2.4 m gap just under the surface): every pixel keeps its
LAYERS highest floors, neighbours within STEP metres of height join into
one walkable region, and regions under MIN_AREA square metres are dropped.
Where caves stack, the highest floor left wins.
The image: floors shaded by their slope and coloured by height (lighter =
higher), walls as a dark edge, the rest transparent; lossy WebP (1 px a
metre: 0.7 MB, a PNG was 3.8 MB). map.js draws it while the map is
underground; caves.json's "build" (a hash of the picture) is its ?v=.

    python scripts/cave-bake.py [world folder] [px per metre]
        defaults: site/world-v3, 1 (2 takes 4x the memory and bytes)

numpy + pillow.
"""
import hashlib
import json
import os
import struct
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "site", "ForestSite", "wwwroot", "terrain")
AREAS = ("caves", "endgame")
TILE = 128.0          # metres: the work is done a square at a time (memory)
CLEAR = 2.0           # metres of room over a floor (1.2 kept thin gaps between pieces)
STEP = 1.0            # metres of height between neighbouring floor pixels a metre apart
LAYERS = 4            # floors kept per pixel, highest first
MIN_AREA = 300.0      # square metres: a smaller walkable region is a gap between pieces
FACE_NY = 0.02        # faces steeper than this are walls: no height layer
REACH = 2500.0        # metres from the map's centre: the endgame lab is at z -2290


def load(folder):
    meta = json.load(open(os.path.join(folder, "world.json")))
    assert meta.get("version") == 3, "a version 3 world (files + at)"
    files = {}

    def blob(at):
        f, o, n = at
        if f not in files:
            files[f] = open(os.path.join(folder, meta["files"][f]), "rb").read()
        return files[f][o:o + n]

    meshes = {}

    def mesh(i):
        if i not in meshes:
            m = meta["meshes"][i]
            b = blob(m["at"])
            v = np.frombuffer(b, "<f4", m["nv"] * 3).reshape(-1, 3)
            at = m["nv"] * 12 + (m["nv"] * 8 if m["uv"] else 0) + (m["nv"] * 12 if m.get("n") else 0)
            idx = np.frombuffer(b, "<u4" if m["i32"] else "<u2", m["ni"], at).reshape(-1, 3).astype(np.int64)
            meshes[i] = (v.astype(np.float64), idx)
        return meshes[i]

    tris = []
    for c in meta["chunks"]:
        if c["area"] not in AREAS:
            continue
        b = blob(c["at"])
        n = len(b) // 52
        u = np.frombuffer(b, "<u4", n * 13).reshape(n, 13)
        f = np.frombuffer(b, "<f4", n * 13).reshape(n, 13)
        for k in range(n):
            model = meta["models"][u[k, 0]]
            if model["kind"] != "collide":
                continue
            v, idx = mesh(model["mesh"])
            mat = f[k, 1:13].astype(np.float64).reshape(3, 4)
            w = v @ mat[:, :3].T + mat[:, 3]
            t = w[idx]
            # Unity's front faces wind clockwise: cross(b - a, c - a) is the
            # outward normal; a mirrored instance turns it inside out.
            if np.linalg.det(mat[:, :3]) < 0:
                t = t[:, [0, 2, 1]]
            tris.append(t)
    t = np.concatenate(tris).astype(np.float32)
    # A few collision pieces sit kilometres off the map (x -17840): left out.
    near = (np.abs(t[:, :, [0, 2]]) <= REACH).all(axis=(1, 2))
    return t[near]


def raster(tris, px, x0, z0, nx, nz):
    """Every (pixel, height, facing) sample of the triangles over the nx x nz
    pixel grid at (x0, z0), px metres a pixel: facing 1 up, 0 down."""
    a, b, c = tris[:, 0].astype(np.float64), tris[:, 1].astype(np.float64), tris[:, 2].astype(np.float64)
    nrm = np.cross(b - a, c - a)
    ln = np.linalg.norm(nrm, axis=1)
    ok = ln > 1e-9
    ny = np.where(ok, nrm[:, 1] / np.where(ok, ln, 1), 0)
    keep = np.abs(ny) > FACE_NY
    a, b, c, ny = a[keep], b[keep], c[keep], ny[keep]
    # Pixel space: centres at integers.
    ax, az = (a[:, 0] - x0) / px - 0.5, (a[:, 2] - z0) / px - 0.5
    bx, bz = (b[:, 0] - x0) / px - 0.5, (b[:, 2] - z0) / px - 0.5
    cx, cz = (c[:, 0] - x0) / px - 0.5, (c[:, 2] - z0) / px - 0.5
    lx = np.clip(np.ceil(np.minimum(np.minimum(ax, bx), cx)), 0, nx).astype(np.int64)
    hx = np.clip(np.floor(np.maximum(np.maximum(ax, bx), cx)), -1, nx - 1).astype(np.int64)
    lz = np.clip(np.ceil(np.minimum(np.minimum(az, bz), cz)), 0, nz).astype(np.int64)
    hz = np.clip(np.floor(np.maximum(np.maximum(az, bz), cz)), -1, nz - 1).astype(np.int64)
    w, h = hx - lx + 1, hz - lz + 1
    some = (w > 0) & (h > 0)
    side = np.maximum(w, h)
    out_p, out_h, out_u = [], [], []
    size = 1
    while True:
        sel = np.nonzero(some & (side <= size) & (side > size // 2))[0]
        if len(sel):
            oy, ox = np.mgrid[0:size, 0:size]
            ox, oy = ox.ravel(), oy.ravel()
            step = max(1, 4_000_000 // (size * size))
            for s in range(0, len(sel), step):
                t = sel[s:s + step]
                X = lx[t][:, None] + ox[None, :]
                Z = lz[t][:, None] + oy[None, :]
                inside = (X <= hx[t][:, None]) & (Z <= hz[t][:, None])
                # Barycentric weights of each pixel centre.
                e0x, e0z = bx[t] - ax[t], bz[t] - az[t]
                e1x, e1z = cx[t] - ax[t], cz[t] - az[t]
                d = e0x * e1z - e1x * e0z
                good = np.abs(d) > 1e-12
                d = np.where(good, d, 1)
                px_, pz_ = X - ax[t][:, None], Z - az[t][:, None]
                v = (px_ * e1z[:, None] - e1x[:, None] * pz_) / d[:, None]
                wv = (e0x[:, None] * pz_ - px_ * e0z[:, None]) / d[:, None]
                eps = -1e-6
                inside &= (v >= eps) & (wv >= eps) & (v + wv <= 1 - eps) & good[:, None]
                y = a[t, 1][:, None] + v * (b[t, 1] - a[t, 1])[:, None] + wv * (c[t, 1] - a[t, 1])[:, None]
                r, k = np.nonzero(inside)
                out_p.append(Z[r, k] * nx + X[r, k])
                out_h.append(y[r, k].astype(np.float32))
                out_u.append(ny[t][r] > 0)
        if not (some & (side > size)).any():
            break
        size *= 2
    if not out_p:
        return np.zeros(0, np.int64), np.zeros(0, np.float32), np.zeros(0, bool), ny
    return np.concatenate(out_p), np.concatenate(out_h), np.concatenate(out_u), ny


def floors(tris, px):
    """The LAYERS highest floors per pixel ([layer, row, column], highest
    first, NaN = none) and the grid's corner."""
    lo = tris.reshape(-1, 3).min(0)
    hi = tris.reshape(-1, 3).max(0)
    x0, z0 = np.floor(lo[0]), np.floor(lo[2])
    nx, nz = int(np.ceil((hi[0] - x0) / px)), int(np.ceil((hi[2] - z0) / px))
    grid = np.full((LAYERS, nz, nx), np.nan, np.float32)
    tmin = tris[:, :, [0, 2]].min(1)
    tmax = tris[:, :, [0, 2]].max(1)
    tp = int(TILE / px)
    for tz in range(0, nz, tp):
        for tx in range(0, nx, tp):
            wx0, wz0 = x0 + tx * px, z0 + tz * px
            wx1, wz1 = wx0 + tp * px, wz0 + tp * px
            sel = (tmax[:, 0] >= wx0) & (tmin[:, 0] <= wx1) & (tmax[:, 1] >= wz0) & (tmin[:, 1] <= wz1)
            if not sel.any():
                continue
            w, h = min(tp, nx - tx), min(tp, nz - tz)
            p, y, up, ny = raster(tris[sel], px, wx0, wz0, w, h)
            if not len(p):
                continue
            o = np.lexsort((y, p))
            p, y, up = p[o], y[o], up[o]
            nxt_same = np.r_[p[1:] == p[:-1], False]
            nxt_y = np.r_[y[1:], np.inf]
            nxt_up = np.r_[up[1:], True]
            ok = up & nxt_same & ~nxt_up & (nxt_y - y >= CLEAR)
            # Highest first: a pixel's floors from the end of its sorted run.
            fp, fy = p[ok][::-1], y[ok][::-1]
            if not len(fp):
                continue
            start = np.r_[True, fp[1:] != fp[:-1]]
            first = np.maximum.accumulate(np.where(start, np.arange(len(fp)), 0))
            rank = np.arange(len(fp)) - first
            keep = rank < LAYERS
            sub = np.full((LAYERS, w * h), np.nan, np.float32)
            sub[rank[keep], fp[keep]] = fy[keep]
            grid[:, tz:tz + h, tx:tx + w] = sub.reshape(LAYERS, h, w)
        print(f"  rows {tz}/{nz}", flush=True)
    return grid, x0, z0


def walkable(layers, px):
    """The highest floor per pixel among the walkable regions of MIN_AREA
    or more (NaN = none)."""
    K, nz, nx = layers.shape
    have = ~np.isnan(layers)
    ids = np.full(layers.shape, -1, np.int64)
    n = int(have.sum())
    ids[have] = np.arange(n)
    # Edges between a floor and one a pixel east / north within STEP.
    ea, eb = [], []
    step = STEP * px
    for i in range(K):
        for j in range(K):
            for a_sl, b_sl in (((slice(None), slice(0, -1)), (slice(None), slice(1, None))),
                               ((slice(0, -1), slice(None)), (slice(1, None), slice(None)))):
                ya, yb = layers[i][a_sl], layers[j][b_sl]
                m = np.abs(ya - yb) <= step
                ea.append(ids[i][a_sl][m])
                eb.append(ids[j][b_sl][m])
    ea, eb = np.concatenate(ea), np.concatenate(eb)
    # Union-find by hooking to the smaller label and pointer jumping.
    lab = np.arange(n)
    while True:
        la, lb = lab[ea], lab[eb]
        lo = np.minimum(la, lb)
        old = lab.copy()
        np.minimum.at(lab, la, lo)
        np.minimum.at(lab, lb, lo)
        while True:
            nxt = lab[lab]
            if np.array_equal(nxt, lab):
                break
            lab = nxt
        if np.array_equal(lab, old):
            break
    area = np.bincount(lab, minlength=n) * px * px
    good = area[lab] >= MIN_AREA
    print(f"  {n} floor cells, {len(np.unique(lab))} regions, {int(good.sum())} cells kept")
    keep = np.zeros(layers.shape, bool)
    keep[have] = good
    out = np.where(keep, layers, np.nan)
    # Highest first: the first kept layer of each pixel.
    grid = np.full((nz, nx), np.nan, np.float32)
    for i in range(K - 1, -1, -1):
        grid = np.where(keep[i], out[i], grid)
    return grid


def box(a, r):
    """The sum over a (2r+1)^2 square round each cell (edges clipped)."""
    c = np.pad(a, r).cumsum(0).cumsum(1)
    c = np.pad(c, ((1, 0), (1, 0)))
    n = 2 * r + 1
    return c[n:, n:] - c[:-n, n:] - c[n:, :-n] + c[:-n, :-n]


def picture(grid, px):
    have = ~np.isnan(grid)
    # One lone pixel (a gap between two faces) is filled from its neighbours.
    g = np.where(have, grid, 0)
    cnt = sum(np.roll(np.roll(have, dz, 0), dx, 1).astype(np.int32) for dz in (-1, 0, 1) for dx in (-1, 0, 1))
    tot = sum(np.roll(np.roll(g, dz, 0), dx, 1) for dz in (-1, 0, 1) for dx in (-1, 0, 1))
    hole = ~have & (cnt >= 6)
    grid = np.where(hole, tot / np.maximum(cnt, 1), grid)
    have = ~np.isnan(grid)
    g = np.where(have, grid, np.nan)
    # Slope shading, light from the north-west as the relief, on heights
    # smoothed over a few metres: per facet it was noise.
    fill = np.where(have, g, np.nanmean(g))
    w = have.astype(np.float64)
    r = max(1, int(round(2.0 / px)))
    num, den = box(np.where(have, g, 0) * w, r), box(w, r)
    smooth = np.where(den > 0, num / np.maximum(den, 1e-9), fill)
    dz_ = np.gradient(smooth, px)
    sx, sz = dz_[1], dz_[0]
    shade = np.clip(0.85 + (-sx + sz) * 0.3, 0.6, 1.12)
    lo, hi = np.nanpercentile(g, 2), np.nanpercentile(g, 98)
    k = np.clip((smooth - lo) / max(1e-3, hi - lo), 0, 1)
    # Low = brown-grey, high = pale sand: both clear of the sea's navy.
    deep, high = np.array([128, 110, 92], float), np.array([226, 210, 176], float)
    rgb = (deep[None, None] * (1 - k[..., None]) + high[None, None] * k[..., None]) * shade[..., None]
    # Walls: floor pixels next to no floor.
    edge = have & ~(np.roll(have, 1, 0) & np.roll(have, -1, 0) & np.roll(have, 1, 1) & np.roll(have, -1, 1))
    rgb[edge] = rgb[edge] * 0.5
    a = np.where(have, 255, 0)
    img = np.dstack([np.clip(rgb, 0, 255), a]).astype(np.uint8)
    return img, have


def main():
    folder = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "site", "world-v3")
    ppm = float(sys.argv[2]) if len(sys.argv) > 2 else 1.0
    px = 1.0 / ppm
    tris = load(folder)
    print(f"{len(tris)} collision triangles in {', '.join(AREAS)}")
    layers, x0, z0 = floors(tris, px)
    grid = walkable(layers, px)
    img, have = picture(grid, px)
    rows, cols = np.nonzero(have.any(1))[0], np.nonzero(have.any(0))[0]
    r0, r1, c0, c1 = rows[0], rows[-1] + 1, cols[0], cols[-1] + 1
    img = img[r0:r1, c0:c1]
    # Row 0 of the picture is north (z max), as map.jpg.
    img = img[::-1]
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "caves.webp")
    Image.fromarray(img, "RGBA").save(path, quality=85, method=6)
    meta = {
        "image": "caves.webp",
        "build": hashlib.sha256(open(path, "rb").read()).hexdigest()[:12],
        "x0": round(float(x0 + c0 * px), 3), "z0": round(float(z0 + r0 * px), 3),
        "sizeX": round(float((c1 - c0) * px), 3), "sizeZ": round(float((r1 - r0) * px), 3),
        "floors": int(have.sum()),
    }
    json.dump(meta, open(os.path.join(OUT, "caves.json"), "w"), indent=1)
    print(meta, os.path.getsize(path), "bytes")


if __name__ == "__main__":
    main()
