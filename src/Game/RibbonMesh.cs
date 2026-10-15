using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The run lines' ribbons as meshes, rebuilt every frame (T-0368).
    //
    // A ribbon point is a ROW of six vertices across the line (soft edge,
    // dark edge, colour, colour, dark edge, soft edge: Offsets in pixels
    // times the point's one-pixel side vector, Data/RibbonPath). Every row
    // is joined to the row before by five quads - a FIXED index pattern,
    // so the triangles are only uploaded when a mesh changes size, not
    // every frame (building 30 indices a point every frame cost ~0.3 ms
    // for a 1,500-point line on the game's Mono, bridge 2026-10-15).
    //
    // Where a strip ends and another starts, two rows are COLLAPSED (all
    // six vertices on one point): the last point's, then the next one's.
    // A row lies on a straight line through its point, so the quads from a
    // row to its own collapsed copy have no area, and so have those between
    // two collapsed rows - nothing is drawn across the gap. The unused rows
    // at the end of a mesh are collapsed the same way.
    //
    // Arrays are written in place (no lists, no per-frame allocation) and
    // handed to the mesh whole; a mesh grows in steps of Step rows, shrinks
    // when a quarter of it is used, and holds at most RowsPerMesh rows (16-bit
    // indices on Unity 5.6) - a longer frame goes on in the next mesh, the
    // strip continued from its last row.
    // ------------------------------------------------------------------
    public sealed class RibbonMesh
    {
        public const int Across = 6;
        private const int RowsPerMesh = 8192;   // 49,152 vertices
        private const int Step = 512;

        /// The six offsets across a row, in pixels (negative = left).
        public readonly float[] Offsets = new float[Across];

        /// Vertices drawn by the last frame and the meshes they took.
        public int Vertices { get; private set; }
        public int Meshes { get { return _used; } }

        private sealed class Chunk
        {
            public Mesh Mesh;
            public Vector3[] V = new Vector3[0];
            public Color32[] C = new Color32[0];
            public int Rows;          // capacity
            public int Dirty;         // rows not collapsed (written last time)
            public bool Layout;       // size changed: triangles to upload
        }

        private readonly List<Chunk> _chunks = new List<Chunk>();
        private int _current, _used, _row;

        // The last row written, to continue a strip in the next mesh.
        private bool _hasLast;
        private float _lpx, _lpy, _lpz, _lsx, _lsy, _lsz;
        private Color32 _lc, _ld;

        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);

        public void Begin()
        {
            _current = 0;
            _used = 0;
            _row = 0;
            _hasLast = false;
            Vertices = 0;
            if (_chunks.Count == 0) _chunks.Add(NewChunk());
        }

        /// One point of a strip: its centre, one pixel across (world), the
        /// colour and the dark edge's colour. `first`: the strip starts here.
        public void Row(float px, float py, float pz, float sx, float sy, float sz, Color32 c, Color32 d, bool first)
        {
            if (_row + 4 > RowsPerMesh)
            {
                // Full: close this mesh, go on in the next from the last row.
                Finish();
                _current++;
                if (_chunks.Count <= _current) _chunks.Add(NewChunk());
                _row = 0;
                if (!first && _hasLast) Write(_lpx, _lpy, _lpz, _lsx, _lsy, _lsz, _lc, _ld);
            }
            else if (first && _row > 0)
            {
                Collapse(_row++, _lpx, _lpy, _lpz);
                Collapse(_row++, px, py, pz);
            }
            Write(px, py, pz, sx, sy, sz, c, d);
        }

        /// Uploads the frame's meshes (call once after the last Row).
        public void End()
        {
            Finish();
        }

        /// Draws them (the material's pass already set).
        public void Draw()
        {
            for (int i = 0; i < _used; i++) Graphics.DrawMeshNow(_chunks[i].Mesh, Matrix4x4.identity);
        }

        public void Destroy()
        {
            for (int i = 0; i < _chunks.Count; i++)
                if (_chunks[i].Mesh != null) UnityEngine.Object.Destroy(_chunks[i].Mesh);
            _chunks.Clear();
        }

        private static Chunk NewChunk()
        {
            Chunk ch = new Chunk();
            ch.Mesh = new Mesh();
            ch.Mesh.hideFlags = HideFlags.HideAndDontSave;
            ch.Mesh.MarkDynamic();
            return ch;
        }

        private void Write(float px, float py, float pz, float sx, float sy, float sz, Color32 c, Color32 d)
        {
            Chunk ch = _chunks[_current];
            Ensure(ch, _row + 2);
            int b = _row * Across;
            Vector3[] v = ch.V;
            Color32[] col = ch.C;
            for (int q = 0; q < Across; q++)
            {
                float o = Offsets[q];
                v[b + q].x = px + sx * o;
                v[b + q].y = py + sy * o;
                v[b + q].z = pz + sz * o;
            }
            col[b] = Clear; col[b + 1] = d; col[b + 2] = c;
            col[b + 3] = c; col[b + 4] = d; col[b + 5] = Clear;
            _row++;

            _hasLast = true;
            _lpx = px; _lpy = py; _lpz = pz;
            _lsx = sx; _lsy = sy; _lsz = sz;
            _lc = c; _ld = d;
        }

        private void Collapse(int row, float x, float y, float z)
        {
            Chunk ch = _chunks[_current];
            Ensure(ch, row + 1);
            int b = row * Across;
            Vector3[] v = ch.V;
            for (int q = 0; q < Across; q++) { v[b + q].x = x; v[b + q].y = y; v[b + q].z = z; }
        }

        private static void Ensure(Chunk ch, int rows)
        {
            if (rows <= ch.Rows) return;
            int n = ((rows + Step - 1) / Step) * Step;
            if (n > RowsPerMesh) n = RowsPerMesh;
            Array.Resize(ref ch.V, n * Across);
            Array.Resize(ref ch.C, n * Across);
            ch.Rows = n;
            ch.Layout = true;
        }

        /// Closes the mesh being filled: a collapsed row after the last, the
        /// rows written last time collapsed, the arrays to the mesh.
        private void Finish()
        {
            Chunk ch = _chunks[_current];
            if (_row == 0) return;    // nothing drawn into it this frame

            Collapse(_row, _lpx, _lpy, _lpz);
            int n = _row + 1;
            for (int r = n; r < ch.Dirty && r < ch.Rows; r++) Collapse(r, _lpx, _lpy, _lpz);
            ch.Dirty = n;

            // Mostly empty: shrink (new arrays; the rest of them is zero, collapsed).
            if (ch.Rows > Step && n * 4 < ch.Rows)
            {
                int rows = ((n * 2 + Step - 1) / Step) * Step;
                Vector3[] v = new Vector3[rows * Across];
                Color32[] c = new Color32[rows * Across];
                Array.Copy(ch.V, v, n * Across);
                Array.Copy(ch.C, c, n * Across);
                ch.V = v;
                ch.C = c;
                ch.Rows = rows;
                ch.Layout = true;
            }

            Mesh m = ch.Mesh;
            if (ch.Layout)
            {
                ch.Layout = false;
                m.Clear();
                m.vertices = ch.V;
                m.colors32 = ch.C;
                m.triangles = Pattern(ch.Rows);
            }
            else
            {
                m.vertices = ch.V;
                m.colors32 = ch.C;
            }
            Vertices += n * Across;
            _used = _current + 1;
        }

        /// Five quads from each row to the next, for `rows` rows.
        private static int[] Pattern(int rows)
        {
            int[] t = new int[(rows - 1) * (Across - 1) * 6];
            int k = 0;
            for (int r = 1; r < rows; r++)
            {
                int a = (r - 1) * Across, b = r * Across;
                for (int q = 0; q < Across - 1; q++)
                {
                    t[k++] = a + q; t[k++] = a + q + 1; t[k++] = b + q + 1;
                    t[k++] = a + q; t[k++] = b + q + 1; t[k++] = b + q;
                }
            }
            return t;
        }
    }
}
