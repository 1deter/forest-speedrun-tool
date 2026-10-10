namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // When a restore's forced garbage collection is worth running (T-0202).
    // The game's loader collects at the end of every in-place load; a
    // restart loop then pays a full collection (~100 ms over a ~280 MB
    // heap) every restore for ~30 MB of garbage. Kept inside the restore -
    // where a hitch already is - but only once enough garbage has built up
    // since the last collection: the budget sits well under the volume the
    // collector waits for by itself (~100-200 MB here, game-notes
    // *Performance*), so restores alone never leave a collection due
    // for the middle of play. Pure, so it is tested; Game/LoaderCollect
    // reads the heap.
    // ------------------------------------------------------------------
    public static class GarbageBudget
    {
        /// The garbage a restore may leave before its collection runs.
        public const long Bytes = 64L * 1024 * 1024;

        /// `heapNow`: the heap in use now (garbage included);
        /// `heapAfterLast`: in use just after the last collection, 0 when
        /// none was seen yet (then collect, as the game does).
        public static bool Due(long heapNow, long heapAfterLast, long budget)
        {
            if (heapAfterLast <= 0) return true;
            return heapNow - heapAfterLast >= budget;
        }
    }
}
