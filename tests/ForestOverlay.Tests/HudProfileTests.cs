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

        // The settings a profile holds and their defaults, as the plugin
        // registers them (HudSettings, the splits + results panels).
        private static readonly string[] Keys =
        {
            "HUD.ShowSpeed", "HUD.X", "HUD.Y", "HUD.Compact", "HUD.TextSize",
            "Splits.Panel", "Splits.PanelX", "Splits.PanelWidth", "Splits.Decimals", "Splits.ResultsX",
        };
        private static readonly string[] Defaults = { "true", "10", "10", "false", "0", "true", "-1", "320", "2", "-1" };

        // What the plugin writes (HudWidgets.Capture): every setting as it is now + the layout.
        private static HudProfile Written(IList<string> values, HudLayout layout)
        {
            HudProfile p = new HudProfile();
            for (int i = 0; i < Keys.Length; i++) p.Set(Keys[i], values[i]);
            p.Layout = layout;
            return p;
        }

        // Putting a profile in (HudWidgets.Apply): a setting the file has none of takes its default.
        private static string[] Applied(HudProfile p)
        {
            string[] v = new string[Keys.Length];
            for (int i = 0; i < Keys.Length; i++) v[i] = p.Get(Keys[i]) ?? Defaults[i];
            return v;
        }

        [Fact]
        public void AResetProfileEqualsAFreshDefault()
        {
            // T-0303: a profile changed everywhere - values, column, Compact,
            // Text size, both panels, placed values and their text.
            string[] changed = { "false", "300", "200", "true", "24", "false", "40", "500", "3", "600" };
            HudLayout layout = new HudLayout();
            layout.Detach("ShowSpeed", 24f, 80f).Scale = 2.5f;
            layout.SetText("ShowPosition", "Pos ", "");
            HudProfile before = Written(changed, layout);
            Assert.NotEqual(Written(Defaults, new HudLayout()).Format(), before.Format());

            // Reset profile puts the default look in, then writes the profile.
            HudProfile look = HudProfile.DefaultLook();
            HudProfile reset = Written(Applied(look), look.Layout);

            // A fresh install's Default: the config's defaults, no old layout file.
            HudProfile fresh = Written(Defaults, HudLayout.Parse(""));

            Assert.Equal(fresh.Format(), reset.Format());
            Assert.Empty(reset.Layout.Widgets);
            for (int i = 0; i < Keys.Length; i++) Assert.Equal(Defaults[i], reset.Get(Keys[i]));
        }

        [Fact]
        public void TheDefaultLookHasNoSettingAndNoValueOfItsOwn()
        {
            HudProfile look = HudProfile.Parse(HudProfile.DefaultLook().Format());
            Assert.Equal(0, look.Count);
            Assert.Empty(look.Layout.Widgets);
            // Two calls never share a layout (a reset profile edited later must
            // not change the next New).
            HudProfile a = HudProfile.DefaultLook();
            a.Layout.Detach("ShowSpeed", 1f, 2f);
            Assert.Empty(HudProfile.DefaultLook().Layout.Widgets);
        }
    }
}
