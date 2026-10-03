using System;
using System.Collections.Generic;
using System.Globalization;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Game actions pressed / held by the test bridge (Game/InputInject
    // reads this from its patches on the game's Input.GetButton* /
    // GetAxis). Pure: frame numbers and times come in as arguments.
    //
    // A command run on frame F starts on F+1, so every script reads the
    // same thing for a whole frame wherever it sits in the update order:
    //   press Jump 3   down F+1, held F+1..F+3, up F+4
    //   hold Run 2.5   down F+1, held until 2.5 s have passed; the first
    //                  read after that ends it on the NEXT frame (up)
    //   release Run    held through F, up F+1
    // Names compare without case (the game's are "Jump", "Run", ...).
    // ------------------------------------------------------------------
    public sealed class InjectedInputs
    {
        private sealed class Entry
        {
            public string Name;
            public bool IsAxis;
            public float Value;
            public int Down;
            public int Up = int.MaxValue;          // first frame no longer held
            public float UntilTime = float.MaxValue;
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _drop = new List<string>();

        /// Any injection still around (the patches return at once when not).
        public bool Any { get { return _entries.Count > 0; } }

        public void Press(string name, int frame, int frames)
        {
            if (frames < 1) frames = 1;
            Entry e = Begin(name, false, 1f, frame);
            e.Up = frame + 1 + frames;
        }

        /// seconds <= 0: until a release.
        public void Hold(string name, int frame, float seconds, float now)
        {
            Entry e = Begin(name, false, 1f, frame);
            if (seconds > 0f) e.UntilTime = now + seconds;
        }

        /// seconds <= 0: until a release.
        public void SetAxis(string name, float value, int frame, float seconds, float now)
        {
            Entry e = Begin(name, true, value, frame);
            if (seconds > 0f) e.UntilTime = now + seconds;
        }

        /// Ends a hold / axis after this frame; false when nothing by that name.
        public bool Release(string name, int frame)
        {
            Entry e;
            if (!_entries.TryGetValue(name, out e)) return false;
            if (e.Up > frame + 1) e.Up = Math.Max(frame + 1, e.Down);
            return true;
        }

        public int ReleaseAll(int frame)
        {
            int n = 0;
            foreach (Entry e in _entries.Values)
                if (e.Up > frame + 1) { e.Up = Math.Max(frame + 1, e.Down); n++; }
            return n;
        }

        public bool Held(string name, int frame, float now)
        {
            Entry e = Find(name, false, frame, now);
            return e != null && frame >= e.Down && frame < e.Up;
        }

        public bool Down(string name, int frame, float now)
        {
            Entry e = Find(name, false, frame, now);
            return e != null && frame == e.Down && e.Up > e.Down;
        }

        public bool Up(string name, int frame, float now)
        {
            Entry e = Find(name, false, frame, now);
            return e != null && frame == e.Up;
        }

        public bool TryAxis(string name, int frame, float now, out float value)
        {
            value = 0f;
            Entry e = Find(name, true, frame, now);
            if (e == null || frame < e.Down || frame >= e.Up) return false;
            value = e.Value;
            return true;
        }

        /// True on the first frame of an axis injection (Input.GetAxisDown).
        public bool AxisDown(string name, int frame, float now)
        {
            Entry e = Find(name, true, frame, now);
            return e != null && frame == e.Down;
        }

        /// The entry is over (or was never there): its up frame has come.
        public bool Ended(string name, int frame, float now)
        {
            Entry e;
            if (!_entries.TryGetValue(name, out e)) return true;
            Resolve(e, frame, now);
            return frame >= e.Up;
        }

        /// Once a frame: timed holds ended, finished entries dropped (kept
        /// through their up frame so GetButtonUp can see it).
        public void Settle(int frame, float now)
        {
            _drop.Clear();
            foreach (Entry e in _entries.Values)
            {
                Resolve(e, frame, now);
                if (e.Up != int.MaxValue && frame > e.Up) _drop.Add(e.Name);
            }
            for (int i = 0; i < _drop.Count; i++) _entries.Remove(_drop[i]);
        }

        public void Describe(int frame, float now, List<string> o)
        {
            if (_entries.Count == 0) { o.Add("nothing pressed or held"); return; }
            foreach (Entry e in _entries.Values)
            {
                Resolve(e, frame, now);
                string what = e.IsAxis ? "axis " + e.Name + " = " + e.Value.ToString("0.###", CultureInfo.InvariantCulture) : e.Name;
                string state = frame < e.Down ? "starts next frame"
                    : frame >= e.Up ? "released"
                    : e.Up != int.MaxValue ? "held, " + (e.Up - frame) + " frame(s) left"
                    : e.UntilTime != float.MaxValue ? "held, " + Math.Max(0f, e.UntilTime - now).ToString("0.00", CultureInfo.InvariantCulture) + " s left"
                    : "held until released";
                o.Add(what + ": " + state);
            }
        }

        private Entry Begin(string name, bool axis, float value, int frame)
        {
            Entry e = new Entry();
            e.Name = name;
            e.IsAxis = axis;
            e.Value = value;
            e.Down = frame + 1;
            _entries[name] = e;
            return e;
        }

        private Entry Find(string name, bool axis, int frame, float now)
        {
            if (name == null || _entries.Count == 0) return null;
            Entry e;
            if (!_entries.TryGetValue(name, out e) || e.IsAxis != axis) return null;
            Resolve(e, frame, now);
            return e;
        }

        private static void Resolve(Entry e, int frame, float now)
        {
            if (e.Up == int.MaxValue && now >= e.UntilTime) e.Up = Math.Max(frame + 1, e.Down + 1);
        }
    }
}
