using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ForestOverlay.BridgeMcp;
using Xunit;

namespace ForestOverlay.Tests
{
    // Gotcha 95 (T-0133): the MCP server's stdin is a pipe that never closes, so a
    // helper process it starts must get a closed stdin of its own, or one that
    // reads it (a git, a python) hangs and the tool says "cancelled".
    public class ChildProcessTests
    {
        private static string Python()
        {
            foreach (string name in new[] { "python3", "python" })
            {
                try
                {
                    ProcessStartInfo probe = new ProcessStartInfo(name, "-c \"pass\"") { UseShellExecute = false, CreateNoWindow = true };
                    using (Process p = Process.Start(probe)) { p.WaitForExit(); if (p.ExitCode == 0) return name; }
                }
                catch (System.ComponentModel.Win32Exception) { }
            }
            return null;
        }

        [Fact]
        public void A_child_that_reads_stdin_sees_the_end_of_it_at_once()
        {
            string python = Python();
            if (python == null) return;   // no python on this machine: the source scan below still guards the calls

            ProcessStartInfo psi = new ProcessStartInfo(python, "-c \"import sys; sys.stdout.write('stdin=' + repr(sys.stdin.read()))\"")
            {
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using (Process p = ChildProcess.Start(psi))
            {
                // Without the closed stdin, read() never returns and this wait runs out.
                Assert.True(p.WaitForExit(20000), "the child waited for stdin - it was inherited, not closed");
                Assert.Equal("stdin=''", p.StandardOutput.ReadToEnd());
                Assert.Equal(0, p.ExitCode);
            }
        }

        [Fact]
        public void Every_process_the_server_starts_goes_through_ChildProcess_or_is_a_shell_launch()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "tools", "BridgeMcp"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            List<string> bad = new List<string>();
            int started = 0;
            foreach (string file in Directory.GetFiles(Path.Combine(dir, "tools", "BridgeMcp"), "*.cs"))
            {
                if (Path.GetFileName(file) == "ChildProcess.cs") continue;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.TrimStart().StartsWith("//") || !line.Contains("Process.Start(")) continue;
                    started++;
                    // Allowed: ChildProcess.Start(...), or a launch with the shell (the game; no stdin to redirect).
                    if (line.Contains("ChildProcess.Start(") || line.Contains("UseShellExecute = true")) continue;
                    bad.Add(Path.GetFileName(file) + ":" + (i + 1) + "  " + line.Trim());
                }
            }
            Assert.True(started >= 3, "the scan found only " + started + " Process.Start calls - did the pattern stop matching?");
            Assert.True(bad.Count == 0,
                "WHAT: a helper process started without a closed stdin.\nWHY: the MCP server's stdin never closes; a child that inherits it can wait forever and the tool reports 'cancelled' (gotcha 95).\nFIX: start it with ChildProcess.Start(psi) (tools/BridgeMcp/ChildProcess.cs).\n" + string.Join("\n", bad));
        }
    }
}
