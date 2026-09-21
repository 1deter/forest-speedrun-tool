using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Tracks whether anything run-illegal has been used this session.
    //
    // The project deliberately separates info-only features (velocity,
    // timer, item counts - these only read) from state-altering ones
    // (teleport, position restore, player lock - these write). A runner
    // recording a attempt needs to be able to see at a glance that a
    // practice tool was touched, so the HUD shows a marker as soon as one
    // is used. It is sticky on purpose: it can only be cleared by an
    // explicit reset, never automatically, so it cannot quietly disappear
    // mid-run.
    // ------------------------------------------------------------------
    public sealed class PracticeState
    {
        private string _reason;
        private int _useCount;

        // Cached because the HUD draws this every OnGUI pass, and OnGUI
        // runs several times per frame. Rebuilt only when it changes.
        private readonly GUIContent _label = new GUIContent("clean (info-only)");

        public bool Used { get { return _reason != null; } }
        public string Reason { get { return _reason; } }
        public int UseCount { get { return _useCount; } }

        public void Mark(string reason)
        {
            _useCount++;
            _reason = reason;
            Rebuild();
        }

        public void Reset()
        {
            _reason = null;
            _useCount = 0;
            Rebuild();
        }

        public GUIContent Label { get { return _label; } }

        private void Rebuild()
        {
            _label.text = _reason == null
                ? "clean (info-only)"
                : "PRACTICE - " + _reason + " (x" + _useCount + ")";
        }
    }
}
