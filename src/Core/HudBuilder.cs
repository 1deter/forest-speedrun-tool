using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Collects the HUD text for one refresh.
    //
    // WHY THIS EXISTS: OnGUI runs several times per frame, so building
    // strings there is the documented cause of the ~1/sec GC spike this
    // project already hit once. Modules therefore write their lines here
    // on the throttled Tick (10 Hz), OnGUI only reads the cached result,
    // and the GUIContent objects are reused between refreshes instead of
    // being reallocated.
    // ------------------------------------------------------------------
    public sealed class HudBuilder
    {
        private readonly List<GUIContent> _lines = new List<GUIContent>();
        private int _count;

        public int Count { get { return _count; } }

        public void Begin()
        {
            _count = 0;
        }

        // Reuses the GUIContent at this slot when one already exists, so a
        // steady-state HUD allocates nothing after the first few frames.
        public void Line(string text)
        {
            if (text == null) text = "";

            if (_count < _lines.Count) _lines[_count].text = text;
            else _lines.Add(new GUIContent(text));

            _count++;
        }

        public void Pair(string label, string value)
        {
            Line(label.PadRight(7) + value);
        }

        public GUIContent At(int index)
        {
            return _lines[index];
        }
    }
}
