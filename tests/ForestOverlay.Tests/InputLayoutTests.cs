using System;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class InputLayoutTests
    {
        [Fact]
        public void FreeSlot_IsOnScreenAndNeverOverlaps()
        {
            InputLayout l = InputLayout.Default(1366, 768);
            string[] keys = { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "Z", "X", "Wheel" };
            for (int i = 0; i < keys.Length; i++)
            {
                float x, y;
                l.FreeSlot(keys[i], 1366, 768, out x, out y);
                float w = InputLayout.TileW(keys[i], 1f), h = InputLayout.TileH(keys[i], 1f);
                Assert.True(x >= 0 && y >= 0 && x + w <= 1366 && y + h <= 768);
                for (int j = 0; j < l.Tiles.Count; j++)
                {
                    InputTile t = l.Tiles[j];
                    bool overlap = x < t.X + InputLayout.TileW(t.Key, t.Scale) && t.X < x + w
                        && y < t.Y + InputLayout.TileH(t.Key, t.Scale) && t.Y < y + h;
                    Assert.False(overlap, keys[i]);
                }
                if (i == 0) Assert.Equal(768 - 23.04f - h, y, 1);
                if (i == 0) Assert.Equal(1366 - InputLayout.RingClear - w, x, 1);
                Assert.NotNull(l.Add(keys[i], x, y));
            }
        }

        [Fact]
        public void DefaultHasTheAuthorsInputsAndRoundTrips()
        {
            InputLayout d = InputLayout.Default();
            string[] keys = { "W", "A", "S", "D", "Space", "LeftShift", "LeftControl", "Mouse0", "Mouse1", InputLayout.MouseKey };
            for (int i = 0; i < keys.Length; i++) Assert.NotNull(d.Find(keys[i]));
            Assert.Equal(keys.Length, d.Tiles.Count);

            InputLayout b = InputLayout.Parse(d.Format());
            Assert.Equal(d.Format(), b.Format());
            Assert.Equal(Math.Round(d.Find("Space").X), b.Find("Space").X);
        }

        [Fact]
        public void EachTileKeepsItsOwnPlaceSizeAndSwitch()
        {
            InputLayout l = new InputLayout();
            l.Add("E", 500f, 20f).Scale = 2.5f;
            l.Add("Q", 12f, 700f).Shown = false;
            InputLayout b = InputLayout.Parse(l.Format());
            Assert.Equal(500f, b.Find("E").X);
            Assert.Equal(20f, b.Find("E").Y);
            Assert.Equal(2.5f, b.Find("E").Scale);
            Assert.False(b.Find("Q").Shown);
            Assert.True(b.Find("E").Shown);
        }

        [Fact]
        public void BadInputIsSkippedAndNumbersClamped()
        {
            InputLayout b = InputLayout.Parse("W,10,20,99,1;;bad key,1,2;X,nan,3;E,5,6;E,7,8;Q,-5,6,0.01,1;,1,2");
            Assert.Equal(3, b.Tiles.Count);
            Assert.Equal(InputLayout.MaxScale, b.Find("W").Scale);
            Assert.Equal(5f, b.Find("E").X);   // the second E is dropped
            Assert.Equal(0f, b.Find("Q").X);
            Assert.Equal(InputLayout.MinScale, b.Find("Q").Scale);
            Assert.Empty(InputLayout.Parse(null).Tiles);
            Assert.Empty(InputLayout.Parse("garbage").Tiles);
        }

        [Fact]
        public void AddRefusesADuplicateAndRemoveTakesItOut()
        {
            InputLayout l = new InputLayout();
            Assert.NotNull(l.Add("W", 0f, 0f));
            Assert.Null(l.Add("W", 5f, 5f));
            Assert.Null(l.Add("a b", 5f, 5f));
            Assert.True(l.Remove("W"));
            Assert.False(l.Remove("W"));
        }

        [Fact]
        public void LabelsAreShortAndWideKeysAreWide()
        {
            Assert.Equal("Shift", InputLayout.LabelFor("LeftShift"));
            Assert.Equal("M1", InputLayout.LabelFor("Mouse0"));
            Assert.Equal("1", InputLayout.LabelFor("Alpha1"));
            Assert.Equal("W", InputLayout.LabelFor("W"));
            Assert.Equal(3f, InputLayout.UnitsW("Space"));
            Assert.Equal(1f, InputLayout.UnitsW("W"));
            Assert.Equal(2f, InputLayout.UnitsH(InputLayout.MouseKey));
        }

        [Theory]
        [InlineData(1366, 768)]
        [InlineData(1920, 1080)]
        [InlineData(2560, 1440)]
        public void DefaultFitsAnyScreenWithNoOverlap(int w, int h)
        {
            InputLayout d = InputLayout.Default(w, h);
            Assert.Equal(10, d.Tiles.Count);
            for (int i = 0; i < d.Tiles.Count; i++)
            {
                InputTile a = d.Tiles[i];
                float aw = InputLayout.TileW(a.Key, a.Scale), ah = InputLayout.TileH(a.Key, a.Scale);
                Assert.True(a.X >= 0f && a.Y >= 0f && a.X + aw <= w && a.Y + ah <= h, a.Key + " is off the screen");
                for (int j = i + 1; j < d.Tiles.Count; j++)
                {
                    InputTile b = d.Tiles[j];
                    float bw = InputLayout.TileW(b.Key, b.Scale), bh = InputLayout.TileH(b.Key, b.Scale);
                    bool apart = a.X + aw <= b.X || b.X + bw <= a.X || a.Y + ah <= b.Y || b.Y + bh <= a.Y;
                    Assert.True(apart, a.Key + " overlaps " + b.Key);
                }
            }
        }

        [Theory]
        [InlineData(1366, 768)]
        [InlineData(1920, 1080)]
        [InlineData(2560, 1440)]
        public void DefaultSitsBottomCentreClearOfTheGamesMessages(int w, int h)
        {
            // The game's bottom-left messages (HudGui.Grid) and the always-on
            // lines live in the corner (T-0325).
            InputLayout d = InputLayout.Default(w, h);
            float left = float.MaxValue, right = 0f;
            for (int i = 0; i < d.Tiles.Count; i++)
            {
                InputTile t = d.Tiles[i];
                left = Math.Min(left, t.X);
                right = Math.Max(right, t.X + InputLayout.TileW(t.Key, t.Scale));
            }
            Assert.True(left > w * 0.25f, "the corner is not free: " + left);
            Assert.True(Math.Abs((left + right) * 0.5f - w * 0.5f) < 1f, "not centred");
        }

        [Theory]
        [InlineData(1366, 768)]
        [InlineData(1920, 1080)]
        public void OnlyAnUntouchedOldDefaultMoves(int w, int h)
        {
            // The pre-T-0325 corner layout at 1366x768 (Ctrl at 243: one at 248 was moved by hand).
            string old = "W,75,605,1,1;A,27,653,1,1;S,75,653,1,1;D,123,653,1,1;LeftShift,27,701,1,1;Space,99,701,1,1;" +
                         "LeftControl,243,701,1,1;Mouse0,363,605,1,1;Mouse1,411,605,1,1;Mouse,363,653,1,1";
            if (w == 1366) Assert.True(InputLayout.IsOldDefault(old, w, h));
            Assert.False(InputLayout.IsOldDefault(InputLayout.Default(w, h).Format(), w, h));
            Assert.False(InputLayout.IsOldDefault(old.Replace("W,75,605", "W,80,605"), 1366, 768));   // a runner moved W
            Assert.False(InputLayout.IsOldDefault(old.Replace(";Mouse,363,653,1,1", ""), 1366, 768)); // a tile removed
            Assert.False(InputLayout.IsOldDefault("", w, h));
        }

        [Fact]
        public void LongLabelsGetWiderTilesSoNothingClips()
        {
            Assert.True(InputLayout.UnitsW("Wheel") > 1f);
            Assert.True(InputLayout.UnitsW("LeftBracket") > InputLayout.UnitsW("PageDown"));
            Assert.Equal(1f, InputLayout.UnitsW("Mouse0"));
        }

        [Fact]
        public void PositionsConvertToAnotherReferenceScreen()
        {
            string s = InputLayout.ConvertPositions("W,100,200,1,1;E,5,5,2,1;Q,0,50,1,0", 1920, 1080, 3840, 2160);
            InputLayout l = InputLayout.Parse(s);
            Assert.Equal(200f, l.Find("W").X);
            Assert.Equal(400f, l.Find("W").Y);
            Assert.Equal(2f, l.Find("W").Scale);   // sizes follow the height ratio
            Assert.Equal(InputLayout.MaxScale, l.Find("E").Scale);   // clamped
            Assert.False(l.Find("Q").Shown);
            Assert.Equal(100f, l.Find("Q").Y);
        }
    }
}
