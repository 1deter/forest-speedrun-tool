using System;
using System.IO;
using ForestOverlay.Data;
using ForestOverlay.Updater;
using Xunit;

namespace ForestOverlay.Tests
{
    // A browser saved the plugin as ForestOverlay(1).dll and no update ever
    // installed (runner, 2026-09-23). Staging plus the patcher's swap must end
    // with one ForestOverlay.dll whatever the plugin was called.
    public class UpdateStagingTests : IDisposable
    {
        private readonly string _dir;

        public UpdateStagingTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "fo-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string P(string name) { return Path.Combine(_dir, name); }
        private static byte[] Dll(string marker) { return System.Text.Encoding.ASCII.GetBytes("MZ" + marker); }
        private string Text(string name) { return System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(P(name))); }
        private void Install() { PendingSwap.Apply(P("ForestOverlay.dll"), null, delegate(string s) { }); }

        [Fact]
        public void CanonicalNameStagesBesideItselfAndStays()
        {
            File.WriteAllBytes(P("ForestOverlay.dll"), Dll("old"));

            UpdateStaging.Result r = UpdateStaging.Stage(P("ForestOverlay.dll"), Dll("new"));

            Assert.Equal(P("ForestOverlay.dll.pending"), r.Pending);
            Assert.Null(r.Aside);
            Assert.Equal("MZold", Text("ForestOverlay.dll"));
        }

        [Fact]
        public void RenamedPluginStagesTheCanonicalNameAndMovesItselfToTheBackup()
        {
            File.WriteAllBytes(P("ForestOverlay(1).dll"), Dll("old"));

            UpdateStaging.Result r = UpdateStaging.Stage(P("ForestOverlay(1).dll"), Dll("new"));

            Assert.Equal(P("ForestOverlay.dll.pending"), r.Pending);
            Assert.Equal(P("ForestOverlay.dll.bak"), r.Aside);
            Assert.False(File.Exists(P("ForestOverlay(1).dll")));
            Assert.Equal("MZold", Text("ForestOverlay.dll.bak"));
        }

        [Fact]
        public void RenamedPluginEndsAsOneForestOverlayDllAfterTheRestart()
        {
            File.WriteAllBytes(P("ForestOverlay(1).dll"), Dll("old"));
            UpdateStaging.Stage(P("ForestOverlay(1).dll"), Dll("new"));

            Install();

            Assert.Equal("MZnew", Text("ForestOverlay.dll"));
            Assert.Equal("MZold", Text("ForestOverlay.dll.bak"));
            Assert.Equal(new[] { P("ForestOverlay.dll") }, Directory.GetFiles(_dir, "*.dll"));
        }

        [Fact]
        public void ARetriedDownloadAfterTheMoveStillReportsIt()
        {
            File.WriteAllBytes(P("ForestOverlay(1).dll"), Dll("old"));
            UpdateStaging.Stage(P("ForestOverlay(1).dll"), Dll("new"));

            UpdateStaging.Result again = UpdateStaging.Stage(P("ForestOverlay(1).dll"), Dll("new2"));

            Assert.Equal(P("ForestOverlay.dll.bak"), again.Aside);
            Assert.Null(again.AsideError);
            Assert.Equal("MZnew2", Text("ForestOverlay.dll.pending"));
        }

        [Fact]
        public void BothNamesPresentStillEndWithOnePlugin()
        {
            File.WriteAllBytes(P("ForestOverlay.dll"), Dll("other"));
            File.WriteAllBytes(P("ForestOverlay (1).dll"), Dll("running"));
            UpdateStaging.Stage(P("ForestOverlay (1).dll"), Dll("new"));

            Install();

            Assert.Equal("MZnew", Text("ForestOverlay.dll"));
            Assert.Equal(new[] { P("ForestOverlay.dll") }, Directory.GetFiles(_dir, "*.dll"));
        }

        [Fact]
        public void NameChecksIgnoreCase()
        {
            Assert.True(UpdateStaging.IsCanonical(P("forestoverlay.DLL")));
            Assert.Null(UpdateStaging.AsidePath(P("forestoverlay.DLL")));
        }

        [Theory]
        [InlineData("ForestOverlay(1).dll", true)]
        [InlineData("ForestOverlay (2).dll", true)]
        [InlineData("forestoverlay-copy.DLL", true)]
        [InlineData("ForestOverlay.dll", false)]
        [InlineData("ForestOverlay.dll.bak", false)]
        [InlineData("ForestOverlay(1).dll.pending", false)]
        [InlineData("OtherPlugin.dll", false)]
        public void StrayCopyCandidates(string name, bool expected)
        {
            Assert.Equal(expected, UpdateStaging.MaybeStrayCopy(name));
        }

        [Theory]
        [InlineData("ForestOverlay(1).dll.pending", true)]
        [InlineData("ForestOverlay.dll.pending", false)]
        [InlineData("OtherPlugin.dll.pending", false)]
        [InlineData("ForestOverlay(1).dll", false)]
        public void StalePendingFiles(string name, bool expected)
        {
            Assert.Equal(expected, UpdateStaging.IsStalePending(name));
        }
    }
}
