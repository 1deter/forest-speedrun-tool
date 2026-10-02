"""Packs for the 3D world's export (scripts/world-extract.py, the writer
stage): the meshes (m/<i>.bin), textures (t/<i>.jpg / .png) and instance
chunks (c/<area>_<x>_<z>.bin) joined into a few files, so a view costs a few
requests instead of one per chunk, mesh pack and texture pack (2026-10-02,
measured on a local site with the live world: the Labskip spot's 3D view 188
requests -> 60, 38.6 -> 41.9 MB, the picture pixel for pixel the same) - plus
lighter copies of some meshes for phones (LODs).

  b/<i>.bin    blobs back to back, each 4-byte aligned (the site reads float32s
               and uint32s in place). world.json "files" = these files; "version" 3.

Every blob's place is [file, byte offset, length]: a mesh's "at", a chunk's
"at", "textures"[i] = [jpg place or null, png place or null] (only the
variant the site reads).

Which file a blob goes in, by the chunks whose instances use it (simulated
over 25 views of the live world first: 146 -> 37 requests a view on average
for ~12% more bytes - chunks just past the view's reach, which a pan loads
anyway; smaller cells cost requests, bigger ones bytes):
- a chunk's instances, and a mesh / texture only one chunk uses: the area's
  CHUNK_CELL-metre cell of that chunk;
- COMMON chunks or more (trees, rocks, cave walls): the area's common files -
  fetched once, then cached for every chunk;
- a few: the area's CELL-metre cell of the users' mean column;
- a LOD: the area's LOD files - only phones fetch them.
Each file is cut at PACK_MAX bytes. A mesh no instance uses is dropped.

Phone LODs: a mesh a rendered model uses, with LOD_MIN triangles or more,
gets a copy with a quarter of them, else half (fast-simplification's
quadric collapses, per submesh), the first whose vertices stay within
LOD_ERROR (99 %) and LOD_ERROR_MAX (all) of the original's surface at the
largest scale it is drawn at ("err": both, metres) - else none.
meshes[i]["lod"] = j, the copy's entry carries "of": i. Measured on the live
world (2026-10-02): at a quarter Cave 6's ground pieces sat 20-70 cm off and
sank under the floor layers over them on a phone, trees lost branches
(metres), the cave walls stay 13 cm+ off at a half; within 2 cm / 10 cm 60
meshes qualify - cave spikes (5k instances), props, bodies, bone piles. A
modest cut (the Labskip view 70.6M -> 67.9M triangles on a phone): the heavy
meshes are the ones that cannot lose detail unseen. The LODs are 2.7 MB in
all, so there is one copy of every file (phone copies of the files would
have saved phones under 2.5 MB for 44 MB more upload).
The copy keeps a subset of the original vertices (each collapsed group is
drawn at its member nearest the optimal point), so positions and UVs are the
original values and textures do not swim; it carries the full mesh's vertex
normals ("n": float32 x 3 after the uv), so it shades as the full one does,
not by its fewer, larger faces. Cut-out submeshes (leaf cards, grass: a
material with "cut") are kept whole - decimating them drops whole cards
(holes in the trees, 2026-10-02). Collision keeps the full mesh. Without
numpy / fast-simplification (pip install fast-simplification) there are no LODs.

    python scripts/world_pack.py repack <world folder> <out>   a world (version 1-3) in this layout, a new build
    python scripts/world_pack.py test                          round trip on a synthetic world
    python scripts/world_pack.py synthetic <out> [1|3]         a tiny world to upload to a local site

Standard library only, except the LODs (numpy, fast-simplification).
"""
import json
import os
import random
import shutil
import struct
import sys
import time

COMMON = 8
CELL = 1000.0
CHUNK_CELL = 500.0
PACK_MAX = 8 * 1024 * 1024
LOD_MIN = 1000
LOD_RATIOS = (0.25, 0.5)  # tried in turn: the first within the errors below
LOD_ERROR = 0.02        # metres, at the mesh's largest drawn scale: LOD_PERCENTILE % of the vertices
LOD_PERCENTILE = 99
LOD_ERROR_MAX = 0.1     # metres: every vertex
LOD_KEEP = 0.6          # a copy with more than this share of the triangles is not worth it
SUB_MIN = 64            # a submesh under this many triangles is kept whole


# --- meshes ---------------------------------------------------------------------

