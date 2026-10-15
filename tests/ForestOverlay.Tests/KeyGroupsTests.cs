using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    public class KeyGroupsTests
    {
        [Theory]
        // The author's developer-only keys (T-0263): dumps, type explorer, TAS, QA.
        [InlineData("dump.write", KeyGroups.Developer)]
        [InlineData("panel.explorer", KeyGroups.Developer)]
        [InlineData("tas.stop", KeyGroups.Developer)]
        [InlineData("tas.record", KeyGroups.Developer)]
        [InlineData("tas.replayLatest", KeyGroups.Developer)]
        [InlineData("qa.mark", KeyGroups.Developer)]
        [InlineData("tab.qa", KeyGroups.Developer)]
        [InlineData("practice.saveSpot", KeyGroups.Practice)]
        [InlineData("practice.goToSpot", KeyGroups.Practice)]
        [InlineData("run.toggleMode", KeyGroups.Practice)]
        [InlineData("run.restartCheckpoint", KeyGroups.Practice)]
        [InlineData("debug.freecam", KeyGroups.Practice)]
        [InlineData("debug.trajectory", KeyGroups.Developer)]
        [InlineData("run.manualSplit", KeyGroups.Runs)]
        [InlineData("run.abort", KeyGroups.Runs)]
        [InlineData("run.cycleComparison", KeyGroups.Runs)]
        [InlineData("run.closeResults", KeyGroups.Runs)]
        [InlineData("run.replayCamera", KeyGroups.Runs)]
        [InlineData("timer.toggle", KeyGroups.Runs)]
        [InlineData("timer.reset", KeyGroups.Runs)]
        [InlineData("timer.split", KeyGroups.Runs)]
        [InlineData("panel.main", KeyGroups.Window)]
        [InlineData("tab.settings", KeyGroups.Window)]
        [InlineData("tab.practice", KeyGroups.Window)]
        [InlineData("tab.runs", KeyGroups.Window)]
        [InlineData("tab.developer", KeyGroups.Window)]
        [InlineData("ui.toggleAll", KeyGroups.Window)]
        [InlineData("ui.toggleInfo", KeyGroups.Window)]
        public void EveryKnownKeyHasItsGroup(string id, int group)
        {
            Assert.Equal(group, KeyGroups.Of(id));
            Assert.Equal(group == KeyGroups.Developer, KeyGroups.IsDeveloper(id));
        }

        [Fact]
        public void AnUnknownKeyIsShownUnderOtherNeverHidden()
        {
            Assert.Equal(KeyGroups.Other, KeyGroups.Of("newmodule.something"));
            Assert.Equal(KeyGroups.Other, KeyGroups.Of(""));
            Assert.Equal(KeyGroups.Other, KeyGroups.Of(null));
            Assert.Contains(KeyGroups.Other, KeyGroups.Order);
        }

        [Fact]
        public void SettingsShowsEveryGroupButDeveloperOnce()
        {
            HashSet<int> seen = new HashSet<int>();
            foreach (int g in KeyGroups.Order)
            {
                Assert.True(seen.Add(g));
                Assert.NotEqual(KeyGroups.Developer, g);
                Assert.False(string.IsNullOrEmpty(KeyGroups.Headings[g]));
            }
            Assert.Equal(KeyGroups.Headings.Length - 1, seen.Count);
        }

        [Fact]
        public void TheTrajectoryKeyIsReboundBesideItsSwitch()
        {
            // T-0322: under the Experimental switch, still a developer key
            // (never in the runner's list).
            Assert.True(KeyGroups.BesideSwitch("debug.trajectory"));
            Assert.True(KeyGroups.IsDeveloper("debug.trajectory"));
            Assert.False(KeyGroups.BesideSwitch("debug.freecam"));
            Assert.False(KeyGroups.BesideSwitch("dump.write"));
            Assert.False(KeyGroups.BesideSwitch(null));
        }

        [Fact]
        public void HudValuesAreGroupedFromTheFirstLine()
        {
            Assert.NotNull(HudLines.HeadingAt(0));
            int headings = 0;
            bool lockedGroupStarted = false;
            for (int i = 0; i < HudLines.All.Length; i++)
            {
                string h = HudLines.HeadingAt(i);
                if (h != null) headings++;
                // The always-shown values come last and have no heading (author, 2026-10-11).
                if (HudLines.All[i].Locked)
                {
                    Assert.Null(h);
                    lockedGroupStarted = true;
                }
                if (lockedGroupStarted) Assert.True(HudLines.All[i].Locked, HudLines.All[i].Name);
            }
            Assert.Equal(6, headings);   // every group's first line is still in the list
            Assert.Null(HudLines.HeadingAt(-1));
            Assert.Null(HudLines.HeadingAt(HudLines.All.Length));
        }
    }
}
