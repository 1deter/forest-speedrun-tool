using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Dev questions about rendering, for the bridge (raw FPS work, Next up
    // 6). Each camera costs the main thread ~0.25 ms a frame whatever it
    // draws (Unity culls every renderer for it - ~20k on the surface), so
    // a camera with nothing to draw, or drawing into a texture nothing
    // shows, is the cheapest frame time there is. These answer "does it
    // draw anything" and "who reads its texture" before a patch touches it.
    //
    // Results go to the log (the bridge shortens long replies); the call
    // returns a one-line summary. Slow (walks every renderer / material):
    // bridge use only, never per frame.
    // ------------------------------------------------------------------
    public static class RenderProbe
    {
        public static ManualLogSource Log;

        /// Renderers a camera could draw: active and enabled, on a layer
        /// of its culling mask, bounds inside its frustum (farClip too).
        /// `camera` = a camera's name (first match) or "all".
        public static string CameraContents(string camera)
        {
            Camera[] cams = Camera.allCameras;
            int done = 0;
            StringBuilder summary = new StringBuilder();
            Renderer[] all = UnityEngine.Object.FindObjectsOfType(typeof(Renderer)) as Renderer[];
            if (all == null) return "no renderers";
            for (int c = 0; c < cams.Length; c++)
            {
                Camera cam = cams[c];
                if (cam == null) continue;
                if (camera != "all" && cam.name != camera) continue;
                Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
                int mask = cam.cullingMask, onLayer = 0, inside = 0;
                Dictionary<string, int> names = new Dictionary<string, int>();
                for (int i = 0; i < all.Length; i++)
                {
                    Renderer r = all[i];
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if ((mask & (1 << r.gameObject.layer)) == 0) continue;
                    onLayer++;
                    if (!GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                    inside++;
                    string key = r.GetType().Name + " " + Path(r.transform);
                    int n;
                    names.TryGetValue(key, out n);
                    names[key] = n + 1;
                }
                StringBuilder sb = new StringBuilder();
                sb.Append("Render probe: camera '").Append(cam.name).Append("' (#").Append(cam.GetInstanceID())
                  .Append(", depth ").Append(cam.depth).Append(", mask ").Append(mask)
                  .Append(", target ").Append(cam.targetTexture != null ? "'" + cam.targetTexture.name + "' #" + cam.targetTexture.GetInstanceID() : "screen")
                  .Append("): ").Append(all.Length).Append(" renderers active, ").Append(onLayer).Append(" on its layers, ")
                  .Append(inside).Append(" in its frustum");
                int shown = 0;
                foreach (KeyValuePair<string, int> kv in names)
                {
                    if (shown++ == 15) { sb.Append(", ..."); break; }
                    sb.Append(shown == 1 ? ": " : ", ").Append(kv.Key);
                    if (kv.Value > 1) sb.Append(" x").Append(kv.Value);
                }
                if (Log != null) Log.LogInfo(sb.ToString());
                summary.Append(cam.name).Append(' ').Append(inside).Append('/').Append(onLayer).Append("; ");
                done++;
            }
            return done == 0 ? "no camera named '" + camera + "'" : summary.ToString();
        }

        /// Materials (loaded, all) whose texture property is set to a
        /// texture named `textureName` or with instance id `textureName`
        /// ("#123"), checking each property in `props` (comma-separated),
        /// and the renderers (loaded, all) that use those materials.
        public static string TextureUsers(string textureName, string props)
        {
            string[] names = (props ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            int wantId = 0;
            bool byId = textureName.StartsWith("#") && int.TryParse(textureName.Substring(1), out wantId);
            UnityEngine.Object[] mats = Resources.FindObjectsOfTypeAll(typeof(Material));
            List<Material> hits = new List<Material>();
            StringBuilder sb = new StringBuilder();
            sb.Append("Render probe: texture ").Append(textureName).Append(" in ").Append(mats.Length).Append(" materials");
            int declaring = 0;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i] as Material;
                if (m == null) continue;
                for (int p = 0; p < names.Length; p++)
                {
                    string prop = names[p].Trim();
                    if (!m.HasProperty(prop)) continue;
                    declaring++;
                    Texture t = m.GetTexture(prop);
                    if (t == null) continue;
                    bool match = byId ? t.GetInstanceID() == wantId : t.name == textureName;
                    if (!match) continue;
                    hits.Add(m);
                    sb.Append(hits.Count == 1 ? ": " : ", ").Append('\'').Append(m.name).Append("'.").Append(prop)
                      .Append(" (shader ").Append(m.shader != null ? m.shader.name : "?").Append(')');
                }
            }
            sb.Append(" | ").Append(declaring).Append(" material properties checked, ").Append(hits.Count).Append(" use it");
            if (hits.Count > 0)
            {
                UnityEngine.Object[] rs = Resources.FindObjectsOfTypeAll(typeof(Renderer));
                int users = 0;
                for (int i = 0; i < rs.Length && users < 20; i++)
                {
                    Renderer r = rs[i] as Renderer;
                    if (r == null) continue;
                    Material[] ms = r.sharedMaterials;
                    for (int k = 0; k < ms.Length; k++)
                        if (ms[k] != null && hits.Contains(ms[k]))
                        {
                            users++;
                            sb.Append(users == 1 ? " | renderers: " : ", ").Append(Path(r.transform))
                              .Append(r.gameObject.activeInHierarchy && r.enabled ? "" : " (off)")
                              .Append(" at ").Append(r.transform.position.ToString("0"));
                            break;
                        }
                }
            }
            if (Log != null) Log.LogInfo(sb.ToString());
            return hits.Count + " material(s) use it (log)";
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            for (int i = 0; i < 3 && t.parent != null; i++)
            {
                t = t.parent;
                p = t.name + "/" + p;
            }
            return p;
        }
    }
}
