using System;
using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using UnityEngine;
using UnityEngine.Rendering;

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
    // Game/LatePass, with OnRenderObject as the fallback (a plain
    // depth-tested draw); the main view only (DrawTarget).
    //
    // In the late pass paint shows only where it lies ON the surface
    // (T-0296 / T-0299): drawn into a target of its own against the
    // scene's depth where the scene is no nearer than DrawBand in front of
    // it (so it is not hidden by the rock it is on - the game's coarser
    // meshes and colliders sit a little off the full-detail one - but is
    // hidden by things in front), then cleared where it still lies in
    // front of the scene when pushed CutBand further away (past a wall's
    // edge, where the scene is the ground far behind, it is cut off rather
    // than hanging in the air). The clear only hits paint off its surface:
    // paint hidden behind a trunk, pushed back, is still behind the trunk
    // and clears nothing (kept first and cleared after, the far side's
    // stroke took the paint on the trunk's near side with it, T-0351).
    // The bands are shares of the distance, done by scaling the meshes
    // about the eye: every vertex slides along its own view ray, so the
    // picture does not move.
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
        /// How far behind the scene paint still shows, as a share of its
        /// distance (2 %: 4 cm at 2 m, 60 cm at 30 m): anything further in
        /// front of it hides it. Static, not const: tuned over the bridge.
        public static float DrawBand = 0.02f;
        /// How far in front of the scene paint still shows (8 %: 16 cm at
        /// 2 m): further out it is past an edge and cut. Wider than DrawBand:
        /// a dot painted from far sits up to ~10 cm off the wall the player
        /// later sees up close (its collider), and a 2 % cut took a whole
        /// stroke painted at 3 m (bridge 2026-10-15).
        public static float CutBand = 0.08f;
        /// How far the drawn surface under the crosshair may be from the
        /// collider the ray hits and still be used: metres + share of distance.
        private const float ProbeSlack = 1.5f, ProbeSlackShare = 0.05f;
        /// ... or this close to some collider (a rock's sphere sits within ~1 m of the rock).
        private const float NearCollider = 1.2f;
        /// A mesh / terrain collider this close to the drawn surface is taken
        /// instead of it: metres, or a share of the distance if more.
        private const float SnapSlack = 0.3f, SnapSlackShare = 0.03f;
        /// A mesh collider is taken only from this far: nearer, the drawn
        /// surface is the full-detail mesh and the collider is up to 10 cm
        /// off it (Cave 6 wall at 3 m, bridge 2026-10-15). Static: tuned
        /// over the bridge.
        public static float SnapFrom = 10f;

        private static readonly float[] RimCos = new float[Rim];
        private static readonly float[] RimSin = new float[Rim];

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private int _chunks;                       // meshes in use
        private PaintSet _built;                   // the set the meshes show

        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Color32> _colours = new List<Color32>();
        private readonly List<int> _tris = new List<int>();

        private Material _material;      // the fallback draw
        private Material _bandDraw;      // late pass: paint where its near edge is in front of the scene
        private Material _bandCut;       // late pass: clears it where its far edge is still in front
        private Material _over;          // late pass: the paint target blended onto the picture

        // The palette as written into the vertices. The game renders in
        // linear colour space and vertex colours are not converted, so a
        // swatch's sRGB values went in raw and came out lighter (red read
        // as pink, author's shot 2026-10-10): converted once here.
        private Color32[] _paletteFor;
        private Color32[] _vertexPalette;

        private Color32[] VertexPalette()
        {
            if (ReferenceEquals(_paletteFor, Palette)) return _vertexPalette;
            _paletteFor = Palette;
            _vertexPalette = new Color32[Palette.Length];
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int i = 0; i < Palette.Length; i++)
            {
                Color c = Palette[i];
                if (linear) { float a = c.a; c = c.linear; c.a = a; }
                _vertexPalette[i] = c;
            }
            return _vertexPalette;
        }

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
        /// Raycast are skipped. The drawn surface (Game/SurfaceProbe) when
        /// it agrees with the collider the ray hits, else the collider: a
        /// leaf or a bush far in front of it is not painted on. Where the
        /// collider is the terrain, or a mesh at least SnapFrom away, and
        /// lies within SnapSlack of the drawn surface, the collider's point:
        /// far off the game may draw a coarser cave mesh it swaps for the
        /// full-detail one up close (a Cave 6 wall's +-17 cm at 29 m), and
        /// the mesh collider is closer to the near mesh (within ~10 cm,
        /// bridge 2026-10-15). Nearer than SnapFrom the drawn surface is
        /// the near mesh itself. False for a frame or two at the start of a
        /// stroke while the first reading comes in.
        public static bool Aim(Transform player, float range, out Vector3 point, out Vector3 normal)
        {
            point = Vector3.zero;
            normal = Vector3.up;
            Camera cam = DrawTarget.View();
            if (cam == null) return false;

            SurfaceProbe.Request();
            int mask = ~(1 << 2);
            if (player != null) mask &= ~(1 << player.gameObject.layer);
            RaycastHit hit;

            Vector3 eye, sp, sn;
            if (SurfaceProbe.TryGet(out eye, out sp, out sn))
            {
                Vector3 dir = sp - eye;
                float dist = dir.magnitude;
                if (dist > 0.05f && dist <= range)
                {
                    dir /= dist;
                    bool hitAny = Physics.Raycast(new Ray(eye, dir), out hit, range, mask, QueryTriggerInteraction.Ignore);
                    float off = hitAny ? Mathf.Abs(hit.distance - dist) : float.MaxValue;
                    if (off <= Mathf.Max(SnapSlack, SnapSlackShare * dist)
                        && (hit.collider is TerrainCollider || (hit.collider is MeshCollider && dist >= SnapFrom)))
                    {
                        point = hit.point;
                        normal = hit.normal;
                        LastFrom = AimSource.SurfaceCollider;
                        return true;
                    }
                    // Or a collider close by: the crosshair on a rock's edge
                    // misses its smaller sphere and hits the ground behind.
                    if (!hitAny || off <= ProbeSlack + ProbeSlackShare * dist
                        || Physics.CheckSphere(sp, NearCollider, mask, QueryTriggerInteraction.Ignore))
                    {
                        point = sp;
                        normal = sn;
                        LastFrom = AimSource.Surface;
                        return true;
                    }
                }
            }
            else if (SurfaceProbe.Pending) return false;

            // The collider alone: the probe cannot read (no late pass, no
            // depth texture), or the drawn surface under the crosshair has
            // no collider near it (a bush in front of the ground). A dot on
            // a collider far off the drawn surface (a rock's sphere) is cut
            // from view in the late pass like any paint off its surface.
            Transform t = cam.transform;
            if (!Physics.Raycast(new Ray(t.position, t.forward), out hit, range, mask, QueryTriggerInteraction.Ignore))
                return false;
            point = hit.point;
            normal = hit.normal;
            LastFrom = AimSource.Collider;
            return true;
        }

        public enum AimSource { Collider, Surface, SurfaceCollider }

        /// What the last Aim used: the collider alone, the drawn surface, or
        /// a mesh / terrain collider agreeing with the drawn surface.
        public static AimSource LastFrom;

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
            Color32[] palette = VertexPalette();

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
            _material = Colored(shader, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, CompareFunction.LessEqual, -2f);
            _bandDraw = Colored(shader, BlendMode.One, BlendMode.Zero, CompareFunction.LessEqual, 0f);
            _bandCut = Colored(shader, BlendMode.Zero, BlendMode.Zero, CompareFunction.Less, 0f);
            Shader over = Shader.Find("Unlit/Transparent");
            if (over != null)
            {
                _over = new Material(over);
                _over.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        private static Material Colored(Shader shader, BlendMode src, BlendMode dst, CompareFunction zTest, float zBias)
        {
            Material m = new Material(shader);
            m.hideFlags = HideFlags.HideAndDontSave;
            m.SetInt("_SrcBlend", (int)src);
            m.SetInt("_DstBlend", (int)dst);
            m.SetInt("_Cull", (int)CullMode.Off);
            m.SetInt("_ZWrite", 0);
            m.SetInt("_ZTest", (int)zTest);
            m.SetFloat("_ZBias", zBias);
            return m;
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

        private bool HasPaint { get { return Visible && _chunks > 0; } }
        public bool WantsLateDraw { get { return HasPaint || SurfaceProbe.Wanted; } }

        public void DrawLate(Camera camera)
        {
            RenderTexture picture = LatePass.Picture;
            RenderBuffer depth = LatePass.SceneDepth;
            if (SurfaceProbe.Wanted)
            {
                SurfaceProbe.Sample(camera);
                LatePass.Rebind();
            }
            if (!HasPaint) return;

            EnsureMaterial();
            if (_material == null) return;
            if (picture == null || _over == null) { DrawMeshes(_material, Matrix4x4.identity); return; }

            RenderTexture paint = RenderTexture.GetTemporary(picture.width, picture.height, 0, picture.format,
                picture.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear, picture.antiAliasing);
            try
            {
                Graphics.SetRenderTarget(paint.colorBuffer, depth);
                GL.Clear(false, true, new Color(0f, 0f, 0f, 0f));
                Vector3 eye = camera.transform.position;
                DrawMeshes(_bandDraw, AboutEye(eye, 1f - DrawBand));
                DrawMeshes(_bandCut, AboutEye(eye, 1f + CutBand));

                // A Blit, not DrawTexture: target to target keeps the rows the right
                // way up (DrawTexture drew the paint mirrored about the crosshair on
                // D3D11, 2026-10-15). Unlit/Transparent blends by alpha.
                Graphics.Blit(paint, picture, _over);
                LatePass.Rebind();
            }
            finally { RenderTexture.ReleaseTemporary(paint); }
        }

        /// Scales about the eye: a point slides along its own view ray.
        private static Matrix4x4 AboutEye(Vector3 eye, float scale)
        {
            return Matrix4x4.TRS(eye * (1f - scale), Quaternion.identity, new Vector3(scale, scale, scale));
        }

        private void OnRenderObject()
        {
            try
            {
                if (!WantsLateDraw) return;
                if (!DrawTarget.ShouldDraw()) return;
                if (LatePass.Covers(Camera.current)) return;
                EnsureMaterial();
                if (_material == null) return;
                DrawMeshes(_material, Matrix4x4.identity);
            }
            catch (Exception ex) { Lifecycle.Fail("PaintDraw.OnRenderObject", ex); }
        }

        private void DrawMeshes(Material material, Matrix4x4 matrix)
        {
            material.SetPass(0);
            for (int c = 0; c < _chunks; c++)
                Graphics.DrawMeshNow(_meshes[c], matrix);
        }

        private void OnDestroy()
        {
            try
            {
                for (int i = 0; i < _meshes.Count; i++) if (_meshes[i] != null) Destroy(_meshes[i]);
                _meshes.Clear();
                if (_material != null) Destroy(_material);
                if (_bandDraw != null) Destroy(_bandDraw);
                if (_bandCut != null) Destroy(_bandCut);
                if (_over != null) Destroy(_over);
                SurfaceProbe.Release();
            }
            catch (Exception ex) { Lifecycle.Fail("PaintDraw.OnDestroy", ex); }
        }
    }
}
