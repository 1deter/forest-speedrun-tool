using System;
using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The run audit log (Data/RunAudit + AttemptChain's `event` lines):
    // events fold into the chain and read back, an old log without them
    // still reads, items merge into a few lines, bursts count, and the
    // rundown says what happened in plain words.
    // ------------------------------------------------------------------
    public class RunAuditTests
    {
        private static AttemptChain Start()
        {
            AttemptChain c = new AttemptChain();
            c.Header("a-0123456789abcdef", "r-1", "R", "0.24.235", "any", "", "", "seed", new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc));
            c.Step(0, -1, true, 0, 0, 0);
            return c;
        }

        [Fact]
        public void EventsAreFoldedAndReadBack()
        {
            AttemptChain c = Start();
            string before = c.Head;
            c.Event(1200, 300, "cave-enter", true, 12.5f, -40.25f, 3f, "Cave 6 - Lawyer Cave");
            Assert.NotEqual(before, c.Head);   // folded: a removed event changes every later code
            c.Step(2000, 1100, true, 1, 1, 1);
            string codeWith = c.Code;
            c.Event(2400, -1, RunAudit.Items, false, 0, 0, 0, "+3 Stick, -1 Rock | sneaky");
            c.End(2500, "reset", -1);
            Assert.Contains("event|1200|300|cave-enter|1250|-4025|300|Cave 6 - Lawyer Cave\n", c.Text);
            Assert.Contains("event|2400|-|items|-|-|-|+3 Stick, -1 Rock   sneaky\n", c.Text);

            AttemptChain.Replay r = AttemptChain.Read(c.Text + AttemptChain.ReportMarker + "\nreport\n");
            Assert.Null(r.Error);
            Assert.Equal(2, r.Events.Count);
            Assert.Equal(1200, r.Events[0].RealMs);
            Assert.Equal(300, r.Events[0].TimerMs);
            Assert.Equal("cave-enter", r.Events[0].Kind);
            Assert.True(r.Events[0].HasPos);
            Assert.Equal(12.5, r.Events[0].X, 3);
            Assert.Equal(-40.25, r.Events[0].Y, 3);
            Assert.Equal("Cave 6 - Lawyer Cave", r.Events[0].Detail);
            Assert.Equal(-1, r.Events[1].TimerMs);
            Assert.False(r.Events[1].HasPos);
            Assert.Empty(r.Flags);   // an event is never a flag
            Assert.Equal(c.Head, r.FinalHead);
            Assert.Equal(codeWith, r.Step(2).Code);

            // Removing an event line breaks every later code.
            string cut = c.Text.Replace("event|1200|300|cave-enter|1250|-4025|300|Cave 6 - Lawyer Cave\n", "");
            AttemptChain.Replay without = AttemptChain.Read(cut);
            Assert.Null(without.Error);
            Assert.NotEqual(codeWith, without.Step(2).Code);
        }

        [Fact]
        public void AnOldLogWithoutEventsStillReads()
        {
            AttemptChain c = Start();
            c.Move(500, "bomb-boost", true, 1, 2, 3, "boost");
            c.Step(1000, -1, true, 0, 0, 0);
            c.End(1100, "reset", -1);
            AttemptChain.Replay r = AttemptChain.Read(c.Text);
            Assert.Null(r.Error);
            Assert.Empty(r.Events);
            Assert.Single(r.Moves);
            Assert.Empty(RunAudit.Rundown(r.Events));
        }

        [Fact]
        public void BadEventLinesAreRefused()
        {
            string text = Start().Text;
            Assert.StartsWith("bad event line", AttemptChain.Read(text + "event|1200|-|cave-enter|1|2|3\n").Error);
            Assert.StartsWith("bad event line", AttemptChain.Read(text + "event|1200|-||1|2|3|d\n").Error);
            Assert.StartsWith("bad event line", AttemptChain.Read(text + "event|1200|x|kind|1|2|3|d\n").Error);
            Assert.StartsWith("bad event line", AttemptChain.Read(text + "event|1200|-|kind|1|y|3|d\n").Error);
            Assert.StartsWith("time goes backwards at an event", AttemptChain.Read(text + "move|900|k|-|-|-|d\nevent|800|-|kind|-|-|-|d\n").Error);
            Assert.Null(AttemptChain.Read(text + "event|1200|-|kind|-|-|-|\n").Error);
        }

        [Fact]
        public void ItemsMergeIntoFewLines()
        {
            RunAudit.ItemTally t = new RunAudit.ItemTally();
            Dictionary<string, int> bag = new Dictionary<string, int> { { "Stick", 2 }, { "Rock", 1 } };
            t.Update(bag, 0);                       // the baseline: no line
            Assert.Equal("", t.Take(5000, true));

            bag["Stick"] = 3; t.Update(bag, 1000);
            bag["Stick"] = 5; t.Update(bag, 1500);
            bag.Remove("Rock"); t.Update(bag, 1600);
            bag["Cloth"] = 1; t.Update(bag, 1700);
            Assert.Equal("", t.Take(2500, false));  // not quiet for 2 s yet
            Assert.Equal(1000, t.PendingSince);
            Assert.Equal("+3 Stick, -1 Rock, +1 Cloth", t.Take(3700, false));
            Assert.Equal("", t.Take(9000, true));   // taken

            // A pick-up and a drop in one window cancel out.
            bag["Cloth"] = 2; t.Update(bag, 10000);
            bag["Cloth"] = 1; t.Update(bag, 10100);
            Assert.Equal("", t.Take(20000, false));

            // Changes that never stop are written every 10 s anyway.
            for (long ms = 30000; ms <= 40000; ms += 1000) { bag["Stick"]++; t.Update(bag, ms); }
            Assert.Equal("+11 Stick", t.Take(40000, false));

            int gained, lost;
            RunAudit.ItemDeltas("+3 Stick, -1 Rock, +1 Cloth, junk", out gained, out lost);
            Assert.Equal(4, gained);
            Assert.Equal(1, lost);
        }

        [Fact]
        public void BurstsCountAndClose()
        {
            RunAudit.Burst b = new RunAudit.Burst(RunAudit.Hit, "hit", "hits");
            b.Add(1000, -1, true, 1, 2, 3, "regularMale");
            b.Add(2000, -1, true, 9, 9, 9, "regularMale");
            b.Add(3000, -1, true, 9, 9, 9, "creepy");
            Assert.False(b.Closes(7000));
            Assert.True(b.Closes(9000));
            Assert.Equal("", b.Take(7000, false));
            Assert.Equal(1000, b.FirstMs);
            Assert.Equal(1f, b.X);
            Assert.Equal("3 hits (regularMale, creepy)", b.Take(8001, false));
            Assert.Equal("", b.Take(9000, true));
            b.Add(10000, -1, false, 0, 0, 0, null);
            Assert.Equal("1 hit", b.Take(10000, true));
        }

        [Fact]
        public void TheRundownSaysWhatHappened()
        {
            AttemptChain c = Start();
            c.Event(1000, -1, "first-input", true, 0, 0, 0, "the runner took control");
            c.Event(2000, -1, RunAudit.Items, true, 0, 0, 0, "+3 Stick, -1 Rock");
            c.Event(3000, -1, "cave-enter", true, 0, 0, 0, "Cave 1 - Dead Cave");
            c.Event(4000, -1, "cave-exit", true, 0, 0, 0, "Cave 1 - Dead Cave");
            c.Event(5000, -1, "cave-enter", true, 0, 0, 0, "Cave 6 - Lawyer Cave");
            c.Event(6000, -1, RunAudit.PauseOpen, true, 0, 0, 0, null);
            c.Event(9500, -1, RunAudit.PauseClose, true, 0, 0, 0, "open 3.5 s");
            c.Event(10000, -1, RunAudit.Death, true, 0, 0, 0, "died - Reload save on death loads the save");
            c.Event(15000, -1, RunAudit.Reload, false, 0, 0, 0, "the save is loaded");
            c.Event(16000, -1, RunAudit.Tree, true, 0, 0, 0, "4 trees cut down");
            c.Event(17000, -1, RunAudit.Built, true, 0, 0, 0, "LogCabin");
            c.Event(18000, -1, RunAudit.RideStart, true, 0, 0, 0, "zipline");
            c.Event(18500, -1, "rope-grab", true, 0, 0, 0, null);
            c.Event(18800, -1, "rope-leave", true, 0, 0, 0, null);
            c.Event(19000, -1, RunAudit.Setting, true, 0, 0, 0, "cheats allowed: True");
            c.Event(80000, -1, "keycard-door", true, 0, 0, 0, "Vault door (keycard) - door 'x', keycard 210");
            c.Event(90000, -1, RunAudit.Full, false, 0, 0, 0, "12 later events not written");
            c.End(91000, "finished", 90000);
            AttemptChain.Replay r = AttemptChain.Read(c.Text);
            Assert.Null(r.Error);

            List<string> rundown = RunAudit.Rundown(r.Events);
            Assert.Equal("1 death (1 Reload save on death)", rundown[0]);
            Assert.Equal("Game settings changed during the run: 0:19 cheats allowed: True", rundown[1]);
            Assert.Equal("Progress: Keycard door 1:20", rundown[2]);
            Assert.Contains("2 cave entries: Cave 1 - Dead Cave, Cave 6 - Lawyer Cave", rundown);
            Assert.Contains("3 items gained, 1 item used or lost (1 change)", rundown);
            Assert.Contains("1 structure built", rundown);
            Assert.DoesNotContain(rundown, l => l.Contains("tree"));   // T-0244: in the log, not the report
            Assert.Contains("1 ride: zipline", rundown);
            Assert.Contains("1 rope climb", rundown);
            Assert.Contains("Pause menu opened 1 time, 3.5 s in all", rundown);
            Assert.Contains("The audit log was full: 12 later events were not written", rundown);
        }

        // T-0244: the summary counts the moves the game saw too, on one
        // line after the deaths, each kind once in the order first seen.
        [Fact]
        public void RundownCountsTheMoves()
        {
            AttemptChain c = Start();
            c.Event(1000, -1, "first-input", true, 0, 0, 0, "the runner took control");
            c.Event(2000, -1, RunAudit.Death, true, 0, 0, 0, "died");
            c.Event(3000, -1, RunAudit.Bomb, true, 0, 0, 0, null);
            c.Move(3100, MoveDetector.BombBoost, true, 0, 0, 0, "game time stopped");
            c.Move(4000, MoveDetector.ClipKind, true, 0, 0, 0, "into a rock");
            c.Move(5000, MoveDetector.BombBoost, true, 0, 0, 0, "again");
            c.Event(6000, -1, RunAudit.Tree, true, 0, 0, 0, "3 trees cut down (from 0:03)");
            c.End(7000, "reset", -1);
            AttemptChain.Replay r = AttemptChain.Read(c.Text);
            Assert.Null(r.Error);
            List<string> kinds = new List<string>();
            foreach (AttemptChain.MoveInfo m in r.Moves) kinds.Add(m.Kind);

            List<string> rundown = RunAudit.Rundown(r.Events, kinds);
            Assert.Equal(new[] { "1 death", "Moves the game saw: Bomb boost (2), Clip through a solid (1)", "1 bomb went off" }, rundown.ToArray());
            Assert.DoesNotContain("Moves", string.Join("|", RunAudit.Rundown(r.Events).ToArray()));   // without moves, as before
            // moves alone (a log from before the audit log) still count
            Assert.Equal(new[] { "Moves the game saw: Bomb boost (2), Clip through a solid (1)" }, RunAudit.Rundown(null, kinds).ToArray());
            Assert.Empty(RunAudit.Rundown(null, new List<string>()));
        }

        [Fact]
        public void EveryMoveKindHasPlainWords()
        {
            string[] kinds = { MoveDetector.BombBoost, MoveDetector.HugeSpeedKind, MoveDetector.CaveForceLoad,
                               MoveDetector.FallDamageCancel, MoveDetector.LiftKind, MoveDetector.ClipKind };
            foreach (string k in kinds) Assert.NotEqual(k, RunAudit.MoveLabel(k));
            Assert.Equal("Bomb boost", RunAudit.MoveLabel(MoveDetector.BombBoost));
            Assert.Equal("brand-new", RunAudit.MoveLabel("brand-new"));   // a newer plugin's kind still shows
        }

        [Fact]
        public void KindsHaveGroupsAndLabels()
        {
            Assert.Equal("caves", RunAudit.Group("cave-enter"));
            Assert.Equal("progress", RunAudit.Group("megan-to-machine"));
            Assert.Equal("progress", RunAudit.Group("keycard-door"));
            Assert.Equal("deaths", RunAudit.Group(RunAudit.Reload));
            Assert.Equal("world", RunAudit.Group("something-new"));
            Assert.Equal("something-new", RunAudit.Label("something-new"));
            Assert.Equal("Found Timmy", RunAudit.Label("timmy-pickup"));
            // T-0242: a rope only - cave mouths log the cave, cutscenes nothing
            Assert.Equal("Rope climb started", RunAudit.Label("rope-grab"));
            Assert.Equal("Rope climb ended", RunAudit.Label("rope-leave"));
            foreach (string g in RunAudit.Groups) Assert.False(string.IsNullOrEmpty(RunAudit.GroupLabel(g)));
            Assert.Equal("Moves the game saw", RunAudit.GroupLabel(RunAudit.MovesGroup));
            // trees keep their group and label (the replays' markers), not the report
            Assert.Equal("building", RunAudit.Group(RunAudit.Tree));
            Assert.Equal("Trees cut", RunAudit.Label(RunAudit.Tree));
            Assert.False(RunAudit.InReport(RunAudit.Tree));
            Assert.True(RunAudit.InReport(RunAudit.Bomb));
            Assert.True(RunAudit.InReport("something-new"));
            Assert.Equal("1:01:01", RunAudit.Clock(3661000));
            Assert.Equal(3, RunAudit.LeadingCount("3 trees"));
            Assert.Equal(1, RunAudit.LeadingCount("trees"));
        }
    }
}
