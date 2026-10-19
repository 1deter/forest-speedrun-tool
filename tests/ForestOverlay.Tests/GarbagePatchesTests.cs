using ForestOverlay.Data;
using ForestOverlay.Game;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // T-0203: the pure parts of the four garbage patches - the terrain's
    // cell table and layer pick, the ocean key's match rule, and the new
    // FastField setter.
    // ------------------------------------------------------------------
    public class GarbagePatchesTests
    {
        private static float[,,] Weights(params float[] w)
        {
            float[,,] a = new float[1, 1, w.Length];
            for (int i = 0; i < w.Length; i++) a[0, 0, i] = w[i];
            return a;
        }

        // --- terrain ---------------------------------------------------

        [Fact]
        public void Heaviest_FirstMaximumWins()
        {
            Assert.Equal(1, TerrainCells.Heaviest(Weights(0.2f, 0.5f, 0.5f, 0.1f), 4));
        }

        [Fact]
        public void Heaviest_AllZero_IsMinusOne()
        {
            Assert.Equal(-1, TerrainCells.Heaviest(Weights(0f, 0f, 0f), 3));
        }

        [Fact]
        public void Heaviest_NaNIsNeverAbove()
        {
            Assert.Equal(2, TerrainCells.Heaviest(Weights(float.NaN, 0f, 0.3f), 3));
        }

        [Fact]
        public void Index_TruncatesTowardZero_LikeTheGame()
        {
            // 3500 m, 512 cells: 6.84 m a cell.
            Assert.Equal(0, TerrainCells.Index(-3f, 0f, 3500f, 512));
            Assert.Equal(1, TerrainCells.Index(7f, 0f, 3500f, 512));
            Assert.Equal(256, TerrainCells.Index(1750f, 0f, 3500f, 512));
            Assert.Equal(-1, TerrainCells.Index(-7f, 0f, 3500f, 512));
        }

        [Fact]
        public void InRange_EdgesAreOut()
        {
            Assert.True(TerrainCells.InRange(0, 0, 512));
            Assert.True(TerrainCells.InRange(511, 511, 512));
            Assert.False(TerrainCells.InRange(512, 0, 512));
            Assert.False(TerrainCells.InRange(0, -1, 512));
        }

        [Fact]
        public void Table_StartsUnread_KeepsWhatIsSet()
        {
            TerrainCells c = new TerrainCells();
            object data = new object();
            Assert.True(c.Bind(data, 8, 4));
            Assert.Equal((int)TerrainCells.Unread, c.Get(3, 5));
            c.Set(3, 5, 2);
            c.Set(5, 3, -1);
            Assert.Equal(2, c.Get(3, 5));
            Assert.Equal(-1, c.Get(5, 3));
            Assert.True(c.Bind(data, 8, 4));          // the same: kept
            Assert.Equal(2, c.Get(3, 5));
        }

        [Fact]
        public void Table_StartsOver_WhenDataResolutionOrLayersChange()
        {
            TerrainCells c = new TerrainCells();
            object data = new object();
            c.Bind(data, 8, 4);
            c.Set(1, 1, 3);
            c.Bind(new object(), 8, 4);
            Assert.Equal((int)TerrainCells.Unread, c.Get(1, 1));
            c.Bind(data, 8, 4);
            c.Set(1, 1, 3);
            c.Bind(data, 16, 4);
            Assert.Equal((int)TerrainCells.Unread, c.Get(1, 1));
            c.Set(1, 1, 3);
            c.Bind(data, 16, 5);
            Assert.Equal((int)TerrainCells.Unread, c.Get(1, 1));
        }

        [Fact]
        public void Table_ClearForgetsEveryCell()
        {
            TerrainCells c = new TerrainCells();
            c.Bind(new object(), 4, 2);
            c.Set(2, 2, 1);
            c.Clear();
            Assert.Equal((int)TerrainCells.Unread, c.Get(2, 2));
        }

        [Fact]
        public void Table_RefusesWhatItCannotHold()
        {
            TerrainCells c = new TerrainCells();
            object data = new object();
            Assert.False(c.Bind(null, 8, 4));
            Assert.False(c.Bind(data, 0, 4));
            Assert.False(c.Bind(data, TerrainCells.MaxResolution + 1, 4));
            Assert.False(c.Bind(data, 8, 0));
            Assert.False(c.Bind(data, 8, TerrainCells.MaxLayers + 1));
        }

        // --- ocean -----------------------------------------------------

        private static OceanKeyMemo Kept(int type)
        {
            OceanKeyMemo m = new OceanKeyMemo();
            m.Keep(Owner, "key", 64, 15f, 290f, 0.64f, type, 4);
            return m;
        }

        private static readonly object Owner = new object();

        [Fact]
        public void Ocean_SameInputs_ReuseTheKey()
        {
            OceanKeyMemo m = Kept(0);
            Assert.True(m.Matches(Owner, 64, 15f, 290f, 0.64f, 0, 4));
            Assert.Equal("key", m.Key);
        }

        [Fact]
        public void Ocean_AnyInputDifferent_RunsTheGame()
        {
            OceanKeyMemo m = Kept(0);
            Assert.False(m.Matches(Owner, 128, 15f, 290f, 0.64f, 0, 4));
            Assert.False(m.Matches(Owner, 64, 16f, 290f, 0.64f, 0, 4));
            Assert.False(m.Matches(Owner, 64, 15f, 291f, 0.64f, 0, 4));
            Assert.False(m.Matches(Owner, 64, 15f, 290f, 0.65f, 0, 4));
            Assert.False(m.Matches(Owner, 64, 15f, 290f, 0.64f, 2, 4));
            Assert.False(m.Matches(Owner, 64, 15f, 290f, 0.64f, 0, 3));
            Assert.False(m.Matches(new object(), 64, 15f, 290f, 0.64f, 0, 4));
        }

        [Fact]
        public void Ocean_NaNNeverMatches()
        {
            OceanKeyMemo m = new OceanKeyMemo();
            m.Keep(Owner, "key", 64, float.NaN, 290f, 0.64f, 0, 4);
            Assert.False(m.Matches(Owner, 64, float.NaN, 290f, 0.64f, 0, 4));
        }

        [Fact]
        public void Ocean_OnlyUnifiedSpectraAreKept()
        {
            Assert.True(OceanKeyMemo.Cacheable(0));
            Assert.True(OceanKeyMemo.Cacheable(2));
            Assert.False(OceanKeyMemo.Cacheable(1));
            Assert.False(OceanKeyMemo.Cacheable(3));
            OceanKeyMemo phillips = Kept(1);
            Assert.False(phillips.Matches(Owner, 64, 15f, 290f, 0.64f, 1, 4));
        }

        [Fact]
        public void Ocean_ClearForgets()
        {
            OceanKeyMemo m = Kept(0);
            m.Clear();
            Assert.False(m.Matches(Owner, 64, 15f, 290f, 0.64f, 0, 4));
        }

        // --- FastField setter ---------------------------------------------

        private sealed class Pond
        {
            public float Dist;
            public bool Spawned;
            public int Total;
            private int _hidden;
            public int Hidden { get { return _hidden; } }
        }

        [Fact]
        public void InstanceSetter_WritesPublicAndPrivateFields()
        {
            Pond p = new Pond();
            FastField.InstanceSetter<float>(typeof(Pond).GetField("Dist"))(p, 12.5f);
            FastField.InstanceSetter<bool>(typeof(Pond).GetField("Spawned"))(p, true);
            FastField.InstanceSetter<int>(typeof(Pond).GetField("Total"))(p, 7);
            FastField.InstanceSetter<int>(typeof(Pond).GetField("_hidden", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))(p, 3);
            Assert.Equal(12.5f, p.Dist);
            Assert.True(p.Spawned);
            Assert.Equal(7, p.Total);
            Assert.Equal(3, p.Hidden);
        }

        [Fact]
        public void InstanceSetter_NullForStaticOrMissing()
        {
            Assert.Null(FastField.InstanceSetter<int>(null));
        }
    }
}
