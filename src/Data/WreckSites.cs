using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The rule of Game/WreckClearing, pure so it is tested (T-0148): every
    // plane wreck that starts is remembered; one starting within a metre
    // of a live one (an in-place restore's re-created wreck) is a repeat.
    // The first wreck of a scene (after a load the old ones are dead), a
    // wreck at a new place (a restore from another save) and a wreck
    // starting twice are not.
    // ------------------------------------------------------------------
    public sealed class WreckSites<T> where T : class
    {
        public const float SamePlace = 1f;

        private readonly Func<T, bool> _alive;
        private readonly Func<T, Vector3> _position;
        private readonly List<T> _started = new List<T>();

        public WreckSites(Func<T, bool> alive, Func<T, Vector3> position)
        {
            _alive = alive;
            _position = position;
        }

        public int Count { get { return _started.Count; } }

        public void Clear() { _started.Clear(); }

        /// `wreck` starts at `at`: true when a live wreck already stands
        /// there. Remembers it either way.
        public bool IsRepeat(T wreck, Vector3 at)
        {
            bool repeat = false;
            for (int i = _started.Count - 1; i >= 0; i--)
            {
                T w = _started[i];
                if (!_alive(w)) { _started.RemoveAt(i); continue; }
                if (ReferenceEquals(w, wreck)) return false;
                if ((_position(w) - at).sqrMagnitude < SamePlace * SamePlace) repeat = true;
            }
            _started.Add(wreck);
            return repeat;
        }
    }
}
