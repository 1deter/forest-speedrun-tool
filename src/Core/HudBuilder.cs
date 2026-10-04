using System.Collections.Generic;
using ForestOverlay.Data;
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
    //
    // Each Pair is matched to its Settings switch (Data/HudLines) by the
    // module writing it (Source, set by the host) and its label, so a
    // module needs no change for its lines to be switchable.
    // ------------------------------------------------------------------
    public sealed class HudBuilder
    {
        private readonly List<GUIContent> _lines = new List<GUIContent>();
        private int _count;

        public int Count { get { return _count; } }

        /// The [HUD] switches; null = everything shown, normal wording.
        public HudSettings Settings;

        /// Id of the module contributing now (ModuleHost sets it).
        public string Source;

        /// Fewer words: modules that word their lines read this.
        public bool Compact { get { return Settings != null && Settings.Compact; } }

        /// Whether a line of the contributing module is on - for a module
        /// that would rather not build a line nobody sees.
        public bool Shows(string label)
        {
            return Settings == null || Settings.Shows(HudLines.Find(Source, label));
        }

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
            if (!Shows(label)) return;
            Line(HudLines.Pair(label, value, Compact));
        }

        public GUIContent At(int index)
        {
            return _lines[index];
        }
    }
}
