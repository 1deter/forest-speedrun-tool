using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // One short message on screen for a few seconds.
    //
    // For what a runner must see when the window is CLOSED - a death
    // revive or F7 whose restore was refused otherwise only reached the
    // log (author, v0.22.1). With the window open a message belongs beside
    // the button that caused it, so modules show one only when no panel is
    // open. Drawn by the plugin; the text is set when it changes, never
    // built in OnGUI.
    // ------------------------------------------------------------------
    public sealed class Notice
    {
        private readonly GUIContent _content = new GUIContent("");
        private float _start, _until;

        public void Show(string text, float seconds)
        {
            _content.text = text ?? "";
            _start = Time.unscaledTime;
            _until = _start + seconds;
        }

        public bool Active { get { return _content.text.Length > 0 && Time.unscaledTime < _until; } }
        /// Seconds since Show, and how long it shows (the toast's slide and bar).
        public float Elapsed { get { return Time.unscaledTime - _start; } }
        public float Duration { get { return _until - _start; } }
        public GUIContent Content { get { return _content; } }
    }
}
