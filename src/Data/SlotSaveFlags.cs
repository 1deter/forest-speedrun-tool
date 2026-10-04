namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Where a save slot's player was: in the endgame, in a cave - read
    // from the level data's ActiveAreaInfo, without the game's deserializer
    // (a Reload in place decides before it restores, Modules/DeathModule).
    //
    // UnitySerializer's binary (checked on 28 savestates and Slot 1,
    // 2026-10-04; every header's cave / endgame line agreed): a component's
    // data is "SerV10", the type's name, an int32 field count, the field
    // names (a length byte each), then per field its index (uint16) and
    // FF FF, then the value - a bool is 'Y' or 'N':
    //
    //   1F "TheForest.Player.ActiveAreaInfo" 05 00 00 00
    //   0F "_activeAreaHash" 0C "_isInEndgame" 0A "_isInCaves" ...
    //   ... 01 00 FF FF 'Y'   02 00 FF FF 'N' ...
    //
    // Anything else (no block, two blocks that disagree, a field missing)
    // is "unknown" - the caller then takes the game's own load.
    // ------------------------------------------------------------------
    public static class SlotSaveFlags
    {
        private const string TypeName = "TheForest.Player.ActiveAreaInfo";
        private const int ValueWindow = 96;

        /// False when the flags cannot be read.
        public static bool TryRead(byte[] level, out bool inEndgame, out bool inCaves)
        {
            inEndgame = false;
            inCaves = false;
            if (level == null || level.Length < TypeName.Length + 8) return false;

            bool found = false;
            int from = 0;
            while (true)
            {
                int at = IndexOfName(level, from);
                if (at < 0) break;
                from = at + 1;

                bool e, c;
                if (!ReadBlock(level, at + 1 + TypeName.Length, out e, out c)) continue;
                if (found && (e != inEndgame || c != inCaves)) return false;   // two blocks disagree
                inEndgame = e;
                inCaves = c;
                found = true;
            }
            return found;
        }

        // `at`: just after the type name - the field count, the names, the values.
        private static bool ReadBlock(byte[] b, int at, out bool inEndgame, out bool inCaves)
        {
            inEndgame = false;
            inCaves = false;
            if (at + 4 > b.Length) return false;
            int count = b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24);
            if (count < 1 || count > 32) return false;
            int p = at + 4;

            int endgameIndex = -1, cavesIndex = -1;
            for (int i = 0; i < count; i++)
            {
                if (p >= b.Length) return false;
                int len = b[p];
                if (len == 0 || len > 127 || p + 1 + len > b.Length) return false;
                if (Matches(b, p + 1, len, "_isInEndgame")) endgameIndex = i;
                else if (Matches(b, p + 1, len, "_isInCaves")) cavesIndex = i;
                p += 1 + len;
            }
            if (endgameIndex < 0 || cavesIndex < 0) return false;

            int e = BoolValue(b, p, endgameIndex);
            int c = BoolValue(b, p, cavesIndex);
            if (e < 0 || c < 0) return false;
            inEndgame = e == 1;
            inCaves = c == 1;
            return true;
        }

        // 1 / 0 for 'Y' / 'N' after "<index> 00 FF FF", -1 when not found.
        private static int BoolValue(byte[] b, int from, int index)
        {
            int end = from + ValueWindow;
            if (end > b.Length - 5) end = b.Length - 5;
            for (int i = from; i <= end; i++)
            {
                if (b[i] != (byte)(index & 0xFF) || b[i + 1] != (byte)(index >> 8) ||
                    b[i + 2] != 0xFF || b[i + 3] != 0xFF) continue;
                if (b[i + 4] == (byte)'Y') return 1;
                if (b[i + 4] == (byte)'N') return 0;
            }
            return -1;
        }

        // The type name with its length byte in front.
        private static int IndexOfName(byte[] b, int from)
        {
            int n = TypeName.Length;
            for (int i = from; i + 1 + n <= b.Length; i++)
            {
                if (b[i] != n) continue;
                if (Matches(b, i + 1, n, TypeName)) return i;
            }
            return -1;
        }

        private static bool Matches(byte[] b, int at, int len, string s)
        {
            if (len != s.Length || at + len > b.Length) return false;
            for (int i = 0; i < len; i++)
                if (b[at + i] != (byte)s[i]) return false;
            return true;
        }
    }
}
