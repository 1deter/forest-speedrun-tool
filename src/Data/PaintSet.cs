using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // A runner's paint (T-0219): dots where the crosshair hit, joined into
    // strokes, like KSF paint. Pure - the raycast and the mesh are
    // Game/PaintDraw's, the keys and files Modules/PaintModule's.
    //
    // One flat list of dots in the order they were painted. A dot knows
    // its stroke (undo removes the last stroke), its colour and size, and
    // whether it joins the dot before it (a ribbon is drawn between them).
    // Erasing a dot cuts the join to the one after it, so an erased gap is
    // a gap. DirtyFrom is the first dot changed since the drawer last
    // looked: painting only ever touches the tail, so the drawer rebuilds
    // only the mesh chunk at the end.
    //
    // File (config/ForestOverlay/paint/<spot id>.txt), one stroke line and
    // then its dots; "-" ends a dot that does not join the one before:
    //
    //   stroke 0 0.1
    //   12.5 30.25 -40 0 1 0
    //   12.6 30.25 -40 0 1 0
    //   20 31 -40 0 1 0 -
    // ------------------------------------------------------------------
    public struct PaintDot
    {
        public Vector3 P;
        public Vector3 N;
        public int Colour;
        /// Diameter in metres.
        public float Size;
        public bool Joined;
        public int Stroke;
    }

    public sealed class PaintSet
    {
        /// Enough for a lot of lineups; the mesh stays cheap below it.
        public const int MaxDots = 30000;
        /// A hit further than this from the last dot starts a new piece
        /// (the crosshair crossed from a near wall to the far ground).
        public const float JoinMax = 1.5f;
        public const float MinSize = 0.02f;
        public const float MaxSize = 1f;

        private readonly List<PaintDot> _dots = new List<PaintDot>();
        private int _stroke = -1;      // the open stroke's id, -1 = none
        private int _nextStroke;
        private int _openColour;
        private float _openSize;
        private int _openStart;        // index of the open stroke's first dot

        public int Count { get { return _dots.Count; } }
        public PaintDot this[int i] { get { return _dots[i]; } }
        public bool Full { get { return _dots.Count >= MaxDots; } }
        public bool StrokeOpen { get { return _stroke >= 0; } }

        /// Dots in the open stroke (for its log line when it ends).
        public int OpenCount { get { return _stroke >= 0 ? _dots.Count - _openStart : 0; } }

        /// Bumped on every change (for "save if changed").
        public int Version { get; private set; }

        /// The first dot index changed since TakeDirty; int.MaxValue = none.
        public int DirtyFrom { get; private set; }

        public PaintSet() { DirtyFrom = int.MaxValue; }

        /// Returns DirtyFrom and resets it (the drawer, once it rebuilt).
        public int TakeDirty()
        {
            int d = DirtyFrom;
            DirtyFrom = int.MaxValue;
            return d;
        }

        /// Dots closer than this to the last one are skipped: a held key on
        /// a still crosshair adds nothing.
        public static float Spacing(float size)
        {
            return Mathf.Max(0.02f, size * 0.5f);
        }

        public static float ClampSize(float size)
        {
            return Mathf.Clamp(size, MinSize, MaxSize);
        }

        public int StrokeCount
        {
            get
            {
                int n = 0, last = int.MinValue;
                for (int i = 0; i < _dots.Count; i++)
                    if (_dots[i].Stroke != last) { n++; last = _dots[i].Stroke; }
                return n;
            }
        }

        public void BeginStroke(int colour, float size)
        {
            _stroke = _nextStroke++;
            _openColour = colour;
            _openSize = ClampSize(size);
            _openStart = _dots.Count;
        }

        /// Adds a dot to the open stroke. False when it was skipped (too
        /// close to the last dot, no stroke open, or the set is full).
        public bool AddPoint(Vector3 p, Vector3 n)
        {
            if (_stroke < 0 || Full) return false;

            bool joined = false;
            int count = _dots.Count;
            if (count > _openStart)
            {
                PaintDot last = _dots[count - 1];
                float d = Vector3.Distance(last.P, p);
                if (d < Spacing(_openSize)) return false;
                joined = d <= JoinMax;
            }

            PaintDot dot;
            dot.P = p;
            dot.N = n;
            dot.Colour = _openColour;
            dot.Size = _openSize;
            dot.Joined = joined;
            dot.Stroke = _stroke;
            _dots.Add(dot);
            Changed(count);
            return true;
        }

        /// Closes the open stroke; returns its dot count (0: nothing was
        /// painted, and the stroke leaves no trace).
        public int EndStroke()
        {
            int n = OpenCount;
            _stroke = -1;
            return n;
        }

        /// Removes every dot within `radius` of `p` (plus the dot's own
        /// half size). Returns how many went.
        public int EraseNear(Vector3 p, float radius)
        {
            int write = 0, first = int.MaxValue;
            bool cut = false;
            for (int read = 0; read < _dots.Count; read++)
            {
                PaintDot d = _dots[read];
                float reach = radius + d.Size * 0.5f;
                if ((d.P - p).sqrMagnitude <= reach * reach)
                {
                    if (first == int.MaxValue) first = write;
                    cut = true;
                    continue;
                }
                if (cut)
                {
                    d.Joined = false;
                    cut = false;
                }
                _dots[write] = d;
                write++;
            }
            int removed = _dots.Count - write;
            if (removed == 0) return 0;
            _dots.RemoveRange(write, removed);
            if (_stroke >= 0 && _openStart > _dots.Count) _openStart = _dots.Count;
            Changed(first);
            return removed;
        }

        /// Removes the last stroke. Returns its dot count (0 = nothing to undo).
        public int Undo()
        {
            int count = _dots.Count;
            if (count == 0) return 0;
            int id = _dots[count - 1].Stroke;
            int start = count;
            while (start > 0 && _dots[start - 1].Stroke == id) start--;
            _dots.RemoveRange(start, count - start);
            if (_stroke == id) _stroke = -1;
            Changed(start);
            return count - start;
        }

        public int Clear()
        {
            int n = _dots.Count;
            _dots.Clear();
            _stroke = -1;
            if (n > 0) Changed(0);
            return n;
        }

        private void Changed(int from)
        {
            Version++;
            if (from < DirtyFrom) DirtyFrom = from;
        }

        // --- file -----------------------------------------------------------

        public string ToText()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# ForestOverlay paint: a stroke line (colour, size in m), then its dots (x y z, normal; - = not joined)\n");
            int last = int.MinValue;
            for (int i = 0; i < _dots.Count; i++)
            {
                PaintDot d = _dots[i];
                if (d.Stroke != last)
                {
                    last = d.Stroke;
                    sb.Append("stroke ").Append(d.Colour.ToString(CultureInfo.InvariantCulture))
                      .Append(' ').Append(F(d.Size)).Append('\n');
                }
                sb.Append(F(d.P.x)).Append(' ').Append(F(d.P.y)).Append(' ').Append(F(d.P.z)).Append(' ')
                  .Append(F(d.N.x)).Append(' ').Append(F(d.N.y)).Append(' ').Append(F(d.N.z));
                if (!d.Joined) sb.Append(" -");
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string F(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// Reads a file's text. Lines it cannot read are skipped and
        /// counted in `bad`; dots before any stroke line are bad too.
        public static PaintSet Parse(string text, out int bad)
        {
            PaintSet set = new PaintSet();
            bad = 0;
            if (string.IsNullOrEmpty(text)) return set;

            int stroke = -1, colour = 0;
            float size = 0.1f;
            bool firstOfStroke = false;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

                if (f[0] == "stroke")
                {
                    int c;
                    float s;
                    if (f.Length < 3 || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out c)
                        || !TryF(f[2], out s))
                    {
                        bad++;
                        stroke = -1;
                        continue;
                    }
                    stroke = set._nextStroke++;
                    colour = c;
                    size = ClampSize(s);
                    firstOfStroke = true;
                    continue;
                }

                float x, y, z, nx, ny, nz;
                if (stroke < 0 || f.Length < 6 || !TryF(f[0], out x) || !TryF(f[1], out y) || !TryF(f[2], out z)
                    || !TryF(f[3], out nx) || !TryF(f[4], out ny) || !TryF(f[5], out nz) || set.Full)
                {
                    bad++;
                    continue;
                }

                PaintDot d;
                d.P = new Vector3(x, y, z);
                d.N = new Vector3(nx, ny, nz);
                d.Colour = colour;
                d.Size = size;
                d.Joined = !firstOfStroke && !(f.Length > 6 && f[6] == "-");
                d.Stroke = stroke;
                set._dots.Add(d);
                firstOfStroke = false;
            }
            set.DirtyFrom = set._dots.Count > 0 ? 0 : int.MaxValue;
            return set;
        }

        private static bool TryF(string s, out float v)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                && !float.IsNaN(v) && !float.IsInfinity(v);
        }

        /// A spot id as a file name: anything but letters, digits, - _ . becomes _.
        public static string FileNameFor(string spotId)
        {
            if (string.IsNullOrEmpty(spotId)) return "_.txt";
            StringBuilder sb = new StringBuilder(spotId.Length + 4);
            for (int i = 0; i < spotId.Length; i++)
            {
                char c = spotId[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.';
                sb.Append(ok ? c : '_');
            }
            if (sb[0] == '.') sb[0] = '_';
            return sb.Append(".txt").ToString();
        }
    }
}