def mesh_arrays(entry, data):
    """(positions n x 3, uv n x 2 or None, indices) of a mesh's bytes (numpy)."""
    import numpy as np
    nv, ni = entry["nv"], entry["ni"]
    p = np.frombuffer(data, np.float32, nv * 3).reshape(nv, 3)
    at = nv * 12
    uv = None
    if entry["uv"]:
        uv = np.frombuffer(data, np.float32, nv * 2, at).reshape(nv, 2)
        at += nv * 8
    if entry.get("n"):
        at += nv * 12
    idx = np.frombuffer(data, np.uint32 if entry["i32"] else np.uint16, ni, at).astype(np.int64)
    return p, uv, idx


def normals(p, idx):
    """Vertex normals as world3d.js gets them for the full mesh: its indices
    turned (Unity's clockwise faces), then three.js's computeVertexNormals
    (area-weighted face normals summed per vertex, normalised)."""
    import numpy as np
    t = idx.reshape(-1, 3)
    a, b, c = p[t[:, 0]].astype(np.float64), p[t[:, 2]].astype(np.float64), p[t[:, 1]].astype(np.float64)
    fn = np.cross(c - b, a - b)
    n = np.zeros((len(p), 3))
    for k in range(3):
        np.add.at(n, t[:, (0, 2, 1)[k]], fn)
    ln = np.linalg.norm(n, axis=1, keepdims=True)
    return n / np.where(ln > 0, ln, 1)


def decimate(p, idx, sub, ratio, whole):
    """Each submesh with about ratio of its triangles: (triangles per submesh
    as original vertex indices, the error in mesh units). The error: how far
    the original vertices lie from the copy's surface - each vertex against
    the planes of the triangles round the vertex it collapsed into, the
    nearest one; the largest over the mesh. A vertex whose triangles all went
    (a small part dropped) counts its distance to the vertex it went into."""
    import numpy as np
    import fast_simplification as fs
    tris, errs = [], []
    for k, (start, count) in enumerate(sub):
        t = idx[start:start + count].reshape(-1, 3)
        if len(t) >= SUB_MIN and k not in whole:
            used, inv = np.unique(t, return_inverse=True)
            local = inv.reshape(-1, 3).astype(np.int64)
            pts = p[used].astype(np.float64)
            try:
                _, _, coll = fs.simplify(pts, local, target_count=max(int(len(t) * ratio), 16), return_collapses=True)
                dp, dt, mapping = fs.replay_simplification(pts, local, coll)
            except Exception:
                dp = None
            if dp is not None and len(dt):
                # Each new vertex drawn at its member closest to where the
                # collapse put it: an original vertex, its own uv.
                ok = np.nonzero(mapping >= 0)[0]
                d = ((pts[ok] - dp[mapping[ok]]) ** 2).sum(1)
                order = np.lexsort((d, mapping[ok]))
                grp = mapping[ok][order]
                first = np.ones(len(grp), bool)
                first[1:] = grp[1:] != grp[:-1]
                rep = np.full(len(dp), -1, np.int64)
                rep[grp[first]] = ok[order][first]
                t2 = rep[dt]
                if (t2 >= 0).all():
                    keep = (t2[:, 0] != t2[:, 1]) & (t2[:, 1] != t2[:, 2]) & (t2[:, 0] != t2[:, 2])
                    t2, dt = t2[keep], dt[keep]
                    t = used[t2]
                    # The error, against the planes of each fan.
                    q = pts[t2]
                    n = np.cross(q[:, 1] - q[:, 0], q[:, 2] - q[:, 0])
                    ln = np.linalg.norm(n, axis=1)
                    n = n / np.where(ln > 0, ln, 1)[:, None]
                    off = -(n * q[:, 0]).sum(1)
                    ck = dt.reshape(-1)
                    cf = np.repeat(np.arange(len(dt)), 3)
                    o = np.argsort(ck, kind="stable")
                    ks, fo = ck[o], cf[o]
                    kk = mapping[ok]
                    lo = np.searchsorted(ks, kk)
                    cnt = np.searchsorted(ks, kk, "right") - lo
                    gone = ok[cnt == 0]
                    if len(gone):
                        errs.append(np.sqrt(((pts[gone] - pts[rep[mapping[gone]]]) ** 2).sum(1)))
                    has = cnt > 0
                    vv = np.repeat(ok[has], cnt[has])
                    starts = np.repeat(lo[has], cnt[has])
                    within = np.arange(len(vv)) - np.repeat(np.cumsum(cnt[has]) - cnt[has], cnt[has])
                    f = fo[starts + within]
                    dist = np.abs((n[f] * pts[vv]).sum(1) + off[f])
                    dist[ln[f] == 0] = np.inf
                    best = np.full(len(pts), np.inf)
                    np.minimum.at(best, vv, dist)
                    errs.append(best[np.isfinite(best)])
        tris.append(t)
    e = np.concatenate(errs) if errs else np.zeros(0)
    return tris, (float(np.percentile(e, LOD_PERCENTILE)), float(e.max())) if len(e) else (0.0, 0.0)


