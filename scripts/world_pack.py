"""Mesh packs for the 3D world's export (scripts/world-extract.py, the
writer stage): the meshes, one file each (m/<i>.bin), joined into a few
pack files, so a chunk costs a few requests instead of one per mesh.

  p/<i>.bin    meshes back to back, each as its m/<i>.bin was (positions,
               uv, indices), padded to 4 bytes (the site reads float32s in
               place). world.json: "packs" = the pack files, each mesh's
               "pack" = [pack, byte offset, byte length]; "version" 2.

Which pack a mesh goes in, by the chunks whose instances use it:
- one chunk: that chunk's own pack(s) - fetched with the chunk;
- COMMON chunks or more (trees, rocks, cave walls): the area's common
  packs - fetched once, then cached for every chunk;
- a few: a pack per area and NEAR-metre cell (the users' mean column) -
  neighbours load together anyway.
Each pack is cut at PACK_MAX bytes. A mesh no instance uses is dropped.

Textures the same way (2026-10-02: they were most of a 3D view's requests,
458 of 604 for the Labskip spot): t/<i>.jpg and t/<i>.png joined into
q/<i>.bin by the chunks whose models' materials use them; world.json
"texpacks" = the files, "textures"[i] = [jpg pack, offset, length, png
pack, offset, length] (-1 where that file does not exist). t/ is removed.

    python scripts/world_pack.py test              round trip on a synthetic export, both formats
    python scripts/world_pack.py synthetic <out> [1|2]   a tiny world to upload to a local site

Standard library only.
"""
import json
import os
import random
import shutil
import struct
import sys
import time

COMMON = 8
NEAR = 1000.0
PACK_MAX = 4 * 1024 * 1024


