using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // T-0276: Unity's errors reach the session log, one line each, without
    // a per-frame error flooding it; and a crashed session's log is kept
    // beside Unity's crash folder.
    public class UnityLogFilterTests
    {
        [Fact]
        public void FirstErrorIsWrittenWithItsType()
        {
            UnityLogFilter f = new UnityLogFilter();
            string line = f.Next("Error", "d3d11: failed to create 2D texture id=4516 [D3D error was 887a0005]", null, true, 0);
            Assert.Equal("Unity: [Error] d3d11: failed to create 2D texture id=4516 [D3D error was 887a0005] (off the main thread)", line);
        }

        [Fact]
        public void SameMessageWithOtherDigitsIsARepeat()
        {
            UnityLogFilter f = new UnityLogFilter();
            Assert.NotNull(f.Next("Error", "texture id=1", null, false, 0));
            for (int i = 0; i < 49; i++) Assert.Null(f.Next("Error", "texture id=" + (i + 2), null, false, 1 + i * 0.1));
            string later = f.Next("Error", "texture id=99", null, false, 61);
            Assert.Equal("Unity: [Error] texture id=99 - 49 more times since the last line", later);
        }

        [Fact]
        public void DifferentTypeOrTextIsNew()
        {
            UnityLogFilter f = new UnityLogFilter();
            Assert.NotNull(f.Next("Error", "a", null, false, 0));
            Assert.NotNull(f.Next("Assert", "a", null, false, 0));
            Assert.NotNull(f.Next("Error", "b", null, false, 0));
        }

        [Fact]
        public void CapsLinesPerMinuteAndSaysHowManyWereHeld()
        {
            UnityLogFilter f = new UnityLogFilter();
            for (int i = 0; i < UnityLogFilter.PerMinute; i++) Assert.NotNull(f.Next("Error", "msg " + new string('x', i), null, false, 1));
            Assert.Null(f.Next("Error", "one more", null, false, 2));
            Assert.Null(f.Next("Error", "and another", null, false, 3));
            string next = f.Next("Error", "after the minute", null, false, 62);
            Assert.Equal("Unity: [Error] after the minute - 2 other lines held back (over 30 a minute)", next);
        }

        [Fact]
        public void ExceptionAddsItsFirstStackLineAndStaysOneLine()
        {
            UnityLogFilter f = new UnityLogFilter();
            string line = f.Next("Exception", "NullReferenceException: Object reference\nnot set",
                                 "Foo.Bar () [0x00000]\nBaz.Qux ()", false, 0);
            Assert.Equal("Unity: [Exception] NullReferenceException: Object reference not set | at Foo.Bar () [0x00000]", line);
        }

        [Fact]
        public void LongTextIsCut()
        {
            UnityLogFilter f = new UnityLogFilter();
            string line = f.Next("Error", new string('a', 1000), null, false, 0);
            Assert.Equal("Unity: [Error] ".Length + UnityLogFilter.MaxText + 3, line.Length);
        }

        [Fact]
        public void CrashFolderNamesParse()
        {
            System.DateTime t;
            Assert.True(CrashFolders.TryParse("2026-10-04_083030", out t));
            Assert.Equal(new System.DateTime(2026, 10, 4, 8, 30, 30), t);
            Assert.False(CrashFolders.TryParse("BepInEx", out t));
            Assert.False(CrashFolders.TryParse("2026-10-04_08303", out t));
            Assert.False(CrashFolders.TryParse(null, out t));
        }

        [Fact]
        public void CrashFolderMatchesTheSessionThatStartedJustAfterIt()
        {
            List<string> logs = new List<string>
            {
                "LogOutput-2026-10-04_07-10-00.log",   // an earlier session
                "LogOutput-2026-10-04_08-30-52.log",   // the crashed one (plugin up 22 s after the process)
                "LogOutput-2026-10-04_13-15-02.log",   // the next launch
                "stall.log",
            };
            Assert.Equal("LogOutput-2026-10-04_08-30-52.log", CrashFolders.SessionFor("2026-10-04_083030", logs));
        }

        [Fact]
        public void CrashFolderWithoutItsSessionMatchesNothing()
        {
            List<string> logs = new List<string> { "LogOutput-2026-10-04_09-00-00.log", "LogOutput-2026-10-04_08-28-00.log" };
            Assert.Null(CrashFolders.SessionFor("2026-10-04_083030", logs));
            Assert.Null(CrashFolders.SessionFor("not-a-crash", logs));
        }
    }
}
