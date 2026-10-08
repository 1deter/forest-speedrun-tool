"""Read the game's scenes offline: every mesh renderer and mesh collider,
placed in the world, for the website's 3D map.

The meshes are not readable in game (Mesh.isReadable is false - the
collision meshes too), so they are read from the game's own files with
UnityPy (pip install UnityPy). Scenes (BuildSettings order = levelN):
level2 ForestMain_v08 (surface, cave shells, cave collision), level7
endgame_streaming, level10 MainSceneGreebles, level11
MainSceneWorldStorySpots, level15-30 the cave prop scenes.

    python scripts/world-extract.py stats [level ...]
    python scripts/world-extract.py export [out folder]

stats prints what a scene holds; export writes the web format (default
site/world-out, not in git; uploaded to the site like the aerial tiles):

  world.json   layers, materials (colour, texture), meshes, models
               (mesh + its materials + render / collide + layer), chunks,
               files; "version" 3 (1 = one file per blob, 2 = mesh and
               texture packs - both still read by the site)
  b/<i>.bin    everything below packed (scripts/world_pack.py: which file,
               why, the far copies); a mesh's / chunk's "at" = [file, byte
               offset, byte length], "textures"[i] = [jpg place, png place].
               Written as these while the scenes are read, packed at the end:
  m/<i>.bin    a mesh: float32 positions (n x 3), float32 uv (n x 2) when
               "uv", then uint16 or uint32 ("i32") triangle indices; "sub" =
               [first index, count] per submesh (= per material)
  t/<i>.jpg    a material's main texture, at most 256 px
  c/<area>_<cx>_<cz>.bin  the instances in one 250 m column of an area:
               uint32 model, then the world matrix's top 3 rows (12 float32,
               row-major) - Unity's axes (x east, y up, z north). An
               instance goes in the column of its mesh's centre, not of its
               origin (the cave grounds sit at 0,0,0 with world-space
               vertices); one wider than a column goes in <area>_<cx>_<cz>_L
               with the other wide ones. Each chunk's "bb" [x0, z0, x1, z1]
               is where its instances really reach - the site loads by it.

A model's kind: "render", "render-off" (a renderer the scene file keeps
switched off - the game turns it on: the endgame's areas as they are
entered, the LOD scripts) or "collide" (a mesh collider: walls, floors,
invisible blockers).

Areas: "surface", "caves" (the main scene's Caves root, the Cave layer, the
cave prop scenes, and anything whose whole mesh is under the terrain where
it stands - Cave 6's wood panels sit under a root of their own) and
"endgame". LOD groups keep LOD 0 only.

Dev only; the game files are never shipped or committed.
"""
import collections
import time
import os
import sys

import numpy as np
import UnityPy
import world_checks
import world_pack
from UnityPy.helpers.MeshHelper import MeshHandler

GAME = os.path.join(os.environ.get("FOREST_ROOT", r"G:\SteamLibrary\steamapps\common\The Forest"), "TheForest_Data")
SCENES = {2: "ForestMain_v08", 7: "endgame_streaming", 10: "MainSceneGreebles", 11: "MainSceneWorldStorySpots"}
for i, n in enumerate(["01", "02", "03", "04", "05", "06", "07", "08", "09", "10", "HC", "Snow", "", "Junk", "IE", "IW"]):
    SCENES[15 + i] = "Cave_%s_Props_Streaming" % n if n else "CaveProps_Streaming"


def quat_matrix(q):
    x, y, z, w = q
    return np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


