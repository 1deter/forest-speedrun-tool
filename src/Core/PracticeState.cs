using System.Collections.Generic;
using ForestOverlay.Data;
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
        private readonly GUIContent _label = new GUIContent("");

        public bool Used { get { return _reason != null; } }

        /// Draw the HUD line as a warning: practice used outside a run, or
        /// a run that is not valid.
        public bool Warn { get { return _runText != null ? _runInvalid : _reason != null || _modApi || _mods.Length > 0; } }

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

        // ModAPI loaded this session (T-0295): flagged for the whole session,
        // so an attempt start's Reset keeps it. A label, never a lock.
        private bool _modApi;
        public bool ModApi { get { return _modApi; } }

        public void SetModApi(bool on)
        {
            _modApi = on;
            Rebuild();
        }

        // Other mods seen this session (T-0308): the names, "" while none.
        // Sticky like ModAPI; set by Game/ExternalModWatch.
        private string _mods = "";
        public string Mods { get { return _mods; } }

        /// Core/RunMode: told when the names change (a mid-attempt flag).
        public System.Action<string> ModsChanged;

        public void SetMods(string names)
        {
            names = names ?? "";
            if (names == _mods) return;
            _mods = names;
            Rebuild();
            if (ModsChanged != null && names.Length > 0) ModsChanged(names);
        }

        public void Reset()
        {
            _reason = null;
            _useCount = 0;
            Rebuild();
        }

        public GUIContent Label { get { return _label; } }

        // Settings -> HUD -> Compact. The practice flag and the run line
        // keep every word (honest labelling).
        private bool _compact;
        public bool Compact
        {
            get { return _compact; }
            set { if (value != _compact) { _compact = value; Rebuild(); } }
        }

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
            // One feature per line, like a cheat menu's "enabled" list (author,
            // 2026-10-05); no "ON NOW" heading - the orange flags say it (2026-10-10).
            _onLabel.text = string.Join("\n", _on.ToArray());
        }

        private void Rebuild()
        {
            // Run mode's line, else the tool's short name, orange; nothing
            // while clean (author, 2026-10-10: no "PRACTICE" word, no count -
            // the flag says it). Each tool logs its own action; Reason /
            // UseCount stay for the bridge. A ModAPI session's flag sits above.
            _label.text = HudLines.Marker(_modApi, _mods, _runText, _reason);
        }
    }
}
