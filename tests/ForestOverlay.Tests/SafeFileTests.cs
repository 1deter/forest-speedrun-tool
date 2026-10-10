using System;
using System.IO;
using System.Text;
using ForestOverlay.Data;
using Xunit;

namespace ForestOverlay.Tests
{
    // The spot file is rewritten on every Practice edit (T-0217): a crash
    // at any step of a write must leave a whole file behind.
    public class SafeFileTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _file;

        public SafeFileTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "fo-safe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _file = Path.Combine(_dir, "my-segments.txt");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string Tmp { get { return _file + SafeFile.TempSuffix; } }
        private string Bak { get { return _file + SafeFile.BackupSuffix; } }

        [Fact]
        public void WritesANewFile()
        {
            SafeFile.WriteAllText(_file, "one", Encoding.UTF8);

            Assert.Equal("one", File.ReadAllText(_file));
            Assert.False(File.Exists(Tmp));
            Assert.False(File.Exists(Bak));
        }

        [Fact]
        public void ReplacesAnOldFileAndLeavesNothingBeside()
        {
            File.WriteAllText(_file, "old");
            SafeFile.WriteAllText(_file, "new", Encoding.UTF8);

            Assert.Equal("new", File.ReadAllText(_file));
            Assert.Equal(new[] { _file }, Directory.GetFiles(_dir));
        }

        [Fact]
        public void AStaleBackupDoesNotStopAWrite()
        {
            File.WriteAllText(_file, "old");
            File.WriteAllText(Bak, "older");
            SafeFile.WriteAllText(_file, "new", Encoding.UTF8);

            Assert.Equal("new", File.ReadAllText(_file));
            Assert.False(File.Exists(Bak));
        }

        [Fact]
        public void NothingBesideIsNothingToDo()
        {
            File.WriteAllText(_file, "whole");
            Assert.Equal("", SafeFile.Recover(_file));
            Assert.Equal("whole", File.ReadAllText(_file));
        }

        [Fact]
        public void CrashWritingTheTempKeepsTheOldFile()
        {
            File.WriteAllText(_file, "old");
            File.WriteAllText(Tmp, "ne");   // cut short

            Assert.NotEqual("", SafeFile.Recover(_file));
            Assert.Equal("old", File.ReadAllText(_file));
            Assert.False(File.Exists(Tmp));
        }

        [Fact]
        public void CrashAfterTheBackupTakesTheWholeTemp()
        {
            File.WriteAllText(Bak, "old");
            File.WriteAllText(Tmp, "new");

            Assert.NotEqual("", SafeFile.Recover(_file));
            Assert.Equal("new", File.ReadAllText(_file));
            Assert.False(File.Exists(Tmp));
            Assert.False(File.Exists(Bak));
        }

        [Fact]
        public void CrashDeletingTheBackupKeepsTheNewFile()
        {
            File.WriteAllText(_file, "new");
            File.WriteAllText(Bak, "old");

            Assert.NotEqual("", SafeFile.Recover(_file));
            Assert.Equal("new", File.ReadAllText(_file));
            Assert.False(File.Exists(Bak));
        }

        [Fact]
        public void ALoneBackupGoesBack()
        {
            File.WriteAllText(Bak, "old");

            Assert.NotEqual("", SafeFile.Recover(_file));
            Assert.Equal("old", File.ReadAllText(_file));
        }

        [Fact]
        public void AnUnfinishedFirstWriteIsDropped()
        {
            File.WriteAllText(Tmp, "ha");

            Assert.NotEqual("", SafeFile.Recover(_file));
            Assert.False(File.Exists(_file));
            Assert.False(File.Exists(Tmp));
        }
    }
}
