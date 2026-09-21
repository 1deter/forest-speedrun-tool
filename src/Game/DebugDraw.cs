using System;
using System.Collections.Generic;
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
    public sealed class DebugDrawBehaviour : MonoBehaviour
    {
        public bool ShowColliders;
        public bool ShowTriggers;
        public float Radius = 30f;
        public float RefreshInterval = 0.5f;
        public Transform Origin;

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

        private void Refresh()
        {
            _found.Clear();
            if (Origin == null) return;

            Collider[] hits;
            try { hits = Physics.OverlapSphere(Origin.position, Radius); }
            catch (Exception) { return; }

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == null) continue;
                bool trig = hits[i].isTrigger;
                if (trig && !ShowTriggers) continue;
                if (!trig && !ShowColliders) continue;
                _found.Add(hits[i]);
            }
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

            EnsureMaterial();
            if (_material == null) return;

            _material.SetPass(0);

            GL.PushMatrix();
            GL.Begin(GL.LINES);

            for (int i = 0; i < _found.Count; i++)
            {
                Collider c = _found[i];
                if (c == null) continue;

                GL.Color(c.isTrigger ? TriggerColour : SolidColour);

                // An axis-aligned world-space box is not the true shape for
                // a rotated mesh collider, but it is what Unity itself can
                // give cheaply and it is enough to see where a volume is.
                Bounds b = c.bounds;
                DrawWireBox(b.center, b.extents);
            }

            GL.End();
            GL.PopMatrix();
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
    // Detached free camera.
    //
    // Rather than unparenting the game's camera - which leaves
    // SimpleMouseRotator and the head-bob still driving it - this spawns
    // its own camera, copies the important settings across, and disables
    // the original. Nothing the game owns is modified, so exiting is just
    // "destroy mine, re-enable theirs".
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

        private Camera _camera;
        private Camera _suppressed;
        private float _yaw;
        private float _pitch;

        public bool Active { get { return _camera != null; } }

        public void Begin(Camera source)
        {
            if (_camera != null) return;
            if (source == null) return;

            _suppressed = source;

            GameObject go = new GameObject("ForestOverlay_FreeCam");
            go.hideFlags = HideFlags.HideAndDontSave;

            _camera = go.AddComponent<Camera>();
            _camera.CopyFrom(source);
            // CopyFrom brings the target texture and culling mask across,
            // which is what makes the view look identical.
            _camera.transform.position = source.transform.position;
            _camera.transform.rotation = source.transform.rotation;

            Vector3 e = source.transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x > 180f ? e.x - 360f : e.x;

            _suppressed.enabled = false;
        }

        public void End()
        {
            if (_suppressed != null) _suppressed.enabled = true;
            _suppressed = null;

            if (_camera != null)
            {
                UnityEngine.Object.Destroy(_camera.gameObject);
                _camera = null;
            }
        }

        private void Update()
        {
            if (_camera == null) return;

            // If the game tore down its camera (level load), stop rather
            // than leaving an orphan view the player cannot escape.
            if (_suppressed == null) { End(); return; }

            _yaw += Input.GetAxis("Mouse X") * LookSensitivity;
            _pitch -= Input.GetAxis("Mouse Y") * LookSensitivity;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);

            Transform t = _camera.transform;
            t.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            float speed = Speed;
            if (Input.GetKey(KeyCode.LeftShift)) speed *= FastMultiplier;
            if (Input.GetKey(KeyCode.LeftControl)) speed *= SlowMultiplier;

            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += t.forward;
            if (Input.GetKey(KeyCode.S)) move -= t.forward;
            if (Input.GetKey(KeyCode.D)) move += t.right;
            if (Input.GetKey(KeyCode.A)) move -= t.right;
            if (Input.GetKey(KeyCode.E)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;

            t.position += move.normalized * speed * Time.unscaledDeltaTime;
        }

        private void OnDestroy()
        {
            End();
        }
    }
}
