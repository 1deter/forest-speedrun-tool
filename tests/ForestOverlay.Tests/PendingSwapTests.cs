using System;
using System.Collections.Generic;
using System.IO;
using ForestOverlay.Updater;
using Xunit;

namespace ForestOverlay.Tests
{
    // The updater is the one component that can leave a runner with no
    // working plugin, so every path must end with a loadable DLL.
    public class PendingSwapTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _dll;
        private readonly List<string> _log = new List<string>();

        public PendingSwapTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "fo-swap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _dll = Path.Combine(_dir, "ForestOverlay.dll");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static byte[] Dll(string marker) { return System.Text.Encoding.ASCII.GetBytes("MZ" + marker); }
        private string Text(string path) { return System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path)); }
        private PendingSwap.Result Apply(Func<string, bool> valid = null) { return PendingSwap.Apply(_dll, valid, _log.Add); }

        [Fact]
        public void NothingStagedChangesNothing()
        {
            File.WriteAllBytes(_dll, Dll("old"));

            Assert.Equal(PendingSwap.Result.NothingPending, Apply());
            Assert.Equal("MZold", Text(_dll));
        }

        [Fact]
        public void StagedUpdateReplacesThePluginAndKeepsABackup()
        {
            File.WriteAllBytes(_dll, Dll("old"));
            File.WriteAllBytes(_dll + ".pending", Dll("new"));

            Assert.Equal(PendingSwap.Result.Applied, Apply());
            Assert.Equal("MZnew", Text(_dll));
            Assert.Equal("MZold", Text(_dll + ".bak"));
            Assert.False(File.Exists(_dll + ".pending"));
        }

        [Fact]
        public void AnOlderBackupIsReplaced()
        {
            File.WriteAllBytes(_dll, Dll("v2"));
            File.WriteAllBytes(_dll + ".bak", Dll("v1"));
            File.WriteAllBytes(_dll + ".pending", Dll("v3"));

            Assert.Equal(PendingSwap.Result.Applied, Apply());
            Assert.Equal("MZv3", Text(_dll));
            Assert.Equal("MZv2", Text(_dll + ".bak"));
        }

        [Fact]
        public void WorksWhenThePluginIsMissing()
        {
            File.WriteAllBytes(_dll + ".pending", Dll("new"));

            Assert.Equal(PendingSwap.Result.Applied, Apply());
            Assert.Equal("MZnew", Text(_dll));
        }

        [Fact]
        public void AnHtmlErrorPageIsRejectedAndThePluginKept()
        {
            File.WriteAllBytes(_dll, Dll("old"));
            File.WriteAllText(_dll + ".pending", "<html>rate limited</html>");

            Assert.Equal(PendingSwap.Result.Rejected, Apply());
            Assert.Equal("MZold", Text(_dll));
            Assert.True(File.Exists(_dll + ".rejected"));
            Assert.False(File.Exists(_dll + ".pending"));
        }

        [Fact]
        public void TheValidatorCanRejectADll()
        {
            File.WriteAllBytes(_dll, Dll("old"));
            File.WriteAllBytes(_dll + ".pending", Dll("someone else's"));

            Assert.Equal(PendingSwap.Result.Rejected, Apply(p => false));
            Assert.Equal("MZold", Text(_dll));
        }

        [Fact]
        public void AThrowingValidatorCountsAsInvalid()
        {
            File.WriteAllBytes(_dll, Dll("old"));
            File.WriteAllBytes(_dll + ".pending", Dll("new"));

            Assert.Equal(PendingSwap.Result.Rejected, Apply(p => throw new BadImageFormatException()));
            Assert.Equal("MZold", Text(_dll));
        }

        // A plugin that ran as ForestOverlay(1).dll moved itself to the
        // backup when it staged (v0.23.7): no ForestOverlay.dll here.
        [Fact]
        public void ARejectedUpdatePutsTheBackupBackWhenThePluginIsMissing()
        {
            File.WriteAllBytes(_dll + ".bak", Dll("old"));
            File.WriteAllText(_dll + ".pending", "<html>rate limited</html>");

            Assert.Equal(PendingSwap.Result.Rejected, Apply());
            Assert.Equal("MZold", Text(_dll));
            Assert.False(File.Exists(_dll + ".bak"));
        }

        [Fact]
        public void AFailedMovePutsTheBackupBackWhenThePluginIsMissing()
        {
            File.WriteAllBytes(_dll + ".bak", Dll("old"));
            File.WriteAllBytes(_dll + ".pending", Dll("new"));

            using (new FileStream(_dll + ".pending", FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                PendingSwap.Result r = Apply();
                if (!OperatingSystem.IsWindows()) return;   // POSIX allows the move

                Assert.Equal(PendingSwap.Result.Failed, r);
            }

            Assert.Equal("MZold", Text(_dll));
        }

        [Fact]
        public void AFailedMoveRestoresThePlugin()
        {
            File.WriteAllBytes(_dll, Dll("old"));
            File.WriteAllBytes(_dll + ".pending", Dll("new"));

            // Hold the pending file open without delete sharing, so moving
            // it into place fails after the plugin has been backed up.
            using (new FileStream(_dll + ".pending", FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                PendingSwap.Result r = Apply();
                if (!OperatingSystem.IsWindows()) return;   // POSIX allows the move

                Assert.Equal(PendingSwap.Result.Failed, r);
            }

            Assert.Equal("MZold", Text(_dll));
        }
    }
}
