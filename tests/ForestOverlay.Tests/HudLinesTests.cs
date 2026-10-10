using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class HudLinesTests
    {
        [Fact]
        public void ShortReasonIsTheToolsName()
        {
            Assert.Equal("test bridge", HudLines.ShortReason("test bridge: screenshot"));
            Assert.Equal("savestate restore", HudLines.ShortReason("savestate restore (load)"));
            Assert.Equal("teleport", HudLines.ShortReason("teleport: Cave 5 (top)"));
            Assert.Equal("no blood, no stagger", HudLines.ShortReason("no blood, no stagger"));
            Assert.Equal("", HudLines.ShortReason(null));
        }

        [Fact]
        public void FindsLinesByModuleAndLabel()
        {
            Assert.Equal("Speed", HudLines.All[HudLines.Find("runinfo", "Speed")].Name);
            Assert.Equal("Pinned items", HudLines.All[HudLines.Find("inventory", "")].Name);
            Assert.Equal("Items carried", HudLines.All[HudLines.Find("inventory", "Items")].Name);
            // The same label from another module is another line.
            Assert.Equal("100% totals", HudLines.All[HudLines.Find("collectibles", "Items")].Name);
            Assert.Equal("100% totals", HudLines.All[HudLines.Find("collectibles", "Passengers")].Name);
            // Not listed = always shown.
            Assert.Equal(-1, HudLines.Find("runinfo", "Something new"));
            Assert.Equal(-1, HudLines.Find("nosuchmodule", "Speed"));
            Assert.Equal(-1, HudLines.Find(null, "Speed"));
        }

        [Fact]
        public void HonestLabellingIsNeverSwitchable()
        {
            int locked = 0;
            foreach (HudLine l in HudLines.All)
            {
                if (!l.Locked) continue;
                locked++;
                Assert.False(l.Switchable);
                Assert.Null(l.ConfigKey);
                Assert.Contains("Always shown", l.Description);
            }
            Assert.Equal(3, locked);   // ON NOW, the practice marker, run mode's code
            // A Pair can never land on a locked line.
            Assert.Equal(-1, HudLines.Find(HudLines.PluginModule, null));
        }

        [Fact]
        public void ConfigKeysAreUniqueAndDefaultsKeepTheOldLook()
        {
            HashSet<string> keys = new HashSet<string>();
            foreach (HudLine l in HudLines.All)
            {
                if (!l.Switchable) continue;
                Assert.True(keys.Add(l.ConfigKey), l.ConfigKey);
                // Lines added after the HUD settings are opt-in.
                Assert.Equal(l.ConfigKey != "ShowLoadRemoved" && l.ConfigKey != "ShowTotalSpeed", l.DefaultOn);
                Assert.False(string.IsNullOrEmpty(l.Description));
            }
            Assert.True(HudLines.IndexOfKey("ShowTitle") >= 0);
            Assert.Equal(-1, HudLines.IndexOfKey("Nope"));
        }

        [Fact]
        public void PairKeepsTheColumnUnlessCompact()
        {
            Assert.Equal("Speed  4.20", HudLines.Pair("Speed", "4.20", false));
            Assert.Equal("Passengers1/4", HudLines.Pair("Passengers", "1/4", false));   // as before
            Assert.Equal("         Cloth x3", HudLines.Pair("", "  Cloth x3", false));
            Assert.Equal("Speed 4.20", HudLines.Pair("Speed", "4.20", true));
            Assert.Equal("Cloth x3", HudLines.Pair("", "  Cloth x3", true));
            Assert.Equal("Run", HudLines.Pair("Run", null, false).Trim());
        }

        [Fact]
        public void CompactValuesUseFewerWords()
        {
            Assert.Equal("4.20", HudLines.Speed(4.2f, false));
            Assert.Equal("4.20", HudLines.Speed(4.2f, true));
            Assert.Equal("12   (3 stacks)", HudLines.Items(12, 3, false));
            Assert.Equal("12", HudLines.Items(12, 3, true));
            Assert.Equal("1.5, -2.0, 0.3", HudLines.Vector(1.5f, -2f, 0.26f, 1));
            Assert.Equal("up to date (v1.2.3)", HudLines.Update("up to date (v1.2.3)", false));
            Assert.Equal("up to date", HudLines.Update("up to date (v1.2.3)", true));
            Assert.Equal("v2 available (you have 1)", HudLines.Update("v2 available (you have 1)", true));
            Assert.Equal("Forest Overlay v1", HudLines.Title("1", false));
            Assert.Equal("FO v1", HudLines.Title("1", true));
        }

        [Fact]
        public void TextSizeStepsThroughTheOfferedSizes()
        {
            Assert.Equal(10, HudLines.StepTextSize(0, +1));
            Assert.Equal(0, HudLines.StepTextSize(10, -1));
            Assert.Equal(0, HudLines.StepTextSize(0, -1));
            Assert.Equal(24, HudLines.StepTextSize(24, +1));
            Assert.Equal(16, HudLines.StepTextSize(14, +1));
            // A hand-typed size snaps to the nearest offered one first.
            Assert.Equal(16, HudLines.StepTextSize(15, +1));   // 15 -> 14 -> 16
            Assert.Equal(1, HudLines.NearestSize(3));
            Assert.Equal(0, HudLines.NearestSize(-5));
            Assert.Equal("default", HudLines.SizeText(0));
            Assert.Equal("14 px", HudLines.SizeText(14));
        }

        [Fact]
        public void BoxScalesWithTextAndStaysOnScreen()
        {
            Assert.Equal(330f, HudLines.Width(0, 1920f));
            Assert.Equal(18f, HudLines.LineHeight(0));
            Assert.True(HudLines.Width(24, 1920f) > 330f);
            Assert.True(HudLines.LineHeight(24) > 18f);
            Assert.Equal(480f, HudLines.Width(24, 500f));   // never wider than the screen
            Assert.Equal(0f, HudLines.Clamp(-40f, 330f, 1920f));
            Assert.Equal(1590f, HudLines.Clamp(5000f, 330f, 1920f));
            Assert.Equal(0f, HudLines.Clamp(float.NaN, 330f, 1920f));
            Assert.Equal(0f, HudLines.Clamp(50f, 2000f, 1920f));   // bigger than the screen: pinned left
            Assert.Equal(10f, HudLines.Clamp(10f, 330f, 1920f));
        }
    }
}
