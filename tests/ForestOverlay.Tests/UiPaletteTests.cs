using System;
using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class UiPaletteTests
    {
        public static IEnumerable<object[]> Variants()
        {
            foreach (UiPalette p in UiPalette.All) yield return new object[] { p.Id };
        }

        private static double Lin(float c)
        {
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        /// The colour `fg` over `bg` over white (the brightest the game can be
        /// behind a translucent panel), then over that.
        private static double[] Over(uint fg, double[] under)
        {
            double a = UiPalette.A(fg);
            return new[]
            {
                UiPalette.R(fg) * a + under[0] * (1 - a),
                UiPalette.G(fg) * a + under[1] * (1 - a),
                UiPalette.B(fg) * a + under[2] * (1 - a),
            };
        }

        private static double Luminance(double[] c)
        {
            return 0.2126 * Lin((float)c[0]) + 0.7152 * Lin((float)c[1]) + 0.0722 * Lin((float)c[2]);
        }

        private static double Contrast(uint text, uint panel, uint card)
        {
            double[] white = { 1, 1, 1 };
            double[] bg = Over(card, Over(panel, white));
            double[] fg = Over(text, bg);
            double a = Luminance(fg), b = Luminance(bg);
            return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        }

        [Theory, MemberData(nameof(Variants))]
        public void Accent_IsTheLoadingScreensYellow(string id)
        {
            // The yellow-on-black themes; the first redesign and Catppuccin keep their own yellow.
            if (id == "bluegrey" || id.StartsWith("catppuccin")) return;
            UiPalette p = UiPalette.Find(id);
            Assert.Equal(0xE5C501FFu, p.Accent);
            Assert.Equal(229 / 255f, UiPalette.R(p.Accent));
            Assert.Equal(197 / 255f, UiPalette.G(p.Accent));
            Assert.Equal(1 / 255f, UiPalette.B(p.Accent));
        }

        [Theory, MemberData(nameof(Variants))]
        public void Text_IsReadableOnCards_EvenOverAWhiteScene(string id)
        {
            UiPalette p = UiPalette.Find(id);
            Assert.True(Contrast(p.Text, p.Panel, p.Card) >= 7, "text");
            Assert.True(Contrast(p.Soft, p.Panel, p.Card) >= 4.5, "soft");
            Assert.True(Contrast(p.Dim, p.Panel, p.Card) >= 4.5, "dim");
            Assert.True(Contrast(p.Accent, p.Panel, p.Card) >= 4.5, "accent");
            Assert.True(Contrast(p.OnAccent, p.Accent, p.Accent) >= 7, "black on yellow");
        }

        [Theory, MemberData(nameof(Variants))]
        public void TextColours_AreDistinct_SoASwitchCanTellThemApart(string id)
        {
            UiPalette p = UiPalette.Find(id);
            uint[] t = { p.Text, p.Dim, p.Soft, p.Accent, p.AccentHover, p.OnAccent, p.Warn };
            for (int i = 0; i < t.Length; i++)
                for (int j = i + 1; j < t.Length; j++)
                    Assert.NotEqual(t[i], t[j]);
        }

        [Fact]
        public void Ids_AreUnique_AndFindFallsBackToTheSite()
        {
            HashSet<string> ids = new HashSet<string>();
            foreach (UiPalette p in UiPalette.All) Assert.True(ids.Add(p.Id), p.Id);
            Assert.Equal("site", UiPalette.Default.Id);
            Assert.Same(UiPalette.Default, UiPalette.Find("nonsense"));
            Assert.Same(UiPalette.Default, UiPalette.Find(null));
            Assert.Equal("warm", UiPalette.Find("warm").Id);
        }

        [Fact]
        public void SiteVariant_IsTheSitesTokens()
        {
            UiPalette s = UiPalette.Find("site");
            Assert.Equal(0x000000FFu, s.Panel);      // --bg
            Assert.Equal(0x0D0D0DFFu, s.Card);       // --panel
            Assert.Equal(0x222222FFu, s.Border);     // --line
            Assert.Equal(0xE5C501u, s.Text >> 8);    // --text
            Assert.Equal(0x8A7A1CFFu, s.Dim);        // --dim
        }
    }
}
