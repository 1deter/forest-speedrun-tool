using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class HudLayoutTests
    {
        [Fact]
        public void RoundTrips()
        {
            HudLayout a = new HudLayout();
            a.Detach("ShowSpeed", 24.5f, 80f).Scale = 2.5f;
            HudWidgetLayout t = a.Detach("ShowRunTimer", 700f, 40f);
            t.ShowLabel = false;

            HudLayout b = HudLayout.Parse(a.Format());
            Assert.Equal(2, b.Widgets.Count);
            HudWidgetLayout s = b.Find("ShowSpeed");
            Assert.Equal(24.5f, s.X);
            Assert.Equal(2.5f, s.Scale);
            Assert.True(s.ShowLabel);
            Assert.False(b.Find("ShowRunTimer").ShowLabel);
        }

        [Fact]
        public void EmptyAndGarbageGiveAnEmptyLayout()
        {
            Assert.Empty(HudLayout.Parse(null).Widgets);
            Assert.Empty(HudLayout.Parse("").Widgets);
            Assert.Empty(HudLayout.Parse("nonsense\n: free\n# comment\nShowSpeed x=1").Widgets);
        }

        [Fact]
        public void OnlyFreeLinesCountAndNumbersAreClamped()
        {
            HudLayout l = HudLayout.Parse("ShowSpeed: x=5, y=5\nShowPosition: free, scale=99, x=nan, y=7, wat=1\n");
            Assert.Single(l.Widgets);
            Assert.Equal("ShowPosition", l.Widgets[0].Key);
            Assert.Equal(HudLayout.MaxScale, l.Widgets[0].Scale);
            Assert.Equal(0f, l.Widgets[0].X);
            Assert.Equal(7f, l.Widgets[0].Y);
        }

        [Fact]
        public void FirstLineOfAKeyWinsAndCrLfIsFine()
        {
            HudLayout l = HudLayout.Parse("ShowSpeed: free, x=1\r\nShowSpeed: free, x=2\r\n");
            Assert.Single(l.Widgets);
            Assert.Equal(1f, l.Widgets[0].X);
        }

        [Fact]
        public void DetachAndAttach()
        {
            HudLayout l = new HudLayout();
            l.Detach("ShowSpeed", 1, 2);
            l.Detach("ShowSpeed", 3, 4);
            Assert.Single(l.Widgets);
            Assert.Equal(3f, l.Find("ShowSpeed").X);
            Assert.True(l.Attach("ShowSpeed"));
            Assert.False(l.Attach("ShowSpeed"));
            Assert.Null(l.Find("ShowSpeed"));
        }

        [Fact]
        public void ScaleFromDragSnapsAndClamps()
        {
            Assert.Equal(2f, HudLayout.ScaleFromDrag(1f, 100f, 200f));
            Assert.Equal(1.25f, HudLayout.ScaleFromDrag(1f, 100f, 123f));
            Assert.Equal(HudLayout.MinScale, HudLayout.ScaleFromDrag(1f, 100f, 1f));
            Assert.Equal(HudLayout.MaxScale, HudLayout.ScaleFromDrag(2f, 100f, 9000f));
            Assert.Equal(1.5f, HudLayout.ScaleFromDrag(1.5f, 0f, 50f));
        }
    }
}
