namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // When an edit is written (T-0217, the Practice spot autosave).
    //
    // A text field or a dragged radius changes every frame, so a write
    // per change would rewrite the spot file 60 times a second. The
    // write waits until the edits pause (Quiet), but never longer than
    // MaxWait after the first unwritten one, so a long drag is still
    // written while it goes on. Times are the caller's clock in seconds.
    // ------------------------------------------------------------------
    public sealed class EditDebounce
    {
        public readonly float Quiet;
        public readonly float MaxWait;

        private float _first = -1f;
        private float _due = -1f;

        public EditDebounce(float quiet, float maxWait)
        {
            Quiet = quiet;
            MaxWait = maxWait < quiet ? quiet : maxWait;
        }

        public bool Pending { get { return _due >= 0f; } }

        /// An edit landed at `now`.
        public void Edit(float now)
        {
            if (_due < 0f) _first = now;
            float quietEnd = now + Quiet;
            float latest = _first + MaxWait;
            _due = quietEnd < latest ? quietEnd : latest;
        }

        /// True once, when the write is due; the edit is then written.
        public bool Due(float now)
        {
            if (_due < 0f || now < _due) return false;
            Clear();
            return true;
        }

        /// Written by another way (Save, a selection): nothing waits.
        public void Clear()
        {
            _first = -1f;
            _due = -1f;
        }
    }
}
