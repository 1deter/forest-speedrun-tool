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
            a.Detach("ShowRunTimer", 700f, 40f);
            a.SetText("ShowSpeed", "", " u/s");
            a.SetText("ShowPosition", "Pos ", "");

            HudLayout b = HudLayout.Parse(a.Format());
            Assert.Equal(3, b.Widgets.Count);
            HudWidgetLayout s = b.Find("ShowSpeed");
            Assert.True(s.Free);
            Assert.Equal(24.5f, s.X);
            Assert.Equal(2.5f, s.Scale);
            Assert.Equal("", s.Before);
            Assert.Equal(" u/s", s.After);
            Assert.True(b.Find("ShowRunTimer").Free);
            HudWidgetLayout p = b.Find("ShowPosition");
            Assert.False(p.Free);
            Assert.Equal("Pos ", p.Before);
        }

        [Fact]
        public void DockedRowsKeepTheOrderTheyWereDockedIn()
        {
            HudLayout a = new HudLayout();
            a.SetText("ShowPosition", "Pos ", "");   // listed first, docked last
            a.Dock("ShowSpeed");
            a.Dock("ShowRunTimer");
            a.Dock("ShowPosition");
            HudLayout b = HudLayout.Parse(a.Format());
            System.Collections.Generic.List<string> keys = new System.Collections.Generic.List<string>();
            b.DockedKeys(keys);
            Assert.Equal(new[] { "ShowSpeed", "ShowRunTimer", "ShowPosition" }, keys.ToArray());
            b.Attach("ShowSpeed");
            b.Dock("ShowSpeed");   // docked again: last
            b.DockedKeys(keys);
            Assert.Equal(new[] { "ShowRunTimer", "ShowPosition", "ShowSpeed" }, keys.ToArray());
        }

        [Fact]
        public void BareDockedFromAnOlderFileComesFirstInFileOrder()
        {
            HudLayout l = HudLayout.Parse("ShowSpeed: docked\nShowPosition: docked\nShowRunTimer: docked=1\n");
            System.Collections.Generic.List<string> keys = new System.Collections.Generic.List<string>();
            l.DockedKeys(keys);
            Assert.Equal(new[] { "ShowSpeed", "ShowPosition", "ShowRunTimer" }, keys.ToArray());
        }

        [Fact]
        public void ADockedValueStaysInTheColumnWhileThePanelIsNotOnScreen()
        {
            Assert.False(HudLayout.LeavesColumn(false, true, false));
            Assert.True(HudLayout.LeavesColumn(false, true, true));
            Assert.True(HudLayout.LeavesColumn(true, false, false));
            Assert.False(HudLayout.LeavesColumn(false, false, true));
        }

        [Fact]
        public void DockedValueRoundTripsAndExcludesFree()
        {
            HudLayout a = new HudLayout();
            Assert.True(a.Dock("ShowSpeed"));
            Assert.False(a.Dock("ShowSpeed"));
            a.Detach("ShowPosition", 5f, 6f);
            a.Dock("ShowPosition");
            Assert.False(a.Find("ShowPosition").Free);
            HudLayout b = HudLayout.Parse(a.Format());
            Assert.True(b.IsDocked("ShowSpeed"));
            Assert.True(b.IsDocked("ShowPosition"));
            Assert.True(b.Attach("ShowSpeed"));
            Assert.Null(b.Find("ShowSpeed"));
            b.Detach("ShowPosition", 1f, 2f);
            Assert.False(b.IsDocked("ShowPosition"));
            Assert.False(HudLayout.Parse("ShowSpeed: free, x=1, y=2, docked").Find("ShowSpeed").Docked);
        }

        [Fact]
        public void TextWithCommasQuotesAndBackslashesRoundTrips()
        {
            HudLayout a = new HudLayout();
            a.SetText("ShowSpeed", "a, \"b\" = c\\", ", x=1, free");
            HudLayout b = HudLayout.Parse(a.Format());
            HudWidgetLayout s = b.Find("ShowSpeed");
            Assert.False(s.Free);
            Assert.Equal(0f, s.X);
            Assert.Equal("a, \"b\" = c\\", s.Before);
            Assert.Equal(", x=1, free", s.After);
        }

        [Fact]
        public void TextIsOneLineAndCapped()
        {
            Assert.Equal("a b c", HudLayout.CleanText("a\nb\tc"));
            Assert.Equal(HudLayout.MaxText, HudLayout.CleanText(new string('x', 100)).Length);
            Assert.Equal("", HudLayout.CleanText(null));
            // A quote left open by a hand edit: read as typed, the next line skipped, no crash.
            HudLayout l = HudLayout.Parse("ShowSpeed: before=\"x\ny\"");
            Assert.Single(l.Widgets);
            Assert.Equal("\"x", l.Find("ShowSpeed").Before);
        }

        [Fact]
        public void EmptyAndGarbageGiveAnEmptyLayout()
        {
            Assert.Empty(HudLayout.Parse(null).Widgets);
            Assert.Empty(HudLayout.Parse("").Widgets);
            Assert.Empty(HudLayout.Parse("nonsense\n: free\n# comment\nShowSpeed x=1").Widgets);
        }

        [Fact]
        public void LinesWithoutAChangeAreDroppedAndNumbersAreClamped()
        {
            HudLayout l = HudLayout.Parse("ShowSpeed: x=5, y=5\nShowPosition: free, scale=99, x=nan, y=7, wat=1\n");
            Assert.Single(l.Widgets);
            Assert.Equal("ShowPosition", l.Widgets[0].Key);
            Assert.Equal(HudLayout.MaxScale, l.Widgets[0].Scale);
            Assert.Equal(0f, l.Widgets[0].X);
            Assert.Equal(7f, l.Widgets[0].Y);
        }

        [Fact]
        public void FirstRedesignBuildsFilesStillRead()
        {
            HudLayout l = HudLayout.Parse("# ForestOverlay HUD layout\nShowSpeed: free, x=24, y=80, scale=2.5, label=off\n");
            HudWidgetLayout s = l.Find("ShowSpeed");
            Assert.True(s.Free);
            Assert.Equal(80f, s.Y);
        }

        [Fact]
        public void FirstLineOfAKeyWinsAndCrLfIsFine()
        {
            HudLayout l = HudLayout.Parse("ShowSpeed: free, x=1\r\nShowSpeed: free, x=2\r\n");
            Assert.Single(l.Widgets);
            Assert.Equal(1f, l.Widgets[0].X);
        }

        [Fact]
        public void DetachAndAttachKeepTheText()
        {
            HudLayout l = new HudLayout();
            l.Detach("ShowSpeed", 1, 2);
            l.Detach("ShowSpeed", 3, 4);
            Assert.Single(l.Widgets);
            Assert.Equal(3f, l.Find("ShowSpeed").X);
            Assert.True(l.IsFree("ShowSpeed"));
            Assert.True(l.Attach("ShowSpeed"));
            Assert.False(l.Attach("ShowSpeed"));
            Assert.Null(l.Find("ShowSpeed"));      // nothing left to keep

            l.SetText("ShowSpeed", "S ", "");
            l.Detach("ShowSpeed", 5, 6);
            Assert.True(l.Attach("ShowSpeed"));
            Assert.Equal("S ", l.Find("ShowSpeed").Before);
            Assert.False(l.IsFree("ShowSpeed"));
            Assert.True(l.SetText("ShowSpeed", "", ""));
            Assert.Null(l.Find("ShowSpeed"));
            Assert.False(l.SetText("ShowSpeed", "", ""));
        }

        [Fact]
        public void TrendRoundTripsAndIsKeptAlone()
        {
            HudLayout a = new HudLayout();
            Assert.True(a.SetTrend("ShowSpeed", true));
            Assert.False(a.SetTrend("ShowSpeed", true));
            a.Detach("ShowTotalSpeed", 10f, 20f);
            a.SetText("ShowTotalSpeed", "", " u/s");
            a.SetTrend("ShowTotalSpeed", true);

            string text = a.Format();
            Assert.Contains("ShowSpeed: trend\n", text);
            Assert.Contains("after=\" u/s\", trend", text);
            HudLayout b = HudLayout.Parse(text);
            Assert.True(b.HasTrend("ShowSpeed"));
            Assert.False(b.IsFree("ShowSpeed"));
            Assert.True(b.HasTrend("ShowTotalSpeed"));
            Assert.True(b.IsFree("ShowTotalSpeed"));
            Assert.Equal(" u/s", b.Find("ShowTotalSpeed").After);
            Assert.False(b.HasTrend("ShowPosition"));
        }

        [Fact]
        public void TrendOffLeavesNothingToKeep()
        {
            HudLayout l = new HudLayout();
            Assert.False(l.SetTrend("ShowSpeed", false));
            Assert.Null(l.Find("ShowSpeed"));
            l.SetTrend("ShowSpeed", true);
            l.Detach("ShowSpeed", 1f, 2f);
            Assert.True(l.Attach("ShowSpeed"));
            Assert.True(l.HasTrend("ShowSpeed"));          // back in the column, still coloured
            Assert.True(l.SetTrend("ShowSpeed", false));
            Assert.Null(l.Find("ShowSpeed"));
            Assert.False(l.SetTrend(null, true));
        }

        [Fact]
        public void TrendTokenIsCaseBlindAndAnOldFileHasNone()
        {
            Assert.True(HudLayout.Parse("ShowSpeed: TREND").HasTrend("ShowSpeed"));
            HudLayout old = HudLayout.Parse("ShowSpeed: free, x=1, y=2, scale=2");
            Assert.False(old.HasTrend("ShowSpeed"));
            Assert.True(old.IsFree("ShowSpeed"));
        }

        [Fact]
        public void DecorateWrapsTheValue()
        {
            Assert.Equal("12.3", HudLayout.Decorate("", "12.3", ""));
            Assert.Equal("Speed 12.3 u/s", HudLayout.Decorate("Speed ", "12.3", " u/s"));
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
