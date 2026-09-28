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
            foreach (Transform root in prefabs.Keys)
            {
                Vector3 rs = root.localScale;
                sb.Append("prefab\t").Append(Clean(root.name)).Append('\t')
                  .Append(F(rs.x)).Append(' ').Append(F(rs.y)).Append(' ').Append(F(rs.z)).Append('\n');
                // Parts relative to the root's position and rotation: its own scale stays in them.
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
            }
            File.WriteAllText(Path.Combine(dir, "spawned.txt"), sb.ToString());
            string result = lods + " placeholder(s), " + prefabs.Count + " prefab(s), " + parts + " part(s) -> " + dir;
            if (Log != null) Log.LogInfo("World dump: " + result);
            return result;
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
