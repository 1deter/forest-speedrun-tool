using System;
using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Immediate-mode debug rendering.
    //
    // The Forest's own DebugConsole has no wireframe, trigger or collider
    // view (only _capsulemode), so these are drawn by the plugin.
    //
    // GL lines in OnRenderObject are the right tool on Unity 5.6 / net35:
    // no shaders to ship, no Gizmos (editor-only), and nothing added to
    // the scene graph. Hidden/Internal-Colored is a stock shader that has
    // shipped with Unity since well before 5.6.
    //
    // Colliders are gathered on a throttle inside a radius, not every
    // frame: the scene has tens of thousands of them and Physics.Overlap
    // per frame would be far more expensive than the drawing.
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    // Which camera our GL overlays draw into.
    //
    // OnRenderObject runs once for EVERY camera that renders - the main
    // view, but also reflection and UI cameras. Drawing a long run line
    // into each of them multiplied the cost for nothing anyone could see.
    // Only the view the player is looking through is drawn: the freecam
    // while it is on, otherwise Camera.main (the camera freecam flies,
    // so it is the world view). If there is no main camera at all, draw
    // everywhere rather than nowhere.
    // ------------------------------------------------------------------
    public static class DrawTarget
    {
        /// Set by FreeCamBehaviour while it is active.
        public static Camera FreeCam;

        private static int _frame = -1;
        private static Camera _main;

        public static bool ShouldDraw()
        {
            Camera current = Camera.current;
            if (current == null) return false;

            Camera target = FreeCam;
            if (target == null)
            {
                // Camera.main searches by tag; once a frame is plenty.
                if (Time.frameCount != _frame)
                {
                    _frame = Time.frameCount;
                    _main = Camera.main;
                }
                target = _main;
            }

            if (target == null || current == target) return true;

            PerfCounters.SkippedPasses++;
            return false;
        }

        /// The camera the player looks through: the freecam while it is
        /// on, else the main camera (null when there is none). For world
        /// labels in OnGUI (the replay's markers).
        public static Camera View()
        {
            if (FreeCam != null) return FreeCam;
            if (Time.frameCount != _frame)
            {
                _frame = Time.frameCount;
                _main = Camera.main;
            }
            return _main;
        }

        /// Wraps a draw for the perf log: call with the Stopwatch
        /// timestamp taken before drawing and the vertices emitted.
        public static void Record(long startTicks, int vertices)
        {
            PerfCounters.DrawPasses++;
            PerfCounters.Vertices += vertices;
            PerfCounters.RenderTicks += System.Diagnostics.Stopwatch.GetTimestamp() - startTicks;
        }
    }

    public sealed class DebugDrawBehaviour : MonoBehaviour, ILateDrawer
    {
        public bool ShowColliders;
        public bool ShowTriggers;
        public float Radius = 30f;
        public float RefreshInterval = 0.5f;
        public Transform Origin;

        // Filters - see Data/VolumeFilter. MaxSize caps a volume's largest
        // side (0 = no limit); Exclude drops names containing a fragment.
        public float MaxSize;

        /// Hide the parts of a volume behind solid scenery (a depth test
        /// against the scene). Off: every volume shows through walls.
        public bool Occlude;
        public string[] Exclude = new string[0];

        /// What the last refresh hid, so the tab can say why a volume is
        /// not drawn instead of it silently vanishing.
        public int HiddenBySize { get; private set; }
        public int HiddenByName { get; private set; }

        /// The biggest volumes still drawn. Rebuilt on the refresh throttle.
        public readonly LargestList Largest = new LargestList(5);

        private Material _material;
        private readonly List<Collider> _found = new List<Collider>();
        private float _nextRefresh;

        private static readonly Color SolidColour = new Color(0.25f, 0.9f, 0.35f, 0.9f);
        private static readonly Color TriggerColour = new Color(1f, 0.75f, 0.15f, 0.9f);

        private void EnsureMaterial()
        {
            if (_material != null) return;

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return;

            _material = new Material(shader);
            _material.hideFlags = HideFlags.HideAndDontSave;
            _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _material.SetInt("_ZWrite", 0);

            // The same shader for the mesh wireframes; its own material, as
            // their colour goes through _Color (the GL lines' is GL.Color).
            _meshMaterial = new Material(_material);
            _meshMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        // Mesh colliders (T-0367): shipped meshes are not readable on the CPU
        // (Mesh.isReadable false, every one sampled), but their GPU copy is
        // there (GetNativeVertexBufferPtr non-zero, cave collision meshes
        // included), so each is drawn as a wireframe of that copy:
        // GL.wireframe + Graphics.DrawMeshNow, after the GL lines. A convex
        // collider collides as the hull of this mesh. MeshVertexBudget caps
        // the vertices drawn in one frame; past it a mesh shows its bounds.
        // A mesh with no vertices, no GPU buffer or no triangle submesh shows its bounds too:
        // checked once per mesh on the refresh throttle (CheckMesh), as
        // GetNativeVertexBufferPtr waits for the render thread; a mesh not
        // checked yet is a box until it is (counted in neither reason).
        private const int MeshVertexBudget = 600000;
        private const int ChecksPerRefresh = 16;
        private const int MaxChecked = 20000;
        private static readonly Dictionary<int, bool> _drawable = new Dictionary<int, bool>();
        private int _checksLeft;
        private Material _meshMaterial;
        private readonly List<MeshCollider> _meshDraws = new List<MeshCollider>();
        private int _meshVerts;
        private static int _lines;

        /// Mesh colliders drawn as their bounds box in the last draw: over
        /// the frame's vertex budget, and with no mesh / vertices / GPU buffer / triangles.
        public int MeshBoxesLarge { get; private set; }
        public int MeshBoxesUndrawable { get; private set; }

        // Refresh throttle: is this mesh drawable from its GPU copy?
        private void CheckMesh(MeshCollider mc)
        {
            Mesh mesh = mc.sharedMesh;
            if (mesh == null) return;
            int id = mesh.GetInstanceID();
            if (_drawable.ContainsKey(id) || _checksLeft <= 0) return;
            _checksLeft--;
            if (_drawable.Count >= MaxChecked) _drawable.Clear();
            bool ok;
            try { ok = mesh.vertexCount > 0 && mesh.GetNativeVertexBufferPtr(0) != IntPtr.Zero && HasTriangles(mesh); }
            catch (Exception) { ok = false; }
            _drawable[id] = ok;
        }

        // T-0390: a GPU index buffer and a submesh with indices in a triangle
        // topology, else the wireframe pass would draw nothing for it.
        private static bool HasTriangles(Mesh mesh)
        {
            if (mesh.GetNativeIndexBufferPtr() == IntPtr.Zero) return false;
            int subs = mesh.subMeshCount;
            for (int i = 0; i < subs; i++)
            {
                MeshTopology t = mesh.GetTopology(i);
                if ((t == MeshTopology.Triangles || t == MeshTopology.Quads) && mesh.GetIndexCount(i) > 0) return true;
            }
            return false;
        }

        // Draw, GL pass: queue the mesh for the wireframe pass. False = draw
        // the bounds instead.
        private bool QueueMesh(MeshCollider mc)
        {
            Mesh mesh = mc.sharedMesh;
            if (mesh == null) { MeshBoxesUndrawable++; return false; }
            bool ok;
            if (!_drawable.TryGetValue(mesh.GetInstanceID(), out ok)) return false;   // not checked yet
            if (!ok) { MeshBoxesUndrawable++; return false; }
            int n = mesh.vertexCount;
            if (_meshVerts + n > MeshVertexBudget) { MeshBoxesLarge++; return false; }
            _meshVerts += n;
            _meshDraws.Add(mc);
            return true;
        }

        // Draw, after GL.End: the queued meshes as wireframes in the collider
        // colours. pull: the occlusion's toward-the-eye scale (identity off).
        private void DrawMeshWires(Matrix4x4 pull)
        {
            if (_meshDraws.Count == 0 || _meshMaterial == null) return;
            _meshMaterial.SetInt("_ZTest", (int)(Occlude ? UnityEngine.Rendering.CompareFunction.LessEqual
                                                         : UnityEngine.Rendering.CompareFunction.Always));
            int colour = -1;
            // Put back as found: on the OnRenderObject fallback the Views
            // tab's global Wireframe may hold it on for the rest of the camera.
            bool was = GL.wireframe;
            GL.wireframe = true;
            try
            {
                for (int i = 0; i < _meshDraws.Count; i++)
                {
                    MeshCollider mc = _meshDraws[i];
                    if (mc == null) continue;
                    Mesh mesh = mc.sharedMesh;
                    if (mesh == null) continue;
                    int want = mc.isTrigger ? 1 : 0;
                    if (want != colour)
                    {
                        colour = want;
                        _meshMaterial.SetColor("_Color", want == 1 ? TriggerColour : SolidColour);
                        _meshMaterial.SetPass(0);
                    }
                    // Every submesh: the collider uses them all, the
                    // two-argument DrawMeshNow only the first.
                    Matrix4x4 m = pull * mc.transform.localToWorldMatrix;
                    int subs = mesh.subMeshCount;
                    for (int s = 0; s < subs; s++) Graphics.DrawMeshNow(mesh, m, s);
                }
            }
            finally { GL.wireframe = was; }
        }

        /// Forces the next Update to re-gather, so a filter change shows
        /// at once rather than on the next throttle tick.
        public void RefreshSoon()
        {
            _nextRefresh = 0f;
        }

        private void Refresh()
        {
            _found.Clear();
            Largest.Clear();
            HiddenBySize = 0;
            HiddenByName = 0;
            if (Origin == null) return;

            Collider[] hits;
            try { hits = Physics.OverlapSphere(Origin.position, Radius); }
            catch (Exception) { return; }

            bool byName = Exclude != null && Exclude.Length > 0;
            bool byPath = byName && VolumeFilter.NeedsPath(Exclude);
            bool byLayer = byName && VolumeFilter.NeedsLayer(Exclude);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i];
                if (c == null) continue;
                bool trig = c.isTrigger;
                if (trig && !ShowTriggers) continue;
                if (!trig && !ShowColliders) continue;

                Vector3 size = c.bounds.size;
                float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                if (MaxSize > 0f && largest > MaxSize) { HiddenBySize++; continue; }

                // Reading a Unity name allocates, so only when it is needed.
                bool ranks = Largest.WouldRank(largest);
                if (!byName && !ranks) { _found.Add(c); continue; }

                string name = c.name;
                if (byName)
                {
                    string path = byPath ? PathOf(c.transform) : null;
                    string layer = byLayer ? LayerMask.LayerToName(c.gameObject.layer) : null;
                    if (VolumeFilter.IsHidden(name, path, layer, Exclude)) { HiddenByName++; continue; }
                }

                _found.Add(c);
                if (ranks) Largest.Add(name, largest);
            }

            _checksLeft = ChecksPerRefresh;
            for (int i = 0; i < _found.Count; i++)
            {
                MeshCollider mc = _found[i] as MeshCollider;
                if (mc != null) CheckMesh(mc);
            }
        }

        // parents/name, up to 6 levels (the filter's path fragments).
        private static string PathOf(Transform t)
        {
            string path = t.name;
            Transform p = t.parent;
            for (int i = 0; i < 5 && p != null; i++, p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private void Update()
        {
            try
            {
                if (!ShowColliders && !ShowTriggers) { _found.Clear(); return; }
                if (Time.unscaledTime < _nextRefresh) return;
                _nextRefresh = Time.unscaledTime + RefreshInterval;
                Refresh();
            }
            catch (Exception ex) { Lifecycle.Fail("DebugDrawBehaviour.Update", ex); }
        }

        // Drawn after the camera's image effects when Game/LatePass can
        // (true colours); OnRenderObject is the fallback.
        private void OnEnable() { try { LatePass.Register(this); } catch (Exception ex) { Lifecycle.Fail("DebugDrawBehaviour.OnEnable", ex); } }
        private void OnDisable() { try { LatePass.Unregister(this); } catch (Exception ex) { Lifecycle.Fail("DebugDrawBehaviour.OnDisable", ex); } }
        private void LateUpdate() { try { LatePass.Sync(DrawTarget.View()); } catch (Exception ex) { Lifecycle.Fail("DebugDrawBehaviour.LateUpdate", ex); } }
        public bool WantsLateDraw { get { return _found.Count > 0; } }
        public void DrawLate(Camera camera) { DrawLines(); }

        private void OnRenderObject()
        {
            try
            {
                if (!WantsLateDraw) return;
                if (!DrawTarget.ShouldDraw()) return;
                if (LatePass.Covers(Camera.current)) return;
                DrawLines();
            }
            catch (Exception ex) { Lifecycle.Fail("DebugDrawBehaviour.OnRenderObject", ex); }
        }

        private void DrawLines()
        {
            EnsureMaterial();
            if (_material == null) return;

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            // Occluded: test against the scene's depth, the lines pulled 1 %
            // toward the eye so a volume lying on a surface is not eaten by it.
            _material.SetInt("_ZTest", (int)(Occlude ? UnityEngine.Rendering.CompareFunction.LessEqual
                                                    : UnityEngine.Rendering.CompareFunction.Always));
            _material.SetPass(0);
            _lines = 0;
            _meshVerts = 0;
            _meshDraws.Clear();
            MeshBoxesLarge = 0;
            MeshBoxesUndrawable = 0;

            Matrix4x4 pull = Matrix4x4.identity;
            if (Occlude)
            {
                Camera view = DrawTarget.View();
                if (view != null)
                {
                    Vector3 eye = view.transform.position;
                    float k = 0.99f;
                    pull = Matrix4x4.TRS(eye * (1f - k), Quaternion.identity, new Vector3(k, k, k));
                }
            }
            GL.PushMatrix();
            GL.MultMatrix(pull);
            GL.Begin(GL.LINES);

            for (int i = 0; i < _found.Count; i++)
            {
                Collider c = _found[i];
                if (c == null) continue;

                GL.Color(c.isTrigger ? TriggerColour : SolidColour);

                // The real shape for boxes, spheres and capsules (runners:
                // "more detailed hitboxes", v0.24.199); a mesh collider is
                // drawn as its mesh's wireframe after the lines (within a
                // budget), else its world bounds.
                BoxCollider box = c as BoxCollider;
                SphereCollider sphere = box == null ? c as SphereCollider : null;
                CapsuleCollider capsule = box == null && sphere == null ? c as CapsuleCollider : null;
                if (box != null) DrawBox(box);
                else if (sphere != null) DrawSphere(sphere);
                else if (capsule != null) DrawCapsule(capsule);
                else if (c is MeshCollider && QueueMesh((MeshCollider)c)) { }
                else
                {
                    Bounds b = c.bounds;
                    DrawWireBox(b.center, b.extents);
                }
            }

            GL.End();
            GL.PopMatrix();
            DrawMeshWires(pull);
            _meshDraws.Clear();
            DrawTarget.Record(start, _lines * 2 + _meshVerts);
        }

        private const int Segments = 16;

        private static void DrawBox(BoxCollider b)
        {
            Matrix4x4 m = b.transform.localToWorldMatrix;
            Vector3 c = b.center, e = b.size * 0.5f;
            Vector3 p000 = m.MultiplyPoint3x4(c + new Vector3(-e.x, -e.y, -e.z));
            Vector3 p001 = m.MultiplyPoint3x4(c + new Vector3(-e.x, -e.y, e.z));
            Vector3 p010 = m.MultiplyPoint3x4(c + new Vector3(-e.x, e.y, -e.z));
            Vector3 p011 = m.MultiplyPoint3x4(c + new Vector3(-e.x, e.y, e.z));
            Vector3 p100 = m.MultiplyPoint3x4(c + new Vector3(e.x, -e.y, -e.z));
            Vector3 p101 = m.MultiplyPoint3x4(c + new Vector3(e.x, -e.y, e.z));
            Vector3 p110 = m.MultiplyPoint3x4(c + new Vector3(e.x, e.y, -e.z));
            Vector3 p111 = m.MultiplyPoint3x4(c + new Vector3(e.x, e.y, e.z));
            Line(p000, p001); Line(p001, p011); Line(p011, p010); Line(p010, p000);
            Line(p100, p101); Line(p101, p111); Line(p111, p110); Line(p110, p100);
            Line(p000, p100); Line(p001, p101); Line(p011, p111); Line(p010, p110);
        }

        private static float MaxAbs(Vector3 v) { return Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z))); }

        private static void DrawSphere(SphereCollider s)
        {
            Transform t = s.transform;
            Vector3 c = t.TransformPoint(s.center);
            float r = s.radius * MaxAbs(t.lossyScale);
            Ring(c, t.right, t.up, r);
            Ring(c, t.up, t.forward, r);
            Ring(c, t.forward, t.right, r);
        }

        private static void DrawCapsule(CapsuleCollider k)
        {
            Transform t = k.transform;
            Vector3 s = t.lossyScale;
            Vector3 axis, u, v;
            float along, across;
            if (k.direction == 0) { axis = t.right; u = t.up; v = t.forward; along = Mathf.Abs(s.x); across = Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)); }
            else if (k.direction == 2) { axis = t.forward; u = t.right; v = t.up; along = Mathf.Abs(s.z); across = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y)); }
            else { axis = t.up; u = t.right; v = t.forward; along = Mathf.Abs(s.y); across = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z)); }
            Vector3 c = t.TransformPoint(k.center);
            float r = k.radius * across;
            float half = Mathf.Max(0f, k.height * along * 0.5f - r);
            Vector3 a = c + axis * half, b = c - axis * half;
            Ring(a, u, v, r); Ring(b, u, v, r);
            Line(a + u * r, b + u * r); Line(a - u * r, b - u * r);
            Line(a + v * r, b + v * r); Line(a - v * r, b - v * r);
            Ring(a, u, axis, r); Ring(a, v, axis, r);   // the ends' domes, as rings
            Ring(b, u, axis, r); Ring(b, v, axis, r);
        }

        // A circle of radius r around c in the plane of x and y (unit vectors).
        private static void Ring(Vector3 c, Vector3 x, Vector3 y, float r)
        {
            Vector3 prev = c + x * r;
            for (int i = 1; i <= Segments; i++)
            {
                float a = i * (Mathf.PI * 2f / Segments);
                Vector3 p = c + (x * Mathf.Cos(a) + y * Mathf.Sin(a)) * r;
                Line(prev, p);
                prev = p;
            }
        }

        private static void DrawWireBox(Vector3 c, Vector3 e)
        {
            Vector3 p000 = new Vector3(c.x - e.x, c.y - e.y, c.z - e.z);
            Vector3 p001 = new Vector3(c.x - e.x, c.y - e.y, c.z + e.z);
            Vector3 p010 = new Vector3(c.x - e.x, c.y + e.y, c.z - e.z);
            Vector3 p011 = new Vector3(c.x - e.x, c.y + e.y, c.z + e.z);
            Vector3 p100 = new Vector3(c.x + e.x, c.y - e.y, c.z - e.z);
            Vector3 p101 = new Vector3(c.x + e.x, c.y - e.y, c.z + e.z);
            Vector3 p110 = new Vector3(c.x + e.x, c.y + e.y, c.z - e.z);
            Vector3 p111 = new Vector3(c.x + e.x, c.y + e.y, c.z + e.z);

            Line(p000, p001); Line(p001, p011); Line(p011, p010); Line(p010, p000);
            Line(p100, p101); Line(p101, p111); Line(p111, p110); Line(p110, p100);
            Line(p000, p100); Line(p001, p101); Line(p011, p111); Line(p010, p110);
        }

        private static void Line(Vector3 a, Vector3 b)
        {
            _lines++;
            GL.Vertex(a);
            GL.Vertex(b);
        }

        private void OnDestroy()
        {
            try
            {
                if (_material != null) UnityEngine.Object.Destroy(_material);
                if (_meshMaterial != null) UnityEngine.Object.Destroy(_meshMaterial);
            }
            catch (Exception ex) { Lifecycle.Fail("DebugDrawBehaviour.OnDestroy", ex); }
        }
    }

    // ------------------------------------------------------------------
    // Global wireframe.
    //
    // GL.wireframe is a render-state flag, so it has to be set before the
    // camera draws and cleared afterwards - hence OnPreRender/OnPostRender
    // on a component attached to the camera itself. Leaving it set would
    // turn the whole UI to wireframe too.
    // ------------------------------------------------------------------
    public sealed class WireframeBehaviour : MonoBehaviour
    {
        public bool Enabled;

        private void OnPreRender()
        {
            try
            {
                if (Enabled) GL.wireframe = true;
            }
            catch (Exception ex) { Lifecycle.Fail("WireframeBehaviour.OnPreRender", ex); }
        }

        private void OnPostRender()
        {
            try
            {
                GL.wireframe = false;
            }
            catch (Exception ex) { Lifecycle.Fail("WireframeBehaviour.OnPostRender", ex); }
        }

        private void OnDisable()
        {
            try
            {
                GL.wireframe = false;
            }
            catch (Exception ex) { Lifecycle.Fail("WireframeBehaviour.OnDisable", ex); }
        }
    }

    // ------------------------------------------------------------------
    // Free camera: flies the game's own camera.
    //
    // Until v0.24.136 this spawned a second camera with CopyFrom and
    // disabled the game's. CopyFrom copies the Camera settings only - the
    // ~20 effect scripts on MainCamNew (Sunshine shadows, the atmosphere
    // and fog, post-processing, SSAO, clouds, water) stayed behind, so the
    // freecam view was much darker and had no arms (author: "freecam goes
    // darker"). Now the game's camera itself is moved; every effect keeps
    // running on it.
    //
    // What must NOT fly with it: the camera's gameplay children (the
    // pickup Grabber, the weapon hitTrigger, WaterLevelSensor, SBookPos,
    // followMe) move onto a stand-in left at the camera's place, and the
    // two scripts that steer or report it (SimpleMouseRotator,
    // PlayerCamLocation - its static PlayerLoc is where the game thinks
    // the player's eyes are) are paused. Children the camera renders with
    // (ParticleCam, the cloud plane) stay on it. End puts every piece back
    // where it was and re-enables only what was enabled before.
    //
    // The camera stays in the game's hierarchy, so a level load destroys
    // it as usual and freecam ends itself.
    //
    // Mouse look reads Input.GetAxis("Mouse X"/"Mouse Y"), which keeps
    // working while the hardware cursor is locked.
    // ------------------------------------------------------------------
    public sealed class FreeCamBehaviour : MonoBehaviour
    {
        public float Speed = 12f;
        public float FastMultiplier = 4f;
        public float SlowMultiplier = 0.25f;
        public float LookSensitivity = 2.5f;

        /// Scripts on the camera that steer it or report its position.
        private static readonly string[] PausedScripts = { "SimpleMouseRotator", "PlayerCamLocation" };

        private Camera _camera;
        private Vector3 _homeLocalPos;
        private Quaternion _homeLocalRot;
        private Transform _standIn;
        private readonly List<Transform> _moved = new List<Transform>();
        private readonly List<Behaviour> _paused = new List<Behaviour>();
        private Vector3 _pos;
        private float _yaw;
        private float _pitch;

        /// Set when Begin / End has something to say (logged by the module).
        public string LastReport = "";

        /// The one flying the view (Debug views' freecam, or the replay
        /// camera's own copy of this behaviour): a second Begin while one
        /// is on is refused - both would park the same camera.
        public static FreeCamBehaviour InUse;

        /// False while the overlay window is open: the mouse is then
        /// pointing at buttons and the keys are typing into fields.
        public bool InputEnabled = true;

        public bool Active { get { return _camera != null; } }

        /// The flown camera, or null when freecam is off. Debug drawing
        /// centres on this while it is active.
        public Camera Camera { get { return _camera; } }

        /// False when it could not take the view (no camera, or another
        /// one is flying it - LastReport says which).
        public bool Begin(Camera source)
        {
            if (_camera != null) return true;
            if (source == null) { LastReport = "no camera"; return false; }
            if (InUse != null && InUse != this && InUse.Active)
            {
                LastReport = "the view is already flown by " + InUse.Owner;
                return false;
            }

            InUse = this;
            _camera = source;
            Transform t = source.transform;
            _homeLocalPos = t.localPosition;
            _homeLocalRot = t.localRotation;

            // The stand-in holds the gameplay children at the camera's
            // place under the camera's own parent, so they follow the
            // player exactly as before.
            GameObject standIn = new GameObject("ForestOverlay_CamStandIn");
            _standIn = standIn.transform;
            _standIn.SetParent(t.parent, false);
            _standIn.localPosition = _homeLocalPos;
            _standIn.localRotation = _homeLocalRot;
            _standIn.localScale = t.localScale;

            _moved.Clear();
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                Transform child = t.GetChild(i);
                if (RendersWithCamera(child)) continue;
                _moved.Add(child);
            }
            for (int i = 0; i < _moved.Count; i++) _moved[i].SetParent(_standIn, true);

            _paused.Clear();
            for (int i = 0; i < PausedScripts.Length; i++)
            {
                Behaviour b = source.GetComponent(PausedScripts[i]) as Behaviour;
                if (b == null || !b.enabled) continue;
                b.enabled = false;
                _paused.Add(b);
            }

            _pos = t.position;
            Vector3 e = t.eulerAngles;
            _yaw = e.y;
            _pitch = e.x > 180f ? e.x - 360f : e.x;

            DrawTarget.FreeCam = _camera;
            LastReport = Owner + ": flying '" + source.name + "', " + _moved.Count
                + " child(ren) left at the player, " + _paused.Count + " script(s) paused";
            return true;
        }

        /// Who flies the view, for messages ("Freecam", "the replay camera").
        public string Owner = "Freecam";

        /// Puts the flown camera at a pose (the aerial capture, the bridge);
        /// held there by LateUpdate like a flown one.
        public void Place(Vector3 position, float pitch, float yaw)
        {
            _pos = position;
            _pitch = pitch;
            _yaw = yaw;
            if (_camera != null)
            {
                _camera.transform.position = position;
                _camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
        }

        public void End()
        {
            if (InUse == this) InUse = null;
            if (_camera != null) DrawTarget.FreeCam = null;
            Camera cam = _camera;
            _camera = null;

            if (cam != null)
            {
                Transform t = cam.transform;
                t.localPosition = _homeLocalPos;
                t.localRotation = _homeLocalRot;
                for (int i = 0; i < _moved.Count; i++)
                    if (_moved[i] != null) _moved[i].SetParent(t, true);
                for (int i = 0; i < _paused.Count; i++)
                    if (_paused[i] != null) _paused[i].enabled = true;
            }
            _moved.Clear();
            _paused.Clear();

            if (_standIn != null) UnityEngine.Object.Destroy(_standIn.gameObject);
            _standIn = null;
        }

        /// A child the camera renders with (ParticleCam, the cloud plane):
        /// a camera, or a renderer with no collider. Everything else is
        /// gameplay and stays with the player.
        private static bool RendersWithCamera(Transform child)
        {
            if (child.GetComponent<Camera>() != null) return true;
            return child.GetComponent<Renderer>() != null && child.GetComponent<Collider>() == null;
        }

        private void Update()
        {
            try
            {
                if (_camera == null) return;
                if (!InputEnabled) return;

                _yaw += Input.GetAxis("Mouse X") * LookSensitivity;
                _pitch -= Input.GetAxis("Mouse Y") * LookSensitivity;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);

                float speed = Speed;
                if (Input.GetKey(KeyCode.LeftShift)) speed *= FastMultiplier;
                if (Input.GetKey(KeyCode.LeftControl)) speed *= SlowMultiplier;

                Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
                Vector3 forward = rot * Vector3.forward;
                Vector3 right = rot * Vector3.right;

                Vector3 move = Vector3.zero;
                if (Input.GetKey(KeyCode.W)) move += forward;
                if (Input.GetKey(KeyCode.S)) move -= forward;
                if (Input.GetKey(KeyCode.D)) move += right;
                if (Input.GetKey(KeyCode.A)) move -= right;
                if (Input.GetKey(KeyCode.E)) move += Vector3.up;
                if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;

                _pos += move.normalized * speed * Time.unscaledDeltaTime;
            }
            catch (Exception ex) { Lifecycle.Fail("FreeCamBehaviour.Update", ex); }
        }

        // The camera is still a child of the player's head, which the game
        // moves; the pose is written late in the frame, from our own state.
        private void LateUpdate()
        {
            try
            {
                if (_camera == null) return;
                Transform t = _camera.transform;
                t.position = _pos;
                t.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }
            catch (Exception ex) { Lifecycle.Fail("FreeCamBehaviour.LateUpdate", ex); }
        }

        private void OnDestroy()
        {
            try
            {
                End();
            }
            catch (Exception ex) { Lifecycle.Fail("FreeCamBehaviour.OnDestroy", ex); }
        }
    }

    // ------------------------------------------------------------------
    // Run lines and the ghost marker.
    //
    // Draws the recorded path of a reference attempt and of the run in
    // progress, plus a marker showing where the reference WAS at the
    // current elapsed time - that marker is the ghost you are racing.
    //
    // Positions are handed in as plain point lists rather than Attempts so
    // this stays a dumb renderer with no knowledge of the run model.
    // ------------------------------------------------------------------
    // Beacons the test bridge puts on things for the author to find
    // (`mark`): a tall post with a cross at its foot, drawn through
    // terrain and walls. Author, 2026-09-24: "I don't have a compass" -
    // told to pick up cash "7 m east", he could not tell where to look.
    public sealed class MarkerBehaviour : MonoBehaviour, ILateDrawer
    {
        public const int Max = 16;
        public readonly Vector3[] Points = new Vector3[Max];
        public int Count;

        private Material _material;
        private static readonly Color Colour = new Color(1f, 0.2f, 1f, 1f);

        public bool Add(Vector3 p)
        {
            if (Count >= Max) return false;
            Points[Count++] = p;
            return true;
        }

        // Drawn after the camera's image effects when Game/LatePass can
        // (true colours); OnRenderObject is the fallback.
        private void OnEnable() { try { LatePass.Register(this); } catch (Exception ex) { Lifecycle.Fail("MarkerBehaviour.OnEnable", ex); } }
        private void OnDisable() { try { LatePass.Unregister(this); } catch (Exception ex) { Lifecycle.Fail("MarkerBehaviour.OnDisable", ex); } }
        private void LateUpdate() { try { LatePass.Sync(DrawTarget.View()); } catch (Exception ex) { Lifecycle.Fail("MarkerBehaviour.LateUpdate", ex); } }
        public bool WantsLateDraw { get { return Count > 0; } }
        public void DrawLate(Camera camera) { DrawLines(); }

        private void OnRenderObject()
        {
            try
            {
                if (!WantsLateDraw) return;
                if (!DrawTarget.ShouldDraw()) return;
                if (LatePass.Covers(Camera.current)) return;
                DrawLines();
            }
            catch (Exception ex) { Lifecycle.Fail("MarkerBehaviour.OnRenderObject", ex); }
        }

        private void DrawLines()
        {
            if (_material == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null) return;
                _material = new Material(shader);
                _material.hideFlags = HideFlags.HideAndDontSave;
                _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _material.SetInt("_ZWrite", 0);
                _material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            _material.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);
            GL.Color(Colour);
            for (int i = 0; i < Count; i++)
            {
                Vector3 p = Points[i];
                const float h = 0.6f;
                GL.Vertex(p); GL.Vertex(new Vector3(p.x, p.y + 30f, p.z));
                GL.Vertex(new Vector3(p.x - h, p.y, p.z)); GL.Vertex(new Vector3(p.x + h, p.y, p.z));
                GL.Vertex(new Vector3(p.x, p.y, p.z - h)); GL.Vertex(new Vector3(p.x, p.y, p.z + h));
                GL.Vertex(new Vector3(p.x - h, p.y + 1f, p.z)); GL.Vertex(new Vector3(p.x + h, p.y + 1f, p.z));
            }
            GL.End();
            GL.PopMatrix();
            DrawTarget.Record(start, 8 * Count);
        }
    }

    public sealed class RunLineBehaviour : MonoBehaviour, ILateDrawer
    {
        public bool Show = true;

        public Vector3[] ReferenceLine;
        public int ReferenceCount;
        /// The first reference point drawn (a window of the line, v0.24.191).
        public int ReferenceStart;
        /// The comparison line coloured along its length by each point's
        /// speed (T-0372): ReferenceSpeeds index-aligned with ReferenceLine,
        /// over [SpeedMin, SpeedMax]. Off = the one colour.
        public bool BySpeed;
        public float[] ReferenceSpeeds;
        public float SpeedMin, SpeedMax;
        /// Your run's lines (current, failed), 0..1 (Runs -> Run lines).
        public float Opacity = 1f;
        /// The comparison line: the run lines' opacity during a run, the
        /// replay's while the replay camera plays it (T-0253).
        public float ReferenceOpacity = 1f;
        /// The ghost, 0..1 (Runs -> Ghost and replay).
        public float GhostOpacity = 1f;

        public Vector3[] CurrentLine;
        public int CurrentCount;

        /// The last run that did not finish (v0.24.200), red.
        public Vector3[] FailedLine;
        public int FailedCount;

        public bool HasGhost;
        public Vector3 GhostPosition;

        /// The ghost as a body (Data/GhostFigure: capsule, head, facing
        /// arrow, gaze) instead of the marker; FigureVerts is filled by the
        /// module (FigureCount vertices, line pairs).
        public bool DrawFigure;
        public readonly Vector3[] FigureVerts = new Vector3[ForestOverlay.Data.GhostFigure.Vertices];
        public int FigureCount;

        /// The replay camera's trajectory view: the stretch of the
        /// comparison it frames, drawn bright over the line.
        public readonly Vector3[] PathWindow = new Vector3[96];
        public int PathCount;

        /// Checkpoint markers on the comparison line (T-0371): where its run
        /// reached each split and its end. A dot with a dark ring each;
        /// MarkerOn false = not shown (outside the ahead-only stretch).
        public readonly Vector3[] MarkerPos = new Vector3[ForestOverlay.Data.CheckpointMarks.Max];
        public readonly bool[] MarkerOn = new bool[ForestOverlay.Data.CheckpointMarks.Max];
        public int MarkerCount;
        /// The dot's radius and its ring, pixels at 1080p.
        public static float DotRadius = 5f;
        public static float DotRing = 2f;

        /// The ribbons' core width in pixels at 1080p (Runs -> Run lines ->
        /// Width), scaled with the screen's height (T-0368).
        public float Width = 3f;

        /// How the lines meet plants, rocks and ground (T-0370): 0 hidden
        /// (depth-tested), 1 faint (the hidden parts at a third of the
        /// opacity), 2 drawn through (no depth test).
        public const int BehindHidden = 0, BehindFaint = 1, BehindThrough = 2;
        public int Behind = BehindHidden;
        /// The hidden parts' share of the opacity with BehindFaint.
        public const float FaintShare = 1f / 3f;

        // Tuned over the bridge (static, like Game/PaintDraw's bands).
        /// The dark edge each side, pixels at 1080p. 1 px at 60 % barely
        /// showed against the forest floor; 1.5 px at 85 % reads as a rim
        /// (bridge 2026-10-15, Cave 5 Practice Run).
        public static float Outline = 1.5f;
        /// The soft outer edge (alpha to 0), pixels at 1080p.
        public static float Soft = 1f;
        /// The dark edge's opacity, a share of the line's.
        public static float OutlineAlpha = 0.85f;
        /// Lines fade over the last FadeShare of DrawDistance (metres) and
        /// are not drawn beyond it.
        public static float DrawDistance = 300f;
        public static float FadeShare = 0.25f;
        /// A point closer than this to the last drawn, on screen, is skipped.
        public static float MinPixels = 2f;

        private Material _material;

        private static readonly Color ReferenceColour = new Color(0.35f, 0.75f, 1f, 0.9f);
        private static readonly Color CurrentColour = new Color(1f, 0.95f, 0.35f, 0.9f);
        private static readonly Color GhostColour = new Color(1f, 0.35f, 0.75f, 1f);
        private static readonly Color FailedColour = new Color(1f, 0.3f, 0.25f, 0.75f);
        private static readonly Color PathColour = new Color(1f, 1f, 1f, 0.95f);

        // ------------------------------------------------------------------
        // Ribbons, not GL lines (T-0368): a GL line is always 1 px on D3D11,
        // aliased - a thread lost against foliage. Each line is a strip of
        // quads facing the camera (Data/RibbonPath: near-plane clip, frustum
        // and distance cull, fade, screen decimation, the side vectors),
        // laid across as six vertices per point: soft edge, dark edge, the
        // colour, the colour, dark edge, soft edge - so it reads as a solid
        // band with a dark rim and anti-aliased sides. Rebuilt every frame
        // (the camera moves) into meshes drawn with DrawMeshNow
        // (Game/RibbonMesh): GL immediate mode would be a call per vertex
        // and colour (~20 per point). Still depth-tested, still in the late
        // pass (true colours); vertex colours are raw, as GL.Color's were
        // (game-notes *Overlay colours*), so the look of a colour is
        // unchanged.
        //
        // Cost (bridge 2026-10-15, fresh launch, 1920x1200): the 683 s
        // Elevator Boost run, 1,491 points, ~1,230 in view - 0.30 ms a frame
        // of CPU (GL 0.04 ms as 1 px lines), 186.9 vs 187.6 fps lines on /
        // off; the Cave 5 line (99 points) costs next to nothing.
        // ------------------------------------------------------------------
        private readonly RibbonPath _path = new RibbonPath();
        private readonly RibbonMesh _ribbon = new RibbonMesh();
        private readonly Vector3[] _marker = new Vector3[10];
        private readonly Vector3[] _dot = new Vector3[2];
        /// What the last frame drew (bridge: perf and "why nothing").
        public int LastPoints, LastVertices, LastMeshes;

        private void EnsureMaterial()
        {
            if (_material != null) return;

            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return;

            _material = new Material(shader);
            _material.hideFlags = HideFlags.HideAndDontSave;
            _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _material.SetInt("_ZWrite", 0);
        }

        // Drawn after the camera's image effects when Game/LatePass can
        // (true colours); OnRenderObject is the fallback.
        private void OnEnable() { try { LatePass.Register(this); } catch (Exception ex) { Lifecycle.Fail("RunLineBehaviour.OnEnable", ex); } }
        private void OnDisable() { try { LatePass.Unregister(this); } catch (Exception ex) { Lifecycle.Fail("RunLineBehaviour.OnDisable", ex); } }
        private void LateUpdate() { try { LatePass.Sync(DrawTarget.View()); } catch (Exception ex) { Lifecycle.Fail("RunLineBehaviour.LateUpdate", ex); } }
        public bool WantsLateDraw { get { return Show && (ReferenceCount >= 2 || CurrentCount >= 2 || FailedCount >= 2 || HasGhost || PathCount >= 2); } }
        public void DrawLate(Camera camera) { DrawLines(camera); }

        private void OnRenderObject()
        {
            try
            {
                if (!WantsLateDraw) return;
                if (!DrawTarget.ShouldDraw()) return;
                if (LatePass.Covers(Camera.current)) return;
                DrawLines(Camera.current);
            }
            catch (Exception ex) { Lifecycle.Fail("RunLineBehaviour.OnRenderObject", ex); }
        }

        private void DrawLines(Camera camera)
        {
            EnsureMaterial();
            if (_material == null || camera == null) return;

            long start = System.Diagnostics.Stopwatch.GetTimestamp();

            // The view: pixels per unit at depth 1 and the screen's half size
            // (plus a margin for the ribbon's own width).
            float h = camera.pixelHeight, w = camera.pixelWidth;
            float k = h / (2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
            float scale = h / 1080f;
            float core = Mathf.Max(0.5f, Width * scale) * 0.5f;
            float edge = core + Outline * scale, soft = edge + Soft * scale;
            Transform t = camera.transform;
            _path.SetView(t.position, t.forward, t.right, t.up, camera.nearClipPlane * 1.05f,
                          k, w * 0.5f + soft + 8f, h * 0.5f + soft + 8f, DrawDistance, FadeShare, MinPixels * scale);
            float[] o = _ribbon.Offsets;
            o[0] = -soft; o[1] = -edge; o[2] = -core;
            o[3] = core; o[4] = edge; o[5] = soft;

            LastPoints = 0;
            _ribbon.Begin();

            AddLine(ReferenceLine, ReferenceStart, ReferenceCount, Faded(ReferenceColour, ReferenceOpacity),
                    BySpeed ? ReferenceSpeeds : null);
            AddLine(CurrentLine, 0, CurrentCount, Faded(CurrentColour, Opacity));
            AddLine(FailedLine, 0, FailedCount, Faded(FailedColour, Opacity));
            AddLine(PathWindow, 0, PathCount, PathColour);

            if (HasGhost)
            {
                Color ghost = Faded(GhostColour, GhostOpacity);
                if (DrawFigure && FigureCount > 0)
                {
                    int n = Mathf.Min(FigureCount, FigureVerts.Length) & ~1;
                    for (int i = 0; i < n; i += 2) AddLine(FigureVerts, i, i + 2, ghost);
                }
                else
                {
                    // A small upright marker reads better than a dot at
                    // distance, and shows height difference at a glance.
                    MarkerLines(GhostPosition, 0.45f, 1.8f);
                    for (int i = 0; i < _marker.Length; i += 2) AddLine(_marker, i, i + 2, ghost);
                }
            }
            // Last: it changes the ribbons' offsets.
            if (MarkerCount > 0 && ReferenceCount - ReferenceStart >= 2)
                AddDots(t, k, scale, soft - edge, Faded(ReferenceColour, ReferenceOpacity));
            _ribbon.End();

            // The same meshes, once or twice (T-0370); _Color scales the
            // vertex alpha (Internal-Colored multiplies it, as the wireframes use).
            int behind = Behind;
            if (behind == BehindFaint)
            {
                _material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Greater);
                _material.SetColor("_Color", new Color(1f, 1f, 1f, FaintShare));
                _material.SetPass(0);
                _ribbon.Draw();
            }
            _material.SetInt("_ZTest", (int)(behind == BehindThrough ? UnityEngine.Rendering.CompareFunction.Always
                                                                       : UnityEngine.Rendering.CompareFunction.LessEqual));
            _material.SetColor("_Color", Color.white);
            _material.SetPass(0);
            _ribbon.Draw();

            LastVertices = _ribbon.Vertices;
            LastMeshes = _ribbon.Meshes;
            DrawTarget.Record(start, _ribbon.Vertices);
        }

        /// The checkpoint dots (T-0371): each a round-ish blob of four
        /// short ribbons across each other (up, right and both diagonals,
        /// every one as long as it is wide - an octagon), the dark ring
        /// first (all of them) and the line's colour over it. Same
        /// ribbons, so the line's near clip, cull and fade apply.
        private void AddDots(Transform cam, float k, float scale, float softPx, Color colour)
        {
            float[] o = _ribbon.Offsets;
            float r = DotRadius * scale, ring = DotRing * scale, sf = Mathf.Max(0.5f, softPx);
            Color dark = new Color(0f, 0f, 0f, colour.a);
            Vector3 eye = cam.position, fwd = cam.forward, up = cam.up, right = cam.right;
            for (int pass = 0; pass < 2; pass++)
            {
                float half = pass == 0 ? r + ring : r;
                o[0] = -half - sf; o[1] = -half; o[2] = -half;
                o[3] = half; o[4] = half; o[5] = half + sf;
                Color c = pass == 0 ? dark : colour;
                for (int i = 0; i < MarkerCount; i++)
                {
                    if (!MarkerOn[i]) continue;
                    Vector3 p = MarkerPos[i];
                    float depth = Vector3.Dot(p - eye, fwd);
                    if (depth <= 0.1f) continue;
                    float w = half * depth / k;   // the half width in metres here
                    for (int d = 0; d < 4; d++)
                    {
                        Vector3 dir = d == 0 ? up : d == 1 ? right : d == 2 ? (up + right) * 0.7071f : (up - right) * 0.7071f;
                        _dot[0] = p - dir * w;
                        _dot[1] = p + dir * w;
                        AddLine(_dot, 0, 2, c);
                    }
                }
            }
        }

        private static Color Faded(Color c, float opacity)
        {
            c.a *= Mathf.Clamp01(opacity);
            return c;
        }

        /// One polyline into the meshes: its strips (Data/RibbonPath), a row
        /// of six vertices per point (Game/RibbonMesh).
        private void AddLine(Vector3[] points, int start, int count, Color colour) { AddLine(points, start, count, colour, null); }

        /// `speeds` (index-aligned with points, or null): the colour's red,
        /// green and blue follow SpeedRamp per point, the alpha stays.
        private void AddLine(Vector3[] points, int start, int count, Color colour, float[] speeds)
        {
            if (points == null || count - start < 2) return;

            _path.Clear();
            _path.Add(points, speeds, start, count);
            LastPoints += _path.Count;

            Color32 full = colour;
            Color32 dark = new Color32(0, 0, 0, (byte)(Mathf.Clamp01(OutlineAlpha * colour.a) * 255f));
            float lineA = colour.a, darkA = OutlineAlpha * colour.a;
            Vector3[] p = _path.Points, s = _path.Sides;
            float[] fade = _path.Fade;
            int strips = _path.StripCount;
            for (int k = 0; k < strips; k++)
            {
                int first = _path.Starts[k];
                int end = k + 1 < strips ? _path.Starts[k + 1] : _path.Count;
                for (int j = first; j < end; j++)
                {
                    float f = fade[j];
                    Color32 c = full, d = dark;
                    if (speeds != null)
                    {
                        float cr, cg, cb;
                        ForestOverlay.Data.SpeedRamp.Colour(ForestOverlay.Data.SpeedRamp.Bucket(_path.Values[j], SpeedMin, SpeedMax), out cr, out cg, out cb);
                        c = new Color32((byte)(cr * 255f), (byte)(cg * 255f), (byte)(cb * 255f), full.a);
                    }
                    if (f < 1f)
                    {
                        c.a = (byte)(Mathf.Clamp01(lineA * f) * 255f);
                        d.a = (byte)(Mathf.Clamp01(darkA * f) * 255f);
                    }
                    _ribbon.Row(p[j].x, p[j].y, p[j].z, s[j].x, s[j].y, s[j].z, c, d, j == first);
                }
            }
        }

        /// The ghost's marker as line pairs: a post, a cross at its foot and
        /// one at its top.
        private void MarkerLines(Vector3 p, float halfWidth, float height)
        {
            Vector3 top = new Vector3(p.x, p.y + height, p.z);
            _marker[0] = p; _marker[1] = top;
            _marker[2] = new Vector3(p.x - halfWidth, p.y, p.z); _marker[3] = new Vector3(p.x + halfWidth, p.y, p.z);
            _marker[4] = new Vector3(p.x, p.y, p.z - halfWidth); _marker[5] = new Vector3(p.x, p.y, p.z + halfWidth);
            _marker[6] = new Vector3(top.x - halfWidth, top.y, top.z); _marker[7] = new Vector3(top.x + halfWidth, top.y, top.z);
            _marker[8] = new Vector3(top.x, top.y, top.z - halfWidth); _marker[9] = new Vector3(top.x, top.y, top.z + halfWidth);
        }

        private void OnDestroy()
        {
            try
            {
                if (_material != null) UnityEngine.Object.Destroy(_material);
                _ribbon.Destroy();
            }
            catch (Exception ex) { Lifecycle.Fail("RunLineBehaviour.OnDestroy", ex); }
        }
    }

    /// Is the straight line from the camera to a point blocked by
    /// scenery (T-0370: the run lines' checkpoint labels follow "Behind
    /// things"). One non-allocating line check; solid colliders only
    /// (triggers ignored). The end stops a little short of the point and
    /// the point is lifted, so the ground a dot sits on does not count.
    public static class SightCheck
    {
        private const float Lift = 0.3f, Short = 0.3f;

        public static bool Blocked(Vector3 eye, Vector3 point)
        {
            Vector3 to = point + Vector3.up * Lift;
            Vector3 d = to - eye;
            float len = d.magnitude;
            if (len <= Short + 0.01f) return false;
            Vector3 end = eye + d * ((len - Short) / len);
            return Physics.Linecast(eye, end, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
