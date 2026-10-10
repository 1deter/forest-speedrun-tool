using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Which held-back spot entries the autosave has already told about
    // (T-0217). An invalid entry stays unsaved, and the autosave runs on
    // every pause, Select, Export and Submit: it says so once per entry,
    // not each time. An entry that saves (or goes) is forgotten, so the
    // next time it is held back it is reported again.
    // ------------------------------------------------------------------
    public sealed class HeldReport
    {
        private readonly List<string> _told = new List<string>();

        /// True the first time `id` is held back; false while it stays held.
        public bool First(string id)
        {
            if (_told.Contains(id)) return false;
            _told.Add(id);
            return true;
        }

        /// Keeps only the ids still held back; the rest may be reported again.
        public void Keep(ICollection<string> held)
        {
            for (int i = _told.Count - 1; i >= 0; i--)
                if (!held.Contains(_told[i])) _told.RemoveAt(i);
        }

        public void Clear() { _told.Clear(); }
    }
}
