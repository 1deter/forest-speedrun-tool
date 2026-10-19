using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    /// One action end going down or up at a time into the run.
    public struct KeyChange
    {
        public float T;
        public int Index;   // into KeyTrack.Names
        public bool Down;
    }

    // ------------------------------------------------------------------
    // The inputs of a run (T-0344, build plan in tasks/notes/T-0344.md):
    // which of the game's actions were held, as changes, every frame (a
    // one-frame press - the wheel, a bridge press - is kept: on and off).
    //
    //   keys|<end>|<end>|...       the action ends, in index order: a button
    //                              is its name ("Jump"), an axis action two
    //                              ends ("Vertical+", "Vertical-")
    //   kc|<t>|<i>=<0|1>|...       the ends that changed at t
    //
    // Game/ActionRecorder reads them through Game/GameActions (the action,
    // whatever key the runner bound it to). Playback asks what was held at
    // a time through a Cache that walks forward from its last answer and
    // starts over after a seek back. Pure; linked into the tests and the site.
    // ------------------------------------------------------------------
    public sealed class KeyTrack
    {
        /// A run keeps at most this many changes: a stuck key or a long
        /// recording must not grow the file without end.
        public const int MaxChanges = 20000;

        public const string HeaderPrefix = "keys";
        public const string ChangePrefix = "kc";

        public readonly string[] Names;
        public readonly List<KeyChange> Changes = new List<KeyChange>();

        public KeyTrack(string[] names)
        {
            Names = names ?? new string[0];
        }

        /// False when not kept (an index out of range, or the track is full).
        public bool Add(float t, int index, bool down)
        {
            if (index < 0 || index >= Names.Length || Changes.Count >= MaxChanges) return false;
            KeyChange c;
            c.T = t;
            c.Index = index;
            c.Down = down;
            Changes.Add(c);
            return true;
        }

        /// The last change at or before t (-1 before the first).
        public int IndexAt(float t)
        {
            int lo = -1, hi = Changes.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Changes[mid].T <= t) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        /// The changes from..to (inclusive) applied to `held` (end indexes, in
        /// the order they went down) and `down` (per end). A release with no
        /// press changes nothing.
        public void Apply(int from, int to, List<int> held, bool[] down)
        {
            if (to >= Changes.Count) to = Changes.Count - 1;
            for (int i = from < 0 ? 0 : from; i <= to; i++)
            {
                KeyChange c = Changes[i];
                int at = held.IndexOf(c.Index);
                if (c.Down) { if (at < 0) held.Add(c.Index); }
                else if (at >= 0) held.RemoveAt(at);
                if (down != null && c.Index < down.Length) down[c.Index] = c.Down;
            }
        }

        /// An end's action name and sign: "Vertical-" -> Vertical, -1; "Jump" -> Jump, +1.
        public static string ActionOf(string end, out int sign)
        {
            sign = 1;
            if (string.IsNullOrEmpty(end)) return "";
            char last = end[end.Length - 1];
            if (end.Length > 1 && (last == '+' || last == '-'))
            {
                sign = last == '-' ? -1 : 1;
                return end.Substring(0, end.Length - 1);
            }
            return end;
        }

        public const string KeysNone = "Keys: none";

        // --- the .run lines ---------------------------------------------------------

        public void Write(StringBuilder sb, char nl)
        {
            sb.Append(HeaderPrefix);
            for (int i = 0; i < Names.Length; i++) sb.Append('|').Append(Clean(Names[i]));
            sb.Append(nl);
            for (int i = 0; i < Changes.Count; )
            {
                float t = Changes[i].T;
                sb.Append(ChangePrefix).Append('|').Append(t.ToString("0.####", CultureInfo.InvariantCulture));
                for (; i < Changes.Count && Changes[i].T == t; i++)
                    sb.Append('|').Append(Changes[i].Index.ToString(CultureInfo.InvariantCulture))
                      .Append('=').Append(Changes[i].Down ? '1' : '0');
                sb.Append(nl);
            }
        }

        /// A `keys|` line, split on '|'.
        public static KeyTrack FromHeader(string[] p)
        {
            string[] names = new string[Math.Max(0, p.Length - 1)];
            for (int c = 1; c < p.Length; c++) names[c - 1] = p[c];
            return new KeyTrack(names);
        }

        /// A `kc|` line, split on '|'. Malformed entries are skipped.
        public void ParseChange(string[] p)
        {
            if (p.Length < 3) return;
            float t;
            if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out t)) return;
            for (int c = 2; c < p.Length; c++)
            {
                string e = p[c];
                int eq = e.IndexOf('=');
                int index;
                if (eq <= 0 || eq != e.Length - 2) continue;
                if (!int.TryParse(e.Substring(0, eq), NumberStyles.Integer, CultureInfo.InvariantCulture, out index)) continue;
                char v = e[eq + 1];
                if (v != '0' && v != '1') continue;
                Add(t, index, v == '1');
            }
        }

        private static string Clean(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Replace("|", "/").Replace("\r", "").Replace("\n", "");
        }

        // --- playback ---------------------------------------------------------------

        /// What was held at the playhead: the ends in press order and per end.
        /// Forward it applies only the new changes; a seek back starts over.
        public sealed class Cache
        {
            private KeyTrack _track;
            private int _index = -2;
            private readonly List<int> _held = new List<int>();
            private bool[] _down = new bool[0];
            private string _text = KeysNone;
            private Func<string, string> _labelFor;
            public int Builds;

            /// Moves to t; true when the held set changed.
            public bool Seek(KeyTrack track, float t)
            {
                if (track == null) return false;
                int i = track.IndexAt(t);
                if (ReferenceEquals(track, _track) && i == _index) return false;
                if (ReferenceEquals(track, _track) && _index >= -1 && i > _index) track.Apply(_index + 1, i, _held, _down);
                else
                {
                    _track = track;
                    _held.Clear();
                    if (_down.Length != track.Names.Length) _down = new bool[track.Names.Length];
                    else Array.Clear(_down, 0, _down.Length);
                    track.Apply(0, i, _held, _down);
                }
                _index = i;
                _text = null;
                return true;
            }

            /// Was the end held at the last Seek.
            public bool Held(int end)
            {
                return end >= 0 && end < _down.Length && _down[end];
            }

            /// "Keys: Forward  Sprint  Jump" at t, each end through `label`
            /// (null: the end's own name); rebuilt only when the set changed.
            public string Text(KeyTrack track, float t, Func<string, string> label)
            {
                if (track == null) return "";
                bool moved = Seek(track, t);
                if (!moved && _text != null && label == _labelFor) return _text;
                _labelFor = label;
                if (_held.Count == 0) _text = KeysNone;
                else
                {
                    StringBuilder sb = new StringBuilder("Keys: ");
                    for (int k = 0; k < _held.Count; k++)
                    {
                        if (k > 0) sb.Append("  ");
                        string end = track.Names[_held[k]];
                        sb.Append(label != null ? label(end) : end);
                    }
                    _text = sb.ToString();
                }
                Builds++;
                return _text;
            }
        }
    }

    /// The four quick-select slots (backpack hotkeys 1-4) from t on.
    public struct QuickSlotSample
    {
        public float T;
        public int S1, S2, S3, S4;   // item ids; <= 0 empty

        public int At(int slot)
        {
            switch (slot) { case 0: return S1; case 1: return S2; case 2: return S3; default: return S4; }
        }
    }

    // ------------------------------------------------------------------
    // The items on the backpack's 1-4 hotkeys (T-0344): read at the state
    // cadence (5 Hz), kept when they differ from the last.
    //
    //   qs|<t>|<id1>|<id2>|<id3>|<id4>     ids; names looked up at playback
    // ------------------------------------------------------------------
    public sealed class QuickSlotTrack
    {
        public const string Prefix = "qs";
        public const int Slots = 4;

        public readonly List<QuickSlotSample> Samples = new List<QuickSlotSample>();

        /// Kept only when it differs from the last; false otherwise.
        public bool Add(float t, int[] ids)
        {
            if (ids == null || ids.Length < Slots) return false;
            int s1 = Norm(ids[0]), s2 = Norm(ids[1]), s3 = Norm(ids[2]), s4 = Norm(ids[3]);
            if (Samples.Count > 0)
            {
                QuickSlotSample l = Samples[Samples.Count - 1];
                if (l.S1 == s1 && l.S2 == s2 && l.S3 == s3 && l.S4 == s4) return false;
            }
            QuickSlotSample q;
            q.T = t; q.S1 = s1; q.S2 = s2; q.S3 = s3; q.S4 = s4;
            Samples.Add(q);
            return true;
        }

        // 0 and -1 both mean empty (docs/game-notes.md): one value on file.
        private static int Norm(int id) { return id > 0 ? id : 0; }

        /// The last sample at or before t (-1 before the first).
        public int IndexAt(float t)
        {
            int lo = -1, hi = Samples.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Samples[mid].T <= t) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        public void Write(StringBuilder sb, char nl)
        {
            for (int i = 0; i < Samples.Count; i++)
            {
                QuickSlotSample q = Samples[i];
                sb.Append(Prefix).Append('|').Append(q.T.ToString("0.###", CultureInfo.InvariantCulture))
                  .Append('|').Append(q.S1.ToString(CultureInfo.InvariantCulture))
                  .Append('|').Append(q.S2.ToString(CultureInfo.InvariantCulture))
                  .Append('|').Append(q.S3.ToString(CultureInfo.InvariantCulture))
                  .Append('|').Append(q.S4.ToString(CultureInfo.InvariantCulture)).Append(nl);
            }
        }

        /// A `qs|` line, split on '|'.
        public void Parse(string[] p)
        {
            if (p.Length < 2 + Slots) return;
            float t;
            if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out t)) return;
            int[] ids = new int[Slots];
            for (int i = 0; i < Slots; i++)
                if (!int.TryParse(p[2 + i], NumberStyles.Integer, CultureInfo.InvariantCulture, out ids[i])) return;
            Add(t, ids);
        }

        /// "Slots: 1 Axe Plane  2 -  3 Rock  4 -" for sample `index` (names
        /// through `name`; null or "" shows the id).
        public string Text(int index, Func<int, string> name)
        {
            if (index < 0 || index >= Samples.Count) return "Slots: none recorded yet";
            QuickSlotSample q = Samples[index];
            StringBuilder sb = new StringBuilder("Slots:");
            for (int i = 0; i < Slots; i++)
            {
                int id = q.At(i);
                sb.Append(i == 0 ? " " : "  ").Append(i + 1).Append(' ');
                if (id <= 0) { sb.Append('-'); continue; }
                string n = name != null ? name(id) : null;
                sb.Append(string.IsNullOrEmpty(n) ? "#" + id.ToString(CultureInfo.InvariantCulture) : n);
            }
            return sb.ToString();
        }
    }
}
