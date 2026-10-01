using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // What the scene files do not hold, for the website's 3D world
    // (scripts/world-extract.py reads the rest offline): the objects the
    // game spawns from pools near the player - trees, rocks, the caves'
    // walls and spikes. Their placeholders are in the scene (LOD_Base and
    // subclasses: High / Mid / Low prefabs), and LOD_Base.SetLOD spawns the
    // prefab at the placeholder's _position and rotation. Dev only, driven
    // from the bridge: `call static:ForestOverlay.Game.WorldDump Write`
    // (a few seconds, nothing in the game touched).
    //
    // config/ForestOverlay/world/spawned.txt, tab separated:
    //   lod    <type> <high> <mid> <low> <x y z> <qx qy qz qw> <lossy sx sy sz> <active 0/1>
    //   prefab <name> <root scale x y z>   (once per prefab, then its parts)
    //   part   render|collide <layer> <mesh name> <vertex count> <12 floats: 3x4
    //          matrix from the root's position and rotation - the root's own
    //          scale included> <material;material...>
    //   part   box <layer> - 0 <12 floats: the box as a unit cube's matrix> -
    // A prefab is named by its object name; meshes by name + vertex count
    // (enough to find them in the game's files).
    //
    // Greebles (`call static:ForestOverlay.Game.WorldDump Greebles <name>`,
    // 2026-10-01): the small things a GreebleZone scatters (sticks, rocks,
    // Cave 6's body piles, stalactites) exist only while the player is near,
    // so they are worked out instead: each loaded zone's instances placed
    // the way GreebleZone.SpawnIndex + GreebleUtility.Spawn place them - the
    // zone's seed, the game's own Procedural* draws, the real raycast -
    // without spawning anything (Random's state is put back). One visit's
    // set (author, 2026-10-01): every instance as if fresh, taken ones
    // included. Zones on pooled objects (a tree's sticks and rocks) are
    // worked out once per LOD placeholder instead, with the seed the zone
    // takes from where it stands on a first visit - in game it follows the
    // pool object (game-notes *Greebles*). Only loaded scenes have zones: run it
    // once on the surface (MainSceneGreebles) and once in a cave (the cave
    // prop scenes). world/greebles-<name>.txt:
    //   greeble <prefab> <x y z> <qx qy qz qw>
    //   lod ... as above, for a greeble that is itself an LOD placeholder
    //       (Cave_SpikesSmall, Fern2_Loader: the model spawns from its High)
    //   prefab / part lines as above
    // The player's own GreebleLayer (sticks and small rocks every 10-12 m
    // around the player) is not in it: its ProceduralSeed is empty in this
    // build, so they are random on every spawn - no place to put them.
    // ------------------------------------------------------------------
    public static class WorldDump
    {
        public static ManualLogSource Log;

        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static string Write()
        {
            Type lodType = GameBridge.FindGameType("LOD_Base");
            if (lodType == null) return "no LOD_Base type";
            FieldInfo high = lodType.GetField("High", Any), mid = lodType.GetField("Mid", Any),
                      low = lodType.GetField("Low", Any), pos = lodType.GetField("_position", Any);
            string dir = Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "ForestOverlay"), "world");
            Directory.CreateDirectory(dir);

            StringBuilder sb = new StringBuilder(1 << 20);
            Dictionary<Transform, bool> prefabs = new Dictionary<Transform, bool>();
            int lods = 0;
            UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(lodType);
            for (int i = 0; i < all.Length; i++)
            {
                Component c = all[i] as Component;
                if (c == null || !c.gameObject.scene.IsValid()) continue;   // a prefab asset, not a placeholder
                if (c.transform.root.name == "Pooling") continue;            // itself spawned (greeble zones): random per visit
                Transform h = high.GetValue(c) as Transform, m = mid.GetValue(c) as Transform, l = low.GetValue(c) as Transform;
                if (h == null && m == null && l == null) continue;
                Vector3 p = c.transform.position;
                if (pos != null)
                {
                    object v = pos.GetValue(c);
                    if (v is Vector3 && (Vector3)v != Vector3.zero) p = (Vector3)v;
                }
                Quaternion q = c.transform.rotation;
                Vector3 s = c.transform.lossyScale;
                sb.Append("lod\t").Append(c.GetType().Name).Append('\t').Append(Name(h)).Append('\t').Append(Name(m)).Append('\t').Append(Name(l))
                  .Append('\t').Append(F(p.x)).Append(' ').Append(F(p.y)).Append(' ').Append(F(p.z))
                  .Append('\t').Append(F(q.x)).Append(' ').Append(F(q.y)).Append(' ').Append(F(q.z)).Append(' ').Append(F(q.w))
                  .Append('\t').Append(F(s.x)).Append(' ').Append(F(s.y)).Append(' ').Append(F(s.z))
                  .Append('\t').Append(c.gameObject.activeInHierarchy ? '1' : '0').Append('\n');
                lods++;
                foreach (Transform t in new[] { h, m, l })
                    if (t != null && !prefabs.ContainsKey(t)) prefabs[t] = true;
            }

            int parts = 0;
            foreach (Transform root in prefabs.Keys) parts += Prefab(sb, root);
            File.WriteAllText(Path.Combine(dir, "spawned.txt"), sb.ToString());
            string result = lods + " placeholder(s), " + prefabs.Count + " prefab(s), " + parts + " part(s) -> " + dir;
            if (Log != null) Log.LogInfo("World dump: " + result);
            return result;
        }

        /// A prefab's parts, relative to its root's position and rotation
        /// (the root's own scale stays in them).
        private static int Prefab(StringBuilder sb, Transform root)
        {
            int parts = 0;
            Vector3 rs = root.localScale;
            sb.Append("prefab\t").Append(Clean(root.name)).Append('\t')
              .Append(F(rs.x)).Append(' ').Append(F(rs.y)).Append(' ').Append(F(rs.z)).Append('\n');
            Matrix4x4 toRoot = Matrix4x4.TRS(root.position, root.rotation, Vector3.one).inverse;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer r = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || r == null || !r.enabled) continue;
                Part(sb, "render", mf.gameObject.layer, mf.sharedMesh, toRoot * mf.transform.localToWorldMatrix, r.sharedMaterials);
                parts++;
            }
            foreach (MeshCollider mc in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if (mc.sharedMesh == null || mc.isTrigger || !mc.enabled) continue;
                Part(sb, "collide", mc.gameObject.layer, mc.sharedMesh, toRoot * mc.transform.localToWorldMatrix, null);
                parts++;
            }
            foreach (BoxCollider bc in root.GetComponentsInChildren<BoxCollider>(true))
            {
                if (bc.isTrigger || !bc.enabled) continue;
                Matrix4x4 box = toRoot * bc.transform.localToWorldMatrix * Matrix4x4.TRS(bc.center, Quaternion.identity, bc.size);
                sb.Append("part\tbox\t").Append(bc.gameObject.layer).Append("\t-\t0\t").Append(Matrix(box)).Append("\t-\n");
                parts++;
            }
            return parts;
        }

        // --- greebles ----------------------------------------------------

        private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        public static string Greebles(string name)
        {
            Type zoneType = GameBridge.FindGameType("GreebleZone");
            Type util = GameBridge.FindGameType("GreebleUtility");
            Type defType = GameBridge.FindGameType("GreebleDefinition");
            if (zoneType == null || util == null || defType == null) return "no GreebleZone / GreebleUtility / GreebleDefinition type";
            G g = new G();
            g.Seed = zoneType.GetMethod("GetRandomSeed", Any);
            g.Min = zoneType.GetProperty("MinInstancesModified", Any);
            g.Max = zoneType.GetProperty("MaxInstancesModified", Any);
            g.Shape = zoneType.GetField("Shape", Any); g.Direction = zoneType.GetField("Direction", Any);
            g.Radius = zoneType.GetField("Radius", Any); g.Size = zoneType.GetField("Size", Any);
            g.Defs = zoneType.GetField("GreebleDefinitions", Any);
            g.Type = util.GetMethod("ProceduralGreebleType", Static);
            g.IntValue = util.GetMethod("ProceduralValue", Static, null, new[] { typeof(int), typeof(int) }, null);
            g.FloatValue = util.GetMethod("ProceduralValue", Static, null, new[] { typeof(float), typeof(float) }, null);
            g.Angle = util.GetMethod("ProceduralAngle", Static, null, Type.EmptyTypes, null);
            g.DirFast = util.GetMethod("ProceduralDirectionFast", Static, null, Type.EmptyTypes, null);
            g.Dir = util.GetMethod("ProceduralDirection", Static, null, Type.EmptyTypes, null);
            g.Surface = defType.GetField("SurfaceMask", Any); g.Kill = defType.GetField("KillMask", Any);
            g.Textures = defType.GetField("TerrainTextureMask", Any); g.Normal = defType.GetField("MatchSurfaceNormal", Any);
            g.RandomRot = defType.GetField("RandomizeRotation", Any);
            g.RotX = defType.GetField("AllowRotationX", Any); g.RotY = defType.GetField("AllowRotationY", Any);
            g.RotZ = defType.GetField("AllowRotationZ", Any); g.Prefab = defType.GetField("Prefab", Any);
            foreach (object m in new object[] { g.Seed, g.Min, g.Max, g.Shape, g.Direction, g.Radius, g.Size, g.Defs, g.Type, g.IntValue,
                                                g.FloatValue, g.Angle, g.DirFast, g.Dir, g.Surface, g.Kill, g.Textures, g.Normal,
                                                g.RandomRot, g.RotX, g.RotY, g.RotZ, g.Prefab })
                if (m == null) return "a GreebleZone / GreebleUtility member is missing (game update?)";

            string dir = Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "ForestOverlay"), "world");
            Directory.CreateDirectory(dir);
            StringBuilder sb = new StringBuilder(1 << 20);
            Dictionary<Transform, bool> prefabs = new Dictionary<Transform, bool>();
            Dictionary<string, int> byRoot = new Dictionary<string, int>();
            int zones = 0, skipped = 0;
            g.Out = sb;
            g.Prefabs = prefabs;
            g.Lod = GameBridge.FindGameType("LOD_Base");
            if (g.Lod != null)
            {
                g.High = g.Lod.GetField("High", Any);
                g.Mid = g.Lod.GetField("Mid", Any);
                g.Low = g.Lod.GetField("Low", Any);
            }
            FieldInfo randomSeed = zoneType.GetField("RandomSeed", Any);
            UnityEngine.Random.State saved = UnityEngine.Random.state;
            GameObject temp = new GameObject("ForestOverlay_GreebleProbe");
            try
            {
                UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(zoneType);
                for (int i = 0; i < all.Length; i++)
                {
                    Component z = all[i] as Component;
                    if (z == null || !z.gameObject.scene.IsValid()) continue;
                    if (!z.gameObject.activeInHierarchy || z.transform.root.name == "Pooling") { skipped++; continue; }
                    zones++;
                    Count(byRoot, z.transform.root.name, Zone(g, z, z.transform, (int)g.Seed.Invoke(z, null)));
                }

                // Zones inside the prefabs the LOD placeholders spawn (a tree's
                // sticks and rocks, a cave piece's stalactites): one per
                // placeholder, seeded from where it stands
                // (GetRandomSeed: (int)x + (int)y + (int)z + RandomSeed).
                Type lodType = GameBridge.FindGameType("LOD_Base");
                FieldInfo high = lodType != null ? lodType.GetField("High", Any) : null;
                FieldInfo lodPos = lodType != null ? lodType.GetField("_position", Any) : null;
                Dictionary<Transform, Component[]> inPrefab = new Dictionary<Transform, Component[]>();
                UnityEngine.Object[] lods = high != null ? Resources.FindObjectsOfTypeAll(lodType) : new UnityEngine.Object[0];
                for (int i = 0; i < lods.Length; i++)
                {
                    Component c = lods[i] as Component;
                    if (c == null || !c.gameObject.scene.IsValid() || c.transform.root.name == "Pooling") continue;
                    Transform h = high.GetValue(c) as Transform;
                    if (h == null) continue;
                    Component[] zs;
                    if (!inPrefab.TryGetValue(h, out zs))
                    {
                        List<Component> l = new List<Component>();
                        foreach (Component pz in h.GetComponentsInChildren(zoneType, true))
                        {
                            bool on = true;
                            for (Transform t = pz.transform; t != null && t != h.parent; t = t.parent)
                                if (!t.gameObject.activeSelf) { on = false; break; }
                            if (on) l.Add(pz);
                        }
                        inPrefab[h] = zs = l.ToArray();
                    }
                    if (zs.Length == 0) continue;
                    Vector3 p = c.transform.position;
                    if (lodPos != null)
                    {
                        object v = lodPos.GetValue(c);
                        if (v is Vector3 && (Vector3)v != Vector3.zero) p = (Vector3)v;
                    }
                    // As spawned: at the placeholder, the prefab's own scale -
                    // a cave piece takes the placeholder's (LOD_Cave.SetLOD, gotcha 69).
                    Vector3 scale = c.GetType().Name.StartsWith("LOD_Cave") ? c.transform.lossyScale : h.localScale;
                    Matrix4x4 place = Matrix4x4.TRS(p, c.transform.rotation, scale) *
                                      Matrix4x4.TRS(h.position, h.rotation, h.localScale).inverse;
                    foreach (Component pz in zs)
                    {
                        Matrix4x4 m = place * pz.transform.localToWorldMatrix;
                        Vector3 zp = m.GetColumn(3);
                        temp.transform.position = zp;
                        temp.transform.rotation = Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
                        temp.transform.localScale = new Vector3(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude, m.GetColumn(2).magnitude);
                        int seed = (int)zp.x + (int)zp.y + (int)zp.z + (randomSeed != null ? (int)randomSeed.GetValue(pz) : 0);
                        zones++;
                        Count(byRoot, c.GetType().Name, Zone(g, pz, temp.transform, seed));
                    }
                }
            }
            finally
            {
                UnityEngine.Random.state = saved;
                UnityEngine.Object.Destroy(temp);
            }
            int placed = g.Placed, missed = g.Missed;

            int parts = 0;
            foreach (Transform root in prefabs.Keys) parts += Prefab(sb, root);
            string file = Path.Combine(dir, "greebles-" + Clean(name) + ".txt");
            File.WriteAllText(file, sb.ToString());
            StringBuilder roots = new StringBuilder();
            foreach (KeyValuePair<string, int> kv in byRoot)
                if (kv.Value > 0) roots.Append(roots.Length > 0 ? ", " : "").Append(kv.Key).Append(' ').Append(kv.Value);
            string result = zones + " zone(s), " + placed + " greeble(s) placed, " + missed + " missed (no surface / killed / slope), " +
                            skipped + " zone(s) skipped (inactive / pooled), " + prefabs.Count + " prefab(s), " + parts + " part(s) -> " + file +
                            " (" + roots + ")";
            if (Log != null) Log.LogInfo("World dump, greebles: " + result);
            return result;
        }

        private static void Count(Dictionary<string, int> by, string key, int n)
        {
            int had;
            by.TryGetValue(key, out had);
            by[key] = had + n;
        }

        /// One zone's instances, placed at `at` with `seed` (the zone's own
        /// transform and GetRandomSeed for a scene zone); lines into g.Out.
        private static int Zone(G g, Component z, Transform at, int seed)
        {
            int n = 0;
            UnityEngine.Random.InitState(seed);
            int count = (int)g.IntValue.Invoke(null, new object[] { (int)g.Min.GetValue(z, null), (int)g.Max.GetValue(z, null) + 1 });
            for (int k = 0; k < count; k++)
            {
                Transform prefab;
                Vector3 pos;
                Quaternion rot;
                if (!Place(g, z, at, seed, k, out prefab, out pos, out rot)) { g.Missed++; continue; }
                g.Placed++;
                n++;
                // A loader (an LOD placeholder): its High is what shows, spawned at
                // the placeholder like any other (the lod line, as Write does).
                Component lod = g.High != null ? prefab.GetComponent(g.Lod) : null;
                Transform h = lod != null ? g.High.GetValue(lod) as Transform : null;
                if (h != null)
                {
                    Transform mt = g.Mid.GetValue(lod) as Transform, lt = g.Low.GetValue(lod) as Transform;
                    Vector3 s = prefab.localScale;
                    g.Out.Append("lod\t").Append(lod.GetType().Name).Append('\t').Append(Name(h)).Append('\t').Append(Name(mt)).Append('\t').Append(Name(lt))
                      .Append('\t').Append(F(pos.x)).Append(' ').Append(F(pos.y)).Append(' ').Append(F(pos.z))
                      .Append('\t').Append(F(rot.x)).Append(' ').Append(F(rot.y)).Append(' ').Append(F(rot.z)).Append(' ').Append(F(rot.w))
                      .Append('\t').Append(F(s.x)).Append(' ').Append(F(s.y)).Append(' ').Append(F(s.z)).Append("\t1\n");
                    g.Prefabs[h] = true;
                    continue;
                }
                g.Out.Append("greeble\t").Append(Clean(prefab.name))
                  .Append('\t').Append(F(pos.x)).Append(' ').Append(F(pos.y)).Append(' ').Append(F(pos.z))
                  .Append('\t').Append(F(rot.x)).Append(' ').Append(F(rot.y)).Append(' ').Append(F(rot.z)).Append(' ').Append(F(rot.w))
                  .Append('\n');
                g.Prefabs[prefab] = true;
            }
            return n;
        }

        private sealed class G
        {
            public StringBuilder Out;
            public Type Lod;
            public FieldInfo High, Mid, Low;
            public Dictionary<Transform, bool> Prefabs;
            public int Placed, Missed;
            public MethodInfo Seed, Type, IntValue, FloatValue, Angle, DirFast, Dir;
            public PropertyInfo Min, Max;
            public FieldInfo Shape, Direction, Radius, Size, Defs, Surface, Kill, Textures, Normal, RandomRot, RotX, RotY, RotZ, Prefab;
        }

        private static float PV(G g, float a, float b) { return (float)g.FloatValue.Invoke(null, new object[] { a, b }); }
        private static int PV(G g, int a, int b) { return (int)g.IntValue.Invoke(null, new object[] { a, b }); }

        /// GreebleZone.SpawnIndex + GreebleUtility.Spawn (IL, 2026-10-01), as a
        /// fresh instance (not destroyed, fully grown), without the spawn.
        private static bool Place(G g, Component z, Transform at, int seed, int index, out Transform prefabT, out Vector3 pos, out Quaternion rot)
        {
            prefabT = null; pos = Vector3.zero; rot = Quaternion.identity;
            UnityEngine.Random.InitState(seed + index);
            object def = g.Type.Invoke(null, new object[] { g.Defs.GetValue(z), false, 1000000f });
            if (def == null) return false;
            Vector3 origin = Vector3.zero, dir = Vector3.down;
            float zr = (float)g.Radius.GetValue(z), radius = zr;
            int shape = Convert.ToInt32(g.Shape.GetValue(z)), direction = Convert.ToInt32(g.Direction.GetValue(z));
            if (shape == 0)
            {
                Vector3 v;
                do v = new Vector3(PV(g, -zr, zr), PV(g, -zr, zr), PV(g, -zr, zr));
                while (v.magnitude > zr);
                if (direction == 0) { origin += new Vector3(v.x, 0f, v.y); dir = Vector3.down; }
                else if (direction == 1) { origin += new Vector3(v.x, 0f, v.y); dir = Vector3.up; }
                else if (direction == 2)
                {
                    dir = (Vector3)g.DirFast.Invoke(null, null);
                    dir.y = 0f;
                    dir.Normalize();
                    origin += v - Vector3.Project(v, dir);
                }
                else if (direction == 3) dir = (Vector3)g.Dir.Invoke(null, null);
            }
            else if (shape == 1)
            {
                Vector3 size = (Vector3)g.Size.GetValue(z), half = size * 0.5f;
                Vector3 p = new Vector3(PV(g, -half.x, half.x), PV(g, -half.y, half.y), PV(g, -half.z, half.z));
                int face = 0;
                if (direction == 0) face = 4;
                else if (direction == 1) face = 5;
                else if (direction == 2) face = PV(g, 0, 4);
                else if (direction == 3) face = PV(g, 0, 6);
                switch (face)
                {
                    case 0: dir = Vector3.left; p.x = half.x; radius = size.x; break;
                    case 1: dir = Vector3.right; p.x = -half.x; radius = size.x; break;
                    case 2: dir = Vector3.back; p.z = half.z; radius = size.z; break;
                    case 3: dir = Vector3.forward; p.z = -half.z; radius = size.z; break;
                    case 4: dir = Vector3.down; p.y = half.y; radius = size.y; break;
                    case 5: dir = Vector3.up; p.y = -half.y; radius = size.y; break;
                }
                origin += p;
            }
            Quaternion r = Quaternion.identity;
            if ((bool)g.RandomRot.GetValue(def))
            {
                float x = (bool)g.RotX.GetValue(def) ? (float)g.Angle.Invoke(null, null) : 0f;
                float y = (bool)g.RotY.GetValue(def) ? (float)g.Angle.Invoke(null, null) : 0f;
                float zz = (bool)g.RotZ.GetValue(def) ? (float)g.Angle.Invoke(null, null) : 0f;
                r = Quaternion.Euler(x, y, zz);
            }
            Ray ray = new Ray(at.TransformPoint(origin), at.TransformDirection(dir));

            // GreebleUtility.Spawn(def, ray, radius, r, 0.5)
            if (!(radius > 0f)) return false;
            int kill = ((LayerMask)g.Kill.GetValue(def)).value;
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, radius, ((LayerMask)g.Surface.GetValue(def)).value | kill)) return false;
            if ((kill & (1 << (hit.collider.gameObject.layer & 31))) > 0) return false;
            if (Vector3.Dot(ray.direction, hit.normal) > -0.5f) return false;
            int[] textures = g.Textures.GetValue(def) as int[];
            if (textures != null && textures.Length > 0)
            {
                Terrain t = hit.collider.GetComponent<Terrain>();
                if (t)
                {
                    Vector3 tp = t.GetPosition();
                    TerrainData td = t.terrainData;
                    int res = td.alphamapResolution;
                    int ax = (int)((hit.point.x - tp.x) / td.size.x * res), az = (int)((hit.point.z - tp.z) / td.size.z * res);
                    bool ok = false;
                    if (ax >= 0 && az >= 0 && ax < res && az < res)
                    {
                        float[,,] a = td.GetAlphamaps(ax, az, 1, 1);
                        float best = 0f;
                        int layer = -1;
                        for (int l = td.alphamapLayers - 1; l >= 0; l--)
                            if (a[0, 0, l] > best) { best = a[0, 0, l]; layer = l; }
                        if (layer >= 0)
                            for (int m = 0; m < textures.Length; m++)
                                if (textures[m] == layer) { ok = true; break; }
                    }
                    if (!ok) return false;
                }
            }
            pos = hit.point;
            if ((bool)g.Normal.GetValue(def))
            {
                Vector3 a = Vector3.Cross(Vector3.forward, hit.normal);
                Vector3 b = Vector3.Cross(a, hit.normal);
                Quaternion q = Quaternion.LookRotation(b, hit.normal);
                r = (bool)g.RandomRot.GetValue(def) ? q * r : q;
            }
            GameObject prefab = g.Prefab.GetValue(def) as GameObject;
            if (prefab == null) return false;
            pos += r * prefab.transform.position;
            rot = r * prefab.transform.rotation;
            prefabT = prefab.transform;
            return true;
        }

        private static void Part(StringBuilder sb, string kind, int layer, Mesh mesh, Matrix4x4 m, Material[] mats)
        {
            sb.Append("part\t").Append(kind).Append('\t').Append(layer).Append('\t').Append(Clean(mesh.name)).Append('\t')
              .Append(mesh.vertexCount).Append('\t').Append(Matrix(m)).Append('\t');
            if (mats == null || mats.Length == 0) sb.Append('-');
            else for (int i = 0; i < mats.Length; i++)
                {
                    if (i > 0) sb.Append(';');
                    sb.Append(mats[i] != null ? Clean(mats[i].name) : "-");
                }
            sb.Append('\n');
        }

        private static string Matrix(Matrix4x4 m)
        {
            StringBuilder b = new StringBuilder(160);
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 4; c++)
                {
                    if (r > 0 || c > 0) b.Append(' ');
                    b.Append(F(m[r, c]));
                }
            return b.ToString();
        }

        private static string Name(Transform t) { return t != null ? Clean(t.name) : "-"; }
        private static string Clean(string s) { return string.IsNullOrEmpty(s) ? "-" : s.Replace('\t', ' ').Replace('\n', ' ').Replace(';', ','); }
        private static string F(float v) { return v.ToString("0.#####", CultureInfo.InvariantCulture); }
    }
}