def groups(users, chunk):
    """{group key: [mesh index, ...]}. users: mesh index -> set of chunk keys
    (area, cx, cz, wide), cx / cz in columns of `chunk` metres."""
    out = {}
    for ix in sorted(users):
        us = sorted(users[ix])
        if not us:
            continue
        if len(us) == 1:
            key = ("own",) + tuple(us[0])
        elif len(us) >= COMMON:
            key = ("common", us[0][0])
        else:
            mx = sum(u[1] for u in us) / len(us) * chunk
            mz = sum(u[2] for u in us) / len(us) * chunk
            key = ("near", us[0][0], int(mx // NEAR), int(mz // NEAR))
        out.setdefault(key, []).append(ix)
    return out


def write(out, meshes, users, chunk):
    """Packs the m/<i>.bin files under out into p/<i>.bin, sets each used
    mesh's "pack" in meshes (world.json's list), removes m/. Returns the
    pack names for world.json."""
    shutil.rmtree(os.path.join(out, "p"), ignore_errors=True)    # a previous export's (more packs: left over)
    os.makedirs(os.path.join(out, "p"))
    packs = []
    f, size = None, 0
    for key, ixs in sorted(groups(users, chunk).items(), key=lambda kv: tuple(str(k) for k in kv[0])):
        new = True
        for ix in ixs:
            with open(os.path.join(out, "m", "%d.bin" % ix), "rb") as m:
                data = m.read()
            if new or (size and size + len(data) > PACK_MAX):
                if f:
                    f.close()
                packs.append("p/%d.bin" % len(packs))
                f, size, new = open(os.path.join(out, packs[-1]), "wb"), 0, False
            meshes[ix]["pack"] = [len(packs) - 1, size, len(data)]
            f.write(data)
            pad = -len(data) % 4
            f.write(b"\0" * pad)
            size += len(data) + pad
    if f:
        f.close()
    shutil.rmtree(os.path.join(out, "m"), ignore_errors=True)
    return packs


def used_variants(materials):
    """The (texture, "jpg" / "png") files the site reads, as world3d.js
    picks them: a cut-out's main texture as .png (the .jpg when it has no
    .png), every other one - and the top layers - as .jpg. Packing both
    sent each cut-out twice (a view fetched 17.4 MB of textures for 10.4
    it used, 2026-10-02)."""
    used = set()
    for d in materials:
        t = d.get("tex", -1)
        if t is not None and t >= 0:
            used.add((t, "png" if d.get("cut") else "jpg"))
        top = d.get("top", -1)
        if top is not None and top >= 0:
            used.add((top, "jpg"))
    return used


def write_textures(out, users, chunk, used=None):
    """Packs t/<i>.jpg / .png into q/<i>.bin by users (texture index ->
    set of chunk keys, as for meshes). used: the (index, ext) files to
    keep (used_variants; None = all) - a cut-out wanting a .png it does not
    have reads the .jpg. Returns (pack names, entries) for world.json's
    "texpacks" / "textures"; removes t/."""
    tdir = os.path.join(out, "t")
    names = os.listdir(tdir) if os.path.isdir(tdir) else []
    count = 1 + max([int(n.split(".")[0]) for n in names if n.split(".")[0].isdigit()] or [-1])
    entries = [[-1, 0, 0, -1, 0, 0] for _ in range(count)]
    shutil.rmtree(os.path.join(out, "q"), ignore_errors=True)
    os.makedirs(os.path.join(out, "q"))
    packs = []
    f, size = None, 0
    for key, ixs in sorted(groups(users, chunk).items(), key=lambda kv: tuple(str(k) for k in kv[0])):
        new = True
        for ix in ixs:
            has_png = os.path.exists(os.path.join(tdir, "%d.png" % ix))
            for slot, ext in ((0, "jpg"), (3, "png")):
                path = os.path.join(tdir, "%d.%s" % (ix, ext))
                if not os.path.exists(path):
                    continue
                wanted = used is None or (ix, ext) in used or (ext == "jpg" and (ix, "png") in used and not has_png)
                if not wanted:
                    continue
                with open(path, "rb") as t:
                    data = t.read()
                if new or (size and size + len(data) > PACK_MAX):
                    if f:
                        f.close()
                    packs.append("q/%d.bin" % len(packs))
                    f, size, new = open(os.path.join(out, packs[-1]), "wb"), 0, False
                entries[ix][slot:slot + 3] = [len(packs) - 1, size, len(data)]
                f.write(data)
                size += len(data)
    if f:
        f.close()
    shutil.rmtree(tdir, ignore_errors=True)
    return packs, entries


def read_texture(out, meta, ix, ext):
    """One texture file's bytes from an export in either format (the test)."""
    if "textures" not in meta:
        path = os.path.join(out, "t", "%d.%s" % (ix, ext))
        return open(path, "rb").read() if os.path.exists(path) else None
    e = meta["textures"][ix] if ix < len(meta["textures"]) else None
    s = 0 if ext == "jpg" else 3
    if not e or e[s] < 0:
        return None
    with open(os.path.join(out, meta["texpacks"][e[s]]), "rb") as f:
        f.seek(e[s + 1])
        return f.read(e[s + 2])


def read(out, meta, ix):
    """One mesh's bytes from an export in either format (for the test)."""
    m = meta["meshes"][ix]
    if "packs" not in meta:
        with open(os.path.join(out, "m", "%d.bin" % ix), "rb") as f:
            return f.read()
    if "pack" not in m:
        return None
    p, off, n = m["pack"]
    with open(os.path.join(out, meta["packs"][p]), "rb") as f:
        f.seek(off)
        return f.read(n)


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


def synthetic(out, version, x0=400.0, z0=-100.0, chunk=250, seed=1):
    """A tiny world around (x0, z0) - the Slot 1 tree spot by default: a 3 x 3
    block of surface chunks, boxes of a few sizes (some in one chunk only,
    one in every chunk, one in two), a solid and a coloured material, a
    collider. version 1 = one file per mesh, 2 = packs."""
    rnd = random.Random(seed)
    shutil.rmtree(out, ignore_errors=True)
    for d in ("m", "t", "c"):
        os.makedirs(os.path.join(out, d))
    meshes = []
    for k in range(14):
        data, e = box(2 + k, 3 + (k % 4) * 4, 2 + k % 3, i32=(k == 5), uv=(k != 7))
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
    cx0, cz0 = int(x0 // chunk) - 1, int(z0 // chunk) - 1
    chunks, users = [], {}
    for a in range(3):
        for b in range(3):
            cx, cz = cx0 + a, cz0 + b
            items = [(0, 10, 10), (13, 30, 30)]                       # in every chunk: common
            items.append((1 + a * 3 + b, 60, 60))                     # one chunk only
            if a == 1 and b < 2:
                items.append((10, 90, 120))                           # two chunks: a "near" pack
            items += [(11 + (a + b) % 2, rnd.uniform(0, chunk), rnd.uniform(0, chunk)) for _ in range(5)]
            name = "c/surface_%d_%d.bin" % (cx, cz)
            with open(os.path.join(out, name), "wb") as f:
                for model, x, z in items:
                    f.write(struct.pack("<I12f", model, 1, 0, 0, cx * chunk + x, 0, 1, 0, 80, 0, 0, 1, cz * chunk + z))
                    users.setdefault(models[model]["mesh"], set()).add(("surface", cx, cz, False))
            chunks.append({"file": name, "area": "surface", "x": cx * chunk, "z": cz * chunk, "y0": 80, "y1": 80,
                           "n": len(items), "tris": 0, "bb": [cx * chunk - 10, cz * chunk - 10, cx * chunk + chunk + 10, cz * chunk + chunk + 10]})
    meta = {"version": 1, "build": int(time.time()), "chunk": chunk, "layers": {"0": "Default"}, "materials": mats,
            "meshes": meshes, "models": models, "chunks": chunks}
    if version == 2:
        meta["packs"] = write(out, meshes, users, chunk)
        meta["version"] = 2
        tusers = {}
        for mi, model in enumerate(models):
            for m in model["mats"]:
                for t in (mats[m].get("tex", -1), mats[m].get("top", -1)):
                    if t >= 0:
                        tusers.setdefault(t, set()).update(users.get(model["mesh"], set()))
        meta["texpacks"], meta["textures"] = write_textures(out, tusers, chunk, used_variants(mats))
    with open(os.path.join(out, "world.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f, separators=(",", ":"))
    return meta, users


def png(w=8, h=8):
    """A small opaque checker PNG (no imaging library needed)."""
    import zlib
    rows = b"".join(b"\0" + b"".join(bytes((200, 170, 120, 255) if (x + y) % 2 else (90, 110, 70, 255)) for x in range(w)) for y in range(h))
    chunk = lambda t, d: struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + \
        chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b"")


def test(tmp):
    """Both formats from the same synthetic world: every used mesh's bytes
    are the same, packs are 4-byte aligned and grouped as documented."""
    a, b = os.path.join(tmp, "v1"), os.path.join(tmp, "v2")
    m1, users = synthetic(a, 1)
    m2, _ = synthetic(b, 2)
    assert not os.path.exists(os.path.join(b, "m")), "m/ left beside the packs"
    for ix in range(len(m1["meshes"])):
        one, two = read(a, m1, ix), read(b, m2, ix)
        if ix in users:
            assert one == two, "mesh %d differs" % ix
            p, off, n = m2["meshes"][ix]["pack"]
            assert off % 4 == 0 and n == len(one), (ix, off, n)
        else:
            assert two is None, "unused mesh %d packed" % ix
    for p in m2["packs"]:
        assert os.path.getsize(os.path.join(b, p)) % 4 == 0
    # Textures: the same bytes out of the packs, t/ gone, a missing file -1.
    assert not os.path.exists(os.path.join(b, "t")), "t/ left beside the texture packs"
    for ix, ext in ((0, "png"), (1, "jpg"), (1, "png")):
        assert read_texture(a, m1, ix, ext) == read_texture(b, m2, ix, ext), (ix, ext)
    # The cut-out's .jpg is never read (world3d.js takes its .png): not packed.
    assert read_texture(a, m1, 0, "jpg") is not None and read_texture(b, m2, 0, "jpg") is None
    assert read_texture(b, m2, 0, "png") is not None
    assert len(m2["texpacks"]) >= 1 and len(m2["textures"]) == 2
    # Grouping: the box in every chunk shares a pack with the collider; each
    # chunk's own mesh sits alone; the two-chunk mesh in a "near" pack.
    pk = lambda ix: m2["meshes"][ix]["pack"][0]
    assert pk(0) == pk(13), "common meshes split"
    own = {pk(1 + k) for k in range(9)}
    assert len(own) == 9 and pk(0) not in own and pk(10) not in own | {pk(0)}
    # PACK_MAX: a group over it is cut, nothing lost.
    global PACK_MAX
    keep, PACK_MAX = PACK_MAX, 300
    try:
        m3, _ = synthetic(os.path.join(tmp, "v2small"), 2)
        assert len(m3["packs"]) > len(m2["packs"])
        for ix in users:
            assert read(os.path.join(tmp, "v2small"), m3, ix) == read(a, m1, ix)
    finally:
        PACK_MAX = keep
    # One chunk on a cold page: its file + a file per mesh before, + a file per pack after.
    centre = [ix for ix, us in users.items() if ("surface", 1, -1, False) in us]
    packs = {pk(ix) for ix in centre}
    print("ok: %d meshes in %d packs; the centre chunk, cold: %d requests before (chunk + %d meshes), "
          "%d after (chunk + %d packs)" % (len(users), len(m2["packs"]), 1 + len(centre), len(centre),
                                          1 + len(packs), len(packs)))


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "test":
        import tempfile
        with tempfile.TemporaryDirectory() as t:
            test(t)
    elif len(sys.argv) > 2 and sys.argv[1] == "synthetic":
        synthetic(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 2)
        print("wrote", sys.argv[2])
    else:
        print(__doc__)
