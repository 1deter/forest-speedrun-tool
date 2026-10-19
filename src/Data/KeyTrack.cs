using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ForestOverlay.Data
{
    /// One key going down or up at a time into the recording.
    public struct KeyChange
    {
        public float T;
        public string Key;
        public bool Down;
    }

    // ------------------------------------------------------------------
    // The keys pressed during a recording (T-0344): the recorder's `k|`
    // track, changes only - a key's press and its release, by its short
    // name ("W", "Shift", "M1"). The physical keys (what the fingers did,
    // as the input overlay's Raw input), whatever the game's bindings.
    //
    //   k|<t>|+W|-Space|...      changes at one time share a line
    //
    // Playback asks which keys were held at a time (HeldText, through a
    // Cache that walks forward from its last answer while the replay plays
    // on, and starts over only after a seek back). Pure; linked into the
    // tests and the site.
    // ------------------------------------------------------------------
    public sealed class KeyTrack
    {
        /// A recording keeps at most this many changes (a key held and let
        /// go is two): a long recording must not grow without end.
        public const int MaxChanges = 20000;

        public const string Prefix = "k";

        public readonly List<KeyChange> Changes = new List<KeyChange>();

        /// False when the track is full (the change is not kept).
        public bool Add(float t, string key, bool down)
        {
            if (string.IsNullOrEmpty(key) || Changes.Count >= MaxChanges) return false;
            KeyChange c;
            c.T = t;
            c.Key = key;
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

        /// The keys held after the changes up to `index` (inclusive), in
        /// the order they went down - into `held` (cleared first).
        public void HeldAfter(int index, List<string> held)
        {
            held.Clear();
            Apply(0, index, held);
        }

        /// The changes from..to (inclusive) applied to `held`: a press adds
        /// the key once, a release takes it out (a release with no press -
        /// a key already down when the recording began - changes nothing).
        public void Apply(int from, int to, List<string> held)
        {
            if (to >= Changes.Count) to = Changes.Count - 1;
            for (int i = from < 0 ? 0 : from; i <= to; i++)
            {
                KeyChange c = Changes[i];
                int at = held.IndexOf(c.Key);
                if (c.Down) { if (at < 0) held.Add(c.Key); }
                else if (at >= 0) held.RemoveAt(at);
            }
        }

        /// "Keys: W  Shift  Space", or "Keys: none".
        public static string Text(List<string> held)
        {
            if (held.Count == 0) return KeysNone;
            StringBuilder sb = new StringBuilder("Keys: ");
            for (int i = 0; i < held.Count; i++)
            {
                if (i > 0) sb.Append("  ");
                sb.Append(held[i]);
            }
            return sb.ToString();
        }

        public const string KeysNone = "Keys: none";

        // --- the .run lines ---------------------------------------------------------

        public void Write(StringBuilder sb, char nl)
        {
            for (int i = 0; i < Changes.Count; )
            {
                float t = Changes[i].T;
                sb.Append(Prefix).Append('|').Append(t.ToString("0.###", CultureInfo.InvariantCulture));
                for (; i < Changes.Count && Changes[i].T == t; i++)
                    sb.Append('|').Append(Changes[i].Down ? '+' : '-').Append(Changes[i].Key);
                sb.Append(nl);
            }
        }

        /// One `k|` line, split on '|'. Malformed entries are skipped.
        public void Parse(string[] p)
        {
            if (p.Length < 3) return;
            float t;
            if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out t)) return;
            for (int c = 2; c < p.Length; c++)
            {
                string e = p[c];
                if (e.Length < 2 || (e[0] != '+' && e[0] != '-')) continue;
                Add(t, e.Substring(1), e[0] == '+');
            }
        }

        // --- playback ---------------------------------------------------------------

        /// HeldText at the playhead, rebuilt only when the change index
        /// moves; forward it applies only the new changes.
        public sealed class Cache
        {
            private KeyTrack _track;
            private int _index = -2;
            private string _text = KeysNone;
            private readonly List<string> _held = new List<string>();
            public int Builds;

            public string Get(KeyTrack track, float t)
            {
                if (track == null) return "";
                int i = track.IndexAt(t);
                if (ReferenceEquals(track, _track) && i == _index) return _text;
                if (ReferenceEquals(track, _track) && _index >= -1 && i > _index) track.Apply(_index + 1, i, _held);
                else track.HeldAfter(i, _held);
                _track = track;
                _index = i;
                _text = Text(_held);
                Builds++;
                return _text;
            }
        }
    }
}
