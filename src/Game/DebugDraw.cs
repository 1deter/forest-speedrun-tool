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

    public sealed class DebugDrawBehaviour : MonoBehaviour
    {
        public bool ShowColliders;
        public bool ShowTriggers;
        public float Radius = 30f;
        public float RefreshInterval = 0.5f;
        public Transform Origin;

        // Filters - see Data/VolumeFilter. MaxSize caps a volume's largest
        // side (0 = no limit); Exclude drops names containing a fragment.
        public float MaxSize;
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
            if (!ShowColliders && !ShowTriggers) { _found.Clear(); return; }
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        private void OnRenderObject()
        {
            if (_found.Count == 0) return;
            if (!DrawTarget.ShouldDraw()) return;

            EnsureMaterial();
            if (_material == null) return;

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            _material.SetPass(0);

            GL.PushMatrix();
            GL.Begin(GL.LINES);

            for (int i = 0; i < _found.Count; i++)
            {
                Collider c = _found[i];
                if (c == null) continue;

                GL.Color(c.isTrigger ? TriggerColour : SolidColour);

                // The real shape for boxes, spheres and capsules (runners:
                // "more detailed hitboxes", v0.24.199); a mesh collider keeps
                // its world bounds - the true shape is the mesh itself.
                BoxCollider box = c as BoxCollider;
                SphereCollider sphere = box == null ? c as SphereCollider : null;
                CapsuleCollider capsule = box == null && sphere == null ? c as CapsuleCollider : null;
                if (box != null) DrawBox(box);
                else if (sphere != null) DrawSphere(sphere);
                else if (capsule != null) DrawCapsule(capsule);
                else
                {
                    Bounds b = c.bounds;
                    DrawWireBox(b.center, b.extents);
                }
            }

            GL.End();
            GL.PopMatrix();
            DrawTarget.Record(start, _found.Count * 24);
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
            GL.Vertex(a);
            GL.Vertex(b);
        }

        private void OnDestroy()
        {
            if (_material != null) UnityEngine.Object.Destroy(_material);
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
            if (Enabled) GL.wireframe = true;
        }

        private void OnPostRender()
        {
            GL.wireframe = false;
        }

        private void OnDisable()
        {
            GL.wireframe = false;
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

        // The camera is still a child of the player's head, which the game
        // moves; the pose is written late in the frame, from our own state.
        private void LateUpdate()
        {
            if (_camera == null) return;
            Transform t = _camera.transform;
            t.position = _pos;
            t.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        private void OnDestroy()
        {
            End();
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
    public sealed class MarkerBehaviour : MonoBehaviour
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

        private void OnRenderObject()
        {
            if (Count == 0 || !DrawTarget.ShouldDraw()) return;
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

    public sealed class RunLineBehaviour : MonoBehaviour
    {
        public bool Show = true;

        public Vector3[] ReferenceLine;
        public int ReferenceCount;
        /// The first reference point drawn (a window of the line, v0.24.191).
        public int ReferenceStart;
        /// Lines and ghost, 0..1 (Runs -> Line options).
        public float Opacity = 1f;

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

        private Material _material;

        private static readonly Color ReferenceColour = new Color(0.35f, 0.75f, 1f, 0.9f);
        private static readonly Color CurrentColour = new Color(1f, 0.95f, 0.35f, 0.9f);
        private static readonly Color GhostColour = new Color(1f, 0.35f, 0.75f, 1f);
        private static readonly Color FailedColour = new Color(1f, 0.3f, 0.25f, 0.75f);
        private static readonly Color PathColour = new Color(1f, 1f, 1f, 0.95f);

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

        private void OnRenderObject()
        {
            if (!Show) return;
            if (ReferenceCount < 2 && CurrentCount < 2 && FailedCount < 2 && !HasGhost && PathCount < 2) return;
            if (!DrawTarget.ShouldDraw()) return;

            EnsureMaterial();
            if (_material == null) return;

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            _material.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);

            DrawStrip(ReferenceLine, ReferenceStart, ReferenceCount, Faded(ReferenceColour));
            DrawStrip(CurrentLine, 0, CurrentCount, Faded(CurrentColour));
            DrawStrip(FailedLine, 0, FailedCount, Faded(FailedColour));
            DrawStrip(PathWindow, 0, PathCount, PathColour);

            int extra = PathCount > 1 ? 2 * (PathCount - 1) : 0;
            if (HasGhost)
            {
                GL.Color(Faded(GhostColour));
                if (DrawFigure && FigureCount > 0)
                {
                    int n = Mathf.Min(FigureCount, FigureVerts.Length) & ~1;
                    for (int i = 0; i < n; i++) GL.Vertex(FigureVerts[i]);
                    extra += n;
                }
                else
                {
                    // A small upright marker reads better than a dot at
                    // distance, and shows height difference at a glance.
                    DrawMarker(GhostPosition, 0.45f, 1.8f);
                    extra += 10;
                }
            }

            GL.End();
            GL.PopMatrix();
            DrawTarget.Record(start, 2 * (Mathf.Max(0, ReferenceCount - ReferenceStart - 1) + Mathf.Max(0, CurrentCount - 1)) + extra);
        }

        private Color Faded(Color c)
        {
            c.a *= Mathf.Clamp01(Opacity);
            return c;
        }

        private static void DrawStrip(Vector3[] points, int start, int count, Color colour)
        {
            if (points == null || count - start < 2) return;

            GL.Color(colour);
            int n = Mathf.Min(count, points.Length);

            for (int i = Mathf.Max(1, start + 1); i < n; i++)
            {
                GL.Vertex(points[i - 1]);
                GL.Vertex(points[i]);
            }
        }

        private static void DrawMarker(Vector3 p, float halfWidth, float height)
        {
            Vector3 top = new Vector3(p.x, p.y + height, p.z);

            // Vertical post.
            GL.Vertex(p); GL.Vertex(top);

            // Cross at the base so it is visible from above.
            GL.Vertex(new Vector3(p.x - halfWidth, p.y, p.z));
            GL.Vertex(new Vector3(p.x + halfWidth, p.y, p.z));
            GL.Vertex(new Vector3(p.x, p.y, p.z - halfWidth));
            GL.Vertex(new Vector3(p.x, p.y, p.z + halfWidth));

            // Cross at the top.
            GL.Vertex(new Vector3(top.x - halfWidth, top.y, top.z));
            GL.Vertex(new Vector3(top.x + halfWidth, top.y, top.z));
            GL.Vertex(new Vector3(top.x, top.y, top.z - halfWidth));
            GL.Vertex(new Vector3(top.x, top.y, top.z + halfWidth));
        }

        private void OnDestroy()
        {
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}
