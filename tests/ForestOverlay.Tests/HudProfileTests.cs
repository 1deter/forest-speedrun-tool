using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class HudProfileTests
    {
        [Fact]
        public void SettingsAndLayoutRoundTrip()
        {
            HudProfile a = new HudProfile();
            a.Set("HUD.ShowSpeed", "true");
            a.Set("HUD.X", "10");
            a.Set("Splits.PanelX", "-1");
            a.Layout.Detach("ShowSpeed", 24f, 80f).Scale = 2.5f;
            a.Layout.SetText("ShowPosition", "Pos ", "");

            HudProfile b = HudProfile.Parse(a.Format());
            Assert.Equal(3, b.Count);
            Assert.Equal("true", b.Get("HUD.ShowSpeed"));
            Assert.Equal("10", b.Get("HUD.X"));
            Assert.Equal("-1", b.Get("Splits.PanelX"));
            Assert.Null(b.Get("HUD.Compact"));
            Assert.True(b.Layout.Find("ShowSpeed").Free);
            Assert.Equal(2.5f, b.Layout.Find("ShowSpeed").Scale);
            Assert.Equal("Pos ", b.Layout.Find("ShowPosition").Before);
        }

        [Fact]
        public void AnOldLayoutFileReadsAsAProfileWithNoSettings()
        {
            HudLayout old = new HudLayout();
            old.Detach("ShowSpeed", 5f, 6f);
            HudProfile p = HudProfile.Parse(old.Format());
            Assert.Equal(0, p.Count);
            Assert.True(p.Layout.Find("ShowSpeed").Free);
        }

        [Fact]
        public void ForgivingParse()
        {
            HudProfile p = HudProfile.Parse("@ = x\n@HUD.X=12\r\n@HUD.X = 99\n@nonsense\n\n# c\nShowSpeed: before=\"S \"");
            Assert.Equal(1, p.Count);
            Assert.Equal("12", p.Get("HUD.X"));     // the first one counts
            Assert.Equal("S ", p.Layout.Find("ShowSpeed").Before);
            Assert.Equal(0, HudProfile.Parse(null).Count);
        }

        [Fact]
        public void SetKeepsOneLine()
        {
            HudProfile p = new HudProfile();
            p.Set("A.B", "x\ny");
            p.Set("A.B", " z ");
            Assert.Equal(1, p.Count);
            Assert.Equal("z", HudProfile.Parse(p.Format()).Get("A.B"));
        }

        [Fact]
        public void NameRules()
        {
            List<string> taken = new List<string> { "Default", "Runs" };
            Assert.Null(HudProfileNames.Problem("Mine", taken));
            Assert.NotNull(HudProfileNames.Problem("", taken));
            Assert.NotNull(HudProfileNames.Problem("default", taken));
            Assert.NotNull(HudProfileNames.Problem("a/b", taken));
            Assert.NotNull(HudProfileNames.Problem("a:b", taken));
            Assert.NotNull(HudProfileNames.Problem("end.", taken));
            Assert.NotNull(HudProfileNames.Problem("con", taken));
            Assert.NotNull(HudProfileNames.Problem(new string('x', HudProfileNames.MaxLength + 1), taken));
            Assert.Null(HudProfileNames.Problem(new string('x', HudProfileNames.MaxLength), taken));
        }

        [Fact]
        public void UniqueNames()
        {
            List<string> taken = new List<string> { "Default", "default 2" };
            Assert.Equal("Mine", HudProfileNames.Unique("Mine", taken));
            Assert.Equal("Default 3", HudProfileNames.Unique("Default", taken));
            string longName = new string('x', HudProfileNames.MaxLength);
            taken.Add(longName);
            string u = HudProfileNames.Unique(longName, taken);
            Assert.True(u.Length <= HudProfileNames.MaxLength);
            Assert.EndsWith(" 2", u);
        }
    }
}
