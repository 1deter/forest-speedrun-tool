using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Filtering for the collider / trigger debug views.
    //
    // The world is full of huge volumes - area bounds, cave load boxes,
    // terrain - that swamp the small hitboxes a runner is actually looking
    // for. Two filters: a size cap on a volume's largest side, and an
    // exclude list of name fragments matched against the GameObject name.
    //
    // Pure, so it is linked into the tests. Game/DebugDraw applies it.
    // ------------------------------------------------------------------
    public static class VolumeFilter
    {
        /// Splits the comma-separated exclude field into trimmed, non-empty
        /// fragments. Called when the text changes, not per frame.
        public static string[] ParseExclude(string text)
        {
            List<string> parts = new List<string>();
            if (string.IsNullOrEmpty(text)) return parts.ToArray();

            string[] raw = text.Split(',');
            for (int i = 0; i < raw.Length; i++)
            {
                string p = raw[i].Trim();
                if (p.Length > 0) parts.Add(p);
            }
            return parts.ToArray();
        }

        /// Case-insensitive substring match against any fragment.
        public static bool IsExcluded(string name, string[] exclude)
        {
            if (exclude == null || name == null) return false;
            for (int i = 0; i < exclude.Length; i++)
            {
                string e = exclude[i];
                if (e.Length > 0 && name.IndexOf(e, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// Adds a fragment to the comma-separated field, unless an existing
        /// fragment already covers it.
        public static string AddToExclude(string text, string name)
        {
            if (string.IsNullOrEmpty(name)) return text ?? "";
            string[] current = ParseExclude(text);
            if (IsExcluded(name, current)) return text;
            return current.Length == 0 ? name : string.Join(", ", current) + ", " + name;
        }
    }

    // ------------------------------------------------------------------
    // The N biggest volumes still drawn, largest first - the likely
    // candidates for the exclude list. Fixed arrays, no allocation per add.
    // A name already listed keeps only its biggest size, so one repeated
    // prefab cannot fill the whole list.
    // ------------------------------------------------------------------
    public sealed class LargestList
    {
        public readonly string[] Names;
        public readonly float[] Sizes;
        public int Count { get; private set; }

        public LargestList(int capacity)
        {
            Names = new string[capacity];
            Sizes = new float[capacity];
        }

        public int Capacity { get { return Names.Length; } }

        public void Clear()
        {
            for (int i = 0; i < Count; i++) Names[i] = null;
            Count = 0;
        }

        /// True if a volume this big could enter the list. Lets the caller
        /// skip reading a Unity object's name (which allocates) when not.
        public bool WouldRank(float size)
        {
            return Count < Capacity || size > Sizes[Count - 1];
        }

        public void Add(string name, float size)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Names[i] != name) continue;
                if (size <= Sizes[i]) return;
                RemoveAt(i);
                break;
            }

            int at = Count;
            while (at > 0 && Sizes[at - 1] < size) at--;
            if (at >= Capacity) return;

            int last = Count < Capacity ? Count : Capacity - 1;
            for (int j = last; j > at; j--)
            {
                Names[j] = Names[j - 1];
                Sizes[j] = Sizes[j - 1];
            }
            Names[at] = name;
            Sizes[at] = size;
            if (Count < Capacity) Count++;
        }

        private void RemoveAt(int i)
        {
            for (int j = i; j < Count - 1; j++)
            {
                Names[j] = Names[j + 1];
                Sizes[j] = Sizes[j + 1];
            }
            Count--;
            Names[Count] = null;
        }
    }
}