class Scene:
    """One level file: GameObjects, their world matrices, their meshes."""

    def __init__(self, level):
        self.level = level
        self.env = UnityPy.load(os.path.join(GAME, "level%d" % level))
        self.local = {}      # transform path id -> (4x4 local, father path id)
        self.go_of = {}      # transform path id -> GameObject path id
        self.gos = {}        # GameObject path id -> (name, layer, active, transform path id)
        self.parts = []      # (kind, GameObject path id, mesh PPtr, enabled)
        self.shapes = []     # (GameObject path id, shape key, 4x4 in the object's space) - box / sphere / capsule colliders
        for o in self.env.objects:
            t = o.type.name
            if t in ("Transform", "RectTransform"):
                d = o.read()
                m = np.eye(4)
                s = d.m_LocalScale
                m[:3, :3] = quat_matrix((d.m_LocalRotation.x, d.m_LocalRotation.y, d.m_LocalRotation.z, d.m_LocalRotation.w)) \
                    * np.array([s.x, s.y, s.z])
                p = d.m_LocalPosition
                m[:3, 3] = (p.x, p.y, p.z)
                self.local[o.path_id] = (m, d.m_Father.path_id)
                self.go_of[o.path_id] = d.m_GameObject.path_id
            elif t == "GameObject":
                d = o.read()
                tr = None
                for c in d.m_Components:
                    pp = c.component if hasattr(c, "component") else c
                    if pp.type.name in ("Transform", "RectTransform") if hasattr(pp, "type") else False:
                        tr = pp.path_id
                self.gos[o.path_id] = [d.m_Name, d.m_Layer, d.m_IsActive, tr]
            elif t == "MeshFilter":
                d = o.read()
                self.parts.append(("render", d.m_GameObject.path_id, d.m_Mesh, True))
            elif t == "MeshCollider":
                d = o.read()
                self.parts.append(("collide", d.m_GameObject.path_id, d.m_Mesh, bool(d.m_Enabled)))
            elif t in ("BoxCollider", "SphereCollider", "CapsuleCollider"):
                d = o.read_typetree()
                if d.get("m_IsTrigger") or not d.get("m_Enabled", 1):
                    continue    # triggers are not walls
                c = d["m_Center"]
                m = np.eye(4)
                m[:3, 3] = (c["x"], c["y"], c["z"])
                if t == "BoxCollider":
                    s = d["m_Size"]
                    m[:3, :3] = np.diag([s["x"], s["y"], s["z"]])
                    key = ("box",)
                elif t == "SphereCollider":
                    m[:3, :3] = np.eye(3) * d["m_Radius"] * 2
                    key = ("sphere",)
                else:
                    r, h = d["m_Radius"], max(d["m_Height"], 2 * d["m_Radius"])
                    key = ("capsule", round(r, 2), round(h, 2), d["m_Direction"])
                self.shapes.append((d["m_GameObject"]["m_PathID"], key, m))
        # A GameObject's transform, when the component list did not say.
        for tid, gid in self.go_of.items():
            if gid in self.gos and self.gos[gid][3] is None:
                self.gos[gid][3] = tid
        self._world = {}

    def world(self, tid):
        """World matrix of a transform, and whether it and every parent is active."""
        if tid in self._world:
            return self._world[tid]
        chain = []
        t = tid
        while t and t in self.local and t not in self._world:
            chain.append(t)
            t = self.local[t][1]
        m, active = self._world.get(t, (np.eye(4), True))
        for t in reversed(chain):
            lm, _ = self.local[t]
            g = self.gos.get(self.go_of.get(t))
            m = m @ lm
            active = active and (g[2] if g else True)
            self._world[t] = (m, active)
        return self._world[tid]


_meshes = {}


def ref_key(pptr):
    """What a PPtr points at: (target file, path id). m_FileID is relative to
    the referencing file - the same number means another file in another
    scene - so it is resolved through that file's externals."""
    f = pptr.assetsfile
    target = f.name if pptr.m_FileID == 0 else f.externals[pptr.m_FileID - 1].path
    return (target.lower().replace("\\", "/").split("/")[-1], pptr.path_id)


def mesh_data(pptr):
    """(vertices n x 3, triangles m x 3) of a mesh, cached by file + path id."""
    key = ref_key(pptr)
    if key in _meshes:
        return _meshes[key]
    try:
        m = pptr.read()
        h = MeshHandler(m)
        h.process()
        v = np.array(h.m_Vertices, dtype=np.float32).reshape(-1, 3)
        tris = []
        for sub in h.get_triangles():
            tris.extend(sub)
        f = np.array(tris, dtype=np.int32).reshape(-1, 3)
        r = (m.m_Name, v, f)
    except Exception as e:
        r = (None, None, str(e))
    _meshes[key] = r
    return r


def stats(levels):
    for level in levels:
        s = Scene(level)
        c = collections.Counter()
        tri = collections.Counter()
        unique = collections.defaultdict(set)
        errors = collections.Counter()
        layers = collections.Counter()
        for kind, gid, pptr, enabled in s.parts:
            g = s.gos.get(gid)
            if not g or not pptr or pptr.path_id == 0:
                c[kind + " no mesh"] += 1
                continue
            _, active = s.world(g[3])
            if not active or not enabled:
                c[kind + " inactive"] += 1
                continue
            name, v, f = mesh_data(pptr)
            if v is None:
                errors[f[:60]] += 1
                continue
            c[kind] += 1
            tri[kind] += len(f)
            unique[kind].add((pptr.m_FileID, pptr.path_id))
            layers[(kind, g[1])] += 1
        print("level%d %s:" % (level, SCENES.get(level, "?")), dict(c))
        for k in unique:
            print("   %s: %d unique meshes, %d triangles placed" % (k, len(unique[k]), tri[k]))
        print("   layers:", sorted(layers.items()))
        if errors:
            print("   errors:", errors.most_common(5))
        sys.stdout.flush()


