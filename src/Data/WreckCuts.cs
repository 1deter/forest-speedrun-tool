using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The rule of Game/WreckNav, pure so it is tested (T-0278): an
    // in-place restore makes a new plane wreck where the old one stands,
    // and both the new one's nav cut and the old one's removal recompute
    // the navmesh over the same box with the same cutter in it - the same
    // navmesh. Each is skipped only when a wreck at the same pose (place
    // and turn) already has its cut in the navmesh:
    //
    // - a new cut: a live twin whose own cut is in - it was skipped the
    //   same way (Inherited) or the game queued it and the game's list no
    //   longer holds it (Queued, processed). A twin still waiting, or cut
    //   by the game's other route (Other), does not count.
    // - a removal: only of a wreck the overlay removes (Removing), and only
    //   while a live twin at the same pose has been cut (any route): the
    //   twin's cutter stays where the removed one was.
    //
    // No twin: the game's own update runs (a load's first wreck, a restore
    // from another save with the wreck elsewhere).
    // ------------------------------------------------------------------
    public sealed class WreckCuts<T> where T : class
    {
        public const float SamePlace = 0.01f;   // m
        public const float SameTurn = 0.002f;   // between unit axes, ~0.1 degree

        public enum Cut { Queued, Inherited, Other }

        private sealed class Entry
        {
            public T Wreck;
            public Cut Cut;
        }

        private readonly Func<T, bool> _alive;
        private readonly Func<T, Vector3> _position, _forward, _up;
        private readonly List<Entry> _wrecks = new List<Entry>();
        private readonly List<T> _removing = new List<T>();

        public WreckCuts(Func<T, bool> alive, Func<T, Vector3> position, Func<T, Vector3> forward, Func<T, Vector3> up)
        {
            _alive = alive;
            _position = position;
            _forward = forward;
            _up = up;
        }

        public int Count { get { return _wrecks.Count; } }

        public void Clear()
        {
            _wrecks.Clear();
            _removing.Clear();
        }

        /// The game's batched nav cut starts for `wreck`; `pending` says
        /// whether a wreck's queued cut still waits in the game's list.
        /// Returns the twin whose cut covers it (skip the game's), or null
        /// (the game's runs). Remembers it either way.
        public T NewCut(T wreck, Func<T, bool> pending)
        {
            Forget(wreck);
            T twin = null;
            for (int i = 0; i < _wrecks.Count; i++)
            {
                Entry e = _wrecks[i];
                if (e.Cut == Cut.Other || (e.Cut == Cut.Queued && pending(e.Wreck))) continue;
                if (_removing.Contains(e.Wreck) || !SamePose(e.Wreck, wreck)) continue;
                twin = e.Wreck;
                break;
            }
            _wrecks.Add(new Entry { Wreck = wreck, Cut = twin != null ? Cut.Inherited : Cut.Queued });
            return twin;
        }

        /// The game's other (one-at-a-time) nav cut starts for `wreck`: it
        /// runs as the game's; remembered so a removal can lean on it.
        public void OtherCut(T wreck)
        {
            Forget(wreck);
            _wrecks.Add(new Entry { Wreck = wreck, Cut = Cut.Other });
        }

        /// The overlay is about to remove `wreck`.
        public void Removing(T wreck)
        {
            if (!_removing.Contains(wreck)) _removing.Add(wreck);
        }

        /// The game queues the nav removal of `wreck` (its OnDestroy):
        /// returns the live twin that keeps the cut (skip the removal), or
        /// null (the game's runs). The same answer however often asked.
        public T Removal(T wreck)
        {
            if (!_removing.Contains(wreck)) return null;
            Prune(wreck);
            for (int i = 0; i < _wrecks.Count; i++)
            {
                T w = _wrecks[i].Wreck;
                if (ReferenceEquals(w, wreck) || _removing.Contains(w)) continue;
                if (SamePose(w, wreck)) return w;
            }
            return null;
        }

        private void Forget(T wreck)
        {
            Prune(null);
            for (int i = _wrecks.Count - 1; i >= 0; i--)
                if (ReferenceEquals(_wrecks[i].Wreck, wreck)) _wrecks.RemoveAt(i);
        }

        // Drops dead wrecks; `keep` (one being destroyed) stays.
        private void Prune(T keep)
        {
            for (int i = _wrecks.Count - 1; i >= 0; i--)
                if (!ReferenceEquals(_wrecks[i].Wreck, keep) && !_alive(_wrecks[i].Wreck)) _wrecks.RemoveAt(i);
            for (int i = _removing.Count - 1; i >= 0; i--)
                if (!ReferenceEquals(_removing[i], keep) && !_alive(_removing[i])) _removing.RemoveAt(i);
        }

        private bool SamePose(T a, T b)
        {
            return (_position(a) - _position(b)).sqrMagnitude < SamePlace * SamePlace &&
                   (_forward(a) - _forward(b)).sqrMagnitude < SameTurn * SameTurn &&
                   (_up(a) - _up(b)).sqrMagnitude < SameTurn * SameTurn;
        }
    }
}
