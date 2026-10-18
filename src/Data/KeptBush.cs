namespace ForestOverlay.Data
{
    /// A bush / sapling Game/NatureKeeper keeps a copy of, across captures
    /// and Quick loads (T-0419). A cut deletes the bush at once: its copy is
    /// numbered then. A fire does not: the bush stands, burning, until the
    /// game deletes it when the burn ends (or never, when the fire is put
    /// out). So a burnt bush counts as gone at a capture only once the game
    /// has deleted it: until then its number is `Standing`, it is not in
    /// the `cutbushes` line, and a Quick load to a capture it stood at
    /// brings it back. Pure.
    public static class KeptBush
    {
        /// Not gone at any capture since it was lit.
        public const int Standing = int.MaxValue;

        public enum Fate
        {
            Wait,   // still standing and burning: decide later
            Cut,    // gone: number it now (a capture) and list it as cut
            Drop,   // standing and no longer burning: nothing to keep
            Back,   // a Quick load puts the copy back
            Stay,   // gone before the capture: stays gone
        }

        /// At a capture, for a lit bush not yet numbered: `gone` = the game
        /// deleted it, `burning` = its fire is still on.
        public static Fate AtCapture(bool gone, bool burning)
        {
            if (gone) return Fate.Cut;
            return burning ? Fate.Wait : Fate.Drop;
        }

        /// At a Quick load, for a kept copy numbered `seq` (Standing: lit
        /// and not gone at any capture since), against the file's `since`
        /// (-1: another world, as a load - every gone bush comes back).
        public static Fate AtRestore(int seq, int since, bool gone, bool burning)
        {
            if (!gone) return seq == Standing && burning ? Fate.Wait : Fate.Drop;
            if (seq == Standing) return Fate.Back;   // gone after every capture
            return seq <= since ? Fate.Stay : Fate.Back;
        }
    }
}