# --- export -------------------------------------------------------------------

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CHUNK = 250.0
TEX_MAX = 256
# Materials whose main texture is painted once over a whole model hundreds of
# metres across (the south mountains, 1024 px in the game): kept at this size,
# not TEX_MAX - at 256 px a mountain was smooth grey up close (2026-10-03).
TEX_BIG = 1024
BIG_MATERIALS = ("MountainLeft", "MountainMiddle", "MountainRight", "MountainGrassVariation")
CAVE_LAYER = 17
VOLUME_LAYERS = (4, 25)     # Water, Blocker: switched-off cubes with Base_Orange / Default-Diffuse
CUT_SHADERS = ("Foliage", "Leaves", "Transparent", "Cutout", "Grass")
FX_SHADERS = ("Particles/", "Lux/Particles", "Custom/Sheen", "Legacy Shaders/Particles")
BAKE_MATERIALS = ("Default-Material", "lambert2")   # a switched-off renderer with only these (or none) is never drawn
SKIP_MESHES = ("VRTreeRing",)   # the VR mode's ring round a tree trunk (black, never shown on a screen)
PRIMITIVES = ("Cube", "Sphere", "Quad", "Plane", "Cylinder", "Capsule")
CAVE_LODS = ("LOD_Cave", "LOD_CaveEntrance", "LOD_CaveMedium", "LOD_CaveSmall")   # scaled like their placeholder
# Scene roots the game moves at run time: skipped here, exported from the
# in-game dump as they stand (world/placed-*.txt, WorldDump.Placed). The
# yacht: reparented under a spawned yachtWobblePrefab 130 m away.
MOVED_ROOTS = {2: ("Yacht",)}


def lod_rest(scene):
    """GameObject path ids of renderers in LOD 1+ of a LOD group (skipped),
    less those LOD 0 draws too: a group can list one renderer in several
    levels (9 of level2's 156 - the yacht's hull, walls and sails were
    lost that way until 2026-10-01)."""
    renderer_go = {}
    for o in scene.env.objects:
        if o.type.name == "MeshRenderer":
            renderer_go[o.path_id] = o.read_typetree()["m_GameObject"]["m_PathID"]
    rest = set()
    for o in scene.env.objects:
        if o.type.name == "LODGroup":
            lods = o.read_typetree()["m_LODs"]
            first = {r["renderer"]["m_PathID"] for r in lods[0]["renderers"]} if lods else set()
            for l in lods[1:]:
                for r in l["renderers"]:
                    if r["renderer"]["m_PathID"] in first:
                        continue
                    g = renderer_go.get(r["renderer"]["m_PathID"])
                    if g:
                        rest.add(g)
    return rest


def root_name(scene, tid):
    while scene.local.get(tid, (0, 0))[1]:
        tid = scene.local[tid][1]
    g = scene.gos.get(scene.go_of.get(tid))
    return g[0] if g else ""


def obj_path(scene, tid):
    """An object's path from its root ("Sections/GlassOffice_A/GEO/Floor (1)"),
    written as WorldDump writes it (';' -> ',')."""
    names = []
    while tid:
        g = scene.gos.get(scene.go_of.get(tid))
        names.append(g[0] if g else "")
        tid = scene.local.get(tid, (0, 0))[1]
    return "/".join(reversed(names)).replace("\t", " ").replace(";", ",")


class Ground:
    """The terrain's height at a point, from the site's own bake
    (wwwroot/terrain, scripts/terrain-bake.py) - as map.js groundAt."""

    def __init__(self):
        import json
        d = os.path.join(ROOT, "site", "ForestSite", "wwwroot", "terrain")
        self.m = json.load(open(os.path.join(d, "terrain.json")))
        n = self.m["grid"]
        self.h = np.fromfile(os.path.join(d, self.m["heights"]), dtype=np.uint16).reshape(n, n).astype(np.float32)

    def at(self, x, z):
        m, n = self.m, self.m["grid"] - 1
        u, v = (x - m["x0"]) / m["sizeX"] * n, (z - m["z0"]) / m["sizeZ"] * n
        if not (0 <= u <= n and 0 <= v <= n):
            return None
        i, j = min(n - 1, int(u)), min(n - 1, int(v))
        fu, fv = u - i, v - j
        h = self.h
        top = h[j, i] * (1 - fu) + h[j, i + 1] * fu
        bottom = h[j + 1, i] * (1 - fu) + h[j + 1, i + 1] * fu
        return m["y0"] + (top * (1 - fv) + bottom * fv) / 65535 * m["sizeY"]


UNDER = 2.0     # metres: a mesh whose top is this far under the terrain is in a cave


