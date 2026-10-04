using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // Segment events from the game's event bus and the rides (Data/BusEvents)
    // and how one occurrence under several names moves a run on once
    // (OccurrenceGate in Data/SplitSequence).
    public class BusEventsTests
    {
        [Theory]
        [InlineData("LogCabinMed", "log-cabin-med")]
        [InlineData("RoofOLD", "roof-old")]
        [InlineData("BombTimed", "bomb-timed")]
        [InlineData("cliff climb", "cliff-climb")]
        [InlineData("Keycard2", "keycard2")]
        [InlineData("Stick", "stick")]
        [InlineData("TimmyFound", "timmy-found")]
        [InlineData("Upgraded Spear", "upgraded-spear")]
        [InlineData("A, B", "a-b")]
        [InlineData("__x__", "x")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void SlugsAreLowerCaseWordsJoinedByHyphens(string raw, string slug)
        {
            Assert.Equal(slug, BusEvents.Slug(raw));
        }

        [Fact]
        public void SpecificNamesCarryTheGeneralOneAsPrefix()
        {
            Assert.Equal("built-log-cabin", BusEvents.Specific(BusEvents.Built, "LogCabin"));
            Assert.Equal("story-megan-found", BusEvents.Specific(BusEvents.Story, "MeganFound"));
            Assert.Null(BusEvents.Specific(BusEvents.Built, "  "));
            Assert.Equal("kill-deer", BusEvents.KillOf("deer"));
            Assert.Equal("zipline-start", BusEvents.RideEvent("zipline", true));
            Assert.Equal("cliff-climb-end", BusEvents.RideEvent("cliff climb", false));
        }

        [Fact]
        public void AnItemWithNoNameIsItsNumber()
        {
            Assert.Equal("crafted-bomb-timed", BusEvents.ItemEvent(BusEvents.Crafted, 29, "BombTimed"));
            Assert.Equal("used-109", BusEvents.ItemEvent(BusEvents.Used, 109, null));
            Assert.Equal("used-109", BusEvents.ItemEvent(BusEvents.Used, 109, ""));
        }

        [Fact]
        public void GeneralNamesMatchTheRunAuditKinds()
        {
            // The same strings everywhere: the attempt page's timeline kinds.
            Assert.Equal(RunAudit.Built, BusEvents.Built);
            Assert.Equal(RunAudit.Crafted, BusEvents.Crafted);
            Assert.Equal(RunAudit.Used, BusEvents.Used);
            Assert.Equal(RunAudit.Bomb, BusEvents.Bomb);
            Assert.Equal(RunAudit.Story, BusEvents.Story);
            Assert.Equal(RunAudit.RideStart, BusEvents.RideStart);
            Assert.Equal(RunAudit.RideEnd, BusEvents.RideEnd);
        }

        [Fact]
        public void APickerGroupIsGeneralFirstThenSortedSpecificsThenExtras()
        {
            string[] g = BusEvents.Group(BusEvents.Built, new[] { "Shelter", "LogCabin", "Shelter", "", "Fire" }, BusEvents.TreeCut);
            Assert.Equal(new[] { "built", "built-fire", "built-log-cabin", "built-shelter", "tree-cut" }, g);
            Assert.Equal(new[] { "crafted" }, BusEvents.Group(BusEvents.Crafted, null));
        }

        [Fact]
        public void FightAndRideGroupsListEveryName()
        {
            string[] fights = BusEvents.FightEvents();
            Assert.Equal("kill-enemy", fights[0]);
            Assert.Equal("kill-animal", fights[1]);
            Assert.Contains("kill-shark", fights);
            Assert.Contains("hit-by-enemy", fights);
            Assert.Contains("bomb", fights);

            string[] rides = BusEvents.RideEvents();
            Assert.Equal(new[] { "ride-start", "ride-end", "zipline-start", "zipline-end", "sled-start", "sled-end",
                                 "glider-start", "glider-end", "cliff-climb-start", "cliff-climb-end" }, rides);
        }

        [Theory]
        [InlineData("built", "built")]
        [InlineData("Built-Log-Cabin", "built")]
        [InlineData("tree-cut", "built")]
        [InlineData("crafted-anything", "crafted")]
        [InlineData("used-soda", "used")]
        [InlineData("slept", "story")]
        [InlineData("story-timmy-found", "story")]
        [InlineData("kill-deer", "fights")]
        [InlineData("kill-enemy", "fights")]
        [InlineData("bomb", "fights")]
        [InlineData("sled-end", "rides")]
        [InlineData("ride-start", "rides")]
        [InlineData("builtx", null)]
        [InlineData("built-", null)]
        [InlineData("cave-enter", null)]
        [InlineData("", null)]
        public void TypedNamesFindTheirPickerGroup(string name, string group)
        {
            Assert.Equal(group, BusEvents.PrefixGroup(name));
        }

        [Fact]
        public void EveryListedNameHasALabel()
        {
            List<string> all = new List<string>();
            all.AddRange(BusEvents.FightEvents());
            all.AddRange(BusEvents.RideEvents());
            all.AddRange(BusEvents.Group(BusEvents.Built, new[] { "LogCabin" }, BusEvents.TreeCut));
            all.AddRange(BusEvents.Group(BusEvents.Crafted, new[] { "BombTimed" }));
            all.AddRange(BusEvents.Group(BusEvents.Used, new[] { "Soda" }));
            all.AddRange(BusEvents.Group(BusEvents.Story, new[] { "TimmyFound" }, BusEvents.Slept));
            all.Add(BusEvents.EndgameEnter);
            all.Add(BusEvents.EndgameLeave);
            foreach (string e in all) Assert.False(string.IsNullOrEmpty(BusEvents.LabelFor(e)), e);

            Assert.Equal("Built: log cabin", BusEvents.LabelFor("built-log-cabin"));
            Assert.Equal("Killed a deer", BusEvents.LabelFor("kill-deer"));
            Assert.Equal("Got on: cliff climb", BusEvents.LabelFor("Cliff-Climb-Start"));
            Assert.Null(BusEvents.LabelFor("cave-enter"));
            Assert.Null(BusEvents.LabelFor("built-"));
        }

        // --- one occurrence, several names ---------------------------------------

        private static Trigger Event(string name)
        {
            Trigger t = new Trigger();
            t.Kind = TriggerKind.Event;
            t.EventName = name;
            return t;
        }

        // What PracticeRunModule does per frame: one pass per event, the
        // gate skipping companions of an occurrence that moved the run on.
        private static int Feed(SplitSequence s, OccurrenceGate gate, params string[] eventsAndCompanions)
        {
            int moved = 0;
            for (int i = 0; i < eventsAndCompanions.Length; i++)
            {
                string e = eventsAndCompanions[i];
                bool companion = e.StartsWith("+");
                if (companion) e = e.Substring(1);
                if (!gate.Evaluate(companion)) continue;
                SplitEvent r = s.Evaluate(Vector3.zero, null, e, null);
                if (r == SplitEvent.Split || r == SplitEvent.Finished) { moved++; gate.Moved(); }
            }
            s.Evaluate(Vector3.zero, null, null, null);   // the next frame, no event
            return moved;
        }

        [Fact]
        public void SpecificThenGeneralCheckpointsNeedTwoOccurrences()
        {
            SplitSequence s = new SplitSequence();
            OccurrenceGate gate = new OccurrenceGate();
            s.Begin(new List<Trigger> { Event("crafted-bomb-timed"), Event("crafted") }, Event("slept"));

            Assert.Equal(1, Feed(s, gate, "crafted-bomb-timed", "+crafted"));
            Assert.Equal(1, s.Next);
            Assert.Equal(1, Feed(s, gate, "crafted-stick", "+crafted"));
            Assert.Equal(2, s.Next);
        }

        [Fact]
        public void TheGeneralNameStillFiresWhenTheSpecificOneMovedNothing()
        {
            SplitSequence s = new SplitSequence();
            OccurrenceGate gate = new OccurrenceGate();
            s.Begin(new List<Trigger> { Event("cave-enter") }, Event("cave-exit"));

            Assert.Equal(1, Feed(s, gate, "cave-enter-cave06", "+cave-enter"));
            Assert.Equal(1, s.Next);
            Assert.Equal(1, Feed(s, gate, "cave-exit-cave06", "+cave-exit"));
        }

        [Fact]
        public void ARepeatedEventMovesOneCheckpointPerOccurrence()
        {
            SplitSequence s = new SplitSequence();
            OccurrenceGate gate = new OccurrenceGate();
            s.Begin(new List<Trigger> { Event("tree-cut"), Event("tree-cut") }, Event("tree-cut"));

            Assert.Equal(1, Feed(s, gate, "tree-cut"));
            Assert.Equal(1, s.Next);
            // Two trees in one frame: separate occurrences, two splits.
            Assert.Equal(2, Feed(s, gate, "tree-cut", "tree-cut"));
            Assert.True(s.OnlyEndLeft);
        }

        [Fact]
        public void ANewOccurrenceAfterAMoveIsEvaluated()
        {
            // cave-exit-cave01 + cave-exit, then cave-enter-cave02 + cave-enter
            // in one frame: the enter is its own occurrence.
            SplitSequence s = new SplitSequence();
            OccurrenceGate gate = new OccurrenceGate();
            s.Begin(new List<Trigger> { Event("cave-exit"), Event("cave-enter") }, Event("slept"));

            Assert.Equal(2, Feed(s, gate, "cave-exit-cave01", "+cave-exit", "cave-enter-cave02", "+cave-enter"));
            Assert.True(s.OnlyEndLeft);
        }
    }
}
