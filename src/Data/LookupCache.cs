using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The rules of Game/SceneCache, pure so they are tested (T-0148): a
    // lookup's answer is kept and given again while every object in it is
    // still usable (alive, active); otherwise the search runs again.
    // Nothing found is never kept - absence is searched every time - and
    // Clear() (a scene loaded or unloaded) forgets everything.
    // ------------------------------------------------------------------
    public sealed class LookupCache<TKey, T> where T : class
    {
        private readonly Func<T, bool> _usable;
        private readonly Dictionary<TKey, T> _ones = new Dictionary<TKey, T>();
        private readonly Dictionary<TKey, T[]> _alls = new Dictionary<TKey, T[]>();

        /// Searches run, answers given from the cache.
        public int Searches { get; private set; }
        public int Hits { get; private set; }

        public LookupCache(Func<T, bool> usable)
        {
            _usable = usable;
        }

        public void Clear()
        {
            _ones.Clear();
            _alls.Clear();
        }

        /// One object; null when the search finds none (not kept).
        public T One(TKey key, Func<TKey, T> search)
        {
            T kept;
            if (_ones.TryGetValue(key, out kept) && _usable(kept)) { Hits++; return kept; }
            Searches++;
            T found = search(key);
            if (found != null && _usable(found)) _ones[key] = found;
            else _ones.Remove(key);
            return found;
        }

        /// Every object; the array is the cache's own - read it, never
        /// change it. An empty answer is not kept.
        public T[] All(TKey key, Func<TKey, T[]> search)
        {
            T[] kept;
            if (_alls.TryGetValue(key, out kept) && AllUsable(kept)) { Hits++; return kept; }
            Searches++;
            T[] found = search(key) ?? new T[0];
            if (found.Length > 0) _alls[key] = found;
            else _alls.Remove(key);
            return found;
        }

        private bool AllUsable(T[] all)
        {
            if (all.Length == 0) return false;
            for (int i = 0; i < all.Length; i++)
                if (!_usable(all[i])) return false;
            return true;
        }
    }
}
