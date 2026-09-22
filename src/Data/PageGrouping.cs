using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Works out which book page each entry sits on, from the scene
    // hierarchy alone.
    //
    // The game never says "this entry is on the Birds page"; it only
    // gives each entry a tick-mark GameObject drawn on that page. The
    // pages share a parent, so the lowest common ancestor of every tick
    // is the page container, and each tick's ancestor one level below it
    // is its page. That is structural rather than name-based, so it
    // survives the game renaming its page objects.
    //
    // Chains are root-first ids of each tick's ancestors (the tick
    // itself last). Pure so it can be tested without Unity.
    // ------------------------------------------------------------------
    public static class PageGrouping
    {
        public const int NoPage = 0;

        /// For each chain, the id of its page ancestor, or NoPage when the
        /// chain is empty or the ticks do not branch below a common root.
        public static int[] PageIds(IList<int[]> chains)
        {
            int[] result = new int[chains.Count];

            int depth = CommonDepth(chains);   // length of the shared prefix
            if (depth < 0) return result;

            for (int i = 0; i < chains.Count; i++)
            {
                int[] c = chains[i];
                if (c == null || c.Length == 0) continue;

                // A tick sitting directly in the container has no page of
                // its own; it is its own group.
                result[i] = depth < c.Length ? c[depth] : c[c.Length - 1];
            }

            return result;
        }

        /// Length of the prefix shared by every non-empty chain, or -1 when
        /// there is none to share. A single chain has no branching, so its
        /// page is taken as its tick's parent.
        private static int CommonDepth(IList<int[]> chains)
        {
            int[] first = null;
            int count = 0;

            for (int i = 0; i < chains.Count; i++)
            {
                if (chains[i] == null || chains[i].Length == 0) continue;
                if (first == null) first = chains[i];
                count++;
            }

            if (first == null) return -1;
            if (count == 1) return first.Length >= 2 ? first.Length - 2 : 0;

            int depth = first.Length;

            for (int i = 0; i < chains.Count; i++)
            {
                int[] c = chains[i];
                if (c == null || c.Length == 0) continue;

                int n = 0;
                int max = depth < c.Length ? depth : c.Length;
                while (n < max && c[n] == first[n]) n++;
                depth = n;
            }

            return depth;
        }
    }
}
