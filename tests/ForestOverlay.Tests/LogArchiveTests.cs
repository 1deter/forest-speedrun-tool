using System;
using System.Collections.Generic;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // The game replaces LogOutput.log every launch; the kept copies must
    // prune the oldest sessions and nothing else (author: keep 3).
    public class LogArchiveTests
    {
        [Fact]
        public void SessionFileNameRoundTrips()
        {
            string name = LogArchive.SessionFileName(new DateTime(2026, 9, 25, 17, 4, 9));
            Assert.Equal("LogOutput-2026-09-25_17-04-09.log", name);
            Assert.True(LogArchive.IsSessionFile(name));
        }

        [Fact]
        public void OtherFilesAreNotSessions()
        {
            Assert.False(LogArchive.IsSessionFile("LogOutput.log"));
            Assert.False(LogArchive.IsSessionFile("LogOutput-notes.log"));
            Assert.False(LogArchive.IsSessionFile("report.zip"));
            Assert.False(LogArchive.IsSessionFile(null));
        }

        [Fact]
        public void DeletesOldestBeyondKeepAndSparesCurrent()
        {
            List<string> names = new List<string>
            {
                "LogOutput-2026-09-25_10-00-00.log",
                "LogOutput-2026-09-24_09-00-00.log",
                "notes.txt",
                "LogOutput-2026-09-25_12-00-00.log",
                "LogOutput-2026-09-25_11-00-00.log",
                "LogOutput-2026-09-25_13-00-00.log",
            };
            List<string> delete = LogArchive.ToDelete(names, "LogOutput-2026-09-25_13-00-00.log", 3);
            Assert.Equal(new[] { "LogOutput-2026-09-24_09-00-00.log" }, delete.ToArray());
        }

        [Fact]
        public void KeepZeroDeletesEveryPreviousSession()
        {
            List<string> names = new List<string> { "LogOutput-2026-09-25_10-00-00.log", "LogOutput-2026-09-25_11-00-00.log" };
            Assert.Equal(1, LogArchive.ToDelete(names, "LogOutput-2026-09-25_11-00-00.log", 0).Count);
        }

        [Fact]
        public void FewerThanKeepDeletesNothing()
        {
            List<string> names = new List<string> { "LogOutput-2026-09-25_10-00-00.log" };
            Assert.Empty(LogArchive.ToDelete(names, "LogOutput-2026-09-25_11-00-00.log", 3));
        }
    }
}
