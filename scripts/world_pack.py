"""Packs for the 3D world's export (scripts/world-extract.py, the writer
stage): the meshes (m/<i>.bin), textures (t/<i>.jpg / .png) and instance
chunks (c/<area>_<x>_<z>.bin) joined into a few files, so a view costs a few
requests instead of one per chunk, mesh pack and texture pack (2026-10-02,
measured on a local site with the live world: the Labskip spot's 3D view 188
requests -> 60, 38.6 -> 41.9 MB, the picture pixel for pixel the same) - plus
lighter copies of the meshes for drawing them far away (far copies).

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
- a mesh's far copies: right after it, in its group.
Each file is cut at PACK_MAX bytes. A mesh no instance uses is dropped.

Far copies (2026-10-02): a mesh a rendered model draws, of FAR_MIN triangles
or more, gets a chain of lighter copies, each with its error "e" in mesh
units - how far its surface strays from the full mesh's. meshes[i]["far"] =
[j, ...] lightest last, each copy's entry "of": i. world3d.js draws an
instance with the lightest copy whose error, at the instance's scale and
distance, is under a pixel on the screen (FAR_PIXELS there), so the far
copies change nothing visible by construction; most of a view's triangles
are far away (the Labskip view: 90% over 400 m from the target).
- Solid parts are simplified by meshoptimizer as far as an error allows
  (FAR_ERRORS x the mesh's size): border edges stay on their borders (a
  branch's open end; fast-simplification slid them up the branch - whole
  branches went), parts smaller than the error may go, and the vertices are
  the original ones (uvs exact).
- Leaf cards (pieces of a cut-out submesh of at most CARD_TRIS triangles;
  simplifying a card drops it whole) are thinned: FAR_KEEP of them kept,
  each grown about its centre by 1 / sqrt(keep) so the leaves cover about
  as much; the error is how far a grown card reaches past its old edge.
- Each copy is the lightest pairing of a solid error and a card share for
  its error; one is kept only under FAR_GAIN of the triangles before it.
- A copy that moves no vertex is "shared": only its indices, over the full
  mesh's vertices (and their normals) - a few bytes per triangle. A copy with
  grown cards has its own vertices, uvs and the full mesh's normals ("n":
  float32 x 3 after the uv), so it shades as the full one does.
Collision keeps the full mesh. Without numpy / meshoptimizer (pip install
meshoptimizer) there are no far copies.
Before (2026-10-02, the same day): phones only drew 60 "lod" copies within
2 cm of the original - 70.6M -> 67.9M triangles; worlds packed then still
load, with full meshes.

    python scripts/world_pack.py repack <world folder> <out>   a world (version 1-3) in this layout, a new build
    python scripts/world_pack.py test                          round trip on a synthetic world
    python scripts/world_pack.py synthetic <out> [1|3]         a tiny world to upload to a local site

Standard library only, except the far copies (numpy, meshoptimizer).
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
FAR_MIN = 200           # triangles: a lighter mesh gets no far copies
FAR_ERRORS = (0.002, 0.005, 0.01, 0.02, 0.04, 0.08)   # the solid parts' errors tried, x the mesh's size
FAR_KEEP = (0.6, 0.35, 0.2, 0.1)   # the shares of leaf cards tried
FAR_GAIN = 0.7          # a copy is kept only with under this share of the triangles before it
CARD_TRIS = 64          # a cut-out piece of more triangles is not a card: never thinned
CARDS_MIN = 16          # a submesh with fewer cards is not thinned


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


def simplify(p, t, error):
    """A submesh's triangles (m x 3, original vertex indices) simplified by
    meshoptimizer as far as error (x the mesh's size) allows: (triangles, the
    error it reached in mesh units). Border edges stay on their borders, parts
    smaller than the error may go (PRUNE); the vertices are the original ones."""
    import numpy as np
    import meshoptimizer as mo
    pos = np.ascontiguousarray(p, np.float32)
    src = np.ascontiguousarray(t.reshape(-1), np.uint32)
    dst = np.zeros(len(src), np.uint32)
    err = np.zeros(1, np.float32)
    n = mo.simplify(dst, src, pos, target_index_count=0, target_error=error, options=mo.SIMPLIFY_PRUNE, result_error=err)
    return dst[:n].astype(np.int64).reshape(-1, 3), float(err[0]) * mo.simplify_scale(pos)


def pieces(t):
    """Each triangle's connected piece (triangles sharing vertices): labels
    0..n-1 (numpy)."""
    import numpy as np
    used, inv = np.unique(t, return_inverse=True)
    local = inv.reshape(-1, 3)
    lab = np.arange(len(used))
    while True:
        m = lab[local].min(1)
        new = lab.copy()
        np.minimum.at(new, local.reshape(-1), np.repeat(m, 3))
        new = new[new]
        if (new == lab).all():
            break
        lab = new
    return np.unique(lab[local[:, 0]], return_inverse=True)[1].reshape(-1)


def cards_of(p, t, fixed):
    """A cut-out submesh's leaf cards, or None (fewer than CARDS_MIN): its
    pieces of at most CARD_TRIS triangles that share no vertex with a solid
    submesh (fixed: a bool per vertex). (piece per triangle, card per piece,
    rank per piece - the cards in a fixed random order, -1 for the rest -,
    centre and radius per piece, the (vertex, piece) pairs)."""
    import numpy as np
    lab = pieces(t)
    n = lab.max() + 1
    size = np.bincount(lab, minlength=n)
    pinned = np.zeros(n, bool)
    np.logical_or.at(pinned, lab, fixed[t].any(1))
    card = (size <= CARD_TRIS) & ~pinned
    if card.sum() < CARDS_MIN:
        return None
    vp = np.unique(np.stack([t.reshape(-1), np.repeat(lab, 3)], 1), axis=0)
    cnt = np.bincount(vp[:, 1], minlength=n)
    centre = np.stack([np.bincount(vp[:, 1], p[vp[:, 0], k].astype(np.float64), n) for k in range(3)], 1) / cnt[:, None]
    radius = np.zeros(n)
    np.maximum.at(radius, vp[:, 1], np.linalg.norm(p[vp[:, 0]] - centre[vp[:, 1]], axis=1))
    rank = np.full(n, -1)
    ids = np.nonzero(card)[0]
    rank[ids[np.random.default_rng(len(t)).permutation(len(ids))]] = np.arange(len(ids))
    return lab, card, rank, centre, radius, vp


def thin(q, t, cards, keep):
    """keep of a submesh's cards, each grown about its centre so the leaves
    cover about as much as all of them did: (triangles, the error in mesh
    units). With q (positions, float64), the grown cards' vertices are moved
    there. The error: how far a grown card reaches past its old edge (the
    99th percentile) - the dropped ones' place is covered by the grown ones
    round them."""
    import numpy as np
    lab, card, rank, centre, radius, vp = cards
    kept = ~card | (rank < int(np.ceil(keep * card.sum())))
    grow = 1 / np.sqrt(keep)
    if q is not None:
        move = card[vp[:, 1]] & kept[vp[:, 1]]
        v, piece = vp[move, 0], vp[move, 1]
        q[v] = centre[piece] + (q[v] - centre[piece]) * grow
    return t[kept[lab]], float(np.percentile(radius[card], 99)) * (grow - 1)


def far_copies(entry, data, cut=()):
    """Lighter copies of a mesh for drawing it far away: [(world.json entry,
    bytes)], each lighter than the one before and with a larger error "e"
    (mesh units), or [] (too few triangles; no numpy / meshoptimizer). cut:
    its cut-out submeshes - their leaf cards are thinned, the rest simplified."""
    if entry["ni"] // 3 < FAR_MIN:
        return []
    try:
        import numpy as np
        import meshoptimizer  # noqa: F401
    except ImportError:
        return []
    p, uv, idx = mesh_arrays(entry, data)
    tris = [idx[s:s + n].reshape(-1, 3) for s, n in entry["sub"]]
    fixed = np.zeros(len(p), bool)
    for k, t in enumerate(tris):
        if k not in cut:
            fixed[t] = True
    cards = {}
    for k in cut:
        c = cards_of(p, tris[k], fixed) if k < len(tris) else None
        if c:
            cards[k] = c
    # Solid choices: (error, triangles per submesh); a cut-out submesh with no
    # cards is simplified too (a fern frond, a big leaf sheet).
    solid = [(0.0, tris)]
    for rel in FAR_ERRORS:
        got, err = list(tris), 0.0
        for k, t in enumerate(tris):
            if k not in cards and len(t):
                got[k], e = simplify(p, t, rel)
                err = max(err, e)
        solid.append((err, got))
    thinned = {kp: {k: thin(None, tris[k], c, kp) for k, c in cards.items()} for kp in FAR_KEEP} if cards else {}
    keeps = [(0.0, 1.0)] + [(max(e for _, e in thinned[kp].values()), kp) for kp in thinned]
    choices = []
    for es, st in solid:
        for ec, kp in keeps:
            n = sum(len(thinned[kp][k][0]) if kp < 1 and k in cards else len(t) for k, t in enumerate(st))
            choices.append((max(es, ec), n, st, kp))
    choices.sort(key=lambda c: (c[0], c[1]))
    out, prev, nrm = [], entry["ni"] // 3, None
    for err, n, st, kp in choices:
        if n >= prev * FAR_GAIN or n == 0:
            continue
        q = p.astype(np.float64) if kp < 1 else None
        parts = [thin(q, tris[k], cards[k], kp)[0] if kp < 1 and k in cards else t for k, t in enumerate(st)]
        flat = np.concatenate(parts).reshape(-1)
        sub, at = [], 0
        for t in parts:
            sub.append([at, len(t) * 3])
            at += len(t) * 3
        e = {"name": entry["name"], "ni": len(flat), "sub": sub, "bb": entry["bb"], "e": round(err, 5)}
        if q is None:
            e.update(shared=True, nv=entry["nv"], i32=entry["i32"], uv=entry["uv"])
            body = flat.astype(np.uint32 if entry["i32"] else np.uint16).tobytes()
        else:
            if nrm is None:
                nrm = normals(p, idx)
            verts, inv = np.unique(flat, return_inverse=True)
            i32 = len(verts) > 65535
            e.update(nv=len(verts), i32=i32, uv=uv is not None, n=True)
            body = q[verts].astype(np.float32).tobytes()
            if uv is not None:
                body += uv[verts].astype(np.float32).tobytes()
            body += nrm[verts].astype(np.float32).tobytes()
            body += inv.astype(np.uint32 if i32 else np.uint16).tobytes()
        out.append((e, body))
        prev = n
    return out


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


def pack(out, meta, far=True):
    """Packs out/m, out/t and out/c (the chunks meta lists) into out/b and sets
    meta's "files", meshes' "at" (+ "far" and the far copies' entries),
    "textures", chunks' "at", "version" 3; removes m/, t/, c/ and an older
    layout's p/, q/. Returns a line of numbers for the log."""
    import collections
    size = meta["chunk"]
    models, materials, meshes = meta["models"], meta["materials"], meta["meshes"]
    users = collections.defaultdict(set)      # ("m", i) / ("t", i, ext) -> chunk keys
    render = set()                            # meshes a rendered model uses (far copy candidates)
    cut = collections.defaultdict(set)        # mesh -> its cut-out submeshes (leaf cards thinned there)
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
            model = models[struct.unpack_from("<I", data, k * 52)[0]]
            users[("m", model["mesh"])].add(key)
            if model["kind"] != "collide" and not (model["mats"] and all(mi >= 0 and materials[mi].get("fx") for mi in model["mats"])):
                render.add(model["mesh"])     # glints / particles (fx): never drawn - no far copies
            for k, mi in enumerate(model["mats"]):
                if mi >= 0 and materials[mi].get("cut"):
                    cut[model["mesh"]].add(k)
            for mi in model["mats"]:
                if mi < 0:
                    continue
                mat = materials[mi]
                # Only the variant world3d.js reads: a cut-out's main texture
                # as .png (the .jpg when it has no .png), every other one - and
                # the top layers - as .jpg. Packing both sent each cut-out
                # twice (17.4 MB of textures for the 10.4 a view used, 2026-10-02).
                for t, ct in ((mat.get("tex", -1), mat.get("cut")), (mat.get("top", -1), False)):
                    if t is not None and t >= 0:
                        users[("t", t, "png" if ct and str(t) in pngs else "jpg")].add(key)

    def mesh_bytes(i):
        with open(os.path.join(out, "m", "%d.bin" % i), "rb") as f:
            return f.read()

    # blobs: (key, users, order within its group - a mesh's far copies right after it)
    blobs = []
    far_bytes = {}
    far_count, far_meshes, far_size = 0, 0, 0
    for b, us in sorted(users.items(), key=lambda kv: str(kv[0])):
        blobs.append((b, us, (b[0], b[1], 0) if b[0] == "m" else b))
        if b[0] != "m" or not far or b[1] not in render:
            continue
        i = b[1]
        copies = far_copies(meshes[i], mesh_bytes(i), cut[i])
        if copies:
            far_meshes += 1
            meshes[i]["far"] = []
        for k, (e, data) in enumerate(copies):
            e["of"] = i
            j = len(meshes)
            meshes.append(e)
            meshes[i]["far"].append(j)
            far_bytes[j] = data
            blobs.append((("m", j), us, ("m", i, k + 1)))
            far_count += 1
            far_size += len(data)
    for c in meta["chunks"]:
        blobs.append((("c", c["file"]), {chunk_key(c)}, ("c", c["file"])))

    def data_of(b):
        if b[0] == "c":
            return chunk_data[b[1]]
        if b[0] == "m":
            return far_bytes[b[1]] if b[1] in far_bytes else mesh_bytes(b[1])
        with open(os.path.join(out, "t", "%d.%s" % (b[1], b[2])), "rb") as f:
            return f.read()

    groups = collections.defaultdict(list)
    for b, us, order in blobs:
        g = cell_of[next(iter(us))] if b[0] == "c" else group_of(us, size, cell_of)
        groups[g].append((order, b))

    shutil.rmtree(os.path.join(out, "b"), ignore_errors=True)    # a previous export's (more files: left over)
    os.makedirs(os.path.join(out, "b"))
    files = []
    places = {}
    f, at = None, 0
    for g in sorted(groups, key=lambda k: tuple(str(x) for x in k)):
        new = True
        for _, b in sorted(groups[g], key=lambda ob: tuple(str(x) for x in ob[0])):
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
    return "%d files; %d far copies of %d meshes (%.1f MB)" % (len(files), far_count, far_meshes, far_size / 1e6)


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
    out/m, out/t, out/c and the meta (returned) without places or copies
    (far copies, or the phone LODs before them)."""
    with open(os.path.join(src, "world.json"), encoding="utf-8") as f:
        meta = json.load(f)
    shutil.rmtree(out, ignore_errors=True)
    for d in ("m", "t", "c"):
        os.makedirs(os.path.join(out, d))
    originals = [i for i, e in enumerate(meta["meshes"]) if "of" not in e]
    assert originals == list(range(len(originals))), "copies are always after the meshes"
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
        for k in ("pack", "at", "lod", "far"):
            e.pop(k, None)
    for k in ("packs", "texpacks", "textures", "files"):
        meta.pop(k, None)
    meta["version"] = 1
    return meta


def repack(src, out, far=True):
    if os.path.abspath(src) == os.path.abspath(out):
        sys.exit("repack writes a new folder (it clears it first) - give another <out>")
    meta = unpack(src, out)
    line = pack(out, meta, far)
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
    submeshes - a quarter, then the rest (2 * n * n triangles - far copies)."""
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


def bush(cards=300, seed=3):
    """A plant: a solid stem (a 12-sided tube, submesh 0) and leaf cards (two
    triangles each, scattered round it, submesh 1 - a cut-out)."""
    import math
    rnd = random.Random(seed)
    v, uv, idx = [], [], []
    for j in range(5):
        for i in range(12):
            a = i / 12 * 2 * math.pi
            v.append((0.1 * math.cos(a), j * 0.5, 0.1 * math.sin(a)))
            uv.append((i / 12, j / 4))
    for j in range(4):
        for i in range(12):
            a, b = j * 12 + i, j * 12 + (i + 1) % 12
            idx += [a, b, a + 12, b, b + 12, a + 12]
    stem = len(idx)
    for _ in range(cards):
        cx, cy, cz = rnd.uniform(-1, 1), rnd.uniform(0.5, 2), rnd.uniform(-1, 1)
        s, a = rnd.uniform(0.05, 0.12), rnd.uniform(0, math.pi)
        dx, dz = math.cos(a) * s, math.sin(a) * s
        k = len(v)
        v += [(cx - dx, cy - s, cz - dz), (cx + dx, cy - s, cz + dz), (cx + dx, cy + s, cz + dz), (cx - dx, cy + s, cz - dz)]
        uv += [(0, 0), (1, 0), (1, 1), (0, 1)]
        idx += [k, k + 2, k + 1, k, k + 3, k + 2]
    data = b"".join(struct.pack("<3f", *p) for p in v) + b"".join(struct.pack("<2f", *t) for t in uv)
    data += struct.pack("<%dH" % len(idx), *idx)
    entry = {"name": "bush", "nv": len(v), "ni": len(idx), "i32": False, "uv": True, "sub": [[0, stem], [stem, len(idx) - stem]],
             "bb": [-1.2, 0, -1.2, 1.2, 2.2, 1.2]}
    return data, entry


def synthetic(out, version, x0=400.0, z0=-100.0, chunk=250, seed=1):
    """A tiny world around (x0, z0) - the Slot 1 tree spot by default: a 3 x 3
    block of surface chunks, boxes of a few sizes (some in one chunk only,
    one in every chunk, one in two), a heavy hill and a bush with leaf cards
    (far copies) in three, a solid and a cut-out material, a collider.
    version 1 = one file per blob, 3 = packed."""
    rnd = random.Random(seed)
    shutil.rmtree(out, ignore_errors=True)
    for d in ("m", "t", "c"):
        os.makedirs(os.path.join(out, d))
    meshes = []
    for k in range(16):
        data, e = hill() if k == 14 else bush() if k == 15 else box(2 + k, 3 + (k % 4) * 4, 2 + k % 3, i32=(k == 5), uv=(k != 7))
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
    models.append({"mesh": 14, "mats": [0, 1], "kind": "render", "layer": 0})     # a cut-out quarter: one piece, simplified
    models.append({"mesh": 15, "mats": [1, 0], "kind": "render", "layer": 0})     # the stem solid, the leaves cut out
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
                items += [(14, 150, 150), (15, 170, 150)]             # the hill and the bush: three chunks
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


def test_far(m3, b):
    """The far copies of the synthetic world: the hill's shared (its own
    vertices), the bush's with fewer, grown cards and its stem kept; each
    lighter than the one before, errors growing, beside their mesh."""
    import numpy as np
    hill_e, bush_e = m3["meshes"][14], m3["meshes"][15]
    assert all("far" not in e for e in m3["meshes"][:14])
    for full_ix, full in ((14, hill_e), (15, bush_e)):
        fars = full["far"]
        assert fars, "no far copies of %s" % full["name"]
        prev_n, prev_e = full["ni"], 0.0
        for j in fars:
            e = m3["meshes"][j]
            assert e["of"] == full_ix and e["ni"] < prev_n * FAR_GAIN and e["e"] >= prev_e, (full["name"], e["ni"], e["e"])
            assert e["at"][0] == full["at"][0], "a far copy in another file than its mesh"
            assert e["at"][1] > full["at"][1]
            prev_n, prev_e = e["ni"], e["e"]
    p, uv, hidx = mesh_arrays(hill_e, read(b, m3, 14))
    for j in hill_e["far"]:
        e = m3["meshes"][j]
        assert e["shared"] and e["nv"] == hill_e["nv"] and len(read(b, m3, j)) == e["ni"] * 2, "a shared copy carries vertices"
        idx = np.frombuffer(read(b, m3, j), np.uint16)
        assert idx.max() < hill_e["nv"] and len(e["sub"]) == 2
        # Its faces still face up (the winding kept) and it stays within its error of the hill.
        tri = p[idx.reshape(-1, 3)]
        ny = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])[:, 1]
        hy = np.cross(p[hidx[1]] - p[hidx[0]], p[hidx[2]] - p[hidx[0]])[1]
        assert (np.sign(ny) == np.sign(hy)).all(), "far copy faces turned"
    assert m3["meshes"][hill_e["far"][-1]]["ni"] < hill_e["ni"] / 10
    bp, buv, bidx = mesh_arrays(bush_e, read(b, m3, 15))
    stem = bush_e["sub"][0][1]
    area = lambda q, t: np.linalg.norm(np.cross(q[t[:, 1]] - q[t[:, 0]], q[t[:, 2]] - q[t[:, 0]]), axis=1).sum() / 2
    full_leaf = area(bp, bidx[stem:].reshape(-1, 3))
    thinned = False
    for j in bush_e["far"]:
        e = m3["meshes"][j]
        if e.get("shared"):
            continue
        thinned = True
        lp, luv, lidx = mesh_arrays(e, read(b, m3, j))
        ln = np.frombuffer(read(b, m3, j), np.float32, e["nv"] * 3, e["nv"] * 20).reshape(-1, 3)
        assert np.allclose(np.linalg.norm(ln, axis=1), 1, atol=1e-5), "normals not unit"
        a0, n0 = e["sub"][1]
        leaves = lidx[a0:a0 + n0].reshape(-1, 3)
        assert n0 < bush_e["sub"][1][1], "no card dropped"
        # The grown cards cover about what all of them did.
        assert 0.8 < area(lp, leaves) / full_leaf < 1.25, area(lp, leaves) / full_leaf
        assert (n0 // 6) * 6 == n0, "a card cut in half"
    assert thinned, "the bush's cards never thinned"


def test(tmp):
    """The same synthetic world unpacked and packed: every blob's bytes the
    same, places aligned, the far copies (test_far); a repack round trip."""
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
    try:
        import meshoptimizer  # noqa: F401
        has_far = True
    except ImportError:
        has_far = False
    if has_far:
        test_far(m3, b)
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
    # repack: version 3 back to blobs and packed again - the same bytes, the same copies.
    r = os.path.join(tmp, "re")
    repack(b, r)
    with open(os.path.join(r, "world.json"), encoding="utf-8") as f:
        m5 = json.load(f)
    for ix in used:
        assert read(r, m5, ix) == read(a, m1, ix)
    for c1, c5 in zip(m1["chunks"], m5["chunks"]):
        assert read_chunk(a, m1, c1) == read_chunk(r, m5, c5)
    assert len(m5["meshes"]) == len(m3["meshes"]) and all(m5["meshes"][i].get("far") == m3["meshes"][i].get("far") for i in used)
    print("ok: %d files from %d chunks%s" % (len(m3["files"]), len(m3["chunks"]),
          ", far copies: hill %d -> %s, bush %d -> %s triangles" % (
              m3["meshes"][14]["ni"] // 3, [m3["meshes"][j]["ni"] // 3 for j in m3["meshes"][14]["far"]],
              m3["meshes"][15]["ni"] // 3, [m3["meshes"][j]["ni"] // 3 for j in m3["meshes"][15]["far"]]) if has_far else
          " (no far copy check: pip install meshoptimizer)"))


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