class Export:
    def __init__(self, out):
        self.ground = Ground()
        self.out = out
        for d in ("m", "t", "c"):
            os.makedirs(os.path.join(out, d), exist_ok=True)
        self.meshes, self.mesh_ix = [], {}
        self.materials, self.mat_ix = [], {}
        self.textures = {}
        self.clear = {}      # texture index -> share of its pixels under half alpha
        self.black = {}      # texture name -> mean colour, for the near-black ones (gotcha 88)
        self.models, self.model_ix = [], {}
        self.chunks = collections.defaultdict(list)     # (area, cx, cz, wide) -> [(model, matrix)]
        self.reach = {}      # chunk key -> [x0, z0, x1, z1] of its instances' bounds
        self.under = 0       # "surface" instances moved to the caves (under the terrain)
        self.members = set()     # (object path, mesh name, vertex count) an endgame area switches on (area-members.txt)

    @staticmethod
    def key(pptr):
        return ref_key(pptr)

    def mesh(self, pptr):
        k = self.key(pptr)
        if k in self.mesh_ix:
            return self.mesh_ix[k]
        ix = None
        try:
            m = pptr.read()
            h = MeshHandler(m)
            h.process()
            v = np.array(h.m_Vertices, dtype=np.float32).reshape(-1, 3)
            uv = np.array(h.m_UV0, dtype=np.float32).reshape(-1, 2) if h.m_UV0 else None
            if uv is not None and len(uv) != len(v):
                uv = None
            subs, idx = [], []
            for sub in h.get_triangles():
                flat = [i for t in sub for i in t]
                subs.append([len(idx), len(flat)])
                idx.extend(flat)
            if len(v) and idx:
                i32 = len(v) > 65535
                ix = len(self.meshes)
                with open(os.path.join(self.out, "m", "%d.bin" % ix), "wb") as f:
                    f.write(v.tobytes())
                    if uv is not None:
                        f.write(uv.tobytes())
                    f.write(np.array(idx, dtype=np.uint32 if i32 else np.uint16).tobytes())
                lo, hi = v.min(0), v.max(0)
                self.meshes.append({"name": m.m_Name, "nv": len(v), "ni": len(idx), "i32": i32, "uv": uv is not None,
                                    "sub": subs, "bb": [round(float(a), 3) for a in list(lo) + list(hi)]})
        except Exception as e:
            print("   mesh failed:", k, e)
        self.mesh_ix[k] = ix
        return ix

    def write_mesh(self, name, v, idx, subs, uv=None):
        i32 = len(v) > 65535
        ix = len(self.meshes)
        with open(os.path.join(self.out, "m", "%d.bin" % ix), "wb") as f:
            f.write(np.asarray(v, dtype=np.float32).tobytes())
            if uv is not None:
                f.write(np.asarray(uv, dtype=np.float32).tobytes())
            f.write(np.array(idx, dtype=np.uint32 if i32 else np.uint16).tobytes())
        v = np.asarray(v)
        lo, hi = v.min(0), v.max(0)
        self.meshes.append({"name": name, "nv": len(v), "ni": len(idx), "i32": i32, "uv": uv is not None,
                            "sub": subs, "bb": [round(float(a), 3) for a in list(lo) + list(hi)]})
        return ix

    def shape(self, key):
        """A collider primitive as a mesh: a unit box / sphere (scaled by the
        instance), a capsule at its own size."""
        if key in self.mesh_ix:
            return self.mesh_ix[key]
        if key[0] == "box":
            v = [(x, y, z) for x in (-.5, .5) for y in (-.5, .5) for z in (-.5, .5)]
            q = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
            idx = [i for a, b, c, d in q for i in (a, b, c, a, c, d)]
        else:
            if key[0] == "sphere":
                r, h, axis = 0.5, 1.0, 1
            else:
                _, r, h, axis = key
            rings, segs, v, idx = 8, 12, [], []
            half = h / 2 - r
            for i in range(rings + 1):
                a = np.pi * i / rings - np.pi / 2
                yy = r * np.sin(a) + (half if i > rings / 2 else -half if i < rings / 2 else 0)
                for j in range(segs):
                    b = 2 * np.pi * j / segs
                    p = [r * np.cos(a) * np.cos(b), yy, r * np.cos(a) * np.sin(b)]
                    v.append(p if axis == 1 else [p[1], p[0], p[2]] if axis == 0 else [p[0], p[2], p[1]])
            for i in range(rings):
                for j in range(segs):
                    a, b = i * segs + j, i * segs + (j + 1) % segs
                    idx += [a, b, a + segs, b, b + segs, a + segs]
        ix = self.write_mesh("__" + "_".join(str(k) for k in key), np.array(v, dtype=np.float32), idx, [[0, len(idx)]])
        self.mesh_ix[key] = ix
        return ix

    def texture(self, pptr, size=TEX_MAX):
        k = self.key(pptr) if size == TEX_MAX else (self.key(pptr), size)
        if k in self.textures:
            return self.textures[k]
        ix = -1
        try:
            tex = pptr.read()
            img = tex.image.convert("RGBA")
            # Colour and alpha shrunk apart: Pillow's RGBA resize premultiplies
            # by alpha, and the Standard shader's textures keep smoothness
            # there (0 on the lab's concrete / metal) - they came out black
            # (the overlook, the boss room; 2026-10-03).
            rgb, a = img.convert("RGB"), img.getchannel("A")
            rgb.thumbnail((size, size))
            a.thumbnail((size, size))
            img = rgb.copy()
            img.putalpha(a)
            ix = sum(1 for v in self.textures.values() if v >= 0)
            mean = float(np.asarray(img.convert("RGB")).mean())
            if world_checks.is_near_black(mean):
                self.black["%s (t/%d)" % (getattr(tex, "m_Name", "?"), ix)] = mean
            img.convert("RGB").save(os.path.join(self.out, "t", "%d.jpg" % ix), quality=85)
            # Leaves, grass, fences: the alpha cuts the shape out. Kept as a
            # PNG beside the JPEG when a real part of the texture is clear.
            a = np.asarray(img.getchannel("A"))
            self.clear[ix] = float((a < 128).mean())
            if self.clear[ix] > 0.05:
                img.save(os.path.join(self.out, "t", "%d.png" % ix), optimize=True)
        except Exception as e:
            print("   texture failed:", k, e)
        self.textures[k] = ix
        return ix

    def material(self, pptr):
        if pptr is None or pptr.path_id == 0:
            return -1
        k = self.key(pptr)
        if k in self.mat_ix:
            return self.mat_ix[k]
        entry = self.material_entry(pptr.read, k)
        ix = len(self.materials)
        self.materials.append(entry)
        self.mat_ix[k] = ix
        return ix

    def material_entry(self, read, label):
        """A material's colour and textures: "tex" the main texture; "top"
        (+ "topScale") the layer the game's Lux shader lays over upward
        faces - snow on the snow cliffs, grass on cliffs, moss on rocks
        (_WnAlbedoSmoothness); "detail" (+ "detailScale") the Standard
        shader's detail albedo, multiplied in (x2) where the material turns
        it on (_DETAIL_MULX2: the mountains' rock up close, 78 materials)."""
        entry = {"name": "", "color": [0.7, 0.7, 0.7, 1], "tex": -1}
        try:
            m = read()
            entry["name"] = m.m_Name
            try:
                entry["shader"] = m.m_Shader.read().m_ParsedForm.m_Name
            except Exception:
                entry["shader"] = ""
            props = m.m_SavedProperties
            floats = dict(props.m_Floats)
            detail = "_DETAIL_MULX2" in str(getattr(m, "m_ShaderKeywords", ""))
            for name, c in props.m_Colors:
                if name == "_Color":
                    entry["color"] = [round(c.r, 3), round(c.g, 3), round(c.b, 3), round(c.a, 3)]
            for name, te in props.m_TexEnvs:
                if not (te.m_Texture and te.m_Texture.path_id):
                    continue
                if name == "_MainTex":
                    entry["tex"] = self.texture(te.m_Texture, TEX_BIG if m.m_Name in BIG_MATERIALS else TEX_MAX)
                    sc = [round(float(te.m_Scale.x), 3), round(float(te.m_Scale.y), 3)]
                    if sc != [1, 1] and sc[0] and sc[1]:
                        entry["scale"] = sc     # the material's tiling (cave shells: one texture over 200 m without it)
                elif name == "_WnAlbedoSmoothness":
                    entry["top"] = self.texture(te.m_Texture)
                    entry["topScale"] = round(float(te.m_Scale.x), 3) or 1
                elif name == "_DetailAlbedoMap" and detail:
                    entry["detail"] = self.texture(te.m_Texture)
                    entry["detailScale"] = [round(float(te.m_Scale.x), 3) or 1, round(float(te.m_Scale.y), 3) or 1]
            # Cut out by its alpha: foliage / leaves / transparent shaders, a
            # Standard one in cutout mode (_Mode 1). Never by the texture alone:
            # the rock and ground shaders (Lux) keep smoothness in the alpha.
            # A Standard one in Fade / Transparent mode (_Mode 2 / 3) or a legacy
            # transparent shader with a texture full of holes (grills, dirt
            # decals) is cut out too; one without is see-through as a whole -
            # lab windows, glass walls, cabinet doors: "glass" = how opaque,
            # the colour's alpha.
            sh = entry["shader"]
            t = entry["tex"]
            see = (sh.startswith("Standard") and floats.get("_Mode", 0) >= 2) or (
                sh.startswith("Legacy Shaders/Transparent/") and "Cutout" not in sh)
            if t >= 0 and self.clear.get(t, 0) > 0.05 and (
                    see or any(w in sh for w in CUT_SHADERS) or (sh.startswith("Standard") and floats.get("_Mode", 0) == 1)):
                entry["cut"] = True
            elif see:
                entry["glass"] = entry["color"][3]
            if sh.startswith(FX_SHADERS):
                entry["fx"] = True       # glints, particles: not a solid thing (the site skips them)
            if sh.startswith("AFS/"):
                entry["color"] = [1, 1, 1, 1]   # the tree shaders ignore _Color (0,0,0 on pines, fig trees): black trees on the site
        except Exception as e:
            print("   material failed:", label, e)
        return entry

    def place(self, area, model, m):
        """Put an instance in the chunk of its mesh's centre (world bounds of
        the mesh's box under the matrix), a wide one in the wide chunk."""
        bb = self.meshes[self.models[model]["mesh"]]["bb"]
        lo, hi = np.array(bb[:3]), np.array(bb[3:])
        c, e = (lo + hi) / 2, (hi - lo) / 2
        wc = m[:3, :3] @ c + m[:3, 3]
        we = np.abs(m[:3, :3]) @ e
        wide = max(we[0], we[2]) * 2 > CHUNK
        if area == "surface":
            g = self.ground.at(wc[0], wc[2])
            if g is not None and wc[1] + we[1] < g - UNDER:
                area = "caves"      # under the ground: drawn with the caves, not faded with the surface
                self.under += 1
        k = (area, int(np.floor(wc[0] / CHUNK)), int(np.floor(wc[2] / CHUNK)), wide)
        self.chunks[k].append((model, m[:3, :4].astype(np.float32)))
        r = [wc[0] - we[0], wc[2] - we[2], wc[0] + we[0], wc[2] + we[2]]
        o = self.reach.get(k)
        self.reach[k] = r if o is None else [min(o[0], r[0]), min(o[1], r[1]), max(o[2], r[2]), max(o[3], r[3])]

    def read_members(self, path):
        """world/area-members.txt (WorldDump.AreaMembers): every renderer an
        endgame area switches on as it is entered."""
        if not os.path.exists(path):
            print("no", path, "- the endgame's floor quads, concrete cubes and signs left out"
                  " (bridge, in the endgame: call static:ForestOverlay.Game.WorldDump AreaMembers)")
            return
        for line in open(path, encoding="utf-8"):
            f = line.rstrip("\n").split("\t")
            if len(f) == 6 and f[0] == "member":
                self.members.add((f[5], f[2], int(f[3])))
        if not self.members:
            print(path, "has no object paths - write it again (v0.24.183+)")

    def member(self, scene, g, mesh):
        """Whether an endgame area switches this renderer on: its object's path
        in the scene and its mesh (not its place: physics nudges props)."""
        me = self.meshes[mesh]
        return (obj_path(scene, g[3]), me["name"], me["nv"]) in self.members

    def model(self, mesh, mats, kind, layer):
        k = (mesh, tuple(mats), kind, layer)
        if k not in self.model_ix:
            self.model_ix[k] = len(self.models)
            self.models.append({"mesh": mesh, "mats": list(mats), "kind": kind, "layer": layer})
        return self.model_ix[k]

    def scene(self, level):
        s = Scene(level)
        rest = lod_rest(s)
        mats_of = {}
        for o in s.env.objects:
            if o.type.name == "MeshRenderer":
                d = o.read()
                mats_of[d.m_GameObject.path_id] = (bool(d.m_Enabled), list(d.m_Materials))
        placed = collections.Counter()
        moved = MOVED_ROOTS.get(level, ())
        for kind, gid, pptr, enabled in s.parts:
            g = s.gos.get(gid)
            if not g or not pptr or pptr.path_id == 0 or not enabled:
                continue
            m, active = s.world(g[3])
            if not active:
                continue
            if moved and root_name(s, g[3]) in moved:
                continue    # the game moves it: placed-*.txt has it as it stands
            mats = []
            if kind == "render":
                if gid in rest or gid not in mats_of:
                    continue
                mats = [self.material(p) for p in mats_of[gid][1]]
                if not mats_of[gid][0]:
                    kind = "render-off"     # the game switches it on (endgame areas, LOD scripts)
            mesh = self.mesh(pptr)
            if mesh is None:
                continue
            # A switched-off primitive or water / blocker renderer is a volume
            # with a debug material, never seen - unless an endgame area
            # switches it on (AreaMembers): office floors, ceiling tiles,
            # concrete cubes, signs, whiteboard drawings (2026-10-01).
            if kind == "render-off" and (g[1] in VOLUME_LAYERS or self.meshes[mesh]["name"] in PRIMITIVES) \
                    and not (level == 7 and self.member(s, g, mesh)):
                continue
            if kind == "render-off" and all(k < 0 or self.materials[k]["name"] in BAKE_MATERIALS for k in mats):
                continue    # a build leftover (navmesh_patch, cliff_COMBINED, treesExport ...: whole-map meshes, never drawn)
            if mats and all(k >= 0 and self.materials[k]["name"] == "black" for k in mats):
                continue    # Cave1_Blocking ... HC_Blocking: black shells round a whole cave system that hide the void past its openings - on a map they only hide the cave
            if level == 7:
                area = "endgame"
            elif level >= 15 or g[1] == CAVE_LAYER or root_name(s, g[3]) == "Caves":
                area = "caves"
            else:
                area = "surface"
            self.place(area, self.model(mesh, mats, kind, g[1]), m)
            placed[(area, kind)] += 1
        for gid, key, local in s.shapes:
            g = s.gos.get(gid)
            if not g:
                continue
            m, active = s.world(g[3])
            if not active:
                continue
            m = m @ local
            area = "endgame" if level == 7 else "caves" if (level >= 15 or g[1] == CAVE_LAYER or root_name(s, g[3]) == "Caves") else "surface"
            self.place(area, self.model(self.shape(key), [], "collide", g[1]), m)
            placed[(area, "collide " + key[0])] += 1
        print("level%d %s:" % (level, SCENES.get(level, "?")), dict(placed), "-", len(self.meshes), "meshes,",
              len(self.materials), "materials so far")
        sys.stdout.flush()

    def spawned(self, paths):
        """The objects the game spawns from pools (trees, bushes, rocks, cave
        pieces), from the in-game dump (Game/WorldDump, v0.24.173): each
        placeholder's High prefab, its parts' meshes found in the game's
        files by name + vertex count, its materials by name. Also the
        greebles (greebles-*.txt, v0.24.174-175): each placed instance of a
        prefab, the same once across the files (surface / cave dumps)."""
        prefabs, lods, greebles, seen, cur = {}, [], [], set(), None
        for path in paths:
            with open(path, encoding="utf-8") as f:
                for line in f:
                    p = line.rstrip("\n").split("\t")
                    if p[0] == "lod":
                        # a greeble that is a placeholder (greebles-*.txt): once across the files
                        k = ("lod", p[2], tuple(round(float(v), 2) for v in p[5].split()))
                        if path == paths[0] or k not in seen:
                            seen.add(k)
                            lods.append(p)
                    elif p[0] == "greeble":
                        k = (p[1], tuple(round(float(v), 2) for v in p[2].split()))
                        if k not in seen:
                            seen.add(k)
                            greebles.append(p)
                    elif p[0] == "prefab":
                        # a prefab listed by two files: its parts once
                        cur = None if p[1] in prefabs else prefabs.setdefault(p[1], {"scale": [float(v) for v in p[2].split()], "parts": []})
                    elif p[0] == "part" and cur is not None:
                        cur["parts"].append(p)
        for pf in prefabs.values():
            pf["parts"] = [p for p in pf["parts"] if p[3] not in SKIP_MESHES]
        want_mesh = {(p[3], int(p[4])) for pf in prefabs.values() for p in pf["parts"] if p[1] != "box"}
        want_mat = {m for pf in prefabs.values() for p in pf["parts"] for m in p[6].split(";") if m != "-"}
        meshes, mat_objs = {}, {}
        files = [os.path.join(GAME, n) for n in os.listdir(GAME) if n.endswith(".assets")]
        for fp in files:
            env = UnityPy.load(fp)
            for o in env.objects:
                if o.type.name == "Mesh":
                    name = o.peek_name()
                    if any(name == w[0] for w in want_mesh):
                        m = o.read()
                        n = MeshHandler(m)
                        n.process()
                        k = (name, len(n.m_Vertices) // 3 if n.m_Vertices and not isinstance(n.m_Vertices[0], (tuple, list)) else len(n.m_Vertices))
                        if k in want_mesh and k not in meshes:
                            meshes[k] = o
                elif o.type.name == "Material":
                    name = o.peek_name()
                    if name in want_mat and name not in mat_objs:
                        mat_objs[name] = o
        print("spawned: %d placeholders, %d greebles, %d prefabs; meshes found %d of %d, materials %d of %d"
              % (len(lods), len(greebles), len(prefabs), len(meshes), len(want_mesh), len(mat_objs), len(want_mat)))
        missing = sorted(want_mesh - set(meshes))
        if missing:
            print("   not found:", missing[:12])

        def mesh_of(key):
            o = meshes.get(key)
            if o is None:
                return None
            k = ("obj", o.assets_file.name, o.path_id)
            if k not in self.mesh_ix:
                self.mesh_ix[k] = None
                try:
                    m = o.read()
                    h = MeshHandler(m)
                    h.process()
                    v = np.array(h.m_Vertices, dtype=np.float32).reshape(-1, 3)
                    uv = np.array(h.m_UV0, dtype=np.float32).reshape(-1, 2) if h.m_UV0 else None
                    if uv is not None and len(uv) != len(v):
                        uv = None
                    subs, idx = [], []
                    for sub in h.get_triangles():
                        flat = [i for t in sub for i in t]
                        subs.append([len(idx), len(flat)])
                        idx.extend(flat)
                    if len(v) and idx:
                        self.mesh_ix[k] = self.write_mesh(m.m_Name, v, idx, subs, uv)
                except Exception as e:
                    print("   mesh failed:", key, e)
            return self.mesh_ix[k]

        def mat_of(name):
            o = mat_objs.get(name)
            if o is None:
                return -1
            k = ("obj", o.assets_file.name, o.path_id)
            if k not in self.mat_ix:
                entry = self.material_entry(o.read, name)
                self.mat_ix[k] = len(self.materials)
                self.materials.append(entry)
            return self.mat_ix[k]

        placed = collections.Counter()
        for p in lods:
            name = p[2] if p[2] != "-" else p[3] if p[3] != "-" else p[4]
            pf = prefabs.get(name)
            if not pf:
                continue
            x, y, z = (float(v) for v in p[5].split())
            q = [float(v) for v in p[6].split()]
            base = np.eye(4)
            base[:3, :3] = quat_matrix(q)
            base[:3, 3] = (x, y, z)
            cave = p[1] in CAVE_LODS
            area = "caves" if cave else "surface"   # the cave pieces fade with nothing
            # LOD_Cave.SetLOD sets the spawned piece's localScale to the
            # placeholder's lossyScale (0.2 - 50 in the caves), replacing the
            # prefab root's own; the other LOD types keep the prefab's.
            rescale = np.eye(4)
            if cave:
                ls = [float(v) for v in p[7].split()]
                rescale[:3, :3] = np.diag([a / b if b else a for a, b in zip(ls, pf["scale"])])
            for part in pf["parts"]:
                local = np.eye(4)
                local[:3, :4] = np.array([float(v) for v in part[5].split()]).reshape(3, 4)
                local = rescale @ local
                if part[1] == "box":
                    mesh, kind, mats = self.shape(("box",)), "collide", []
                else:
                    mesh = mesh_of((part[3], int(part[4])))
                    kind = part[1]
                    mats = [mat_of(m) for m in part[6].split(";")] if kind == "render" else []
                if mesh is None:
                    continue
                self.place(area, self.model(mesh, mats, kind, int(part[2])), base @ local)
                placed[(area, p[1], kind)] += 1
        # Greebles: the prefab at the placed position and rotation, its own
        # scale (GreeblePlugin.Instantiate); under the terrain = caves (place).
        for p in greebles:
            pf = prefabs.get(p[1])
            if not pf:
                continue
            base = np.eye(4)
            base[:3, :3] = quat_matrix([float(v) for v in p[3].split()])
            base[:3, 3] = [float(v) for v in p[2].split()]
            for part in pf["parts"]:
                local = np.eye(4)
                local[:3, :4] = np.array([float(v) for v in part[5].split()]).reshape(3, 4)
                if part[1] == "box":
                    mesh, kind, mats = self.shape(("box",)), "collide", []
                else:
                    mesh = mesh_of((part[3], int(part[4])))
                    kind = part[1]
                    mats = [mat_of(m) for m in part[6].split(";")] if kind == "render" else []
                if mesh is None:
                    continue
                self.place("surface", self.model(mesh, mats, kind, int(part[2])), base @ local)
                placed[("greeble", kind)] += 1
        for k, v in sorted(placed.items()):
            print("  ", k, v)

    def finish(self):
        import json
        # After the textures are written, before the packing: a resize that read the alpha as coverage
        # (gotcha 88) stops the export instead of shipping black textures.
        problem = world_checks.black_message(self.black, sum(1 for v in self.textures.values() if v >= 0))
        if problem:
            sys.exit(problem)
        print("near-black textures:", len(self.black), "(the game has %d on purpose)" % world_checks.KNOWN_BLACK)
        chunks = []
        for key, items in sorted(self.chunks.items()):
            area, cx, cz, wide = key
            name = "c/%s_%d_%d%s.bin" % (area, cx, cz, "_L" if wide else "")
            with open(os.path.join(self.out, name), "wb") as f:
                for model, mat in items:
                    f.write(np.uint32(model).tobytes())
                    f.write(mat.tobytes())
            tris = sum(self.meshes[self.models[mo]["mesh"]]["ni"] // 3 for mo, _ in items)
            ys = [float(mat[1, 3]) for _, mat in items]
            chunks.append({"file": name, "area": area, "x": cx * CHUNK, "z": cz * CHUNK,
                           "y0": round(min(ys), 1), "y1": round(max(ys), 1), "n": len(items), "tris": tris,
                           "bb": [round(float(v), 1) for v in self.reach[key]]})
        layers = {}
        env = UnityPy.load(os.path.join(GAME, "globalgamemanagers"))
        for o in env.objects:
            if o.type.name == "TagManager":
                layers = {i: n for i, n in enumerate(o.read_typetree()["layers"]) if n}
        meta = {"version": 1, "build": int(time.time()), "chunk": CHUNK, "layers": layers, "materials": self.materials,
                "meshes": self.meshes, "models": self.models, "chunks": chunks}
        meshes = len(self.meshes)
        packed = world_pack.pack(self.out, meta)    # -> version 3: b/<i>.bin, far copies appended to meshes
        with open(os.path.join(self.out, "world.json"), "w", encoding="utf-8", newline="\n") as f:
            json.dump(meta, f, separators=(",", ":"))
        print(self.under, "instances under the terrain filed with the caves")
        print("wrote", len(chunks), "chunks,", len(self.models), "models,", meshes, "meshes,",
              sum(1 for v in self.textures.values() if v >= 0), "textures:", packed, "->", self.out)


def export(out):
    world = os.path.join(os.path.dirname(GAME), "BepInEx", "config", "ForestOverlay", "world")
    # Before the long read: a diagnostic Placed dump left in the folder is a second copy of the geometry (gotcha 78).
    dumps = {}
    for n in world_checks.placed_files(os.listdir(world) if os.path.isdir(world) else []):
        with open(os.path.join(world, n), encoding="utf-8") as f:
            dumps[os.path.join(world, n)] = f.readlines()
    problem = world_checks.duplicates_message(world_checks.duplicate_placements(dumps))
    if problem:
        sys.exit(problem)
    e = Export(out)
    e.read_members(os.path.join(world, "area-members.txt"))
    for level in [2, 7, 11] + list(range(15, 31)):
        e.scene(level)
    spawned = os.path.join(world, "spawned.txt")
    if os.path.exists(spawned):
        e.spawned([spawned] + sorted(os.path.join(world, n) for n in os.listdir(world) if n.startswith(("greebles-", "placed-"))))
    else:
        print("no", spawned, "- trees, rocks and cave pieces left out (bridge: call static:ForestOverlay.Game.WorldDump Write)")
    e.finish()


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "stats":
        stats([int(a) for a in sys.argv[2:]] or sorted(SCENES))
    elif len(sys.argv) > 1 and sys.argv[1] == "export":
        export(sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "site", "world-out"))
    else:
        print(__doc__)
