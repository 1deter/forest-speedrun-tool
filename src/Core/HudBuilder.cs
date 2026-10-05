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

        // What each slot's Pair text was built from: the same label, value
        // and wording at the same slot keep the text as it is, so an
        // unchanged line costs no string (the label padding was one new
        // string per line, ten times a second).
        private readonly List<string> _builtLabel = new List<string>();
        private readonly List<string> _builtValue = new List<string>();
        private readonly List<bool> _builtCompact = new List<bool>();

        // The HUD customiser's view of a slot (Core/HudWidgets): which
        // HudLines entry wrote it (-1: not a switchable line) and its label
        // and value apart, for a line that is its own widget.
        private readonly List<int> _lineIndex = new List<int>();
        private readonly List<GUIContent> _labelC = new List<GUIContent>();
        private readonly List<GUIContent> _valueC = new List<GUIContent>();

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

            if (_count < _lines.Count)
            {
                _lines[_count].text = text;
                _builtLabel[_count] = null;   // not a Pair's text
                _lineIndex[_count] = -1;
            }
            else
            {
                _lines.Add(new GUIContent(text));
                _builtLabel.Add(null);
                _builtValue.Add(null);
                _builtCompact.Add(false);
                _lineIndex.Add(-1);
                _labelC.Add(new GUIContent(""));
                _valueC.Add(new GUIContent(""));
            }

            _count++;
        }

        public void Pair(string label, string value)
        {
            int idx = HudLines.Find(Source, label);
            if (Settings != null && !Settings.Shows(idx)) return;
            if (label == null) label = "";
            if (value == null) value = "";
            bool compact = Compact;
            int slot = _count;
            if (slot < _lines.Count && _builtLabel[slot] != null && _builtCompact[slot] == compact && _lineIndex[slot] == idx &&
                string.Equals(_builtLabel[slot], label) && string.Equals(_builtValue[slot], value))
            {
                _count++;
                return;
            }
            Line(HudLines.Pair(label, value, compact));
            _builtLabel[slot] = label;
            _builtValue[slot] = value;
            _builtCompact[slot] = compact;
            _lineIndex[slot] = idx;
            _labelC[slot].text = label;
            _valueC[slot].text = value.Trim();
        }

        /// The HudLines index of the line a slot holds, or -1.
        public int LineIndex(int index) { return _lineIndex[index]; }
        public GUIContent LabelAt(int index) { return _labelC[index]; }
        public GUIContent ValueAt(int index) { return _valueC[index]; }

        /// The HUD customiser: which lines are their own widgets.
        public HudWidgets Widgets;

        public GUIContent At(int index)
        {
            return _lines[index];
        }
    }
}
