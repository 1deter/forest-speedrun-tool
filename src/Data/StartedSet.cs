using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The rules of Game/ElevatorRides, pure so they are tested (T-0184):
    // objects that started something (a ride) are noted when they start
    // it, so "which ones are running now" is a walk over the few noted,
    // not a scene search. An object is noted once however often it
    // starts; one that is gone or no longer running is dropped when the
    // set is read, so the set never grows past what is running.
    // ------------------------------------------------------------------
    public sealed class StartedSet<T> where T : class
    {
        private readonly List<T> _started = new List<T>();

        /// Objects noted and not yet dropped.
        public int Count { get { return _started.Count; } }

        public void Note(T o)
        {
            if (o == null || _started.Contains(o)) return;
            _started.Add(o);
        }

        /// The noted objects still `alive` and `running`, into `into`
        /// (cleared first); every other one is dropped. `visible` filters
        /// what is handed out without dropping it (a switched-off object
        /// can be switched on again mid-ride).
        public void Running(Func<T, bool> alive, Func<T, bool> running, Func<T, bool> visible, List<T> into)
        {
            into.Clear();
            for (int i = _started.Count - 1; i >= 0; i--)
            {
                T o = _started[i];
                if (o == null || !alive(o) || !running(o)) { _started.RemoveAt(i); continue; }
                if (visible == null || visible(o)) into.Add(o);
            }
            into.Reverse();   // in the order they started
        }

        public void Clear() { _started.Clear(); }
    }
}