def make_lod(entry, data, whole=(), scale=1.0):
    """A lighter copy of a mesh: (world.json entry, bytes), or None (too few
    triangles, nothing gained, too far from the original, no
    fast-simplification). whole: submesh indices kept as they are (cut-outs);
    scale: the largest scale the mesh is drawn at (its error x this is metres)."""
    if entry["ni"] // 3 < LOD_MIN:
        return None
    try:
        import numpy as np
        import fast_simplification  # noqa: F401
    except ImportError:
        return None
    p, uv, idx = mesh_arrays(entry, data)
    for ratio in LOD_RATIOS:
        tris, err = decimate(p, idx, entry["sub"], ratio, whole)
        flat = np.concatenate(tris).reshape(-1) if tris else np.zeros(0, np.int64)
        if len(flat) > entry["ni"] * LOD_KEEP:
            return None
        if err[0] * scale <= LOD_ERROR and err[1] * scale <= LOD_ERROR_MAX:
            break
    else:
        return None
    verts, inv = np.unique(flat, return_inverse=True)
    i32 = len(verts) > 65535
    out = p[verts].astype(np.float32).tobytes()
    if uv is not None:
        out += uv[verts].astype(np.float32).tobytes()
    out += normals(p, idx)[verts].astype(np.float32).tobytes()
    out += inv.astype(np.uint32 if i32 else np.uint16).tobytes()
    sub, at = [], 0
    for t in tris:
        sub.append([at, len(t) * 3])
        at += len(t) * 3
    e = {"name": entry["name"], "nv": len(verts), "ni": len(flat), "i32": i32, "uv": uv is not None, "n": True, "sub": sub,
         "bb": entry["bb"], "err": [round(err[0] * scale, 4), round(err[1] * scale, 4)]}
    return e, out


# --- the files ------------------------------------------------------------------

def chunk_key(c):
    """(area, cx, cz, wide) of a chunk's world.json entry (its file name)."""
    parts = os.path.basename(c["file"])[:-4].split("_")
    wide = parts[-1] == "L"
    if wide:
        parts = parts[:-1]
    return c["area"], int(parts[-2]), int(parts[-1]), wide


