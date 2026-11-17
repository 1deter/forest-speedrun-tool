using System;
using System.IO;
using System.Text;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // The attempt log is appended to while the attempt runs, not rewritten
    // (T-0459): what is on disk must always be the chain so far, and a log
    // a crash cut off must still read.
    public class AttemptLogFileTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _file;

        public AttemptLogFileTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "fo-attempt-" + Guid.NewGuid().ToString("N"));
            _file = Path.Combine(Path.Combine(_dir, "open"), "a-0123456789abcdef.attempt");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static AttemptChain NewChain()
        {
            AttemptChain c = new AttemptChain();
            c.Header("a-0123456789abcdef", "r-1", "R", "0.24.400", "any", "", "", "seed",
                     new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc));
            c.Step(0, -1, true, 1f, 2f, 3f);
            return c;
        }

        private string OnDisk()
        {
            // Read while the writer still holds it, as a runner's editor would.
            using (FileStream fs = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader r = new StreamReader(fs, Encoding.UTF8))
                return r.ReadToEnd();
        }

        [Fact]
        public void TextFromGivesOnlyTheNewLines()
        {
            AttemptChain c = NewChain();
            int before = c.Length;
            c.Step(1000, 10, true, 1f, 2f, 3f);
            Assert.Equal(c.Text, c.Text.Substring(0, before) + c.TextFrom(before));
            Assert.StartsWith("step|", c.TextFrom(before));
            Assert.Equal("", c.TextFrom(c.Length));
            Assert.Equal(c.Text, c.TextFrom(0));
        }

        [Fact]
        public void AppendsSoTheFileIsAlwaysTheChainSoFar()
        {
            AttemptChain c = NewChain();
            AttemptLogFile f = new AttemptLogFile(_file);
            f.Sync(c);
            Assert.Equal(c.Text, OnDisk());

            c.Step(1000, 10, true, 1f, 2f, 3f);
            c.Load(1500, 400, 0);
            c.Step(2000, 20, false, 0, 0, 0);
            f.Sync(c);
            Assert.Equal(c.Text, OnDisk());
            Assert.Equal(c.Length, f.Written);

            f.Sync(c);   // nothing new: nothing written
            Assert.Equal(c.Text, OnDisk());
            f.Close();
            Assert.Equal(c.Text, File.ReadAllText(_file));
        }

        [Fact]
        public void AFileLeftFromBeforeIsReplaced()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file));
            File.WriteAllText(_file, "old text that is not this attempt\n");
            AttemptChain c = NewChain();
            AttemptLogFile f = new AttemptLogFile(_file);
            f.Sync(c);
            f.Close();
            Assert.Equal(c.Text, File.ReadAllText(_file));
        }

        [Fact]
        public void AfterCloseTheNextSyncWritesTheWholeChainAgain()
        {
            AttemptChain c = NewChain();
            AttemptLogFile f = new AttemptLogFile(_file);
            f.Sync(c);
            f.Close();
            Assert.Equal(0, f.Written);
            c.Step(1000, 10, true, 1f, 2f, 3f);
            f.Sync(c);
            f.Close();
            Assert.Equal(c.Text, File.ReadAllText(_file));
        }

        [Fact]
        public void ALogWithNoEndStillReads()
        {
            AttemptChain c = NewChain();
            c.Step(1000, 10, true, 1f, 2f, 3f);
            AttemptLogFile f = new AttemptLogFile(_file);
            f.Sync(c);
            f.Close();

            string sent = AttemptLogFile.Recovered(File.ReadAllText(_file), "report line");
            AttemptChain.Replay r = AttemptChain.Read(sent);
            Assert.Null(r.Error);
            Assert.False(r.Ended);
            Assert.Equal(2, r.Steps.Count);
            Assert.Equal("report line", r.Report);
        }

        [Fact]
        public void ALogCutMidLineReadsUpToItsLastWholeLine()
        {
            AttemptChain c = NewChain();
            c.Step(1000, 10, true, 1f, 2f, 3f);
            string whole = c.Text;
            c.Step(2000, 20, true, 1f, 2f, 3f);
            string cut = c.Text.Substring(0, whole.Length + 7);   // "step|3|" - the crash came mid-append

            Assert.NotNull(AttemptChain.Read(cut).Error);   // the strict reader refuses it as it is
            Assert.Equal(whole, AttemptLogFile.CompleteLines(cut));
            AttemptChain.Replay r = AttemptChain.Read(AttemptLogFile.Recovered(cut, ""));
            Assert.Null(r.Error);
            Assert.Equal(2, r.Steps.Count);
        }

        [Fact]
        public void ARecoveredLogHasTheFinishedLogsShape()
        {
            AttemptChain c = NewChain();
            // What RunModeModule.FullLog builds for a finished log.
            string finished = c.Text + AttemptChain.ReportMarker + "\n" + "the report";
            Assert.Equal(finished, AttemptLogFile.Recovered(c.Text, "the report"));
        }

        [Fact]
        public void AnOpenLogFromBeforeTheChangeIsSentAsItIs()
        {
            // Before T-0459 the open file held chain and report together,
            // and the report need not end in a newline.
            AttemptChain c = NewChain();
            string old = c.Text + AttemptChain.ReportMarker + "\n" + "line one\nline two";
            Assert.Equal(old, AttemptLogFile.Recovered(old, "ignored"));
        }

        [Fact]
        public void EmptyOrLinelessTextRecoversToNothing()
        {
            Assert.Equal("", AttemptLogFile.CompleteLines(null));
            Assert.Equal("", AttemptLogFile.CompleteLines(""));
            Assert.Equal("", AttemptLogFile.CompleteLines("forest-att"));
        }
    }
}
