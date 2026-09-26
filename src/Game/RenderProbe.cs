using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Dev questions about rendering, for the bridge (raw FPS work, Next up
    // 6). Each camera render costs the main thread ~0.2 ms whatever it
    // draws (Unity's own overhead - TimeRender measures the same on the
    // title screen as in the world), so a camera with nothing to draw, or
    // drawing into a texture nothing shows, is the cheapest frame time
    // there is. These answer "does it
    // draw anything" and "who reads its texture" before a patch touches it.
    //
    // Results go to the log (the bridge shortens long replies); the call
    // returns a one-line summary. Slow (walks every renderer / material):
    // bridge use only, never per frame.
    // ------------------------------------------------------------------
    public static class RenderProbe
    {
        public static ManualLogSource Log;

        /// Renderers (active, enabled) on one layer, by type and path, and
        /// how many are inside the main camera's frustum.
        public static string LayerContents(int layer)
        {
            Renderer[] all = UnityEngine.Object.FindObjectsOfType(typeof(Renderer)) as Renderer[];
            if (all == null) return "no renderers";
            Camera main = Camera.main;
            Plane[] planes = main != null ? GeometryUtility.CalculateFrustumPlanes(main) : null;
            int on = 0, inView = 0;
            Dictionary<string, int> names = new Dictionary<string, int>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.gameObject.layer != layer) continue;
                on++;
                bool seen = planes != null && GeometryUtility.TestPlanesAABB(planes, r.bounds);
                if (seen) inView++;
                string key = r.GetType().Name + " " + Path(r.transform) + (seen ? " (in view)" : "");
                int n;
                names.TryGetValue(key, out n);
                names[key] = n + 1;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("Render probe: layer ").Append(layer).Append(" '").Append(LayerMask.LayerToName(layer)).Append("': ")
              .Append(on).Append(" renderers active, ").Append(inView).Append(" in the main camera's frustum");
            int shown = 0;
            foreach (KeyValuePair<string, int> kv in names)
            {
                if (shown++ == 25) { sb.Append(", ..."); break; }
                sb.Append(shown == 1 ? ": " : ", ").Append(kv.Key);
                if (kv.Value > 1) sb.Append(" x").Append(kv.Value);
            }
            if (Log != null) Log.LogInfo(sb.ToString());
            return on + " on layer " + layer + ", " + inView + " in view (log)";
        }

        // --- does a camera enabled mid-frame render in that frame? --------
        private static Camera _lateTarget, _lateTrigger;
        private static int _lateFrames, _lateLeft;
        private static bool _lateDisable;
        private static Camera.CameraCallback _latePre;

        /// For `frames` frames: `target` is disabled, enabled in the
        /// Camera.onPreCull of `trigger` (a camera that renders before it),
        /// and disabled again at the end of the frame. The Frame line's
        /// x/f for `target` then says whether Unity rendered it the same
        /// frame (x1/f) or not at all (absent).
        public static string TestLateEnable(string target, string trigger, int frames)
        {
            return StartLateTest(target, trigger, frames, false);
        }

        /// The reverse: `target` stays enabled, is disabled in the
        /// trigger's onPreCull and enabled again at the end of the frame.
        /// Absent from the Frame line = skipped - but with the last screen
        /// camera (ActionIconCamera) this FROZE THE PICTURE (gotcha 51):
        /// run it for a few frames only, and ask the author to look.
        public static string TestLateDisable(string target, string trigger, int frames)
        {
            return StartLateTest(target, trigger, frames, true);
        }

        private static string StartLateTest(string target, string trigger, int frames, bool disable)
        {
            if (_lateTarget != null) return "a test is running";
            Camera t = null, g = null;
            Camera[] cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                if (cams[i].name == target) t = cams[i];
                if (cams[i].name == trigger) g = cams[i];
            }
            if (t == null || g == null) return "camera not found";
            _lateTarget = t;
            _lateTrigger = g;
            _lateFrames = 0;
            _lateLeft = frames;
            _lateDisable = disable;
            t.enabled = disable;
            _latePre = LatePreCull;
            Camera.onPreCull += _latePre;
            FrameTimer.EndOfFrameHook += LateEndOfFrame;
            return "testing " + frames + " frames";
        }

        private static void LatePreCull(Camera cam)
        {
            if (cam == _lateTrigger && _lateTarget != null) _lateTarget.enabled = !_lateDisable;
        }

        private static void LateEndOfFrame()
        {
            if (_lateTarget == null) return;
            _lateFrames++;
            if (--_lateLeft > 0)
            {
                _lateTarget.enabled = _lateDisable;
                return;
            }
            _lateTarget.enabled = true;
            Camera.onPreCull -= _latePre;
            FrameTimer.EndOfFrameHook -= LateEndOfFrame;
            if (Log != null) Log.LogInfo("Render probe: late-enable test of '" + _lateTarget.name + "' done after " + _lateFrames + " frames.");
            _lateTarget = null;
        }

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

        // --- what the fixed cost of a camera is made of --------------------
        private static Camera _probeCam;
        private static RenderTexture _probeTexture;
        private static readonly List<Behaviour> _offLights = new List<Behaviour>();
        private static readonly List<Renderer> _offRenderers = new List<Renderer>();

        /// Renders a bare camera `n` times back to back and returns the
        /// average ms: at the main camera's place and frustum, forward, no
        /// HDR / MSAA / occlusion culling, into a 64x64 texture, with
        /// `mask` as its culling mask (0 = nothing to draw). The fixed cost
        /// of one camera in this scene, without frame noise; run it before
        /// and after ToggleLights / ToggleRenderers to see what it scales with.
        public static string TimeRender(int mask, int n)
        {
            if (n < 1) n = 1;
            if (_probeCam == null)
            {
                GameObject go = new GameObject("ForestOverlay RenderProbe Camera");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _probeCam = go.AddComponent<Camera>();
                _probeCam.enabled = false;
                _probeTexture = new RenderTexture(64, 64, 16);
                _probeCam.targetTexture = _probeTexture;
            }
            Camera main = Camera.main;
            if (main != null)
            {
                _probeCam.transform.position = main.transform.position;
                _probeCam.transform.rotation = main.transform.rotation;
                _probeCam.fieldOfView = main.fieldOfView;
                _probeCam.nearClipPlane = main.nearClipPlane;
                _probeCam.farClipPlane = main.farClipPlane;
            }
            _probeCam.renderingPath = RenderingPath.Forward;
            _probeCam.allowHDR = false;
            _probeCam.allowMSAA = false;
            _probeCam.useOcclusionCulling = false;
            _probeCam.clearFlags = CameraClearFlags.Depth;
            _probeCam.cullingMask = mask;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++) _probeCam.Render();
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / n;
            string line = "Render probe: bare camera, mask " + mask + ", " + n + " renders: " + ms.ToString("0.000") + " ms each";
            if (Log != null) Log.LogInfo(line);
            return ms.ToString("0.000") + " ms";
        }

        /// Times an existing camera's Render() `n` times (the frame after
        /// shows it drawn again - a test only).
        public static string TimeCamera(string name, int n)
        {
            if (n < 1) n = 1;
            Camera[] cams = Camera.allCameras;
            for (int c = 0; c < cams.Length; c++)
            {
                if (cams[c].name != name) continue;
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < n; i++) cams[c].Render();
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds / n;
                if (Log != null) Log.LogInfo("Render probe: camera '" + name + "' " + n + " renders: " + ms.ToString("0.000") + " ms each");
                return ms.ToString("0.000") + " ms";
            }
            return "no camera named '" + name + "'";
        }

        /// Switches every enabled, active non-directional Light off (and
        /// the same ones back on at the next call). A test only.
        public static string ToggleLights()
        {
            if (_offLights.Count > 0)
            {
                int back = 0;
                for (int i = 0; i < _offLights.Count; i++)
                    if (_offLights[i] != null) { _offLights[i].enabled = true; back++; }
                _offLights.Clear();
                return back + " light(s) back on";
            }
            Light[] all = UnityEngine.Object.FindObjectsOfType(typeof(Light)) as Light[];
            if (all == null) return "no lights";
            for (int i = 0; i < all.Length; i++)
            {
                Light l = all[i];
                if (l == null || !l.enabled || l.type == LightType.Directional) continue;
                l.enabled = false;
                _offLights.Add(l);
            }
            return _offLights.Count + " light(s) off";
        }

        /// Switches enabled, active renderers off (and the same ones back on
        /// at the next call): those whose path starts with `root`, or every
        /// one but the HUD layer (8) for "all". A test only - the world
        /// disappears.
        public static string ToggleRenderers(string root)
        {
            if (_offRenderers.Count > 0)
            {
                int back = 0;
                for (int i = 0; i < _offRenderers.Count; i++)
                    if (_offRenderers[i] != null) { _offRenderers[i].enabled = true; back++; }
                _offRenderers.Clear();
                return back + " renderer(s) back on";
            }
            Renderer[] all = UnityEngine.Object.FindObjectsOfType(typeof(Renderer)) as Renderer[];
            if (all == null) return "no renderers";
            bool every = root == "all";
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (every) { if (r.gameObject.layer == 8) continue; }
                else if (r.transform.root.name != root) continue;
                r.enabled = false;
                _offRenderers.Add(r);
            }
            return _offRenderers.Count + " renderer(s) off";
        }

        /// Enabled, active renderers by scene root (top `top`), plus the
        /// scene's enabled lights, LOD groups and terrains.
        public static string RenderersByRoot(int top)
        {
            Renderer[] all = UnityEngine.Object.FindObjectsOfType(typeof(Renderer)) as Renderer[];
            if (all == null) return "no renderers";
            Dictionary<string, int> roots = new Dictionary<string, int>();
            int on = 0;
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                on++;
                string key = r.transform.root.name;
                int n;
                roots.TryGetValue(key, out n);
                roots[key] = n + 1;
            }
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>(roots);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            Light[] lights = UnityEngine.Object.FindObjectsOfType(typeof(Light)) as Light[];
            int lightsOn = 0, shadowed = 0;
            if (lights != null)
                for (int i = 0; i < lights.Length; i++)
                    if (lights[i] != null && lights[i].enabled)
                    {
                        lightsOn++;
                        if (lights[i].shadows != LightShadows.None) shadowed++;
                    }
            UnityEngine.Object[] lods = UnityEngine.Object.FindObjectsOfType(typeof(LODGroup));
            StringBuilder sb = new StringBuilder();
            sb.Append("Render probe: ").Append(on).Append(" renderers enabled (of ").Append(all.Length).Append("), ")
              .Append(lightsOn).Append(" lights on (").Append(shadowed).Append(" with shadows), ")
              .Append(lods != null ? lods.Length : 0).Append(" LOD groups, ")
              .Append(Terrain.activeTerrains != null ? Terrain.activeTerrains.Length : 0).Append(" terrains; by root");
            for (int i = 0; i < list.Count && i < top; i++)
                sb.Append(i == 0 ? ": " : ", ").Append(list[i].Key).Append(' ').Append(list[i].Value);
            if (Log != null) Log.LogInfo(sb.ToString());
            return on + " renderers enabled, " + lightsOn + " lights (log)";
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
