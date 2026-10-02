using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Run categories (phase 4): the text the site serves, each feature's
    // policy, the game a category asks for, and the report's new lines.
    // ------------------------------------------------------------------
    public class RunCategoryTests
    {
        [Fact]
        public void RoundTrips_DefaultsNotWritten()
        {
            RunCategory c = new RunCategory { Id = "manhunt", Name = "Manhunt", Version = 3, Status = "published", Multiplayer = "yes", AntiSplice = false, Spot = "s-0123456789ab" };
            Assert.True(c.SetPolicy("logs", RunCategory.Forced));
            Assert.True(c.SetPolicy("go", RunCategory.Allowed));
            Assert.False(c.SetPolicy("go", RunCategory.Forced));      // an action is never forced on
            Assert.False(c.SetPolicy("nonsense", RunCategory.Allowed));
            c.SetPolicy("reload", RunCategory.Allowed);                // its default: not written
            c.Banned.Add("OOB");
            c.Rules.Add("-Time starts\nwhen moving");                  // a newline cannot break the format
            c.Rules.Add("");

            string text = c.Format();
            Assert.DoesNotContain("feature reload", text);
            List<RunCategory> back = RunCategory.Parse("﻿" + text.Replace("\n", "\r\n"));
            Assert.Single(back);
            RunCategory b = back[0];
            Assert.Equal(("manhunt", "Manhunt", 3, "published", "yes", false, "s-0123456789ab"),
                         (b.Id, b.Name, b.Version, b.Status, b.Multiplayer, b.AntiSplice, b.Spot));
            Assert.True(b.IsForced("logs"));
            Assert.Equal(RunCategory.Allowed, b.Policy("go"));
            Assert.Equal(RunCategory.Allowed, b.Policy("reload"));
            Assert.True(b.IsLocked("godmode"));
            Assert.Equal(new[] { "OOB" }, b.Banned);
            Assert.Equal(new[] { "-Time starts when moving", "" }, b.Rules);
            Assert.Equal(text, b.Format());
        }

        [Fact]
        public void Parse_SkipsBadIds_UnknownValuesFallBack()
        {
            List<RunCategory> list = RunCategory.Parse("[category]\nid = Bad Id\n\n[category]\nid = ok\ndifficulty = nightmare\nfeature godmode = sometimes\nfuture = x\n");
            Assert.Single(list);
            Assert.Equal("any", list[0].Difficulty);
            Assert.True(list[0].IsLocked("godmode"));
        }

        [Fact]
        public void Find_ByIdThenName()
        {
            List<RunCategory> list = RunCategory.Parse("[category]\nid = any-normal\nname = Any% - Normal\n");
            Assert.Same(list[0], RunCategory.Find(list, "any-normal"));
            Assert.Same(list[0], RunCategory.Find(list, "any% - normal"));
            Assert.Null(RunCategory.Find(list, "Any%"));
        }

        [Fact]
        public void GameMismatch()
        {
            RunCategory c = new RunCategory { Difficulty = "hard", Creative = "no", Multiplayer = "no" };
            Assert.Empty(c.GameMismatch("Hard", false, false));
            Assert.Empty(c.GameMismatch("HardSurvival", false, false));
            Assert.Equal(3, c.GameMismatch("Normal", true, true).Count);
            Assert.Empty(new RunCategory().GameMismatch("Peaceful", true, true));   // any
        }

        [Fact]
        public void MarksBelongToFeatures()
        {
            Assert.Equal("go", RunCategory.FeatureOfMark("teleport: Plane").Key);
            Assert.Equal("savestates", RunCategory.FeatureOfMark("savestate restore (in place)").Key);
            Assert.Equal("nostagger", RunCategory.FeatureOfMark("no blood, no stagger").Key);
            Assert.Equal("bridge", RunCategory.FeatureOfMark("test bridge: call x").Key);
            Assert.Null(RunCategory.FeatureOfMark("something new"));
        }

        [Fact]
        public void Report_CarriesTheCategoryAndTheGame()
        {
            RunReport r = new RunReport { Category = "any-normal", CategoryVersion = 4, Difficulty = "Normal", Creative = false, Multiplayer = true };
            r.Used.Add("godmode");
            RunReport b = RunReport.Parse(r.Format());
            Assert.Equal(("any-normal", 4, "Normal", false, true), (b.Category, b.CategoryVersion, b.Difficulty, b.Creative, b.Multiplayer));
            Assert.Equal(new[] { "godmode" }, b.Used);
            Assert.Contains("Note  Used, allowed by the category: God mode.", r.Findings());
        }
    }
}
