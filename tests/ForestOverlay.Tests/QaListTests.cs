using System.Collections.Generic;
using System.IO;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // Testers answer by number against the list they were sent; the
    // parsed list, the answers file and the report must keep those numbers.
    public class QaListTests
    {
        private const string Sample =
            "# comment\n" +
            "id = qa-test\n" +
            "title = Test list\n" +
            "intro = Answer by number.\n" +
            "section = Resetting\n" +
            "1) Swing, then F7.\n" +
            "seen = ended attack state\n" +
            "2) Same, Full load.\r\n" +
            "section = Other\n" +
            "15) Write dumps.\n";

        [Fact]
        public void ParsesItemsSectionsAndSeen()
        {
            QaList l = QaList.Parse(Sample);
            Assert.Empty(l.Problems);
            Assert.Equal("qa-test", l.Id);
            Assert.Equal("Test list", l.Title);
            Assert.Single(l.Intro);
            Assert.Equal(3, l.Items.Count);
            Assert.Equal(15, l.Items[2].Number);
            Assert.Equal("Other", l.Items[2].Section);
            Assert.Equal("Resetting", l.Items[1].Section);
            Assert.Equal("Same, Full load.", l.Items[1].Text);
            Assert.Single(l.Items[0].Seen);
            Assert.Empty(l.Items[1].Seen);
        }

        [Fact]
        public void ReportsBadLines()
        {
            QaList l = QaList.Parse("seen = x\n1) a\n1) b\nnonsense\ncolour = red\n");
            Assert.Equal(5, l.Problems.Count); // seen with no item, duplicate, nonsense, unknown key, no id
        }

        [Fact]
        public void MatchesIgnoringCase()
        {
            QaList l = QaList.Parse(Sample);
            Assert.True(QaList.Matches(l.Items[0], "[Info] Teleport to 'x': Ended Attack State 'stickAttack'"));
            Assert.False(QaList.Matches(l.Items[1], "anything"));
        }

        [Fact]
        public void AnswersRoundTrip()
        {
            QaList l = QaList.Parse(Sample);
            Dictionary<int, QaAnswer> a = new Dictionary<int, QaAnswer>();
            a[1] = new QaAnswer { Result = QaResult.Pass, Note = "fine\nreally", Seen = "17:21:05 ended attack state 'doCharge'" };
            a[15] = new QaAnswer { Result = QaResult.Fail };
            a[2] = new QaAnswer();

            Dictionary<int, QaAnswer> back = QaList.ParseAnswers(QaList.FormatAnswers(l, a));
            Assert.Equal(2, back.Count);
            Assert.Equal(QaResult.Pass, back[1].Result);
            Assert.Equal("fine really", back[1].Note);
            Assert.Equal("17:21:05 ended attack state 'doCharge'", back[1].Seen);
            Assert.Equal(QaResult.Fail, back[15].Result);
        }

        [Fact]
        public void ReportCountsAndNumbers()
        {
            QaList l = QaList.Parse(Sample);
            Dictionary<int, QaAnswer> a = new Dictionary<int, QaAnswer>();
            a[1] = new QaAnswer { Result = QaResult.Pass };
            a[15] = new QaAnswer { Result = QaResult.Skip, Note = "no nature guide yet" };
            string r = QaList.FormatReport(l, a, "maks", "0.24.56", "2026-09-25 18:00");
            Assert.Contains("Pass 1, fail 0, skipped 1, not answered 1", r);
            Assert.Contains("1) [pass] Swing, then F7.", r);
            Assert.Contains("2) [-] Same, Full load.", r);
            Assert.Contains("   note: no nature guide yet", r);
        }

        [Fact]
        public void ShippedListsParseCleanly()
        {
            string dir = Path.Combine(Path.GetDirectoryName(typeof(QaListTests).Assembly.Location), "../../../../../qa");
            string[] files = Directory.GetFiles(dir, "*.txt");
            Assert.NotEmpty(files);
            for (int i = 0; i < files.Length; i++)
            {
                QaList l = QaList.Parse(File.ReadAllText(files[i]));
                Assert.True(l.Problems.Count == 0, files[i] + ": " + string.Join("; ", l.Problems));
                Assert.NotEmpty(l.Items);
            }
        }
    }
}
