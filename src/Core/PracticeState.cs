using System.Collections.Generic;
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

        /// Draw the HUD line as a warning: practice used outside a run, or
        /// a run that is not valid.
        public bool Warn { get { return _runText != null ? _runInvalid : _reason != null; } }

        /// Core/RunMode: told of every Mark (a Mark during a run flags it).
        public System.Action<string> MarkedDuringRun;

        // Run mode's line replaces the marker while an attempt runs.
        private string _runText;
        private bool _runInvalid;

        public void SetRunText(string text, bool invalid)
        {
            _runText = text;
            _runInvalid = invalid;
            Rebuild();
        }
        public string Reason { get { return _reason; } }
        public int UseCount { get { return _useCount; } }

        public void Mark(string reason)
        {
            _useCount++;
            _reason = reason;
            Rebuild();
            if (MarkedDuringRun != null) MarkedDuringRun(reason);
        }

        public void Reset()
        {
            _reason = null;
            _useCount = 0;
            Rebuild();
        }

        public GUIContent Label { get { return _label; } }

        // What changes the game RIGHT NOW (author, QA 2026-09-26: maks had No
        // stagger on and took it for a new lineup; v0.24.194). The marker
        // says a tool was used; this line says which are still on. Modules
        // call SetOn from Tick - cheap when nothing changes.
        private readonly List<string> _on = new List<string>();
        private readonly GUIContent _onLabel = new GUIContent("");

        public bool AnyOn { get { return _on.Count > 0; } }
        public GUIContent OnLabel { get { return _onLabel; } }

        public void SetOn(string what, bool on)
        {
            int at = _on.IndexOf(what);
            if (on == (at >= 0)) return;
            if (on) _on.Add(what); else _on.RemoveAt(at);
            _onLabel.text = _on.Count == 0 ? "" : "ON NOW: " + string.Join(", ", _on.ToArray());
        }

        private void Rebuild()
        {
            if (_runText != null) { _label.text = _runText; return; }
            _label.text = _reason == null
                ? "clean (info-only)"
                : "PRACTICE - " + _reason + " (x" + _useCount + ")";
        }
    }
}
