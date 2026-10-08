using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The replay's schematic (docs/run-audit-and-replays.md part 2): the
    // comparison run's buildings as wireframe boxes appearing at their
    // time, and a small marker at each thing it did (crafted, ate, kill,
    // ride, pause ...) along its line. GL lines in OnRenderObject, the
    // main view only (DrawTarget.ShouldDraw, gotcha 12) - like the run
    // lines, and like them after the image effects (Game/LatePass) so
    // the colours are not blown out to white. Labels are drawn by the
    // module in OnGUI (cached text).
    //
    // Everything that needs maths (box corners from rotation, colours by
    // kind) is worked out once in SetSource; a frame only emits vertices.
    // ------------------------------------------------------------------
    public sealed class ReplayBehaviour : MonoBehaviour, ILateDrawer
    {
        public bool ShowBuildings = true;
        public bool ShowMarkers = true;
        /// 0..1, the run lines' opacity (Runs -> Line options).
        public float Opacity = 1f;
        /// The replay time: buildings show from their time on (a blueprint
        /// until it is finished); +inf = the end state.
        public float UpTo = float.PositiveInfinity;
        /// Markers before this index are behind the ghost (full colour),
        /// the rest are still ahead of it (faded).
        public int MarkersPassed = int.MaxValue;

        public const float MarkerHeight = 1.4f;
        private const float Diamond = 0.22f;

        private Vector3[] _corners = new Vector3[0];   // 8 per building
        private float[] _from = new float[0];
        private float[] _until = new float[0];
        private bool[] _built = new bool[0];
        private int _buildings;

        private Vector3[] _marks = new Vector3[0];
        private Color[] _markColours = new Color[0];
        private int _markCount;

        private Material _material;

        private static readonly Color PlacedColour = new Color(0.55f, 0.8f, 1f, 0.6f);
        private static readonly Color BuiltColour = new Color(1f, 0.72f, 0.3f, 0.9f);

        /// The run whose buildings and markers are drawn; null clears.
        /// Allocates only when the run changes.
        public void SetSource(Attempt a)
        {
            _buildings = 0;
            _markCount = 0;
            if (a == null) return;

            int nb = a.Buildings.Count;
            if (_corners.Length < nb * 8) _corners = new Vector3[nb * 8];
            if (_from.Length < nb) { _from = new float[nb]; _built = new bool[nb]; }
            float[] until = ReplayMarks.Until(a.Buildings);
            _until = until;
            for (int i = 0; i < nb; i++)
            {
                RunBuilding b = a.Buildings[i];
                Quaternion rot = Quaternion.Euler(b.Euler);
                Vector3 h = b.Size * 0.5f;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 local = b.Center + new Vector3((k & 1) == 0 ? -h.x : h.x, (k & 2) == 0 ? -h.y : h.y, (k & 4) == 0 ? -h.z : h.z);
                    _corners[i * 8 + k] = b.P + rot * local;
                }
                _from[i] = b.T;
                _built[i] = b.State == RunBuilding.Built;
            }
            _buildings = nb;

            int ne = a.Events.Count;
            if (_marks.Length < ne) { _marks = new Vector3[ne]; _markColours = new Color[ne]; }
            for (int i = 0; i < ne; i++)
            {
                _marks[i] = a.Events[i].P;
                _markColours[i] = GroupColour(RunAudit.Group(a.Events[i].Kind));
            }
            _markCount = ne;
        }

        /// A colour per run audit group (Data/RunAudit.Groups).
        public static Color GroupColour(string group)
        {
            switch (group)
            {
                case "progress": return new Color(1f, 0.85f, 0.2f, 1f);
                case "caves": return new Color(0.75f, 0.55f, 1f, 1f);
                case "items": return new Color(0.45f, 1f, 0.5f, 1f);
                case "building": return new Color(1f, 0.6f, 0.2f, 1f);
                case "fights": return new Color(1f, 0.3f, 0.3f, 1f);
                case "deaths": return new Color(1f, 1f, 1f, 1f);
                case "movement": return new Color(0.3f, 0.9f, 1f, 1f);
                case "menu": return new Color(0.75f, 0.75f, 0.75f, 1f);
                default: return new Color(0.9f, 0.9f, 0.9f, 1f);
            }
        }

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
        private void OnEnable() { try { LatePass.Register(this); } catch (System.Exception ex) { Lifecycle.Fail("ReplayBehaviour.OnEnable", ex); } }
        private void OnDisable() { try { LatePass.Unregister(this); } catch (System.Exception ex) { Lifecycle.Fail("ReplayBehaviour.OnDisable", ex); } }
        private void LateUpdate() { try { LatePass.Sync(DrawTarget.View()); } catch (System.Exception ex) { Lifecycle.Fail("ReplayBehaviour.LateUpdate", ex); } }
        public bool WantsLateDraw { get { return (ShowBuildings && _buildings > 0) || (ShowMarkers && _markCount > 0); } }
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
            catch (System.Exception ex) { Lifecycle.Fail("ReplayBehaviour.OnRenderObject", ex); }
        }

        private void DrawLines()
        {
            bool buildings = ShowBuildings && _buildings > 0;
            bool markers = ShowMarkers && _markCount > 0;
            EnsureMaterial();
            if (_material == null) return;

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            int vertices = 0;
            _material.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);

            float op = Mathf.Clamp01(Opacity);
            if (buildings)
            {
                for (int i = 0; i < _buildings; i++)
                {
                    float t = UpTo;
                    if (_from[i] > t) continue;
                    if (!float.IsPositiveInfinity(_until[i]) && t >= _until[i]) continue;
                    Color c = _built[i] ? BuiltColour : PlacedColour;
                    c.a *= op;
                    GL.Color(c);
                    int o = i * 8;
                    // The 12 edges: corners that differ in one axis bit.
                    for (int k = 0; k < 8; k++)
                    {
                        if ((k & 1) == 0) { GL.Vertex(_corners[o + k]); GL.Vertex(_corners[o + (k | 1)]); }
                        if ((k & 2) == 0) { GL.Vertex(_corners[o + k]); GL.Vertex(_corners[o + (k | 2)]); }
                        if ((k & 4) == 0) { GL.Vertex(_corners[o + k]); GL.Vertex(_corners[o + (k | 4)]); }
                    }
                    vertices += 24;
                }
            }

            if (markers)
            {
                for (int i = 0; i < _markCount; i++)
                {
                    Color c = _markColours[i];
                    // Ahead of the ghost: faded, so what is behind it reads first.
                    c.a = op * (i < MarkersPassed ? 0.95f : 0.4f);
                    GL.Color(c);
                    Vector3 p = _marks[i];
                    Vector3 top = new Vector3(p.x, p.y + MarkerHeight, p.z);
                    GL.Vertex(p); GL.Vertex(top);
                    // A small diamond on top.
                    Vector3 n = new Vector3(top.x, top.y + Diamond, top.z);
                    Vector3 s = new Vector3(top.x, top.y - Diamond, top.z);
                    Vector3 e = new Vector3(top.x + Diamond, top.y, top.z);
                    Vector3 w = new Vector3(top.x - Diamond, top.y, top.z);
                    GL.Vertex(n); GL.Vertex(e);
                    GL.Vertex(e); GL.Vertex(s);
                    GL.Vertex(s); GL.Vertex(w);
                    GL.Vertex(w); GL.Vertex(n);
                    vertices += 10;
                }
            }

            GL.End();
            GL.PopMatrix();
            DrawTarget.Record(start, vertices);
        }

        private void OnDestroy()
        {
            try
            {
                if (_material != null) Object.Destroy(_material);
            }
            catch (System.Exception ex) { Lifecycle.Fail("ReplayBehaviour.OnDestroy", ex); }
        }
    }
}
