using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class HudLinesTests
    {
        [Fact]
        public void RunLineNamesTheNewestActionThenModApi()
        {
            string M = AttemptFormat.ModApiFlag;
            Assert.Equal("H", HudLines.RunLine("H", new List<string>(), 0));
            Assert.Equal("H - NOT VALID: ModAPI Detected", HudLines.RunLine("H", new List<string> { M }, 1));
            Assert.Equal("H - NOT VALID: tp, ModAPI", HudLines.RunLine("H", new List<string> { M, "tp" }, 1));
            Assert.Equal("H - NOT VALID: b, ModAPI (+1 more, Runs tab)", HudLines.RunLine("H", new List<string> { M, "a", "b" }, 1));
            Assert.Equal("H - NOT VALID: b (+1 more, Runs tab)", HudLines.RunLine("H", new List<string> { "a", "b" }, 0));
            Assert.Equal("H - NOT VALID: Mods, ModAPI", HudLines.RunLine("H", new List<string> { "Mods", M }, 2));
            Assert.Equal("H - NOT VALID: tp, ModAPI (+1 more, Runs tab)", HudLines.RunLine("H", new List<string> { M, "Mods", "tp" }, 2));
            Assert.Equal("H - NOT VALID: tp (+1 more, Runs tab)", HudLines.RunLine("H", new List<string> { "Mods", "tp" }, 1));
        }

        [Fact]
        public void MarkerPutsTheModApiFlagFirst()
        {
            Assert.Equal("", HudLines.Marker(false, null, null));
            Assert.Equal("teleport", HudLines.Marker(false, null, "teleport: Cave 5 (top)"));
            Assert.Equal("RUN MODE - attempt 1", HudLines.Marker(false, "RUN MODE - attempt 1", "teleport: x"));
            // Sticky: shown while clean too, above the tool or run mode's line.
            Assert.Equal("ModAPI", HudLines.Marker(true, null, null));
            Assert.Equal("ModAPI\nteleport", HudLines.Marker(true, null, "teleport: Cave 5 (top)"));
            // In run mode the run line says it (NOT VALID: ModAPI Detected): once, not twice.
            Assert.Equal("RUN MODE - attempt 1 - NOT VALID: ModAPI Detected",
                HudLines.Marker(true, "RUN MODE - attempt 1 - NOT VALID: ModAPI Detected", null));
        }

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

        private static readonly string[] LeanOff =
        {
            "ShowUpdate", "ShowTotalSpeed", "ShowVelocity", "ShowPosition", "ShowItems",
            "ShowPracticeStatus", "ShowRunDelta", "ShowLoadRemoved", "ShowNextSplit", "ShowLastTime", "ShowPassengers", "ShowLoads",
        };

        [Fact]
        public void EveryDefaultLabelIsAValueAndEndsInASpace()
        {
            foreach (string[] t in HudLines.DefaultText)
            {
                int i = HudLines.IndexOfKey(t[0]);
                Assert.True(i >= 0, t[0]);
                Assert.False(HudLines.All[i].Locked);
                Assert.EndsWith(" ", t[1]);
                Assert.Equal(t[1], HudLayout.CleanText(t[1]));
            }
            // The clock says what it is (and is LiveSplit's big number when popped out).
            Assert.DoesNotContain(HudLines.DefaultText, t => t[0] == "ShowRunTimer");
        }

        [Fact]
        public void ConfigKeysAreUniqueAndDefaultsAreLean()
        {
            HashSet<string> keys = new HashSet<string>();
            foreach (HudLine l in HudLines.All)
            {
                if (l.Locked) continue;
                // Every value has a layout key (T-0018: the 100% totals too).
                Assert.True(keys.Add(l.ConfigKey), l.ConfigKey);
                if (!l.Switchable) continue;
                // The lean default (T-0336): these are ticked by the runner.
                bool off = System.Array.IndexOf(LeanOff, l.ConfigKey) >= 0;
                Assert.Equal(!off, l.DefaultOn);
                Assert.False(string.IsNullOrEmpty(l.Description));
            }
            Assert.True(HudLines.IndexOfKey("ShowTitle") >= 0);
            Assert.Equal(-1, HudLines.IndexOfKey("Nope"));
        }

        [Fact]
        public void OnlyTheSpeedsOfferTheTrendColour()
        {
            Assert.True(HudLines.All[HudLines.IndexOfKey("ShowSpeed")].Trend);
            Assert.True(HudLines.All[HudLines.IndexOfKey("ShowTotalSpeed")].Trend);
            int offered = 0;
            for (int i = 0; i < HudLines.All.Length; i++)
            {
                if (!HudLines.All[i].Trend) continue;
                offered++;
                Assert.NotNull(HudLines.All[i].ConfigKey);   // kept in the layout by its key
                Assert.False(HudLines.All[i].Locked);
            }
            Assert.Equal(2, offered);
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
