using System.Collections.Generic;
using System.Text;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // A save's endgame / cave flags, read from UnitySerializer's binary.
    // The block below is the real ActiveAreaInfo data of Slot 1 (saved in
    // the lab: Y, Y) and of the savestates on the surface (N, N), in a lab
    // capture outside the caves (phantom-a: Y, N), and Cave 6 (tom-c6:
    // N, Y, current cave 5, a hash) - 2026-10-04.
    // ------------------------------------------------------------------
    public class SlotSaveFlagsTests
    {
        private static void Add(List<byte> b, params int[] v) { foreach (int x in v) b.Add((byte)x); }

        private static void Name(List<byte> b, string s)
        {
            b.Add((byte)s.Length);
            b.AddRange(Encoding.ASCII.GetBytes(s));
        }

        private static byte[] Block(char endgame, char caves, bool hash = false, int cave = -1)
        {
            List<byte> b = new List<byte>();
            Add(b, 0x24, 0x38, 0x66, 0x36);                        // the end of the previous object's id
            Add(b, 0xAA, 0, 0, 0, 0xAA, 0, 0, 0);
            Name(b, "SerV10");
            Add(b, 0, 1, 0, 0, 0);
            Name(b, "TheForest.Player.ActiveAreaInfo");
            Add(b, 5, 0, 0, 0);
            Name(b, "_activeAreaHash");
            Name(b, "_isInEndgame");
            Name(b, "_isInCaves");
            Name(b, "_currentCave");
            Name(b, "IsLeavingCaves");
            Add(b, (byte)'2', 0, 0, 0, 0, 0, (byte)'O', 4, 0);
            Add(b, 0, 0, 0xFF, 0xFF);
            if (hash) Add(b, 0xB6, 0x88, 0xD1, 0x04, (byte)'n', (byte)'_', 0x11, 0x00);
            else Add(b, 0, 0, 0, 0, 0, 0, 0, 0x80);
            Add(b, 1, 0, 0xFF, 0xFF, endgame);
            Add(b, 2, 0, 0xFF, 0xFF, caves);
            Add(b, 3, 0, 0xFF, 0xFF, cave & 0xFF, (cave >> 8) & 0xFF, (cave >> 16) & 0xFF, (cave >> 24) & 0xFF, 3);
            Add(b, 4, 0, 0xFF, 0xFF, (byte)'N');
            // The type's name again as a reference elsewhere in the save.
            Add(b, 0x1F);
            b.AddRange(Encoding.ASCII.GetBytes("TheForest.Player.ActiveAreaInfo"));
            Add(b, 0xFF, 0xFF, 0x10);
            b.AddRange(Encoding.ASCII.GetBytes("StoreInformation"));
            return b.ToArray();
        }

        private static void Read(byte[] b, bool endgame, bool caves)
        {
            bool e, c;
            Assert.True(SlotSaveFlags.TryRead(b, out e, out c));
            Assert.Equal(endgame, e);
            Assert.Equal(caves, c);
        }

        [Fact]
        public void ReadsTheFourCombinations()
        {
            Read(Block('Y', 'Y'), true, true);           // Slot 1, the lab
            Read(Block('N', 'N'), false, false);         // the surface
            Read(Block('Y', 'N'), true, false);          // phantom-a
            Read(Block('N', 'Y', false, 5), false, true); // Cave 6
            Read(Block('Y', 'Y', true), true, true);     // Megan's room, an area hash set
        }

        [Fact]
        public void UnknownWhenTheBlockIsMissingOrCut()
        {
            bool e, c;
            Assert.False(SlotSaveFlags.TryRead(null, out e, out c));
            Assert.False(SlotSaveFlags.TryRead(Encoding.ASCII.GetBytes("no such block here, just text"), out e, out c));
            byte[] full = Block('Y', 'Y');
            byte[] cut = new byte[full.Length - 70];
            System.Array.Copy(full, cut, cut.Length);
            Assert.False(SlotSaveFlags.TryRead(cut, out e, out c));
            Assert.False(SlotSaveFlags.TryRead(Block('?', 'Y'), out e, out c));
        }

        [Fact]
        public void TwoBlocksThatDisagreeAreUnknown()
        {
            List<byte> both = new List<byte>(Block('Y', 'Y'));
            both.AddRange(Block('Y', 'Y'));
            Read(both.ToArray(), true, true);
            both = new List<byte>(Block('Y', 'Y'));
            both.AddRange(Block('N', 'Y'));
            bool e, c;
            Assert.False(SlotSaveFlags.TryRead(both.ToArray(), out e, out c));
        }
    }
}