def group_of(users, size, cell_of):
    """The file group of a blob used by these chunk keys."""
    us = sorted(users)
    if len(us) == 1:
        return cell_of[us[0]]
    if len(us) >= COMMON:
        return ("common", us[0][0])
    mx = sum(u[1] for u in us) / len(us) * size
    mz = sum(u[2] for u in us) / len(us) * size
    return ("near", us[0][0], int(mx // CELL), int(mz // CELL))


def pack(out, meta, lods=True):
    """Packs out/m, out/t and out/c (the chunks meta lists) into out/b and sets
    meta's "files", meshes' "at" (+ "lod" and the LOD entries), "textures",
    chunks' "at", "version" 3; removes m/, t/, c/ and an older layout's p/, q/.
    Returns a line of numbers for the log."""
    import collections
    size = meta["chunk"]
    models, materials, meshes = meta["models"], meta["materials"], meta["meshes"]
    users = collections.defaultdict(set)      # ("m", i) / ("t", i, ext) -> chunk keys
    render = set()                            # meshes a rendered model uses (LOD candidates)
    whole = collections.defaultdict(set)      # mesh -> its cut-out submeshes (kept whole in a LOD)
    scale = collections.defaultdict(float)    # mesh -> the largest scale an instance draws it at
    cell_of = {}
    chunk_data = {}
    pngs = set(n[:-4] for n in os.listdir(os.path.join(out, "t")) if n.endswith(".png")) if os.path.isdir(os.path.join(out, "t")) else set()
    for c in meta["chunks"]:
        key = chunk_key(c)
        cell_of[key] = ("cell", key[0], int(key[1] * size // CHUNK_CELL), int(key[2] * size // CHUNK_CELL))
        with open(os.path.join(out, c["file"]), "rb") as f:
            data = f.read()
        chunk_data[c["file"]] = data
        for k in range(len(data) // 52):
            row = struct.unpack_from("<I12f", data, k * 52)
            model = models[row[0]]
            sc = max(sum(row[1 + r * 4 + col] ** 2 for r in range(3)) for col in range(3)) ** 0.5
            scale[model["mesh"]] = max(scale[model["mesh"]], sc)
            users[("m", model["mesh"])].add(key)
            if model["kind"] != "collide" and not (model["mats"] and all(mi >= 0 and materials[mi].get("fx") for mi in model["mats"])):
                render.add(model["mesh"])     # glints / particles (fx): never drawn - no LOD
            for k, mi in enumerate(model["mats"]):
                if mi >= 0 and materials[mi].get("cut"):
                    whole[model["mesh"]].add(k)
            for mi in model["mats"]:
                if mi < 0:
                    continue
                mat = materials[mi]
                # Only the variant world3d.js reads: a cut-out's main texture
                # as .png (the .jpg when it has no .png), every other one - and
                # the top layers - as .jpg. Packing both sent each cut-out
                # twice (17.4 MB of textures for the 10.4 a view used, 2026-10-02).
                for t, cut in ((mat.get("tex", -1), mat.get("cut")), (mat.get("top", -1), False)):
                    if t is not None and t >= 0:
                        users[("t", t, "png" if cut and str(t) in pngs else "jpg")].add(key)

    def mesh_bytes(i):
        with open(os.path.join(out, "m", "%d.bin" % i), "rb") as f:
            return f.read()

    # blobs: (key, users, LOD of an area or None)
    blobs = []
    lod_bytes = {}
    lod_count, full_tris, lod_tris = 0, 0, 0
    for b, us in sorted(users.items(), key=lambda kv: str(kv[0])):
        blobs.append((b, us, None))
        if b[0] != "m" or not lods or b[1] not in render:
            continue
        i = b[1]
        lod = make_lod(meshes[i], mesh_bytes(i), whole[i], scale[i])
        if lod:
            e, data = lod
            e["of"] = i
            j = len(meshes)
            meshes.append(e)
            meshes[i]["lod"] = j
            lod_bytes[j] = data
            blobs.append((("m", j), us, sorted(us)[0][0]))
            lod_count += 1
            full_tris += meshes[i]["ni"] // 3
            lod_tris += e["ni"] // 3
    for c in meta["chunks"]:
        blobs.append((("c", c["file"]), {chunk_key(c)}, None))

    def data_of(b):
        if b[0] == "c":
            return chunk_data[b[1]]
        if b[0] == "m":
            return lod_bytes[b[1]] if b[1] in lod_bytes else mesh_bytes(b[1])
        with open(os.path.join(out, "t", "%d.%s" % (b[1], b[2])), "rb") as f:
            return f.read()

    groups = collections.defaultdict(list)
    for b, us, lod_area in blobs:
        g = ("lod", lod_area) if lod_area else cell_of[next(iter(us))] if b[0] == "c" else group_of(us, size, cell_of)
        groups[g].append(b)

    shutil.rmtree(os.path.join(out, "b"), ignore_errors=True)    # a previous export's (more files: left over)
    os.makedirs(os.path.join(out, "b"))
    files = []
    places = {}
    f, at = None, 0
    for g in sorted(groups, key=lambda k: tuple(str(x) for x in k)):
        new = True
        for b in sorted(groups[g], key=str):
            data = data_of(b)
            if new or (at and at + len(data) > PACK_MAX):
                if f:
                    f.close()
                files.append("b/%d.bin" % len(files))
                f, at, new = open(os.path.join(out, files[-1]), "wb"), 0, False
            places[b] = [len(files) - 1, at, len(data)]
            pad = -len(data) % 4
            f.write(data + b"\0" * pad)
            at += len(data) + pad
    if f:
        f.close()

    for i, e in enumerate(meshes):
        e.pop("pack", None)
        if ("m", i) in places:
            e["at"] = places[("m", i)]
    tcount = 1 + max([b[1] for b in places if b[0] == "t"] or [-1])
    meta["textures"] = [[places.get(("t", i, "jpg")), places.get(("t", i, "png"))] for i in range(tcount)]
    for c in meta["chunks"]:
        c["at"] = places[("c", c["file"])]
    meta["files"] = files
    meta["version"] = 3
    meta.pop("packs", None)
    meta.pop("texpacks", None)
    for d in ("m", "t", "c", "p", "q"):
        shutil.rmtree(os.path.join(out, d), ignore_errors=True)
    return "%d files; %d LODs: %d -> %d triangles" % (len(files), lod_count, full_tris, lod_tris)


# --- reading a world back (repack, the test) -------------------------------------

def blob(folder, meta, place):
    """A blob's bytes from a version 3 world."""
    with open(os.path.join(folder, meta["files"][place[0]]), "rb") as fh:
        fh.seek(place[1])
        return fh.read(place[2])


def read(folder, meta, ix):
    """One mesh's bytes from a world of any version, or None (not stored)."""
    m = meta["meshes"][ix]
    if meta.get("version") == 3:
        return blob(folder, meta, m["at"]) if "at" in m else None
    if "packs" not in meta:
        path = os.path.join(folder, "m", "%d.bin" % ix)
        return open(path, "rb").read() if os.path.exists(path) else None
    if "pack" not in m:
        return None
    p, off, n = m["pack"]
    with open(os.path.join(folder, meta["packs"][p]), "rb") as f:
        f.seek(off)
        return f.read(n)


def read_texture(folder, meta, ix, ext):
    """One texture file's bytes from a world of any version, or None."""
    if meta.get("version") == 3:
        e = meta["textures"][ix] if ix < len(meta["textures"]) else None
        place = e and e[0 if ext == "jpg" else 1]
        return blob(folder, meta, place) if place else None
    if "textures" not in meta:
        path = os.path.join(folder, "t", "%d.%s" % (ix, ext))
        return open(path, "rb").read() if os.path.exists(path) else None
    e = meta["textures"][ix] if ix < len(meta["textures"]) else None
    s = 0 if ext == "jpg" else 3
    if not e or e[s] < 0:
        return None
    with open(os.path.join(folder, meta["texpacks"][e[s]]), "rb") as f:
        f.seek(e[s + 1])
        return f.read(e[s + 2])


def read_chunk(folder, meta, c):
    if meta.get("version") == 3:
        return blob(folder, meta, c["at"])
    with open(os.path.join(folder, c["file"]), "rb") as f:
        return f.read()


def unpack(src, out):
    """A world folder of any version as the export writes it before packing:
    out/m, out/t, out/c and the meta (returned) without places or LODs."""
    with open(os.path.join(src, "world.json"), encoding="utf-8") as f:
        meta = json.load(f)
    shutil.rmtree(out, ignore_errors=True)
    for d in ("m", "t", "c"):
        os.makedirs(os.path.join(out, d))
    originals = [i for i, e in enumerate(meta["meshes"]) if "of" not in e]
    assert originals == list(range(len(originals))), "LOD entries are always after the meshes"
    for i in originals:
        data = read(src, meta, i)
        if data is not None:
            with open(os.path.join(out, "m", "%d.bin" % i), "wb") as f:
                f.write(data)
    count = len(meta.get("textures") or [])
    if "textures" not in meta and os.path.isdir(os.path.join(src, "t")):
        count = 1 + max([int(n.split(".")[0]) for n in os.listdir(os.path.join(src, "t"))] or [-1])
    for i in range(count):
        for ext in ("jpg", "png"):
            data = read_texture(src, meta, i, ext)
            if data is not None:
                with open(os.path.join(out, "t", "%d.%s" % (i, ext)), "wb") as f:
                    f.write(data)
    for c in meta["chunks"]:
        data = read_chunk(src, meta, c)
        with open(os.path.join(out, c["file"]), "wb") as f:
            f.write(data)
        c.pop("at", None)
    meta["meshes"] = [meta["meshes"][i] for i in originals]
    for e in meta["meshes"]:
        for k in ("pack", "at", "lod"):
            e.pop(k, None)
    for k in ("packs", "texpacks", "textures", "files"):
        meta.pop(k, None)
    meta["version"] = 1
    return meta


def repack(src, out, lods=True):
    if os.path.abspath(src) == os.path.abspath(out):
        sys.exit("repack writes a new folder (it clears it first) - give another <out>")
    meta = unpack(src, out)
    line = pack(out, meta, lods)
    meta["build"] = int(time.time())
    with open(os.path.join(out, "world.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f, separators=(",", ":"))
    return line


# --- a synthetic export ----------------------------------------------------------

def box(sx, sy, sz, i32=False, uv=True):
    """A box mesh's m/<i>.bin bytes + its world.json entry (the exporter's layout)."""
    v = [(x * sx, y * sy, z * sz) for x in (-.5, .5) for y in (0, 1) for z in (-.5, .5)]
    # Unity winds front faces clockwise (world3d.js turns them).
    q = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    idx = [i for a, b, c, d in q for i in (a, c, b, a, d, c)]
    data = b"".join(struct.pack("<3f", *p) for p in v)
    if uv:
        data += b"".join(struct.pack("<2f", (k >> 1) & 1, k & 1) for k in range(len(v)))
    data += struct.pack("<%d%s" % (len(idx), "I" if i32 else "H"), *idx)
    entry = {"name": "box", "nv": len(v), "ni": len(idx), "i32": i32, "uv": uv, "sub": [[0, len(idx)]],
             "bb": [-sx / 2, 0, -sz / 2, sx / 2, sy, sz / 2]}
    return data, entry


def hill(n=40, size=20.0, height=0.5):
    """A heavy mesh: an n x n grid bent into a smooth hill with uvs, two
    submeshes - a quarter, then the rest (2 * n * n triangles - a LOD candidate)."""
    import math
    v, uv = [], []
    for j in range(n + 1):
        for i in range(n + 1):
            x, z = (i / n - .5) * size, (j / n - .5) * size
            v.append((x, height * math.exp(-(x * x + z * z) / (size * size / 8)), z))
            uv.append((i / n * 4, j / n * 4))
    idx = []
    for j in range(n):
        for i in range(n):
            a = j * (n + 1) + i
            idx += [a, a + n + 1, a + 1, a + 1, a + n + 1, a + n + 2]
    data = b"".join(struct.pack("<3f", *p) for p in v) + b"".join(struct.pack("<2f", *t) for t in uv)
    data += struct.pack("<%dH" % len(idx), *idx)
    half = len(idx) // 12 * 3
    entry = {"name": "hill", "nv": len(v), "ni": len(idx), "i32": False, "uv": True, "sub": [[0, half], [half, len(idx) - half]],
             "bb": [-size / 2, 0, -size / 2, size / 2, height, size / 2]}
    return data, entry


def synthetic(out, version, x0=400.0, z0=-100.0, chunk=250, seed=1):
    """A tiny world around (x0, z0) - the Slot 1 tree spot by default: a 3 x 3
    block of surface chunks, boxes of a few sizes (some in one chunk only,
    one in every chunk, one in two), a heavy hill (a LOD) in three, a solid and
    a coloured material, a collider. version 1 = one file per blob, 3 = packed."""
    rnd = random.Random(seed)
    shutil.rmtree(out, ignore_errors=True)
    for d in ("m", "t", "c"):
        os.makedirs(os.path.join(out, d))
    meshes = []
    for k in range(15):
        data, e = hill() if k == 14 else box(2 + k, 3 + (k % 4) * 4, 2 + k % 3, i32=(k == 5), uv=(k != 7))
        with open(os.path.join(out, "m", "%d.bin" % k), "wb") as f:
            f.write(data)
        meshes.append(e)
    with open(os.path.join(out, "t", "0.png"), "wb") as f:
        f.write(png())     # a cut-out's texture (.png): the site never asks for t/0.jpg then
    with open(os.path.join(out, "t", "0.jpg"), "wb") as f:
        f.write(png(6, 6))  # beside the cut-out's .png, as the exporter writes: never read
    with open(os.path.join(out, "t", "1.jpg"), "wb") as f:
        f.write(png(4, 4))  # the red material's (any bytes: the test compares them)
    mats = [{"name": "box", "color": [1, 1, 1, 1], "tex": 0, "cut": True}, {"name": "red", "color": [0.8, 0.2, 0.1, 1], "tex": 1}]
    models = [{"mesh": k, "mats": [k % 2], "kind": "render", "layer": 0} for k in range(13)]
    models.append({"mesh": 13, "mats": [], "kind": "collide", "layer": 0})
    models.append({"mesh": 14, "mats": [0, 1], "kind": "render", "layer": 0})     # a cut-out quarter: kept whole
    cx0, cz0 = int(x0 // chunk) - 1, int(z0 // chunk) - 1
    chunks = []
    for a in range(3):
        for b in range(3):
            cx, cz = cx0 + a, cz0 + b
            items = [(0, 10, 10), (13, 30, 30)]                       # in every chunk: common
            items.append((1 + a * 3 + b, 60, 60))                     # one chunk only
            if a == 1 and b < 2:
                items.append((10, 90, 120))                           # two chunks: a "near" file
            if b == 1:
                items.append((14, 150, 150))                          # the hill: three chunks
            items += [(11 + (a + b) % 2, rnd.uniform(0, chunk), rnd.uniform(0, chunk)) for _ in range(5)]
            name = "c/surface_%d_%d.bin" % (cx, cz)
            with open(os.path.join(out, name), "wb") as f:
                for model, x, z in items:
                    f.write(struct.pack("<I12f", model, 1, 0, 0, cx * chunk + x, 0, 1, 0, 80, 0, 0, 1, cz * chunk + z))
            chunks.append({"file": name, "area": "surface", "x": cx * chunk, "z": cz * chunk, "y0": 80, "y1": 80,
                           "n": len(items), "tris": 0, "bb": [cx * chunk - 10, cz * chunk - 10, cx * chunk + chunk + 10, cz * chunk + chunk + 10]})
    meta = {"version": 1, "build": int(time.time()), "chunk": chunk, "layers": {"0": "Default"}, "materials": mats,
            "meshes": meshes, "models": models, "chunks": chunks}
    if version == 3:
        print(pack(out, meta))
    with open(os.path.join(out, "world.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f, separators=(",", ":"))
    return meta


def png(w=8, h=8):
    """A small opaque checker PNG (no imaging library needed)."""
    import zlib
    rows = b"".join(b"\0" + b"".join(bytes((200, 170, 120, 255) if (x + y) % 2 else (90, 110, 70, 255)) for x in range(w)) for y in range(h))
    chunk = lambda t, d: struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + \
        chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b"")


def test(tmp):
    """The same synthetic world unpacked and packed: every blob's bytes the
    same, places aligned, the LOD in a file of its own, a subset of the
    original's vertices with its normals; a repack round trip."""
    a, b = os.path.join(tmp, "v1"), os.path.join(tmp, "v3")
    m1 = synthetic(a, 1)
    m3 = synthetic(b, 3)
    for d in ("m", "t", "c", "p", "q"):
        assert not os.path.exists(os.path.join(b, d)), d + "/ left beside the packs"
    used = {mo["mesh"] for mo in m1["models"]}
    for ix in range(len(m1["meshes"])):
        one, three = read(a, m1, ix), read(b, m3, ix)
        if ix in used:
            assert one == three, "mesh %d differs" % ix
        else:
            assert three is None, "unused mesh %d packed" % ix
    for f in m3["files"]:
        assert os.path.getsize(os.path.join(b, f)) % 4 == 0
    places = [e["at"] for e in m3["meshes"] if "at" in e] + [c["at"] for c in m3["chunks"]] + \
        [p for t in m3["textures"] for p in t if p]
    assert all(p[1] % 4 == 0 for p in places)
    for c1, c3 in zip(m1["chunks"], m3["chunks"]):
        assert read_chunk(a, m1, c1) == read_chunk(b, m3, c3), c1["file"]
    for ix, ext in ((0, "png"), (1, "jpg"), (1, "png")):
        assert read_texture(a, m1, ix, ext) == read_texture(b, m3, ix, ext), (ix, ext)
    # The cut-out's .jpg is never read (world3d.js takes its .png): not packed.
    assert read_texture(b, m3, 0, "jpg") is None and read_texture(b, m3, 0, "png") is not None
    # LODs (with fast-simplification): the hill has one, the boxes none; the
    # LOD in a file of its own (only phones fetch it).
    try:
        import fast_simplification  # noqa: F401
        has_lod = True
    except ImportError:
        has_lod = False
    hill_e = m3["meshes"][14]
    assert all("lod" not in e for e in m3["meshes"][:14])
    if has_lod:
        j = hill_e["lod"]
        lod = m3["meshes"][j]
        files_used = {p[0] for p in places if p is not lod["at"]} - {lod["at"][0]}
        assert lod["of"] == 14 and lod["at"][0] not in files_used, "a LOD in a file desktops fetch"
        assert lod["ni"] < hill_e["ni"] * LOD_KEEP and len(lod["sub"]) == 2 and lod["err"][0] <= LOD_ERROR and lod["err"][1] <= LOD_ERROR_MAX
        assert lod["sub"][0][1] == hill_e["sub"][0][1] and lod["sub"][1][1] < hill_e["sub"][1][1] / 2, "the cut-out submesh decimated"
        import numpy as np
        p, uv, hidx = mesh_arrays(hill_e, read(b, m3, 14))
        lp, luv, lidx = mesh_arrays(lod, read(b, m3, j))
        where = {tuple(x) + tuple(y): k for k, (x, y) in enumerate(zip(p.tolist(), uv.tolist()))}
        assert all(tuple(x) + tuple(y) in where for x, y in zip(lp.tolist(), luv.tolist())), "a LOD vertex not in the original"
        # Its normals: the full mesh's at the same vertices.
        raw = read(b, m3, j)
        ln = np.frombuffer(raw, np.float32, lod["nv"] * 3, lod["nv"] * 20).reshape(-1, 3)
        full_n = normals(p, hidx)
        src = [where[tuple(x) + tuple(y)] for x, y in zip(lp.tolist(), luv.tolist())]
        assert np.allclose(ln, full_n[src], atol=1e-6), "LOD normals not the full mesh's"
        assert np.allclose(np.linalg.norm(ln, axis=1), 1, atol=1e-5) and abs(np.sign(ln[:, 1]).mean()) > 0.98, "LOD normals not unit / not one side of the hill"
        assert lidx.max() < lod["nv"] and (np.bincount(lidx, minlength=lod["nv"]) > 0).all()
        # Its faces still face up (the winding kept).
        tri = lp[lidx.reshape(-1, 3)]
        ny = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])[:, 1]
        hp = p[mesh_arrays(hill_e, read(b, m3, 14))[2].reshape(-1, 3)]
        hy = np.cross(hp[:, 1] - hp[:, 0], hp[:, 2] - hp[:, 0])[:, 1]
        assert (np.sign(ny) == np.sign(hy[0])).mean() > 0.98, "LOD faces turned"
    # Grouping: the box in every chunk shares a file with the collider; a mesh
    # only one chunk uses sits with that chunk's instances.
    fd = lambda ix: m3["meshes"][ix]["at"][0]
    assert fd(0) == fd(13), "common meshes split"
    c_of = {c["file"]: c["at"][0] for c in m3["chunks"]}
    assert fd(1) == c_of["c/surface_%d_%d.bin" % (int(400 // 250) - 1, int(-100 // 250) - 1)]
    # PACK_MAX: a group over it is cut, nothing lost.
    global PACK_MAX
    keep, PACK_MAX = PACK_MAX, 300
    try:
        small = os.path.join(tmp, "v3small")
        m4 = synthetic(small, 3)
        assert len(m4["files"]) > len(m3["files"])
        for ix in used:
            assert read(small, m4, ix) == read(a, m1, ix)
    finally:
        PACK_MAX = keep
    # repack: version 3 back to blobs and packed again - the same bytes.
    r = os.path.join(tmp, "re")
    repack(b, r)
    with open(os.path.join(r, "world.json"), encoding="utf-8") as f:
        m5 = json.load(f)
    for ix in used:
        assert read(r, m5, ix) == read(a, m1, ix)
    for c1, c5 in zip(m1["chunks"], m5["chunks"]):
        assert read_chunk(a, m1, c1) == read_chunk(r, m5, c5)
    print("ok: %d files from %d chunks%s" % (len(m3["files"]), len(m3["chunks"]),
          ", hill %d -> %d triangles on phones" % (hill_e["ni"] // 3, m3["meshes"][hill_e["lod"]]["ni"] // 3) if has_lod else
          " (no LOD check: pip install fast-simplification)"))


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "test":
        import tempfile
        with tempfile.TemporaryDirectory() as t:
            test(t)
    elif len(sys.argv) > 2 and sys.argv[1] == "synthetic":
        synthetic(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 3)
        print("wrote", sys.argv[2])
    elif len(sys.argv) > 3 and sys.argv[1] == "repack":
        t0 = time.time()
        print(repack(sys.argv[2], sys.argv[3]), "in %.0f s ->" % (time.time() - t0), sys.argv[3])
    else:
        print(__doc__)
