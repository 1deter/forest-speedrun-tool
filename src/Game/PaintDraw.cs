using System;
using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The paint tool's world side (T-0219): where the crosshair hits, and
    // the paint drawn as meshes - a flat disc per dot lying on its surface,
    // a ribbon to the dot before it when they join (Data/PaintSet).
    //
    // Meshes, not GL immediate mode: a few thousand dots at ~30 vertices
    // each would be 100k+ GL.Vertex calls a frame. The dots are cut into
    // chunks of ChunkDots; PaintSet.DirtyFrom says the first changed dot,
    // so painting (which only adds at the end) rebuilds the last chunk and
    // a frame with no change rebuilds nothing. Vertex lists are kept and
    // reused (no per-frame allocation).
    //
    // Drawn like the run lines: after the image effects through
    // Game/LatePass (true colours, depth-tested against the scene), with
    // OnRenderObject as the fallback; the main view only (DrawTarget).
    // ------------------------------------------------------------------
    public sealed class PaintDraw : MonoBehaviour, ILateDrawer
    {
        /// The set drawn; null draws nothing. Set by Modules/PaintModule.
        public PaintSet Source;
        /// Practice mode on and nothing hiding paint (the module decides).
        public bool Visible;
        /// The swatches, indexed by PaintDot.Colour.
        public Color32[] Palette = new Color32[] { new Color32(255, 255, 255, 255) };

        private const int ChunkDots = 2000;
        private const int Rim = 8;                 // disc sides
        /// Off the surface, against z-fighting (with the material's bias).
        private const float Lift = 0.015f;

        private static readonly float[] RimCos = new float[Rim];
        private static readonly float[] RimSin = new float[Rim];

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private int _chunks;                       // meshes in use
        private PaintSet _built;                   // the set the meshes show

        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color32> _colours = new List<Color32>();
        private readonly List<int> _tris = new List<int>();

        private Material _material;

        static PaintDraw()
        {
            for (int k = 0; k < Rim; k++)
            {
                float a = k * Mathf.PI * 2f / Rim;
                RimCos[k] = Mathf.Cos(a);
                RimSin[k] = Mathf.Sin(a);
            }
        }

        // --- aiming ---------------------------------------------------------

        /// The surface under the crosshair (the middle of the player's view),
        /// up to `range` m. Triggers, the player's own layer and Ignore
        /// Raycast are skipped.
        public static bool Aim(Transform player, float range, out Vector3 point, out Vector3 normal)
        {
            point = Vector3.zero;
            normal = Vector3.up;
            Camera cam = DrawTarget.View();
            if (cam == null) return false;

            int mask = ~(1 << 2);
            if (player != null) mask &= ~(1 << player.gameObject.layer);
            Transform t = cam.transform;
            RaycastHit hit;
            if (!Physics.Raycast(new Ray(t.position, t.forward), out hit, range, mask, QueryTriggerInteraction.Ignore))
                return false;
            point = hit.point;
            normal = hit.normal;
            return true;
        }

        // --- meshes ---------------------------------------------------------

        private void Rebuild()
        {
            PaintSet set = Source;
            if (!ReferenceEquals(set, _built))
            {
                // Another spot's paint: everything is new.
                _built = set;
                if (set != null) set.TakeDirty();
                RebuildFrom(0);
                return;
            }
            if (set == null) return;
            int dirty = set.TakeDirty();
            if (dirty == int.MaxValue) return;
            RebuildFrom(dirty / ChunkDots);
        }

        private void RebuildFrom(int firstChunk)
        {
            PaintSet set = _built;
            int count = set != null ? set.Count : 0;
            int need = (count + ChunkDots - 1) / ChunkDots;

            for (int c = firstChunk; c < need; c++)
            {
                while (_meshes.Count <= c)
                {
                    Mesh m = new Mesh();
                    m.hideFlags = HideFlags.HideAndDontSave;
                    m.MarkDynamic();
                    _meshes.Add(m);
                }
                BuildChunk(_meshes[c], set, c * ChunkDots, Math.Min(count, (c + 1) * ChunkDots));
            }
            // Chunks past the end keep their mesh (reused later) but are not drawn.
            _chunks = need;
        }

        private void BuildChunk(Mesh mesh, PaintSet set, int from, int to)
        {
            _verts.Clear();
            _colours.Clear();
            _tris.Clear();
            Color32[] palette = Palette;

            for (int i = from; i < to; i++)
            {
                PaintDot d = set[i];
                Color32 col = palette[d.Colour >= 0 && d.Colour < palette.Length ? d.Colour : 0];
                Vector3 n = d.N.sqrMagnitude > 1e-6f ? d.N.normalized : Vector3.up;
                Vector3 c = d.P + n * Lift;
                float r = d.Size * 0.5f;

                // Two axes across the surface.
                Vector3 a = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 b = Vector3.Cross(n, a);

                int centre = _verts.Count;
                _verts.Add(c);
                _colours.Add(col);
                for (int k = 0; k < Rim; k++)
                {
                    _verts.Add(c + (a * RimCos[k] + b * RimSin[k]) * r);
                    _colours.Add(col);
                }
                for (int k = 0; k < Rim; k++)
                {
                    _tris.Add(centre);
                    _tris.Add(centre + 1 + k);
                    _tris.Add(centre + 1 + (k + 1) % Rim);
                }

                if (!d.Joined || i == 0) continue;

                // The ribbon from the dot before: as wide as the dot, lying
                // between the two surfaces.
                PaintDot p = set[i - 1];
                Vector3 pn = p.N.sqrMagnitude > 1e-6f ? p.N.normalized : Vector3.up;
                Vector3 pc = p.P + pn * Lift;
                Vector3 along = c - pc;
                Vector3 side = Vector3.Cross((n + pn).normalized, along);
                if (side.sqrMagnitude < 1e-8f) continue;
                side = side.normalized * r;

                int q = _verts.Count;
                _verts.Add(pc - side); _verts.Add(pc + side);
                _verts.Add(c + side); _verts.Add(c - side);
                _colours.Add(col); _colours.Add(col); _colours.Add(col); _colours.Add(col);
                _tris.Add(q); _tris.Add(q + 1); _tris.Add(q + 2);
                _tris.Add(q); _tris.Add(q + 2); _tris.Add(q + 3);
            }

            mesh.Clear();
            mesh.SetVertices(_verts);
            mesh.SetColors(_colours);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateBounds();
        }

        // --- drawing --------------------------------------------------------

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
            _material.SetFloat("_ZBias", -2f);
        }

        private void OnEnable() { try { LatePass.Register(this); } catch (Exception ex) { Lifecycle.Fail("PaintDraw.OnEnable", ex); } }
        private void OnDisable() { try { LatePass.Unregister(this); } catch (Exception ex) { Lifecycle.Fail("PaintDraw.OnDisable", ex); } }

        private void LateUpdate()
        {
            try
            {
                Rebuild();
                LatePass.Sync(DrawTarget.View());
            }
            catch (Exception ex) { Lifecycle.Fail("PaintDraw.LateUpdate", ex); }
        }

        public bool WantsLateDraw { get { return Visible && _chunks > 0; } }
        public void DrawLate(Camera camera) { DrawMeshes(); }

        private void OnRenderObject()
        {
            try
            {
                if (!WantsLateDraw) return;
                if (!DrawTarget.ShouldDraw()) return;
                if (LatePass.Covers(Camera.current)) return;
                DrawMeshes();
            }
            catch (Exception ex) { Lifecycle.Fail("PaintDraw.OnRenderObject", ex); }
        }

        private void DrawMeshes()
        {
            EnsureMaterial();
            if (_material == null) return;
            _material.SetPass(0);
            for (int c = 0; c < _chunks; c++)
                Graphics.DrawMeshNow(_meshes[c], Matrix4x4.identity);
        }

        private void OnDestroy()
        {
            try
            {
                for (int i = 0; i < _meshes.Count; i++) if (_meshes[i] != null) Destroy(_meshes[i]);
                _meshes.Clear();
                if (_material != null) Destroy(_material);
            }
            catch (Exception ex) { Lifecycle.Fail("PaintDraw.OnDestroy", ex); }
        }
    }
}
