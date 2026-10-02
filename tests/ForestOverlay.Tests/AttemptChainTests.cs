using System;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // Run mode's attempt log and hash chain (phase 2): what the game shows
    // as codes must be exactly what the site recomputes from the log, and
    // any change to the log must change the codes after it.
    // ------------------------------------------------------------------
    public class AttemptChainTests
    {
        private static AttemptChain Sample(string nonce = "00ff")
        {
            AttemptChain c = new AttemptChain();
            c.Header("a-0123456789abcdef", "r-00000000000000aa", "Runner|x", "0.24.216", "Any%", "s-0123456789ab", "abc123",
                     "seed0001", new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc));
            c.Step(1000, -1, true, 1.234f, -2.5f, 300f);
            c.Nonce(1200, nonce);
            c.Step(2000, 500, true, 1.3f, -2.5f, 301f);
            c.Split(2500, 0, 1000);
            c.Step(3000, 1500, false, 0, 0, 0);
            c.End(3100, "finished", 1600);
            return c;
        }

        [Fact]
        public void ReplayRecomputesEveryCode()
        {
            AttemptChain c = Sample();
            AttemptChain.Replay r = AttemptChain.Read(c.Text + AttemptChain.ReportMarker + "\nreport line\n");
            Assert.Null(r.Error);
            Assert.Equal("a-0123456789abcdef", r.AttemptId);
            Assert.Equal("Runner x", r.RunnerName);    // a | in a name cannot break the line
            Assert.Equal("s-0123456789ab", r.SpotId);
            Assert.Equal(3, r.Steps.Count);
            Assert.Equal("00ff", r.Nonce);
            Assert.Equal(1200, r.NonceMs);
            Assert.Equal(1, r.Splits);
            Assert.True(r.Ended);
            Assert.Equal("finished", r.EndReason);
            Assert.Equal(1600, r.FinalTimerMs);
            Assert.Equal("report line", r.Report);
            Assert.Equal(c.Code, r.Steps[2].Code);
            Assert.Equal(c.Head, r.FinalHead);
            Assert.Equal(-1, r.Steps[0].TimerMs);
        }

        [Fact]
        public void PositionsAreWholeCentimetres()
        {
            string text = Sample().Text;
            Assert.Contains("step|1|1000|-|123|-250|30000\n", text);
            Assert.Contains("step|3|3000|1500|-|-|-\n", text);
        }

        [Fact]
        public void AnotherNonceChangesEveryLaterCode()
        {
            AttemptChain.Replay a = AttemptChain.Read(Sample("00ff").Text);
            AttemptChain.Replay b = AttemptChain.Read(Sample("00fe").Text);
            Assert.Equal(a.Steps[0].Head, b.Steps[0].Head);   // before the nonce: the same
            Assert.NotEqual(a.Steps[1].Head, b.Steps[1].Head);
            Assert.NotEqual(a.Steps[2].Head, b.Steps[2].Head);
        }

        [Fact]
        public void AnEditedLogDoesNotRead_OrReadsWithOtherHeads()
        {
            string text = Sample().Text;
            AttemptChain.Replay good = AttemptChain.Read(text);

            // A moved position: still a log, but every head from there differs.
            AttemptChain.Replay moved = AttemptChain.Read(text.Replace("step|2|2000|500|130|", "step|2|2000|500|131|"));
            Assert.Null(moved.Error);
            Assert.Equal(good.Steps[0].Head, moved.Steps[0].Head);
            Assert.NotEqual(good.Steps[1].Head, moved.Steps[1].Head);

            // A dropped step, time going back, an unknown line, a line after the end.
            Assert.NotNull(AttemptChain.Read(text.Replace("step|2|2000|500|130|-250|30100\n", "")).Error);
            Assert.NotNull(AttemptChain.Read(text.Replace("step|3|3000|", "step|3|1500|")).Error);
            Assert.NotNull(AttemptChain.Read(text.Replace("split|", "spilt|")).Error);
            Assert.NotNull(AttemptChain.Read(text + "step|4|4000|-|-|-|-\n").Error);
            Assert.NotNull(AttemptChain.Read("hello\n").Error);
        }

        [Fact]
        public void TheReportIsNotFolded()
        {
            string text = Sample().Text;
            AttemptChain.Replay a = AttemptChain.Read(text + "[report]\nclean\n");
            AttemptChain.Replay b = AttemptChain.Read(text + "[report]\nsomething else\n");
            Assert.Equal(a.FinalHead, b.FinalHead);
        }

        [Fact]
        public void CodesAreFourReadableCharacters()
        {
            Assert.Equal("0000", AttemptChain.CodeOf(new byte[] { 0, 0, 0 }));
            Assert.Equal("ZZZZ", AttemptChain.CodeOf(new byte[] { 0xff, 0xff, 0xf0 }));
            Assert.Equal("1000", AttemptChain.CodeOf(new byte[] { 0x08, 0, 0 }));   // the top 5 bits first
            foreach (char c in "ILOU") Assert.DoesNotContain(c, AttemptChain.Alphabet);

            Assert.Equal("1A0Z", AttemptChain.NormaliseCode("la-oz"));
            Assert.Equal("1A0Z", AttemptChain.NormaliseCode(" 1 A 0 Z "));
            Assert.Null(AttemptChain.NormaliseCode("1A0"));
            Assert.Null(AttemptChain.NormaliseCode("1A0U"));
        }

        [Fact]
        public void AttemptIds()
        {
            string id = AttemptChain.NewAttemptId();
            Assert.True(AttemptChain.IsAttemptId(id));
            Assert.NotEqual(id, AttemptChain.NewAttemptId());
            Assert.False(AttemptChain.IsAttemptId("a-0123456789ABCDEF"));
            Assert.False(AttemptChain.IsAttemptId("a-../../etc/passwd"));
            Assert.False(AttemptChain.IsAttemptId(null));
        }
    }
}
