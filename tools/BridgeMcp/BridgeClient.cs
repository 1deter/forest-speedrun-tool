using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ForestOverlay.BridgeMcp
{
    // ------------------------------------------------------------------
    // Where the game lives. FOREST_ROOT is a User-scope variable on the
    // author's machine and shells spawned by tooling do not inherit it,
    // so the registry copy is read too (as scripts/bridge.sh does).
    // FOREST_BRIDGE overrides the bridge folder alone.
    // ------------------------------------------------------------------
    internal sealed class ForestPaths
    {
        public const string DefaultRoot = @"G:\SteamLibrary\steamapps\common\The Forest";
        public const string SteamAppId = "242760";

        public readonly string Root;
        public readonly string BepInEx;
        public readonly string Plugins;
        public readonly string OverlayConfig;   // BepInEx/config/ForestOverlay
        public readonly string Bridge;
        public readonly string LogOutput;
        public readonly string KeptLogs;
        public readonly string ConfigFile;

        public ForestPaths()
        {
            Root = Env("FOREST_ROOT") ?? DefaultRoot;
            BepInEx = Path.Combine(Root, "BepInEx");
            Plugins = Path.Combine(BepInEx, "plugins");
            OverlayConfig = Path.Combine(BepInEx, "config", "ForestOverlay");
            Bridge = Env("FOREST_BRIDGE") ?? Path.Combine(OverlayConfig, "bridge");
            LogOutput = Path.Combine(BepInEx, "LogOutput.log");
            KeptLogs = Path.Combine(OverlayConfig, "logs");
            ConfigFile = Path.Combine(BepInEx, "config", "com.deter.forestoverlay.cfg");
        }

        public static string Env(string name)
        {
            string v = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(v) && OperatingSystem.IsWindows())
                v = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            return string.IsNullOrEmpty(v) ? null : v;
        }

        /// The `TestBridge = ...` value in the plugin's .cfg, or null.
        public string TestBridgeSetting()
        {
            try
            {
                foreach (string line in File.ReadAllLines(ConfigFile))
                {
                    string t = line.Trim();
                    if (t.StartsWith("TestBridge", StringComparison.Ordinal) && t.Contains('='))
                        return t.Substring(t.IndexOf('=') + 1).Trim();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        /// A file's text while the game may be writing it.
        public static string ReadShared(string path)
        {
            using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader r = new StreamReader(fs, Encoding.UTF8);
            return r.ReadToEnd();
        }
    }

    internal static class GameProcess
    {
        public const string Name = "TheForest";

        public static Process Find()
        {
            Process[] all = Process.GetProcessesByName(Name);
            for (int i = 1; i < all.Length; i++) all[i].Dispose();
            return all.Length > 0 ? all[0] : null;
        }

        public static bool Running
        {
            get
            {
                using Process p = Find();
                return p != null;
            }
        }
    }

    internal sealed class BridgeReply
    {
        /// The game took in.txt.
        public bool Delivered;
        /// Every command's reply arrived (the end marker was seen).
        public bool Complete;
        /// Why the call fell short; null when Complete.
        public string Problem;
        public string Raw = "";
        public BridgeText.Transcript Transcript = new BridgeText.Transcript();

        /// The transcript as the model should read it.
        public string Text(bool withHeaders)
        {
            StringBuilder sb = new StringBuilder();
            foreach (object item in Transcript.Items)
            {
                BridgeText.Block b = item as BridgeText.Block;
                if (b == null) { sb.Append((string)item).Append('\n'); continue; }
                if (withHeaders) sb.Append("> ").Append(b.Command).Append('\n');
                foreach (string l in b.Lines) sb.Append(l).Append('\n');
                if (b.Error != null) sb.Append("error: ").Append(b.Error).Append('\n');
                else if (!b.Closed) sb.Append("(no reply yet - still running in game)\n");
                else if (withHeaders) sb.Append("ok (").Append(b.Took).Append(")\n");
            }
            if (Problem != null) sb.Append(Problem).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }
    }

    // ------------------------------------------------------------------
    // The file transport, as scripts/bridge.sh: write the batch plus an
    // `echo <marker>` into in.tmp, rename it to in.txt, and read out.txt
    // from its old end until the marker comes back. One batch at a time.
    // Unlike bridge.sh, an in.txt the game never read is withdrawn on
    // timeout - left there, it would run on the next launch.
    // ------------------------------------------------------------------
    internal sealed class BridgeClient
    {
        private readonly ForestPaths _paths;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public BridgeClient(ForestPaths paths) { _paths = paths; }

        private string InPath { get { return Path.Combine(_paths.Bridge, "in.txt"); } }
        private string OutPath { get { return Path.Combine(_paths.Bridge, "out.txt"); } }
        private string OldPath { get { return Path.Combine(_paths.Bridge, "out.old.txt"); } }

        /// Why the game is not reading, in one line.
        public string NotReadingReason()
        {
            if (!GameProcess.Running) return "The Forest is not running (no TheForest.exe) - the `game` tool launches it.";
            string setting = _paths.TestBridgeSetting();
            if (setting != null && !setting.Equals("true", StringComparison.OrdinalIgnoreCase))
                return "the game is running but the bridge is off (TestBridge = " + setting +
                       " in " + _paths.ConfigFile + ") - tick Settings -> Test bridge in game.";
            return "the game is running but did not read in.txt - still loading, frozen, or the bridge is off (Settings -> Test bridge).";
        }

        /// Sends commands and waits for every reply. `readWithin`: how long
        /// the game may take to pick in.txt up (long after a launch).
        public async Task<BridgeReply> Send(IList<string> commands, TimeSpan timeout, CancellationToken ct,
                                            TimeSpan? readWithin = null)
        {
            await _gate.WaitAsync(ct);
            try { return await SendLocked(commands, timeout, readWithin ?? TimeSpan.FromSeconds(15), ct); }
            finally { _gate.Release(); }
        }

        private async Task<BridgeReply> SendLocked(IList<string> commands, TimeSpan timeout, TimeSpan readWithin,
                                                   CancellationToken ct)
        {
            BridgeReply reply = new BridgeReply();
            if (!GameProcess.Running)
            {
                reply.Problem = NotReadingReason();
                return reply;
            }
            Directory.CreateDirectory(_paths.Bridge);

            // A batch from another caller (bridge.sh, another session) not
            // taken yet.
            for (int i = 0; i < 20 && File.Exists(InPath); i++) await Task.Delay(250, ct);
            if (File.Exists(InPath))
            {
                reply.Problem = "in.txt from an earlier call is still waiting: " + NotReadingReason();
                return reply;
            }

            string marker = "__mcp_done_" + Guid.NewGuid().ToString("N");
            long start = Length(OutPath);
            bool rotated = false;

            StringBuilder sb = new StringBuilder();
            foreach (string c in commands)
            {
                string line = BridgeText.Line(c);
                if (line.Length > 0) sb.Append(line).Append('\n');
            }
            sb.Append("echo ").Append(marker).Append('\n');
            string tmp = Path.Combine(_paths.Bridge, "in.tmp");
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            File.Move(tmp, InPath, true);

            Stopwatch sw = Stopwatch.StartNew();
            TimeSpan lastAlive = TimeSpan.Zero;
            string text = "";
            try
            {
                while (true)
                {
                    await Task.Delay(120, ct);

                    if (!reply.Delivered && !File.Exists(InPath)) reply.Delivered = true;

                    long len = Length(OutPath);
                    if (!rotated && len < start) rotated = true;   // moved to out.old.txt past 4 MB
                    text = rotated ? ReadFrom(OldPath, start) + ReadFrom(OutPath, 0) : ReadFrom(OutPath, start);

                    if (text.Contains(marker, StringComparison.Ordinal))
                    {
                        reply.Delivered = reply.Complete = true;
                        break;
                    }

                    if (!reply.Delivered && sw.Elapsed > readWithin)
                    {
                        Withdraw();
                        reply.Problem = "not delivered: " + NotReadingReason();
                        break;
                    }
                    if (sw.Elapsed > timeout)
                    {
                        reply.Problem = "timed out after " + (int)timeout.TotalSeconds +
                                        " s - the rest still runs in game; later replies land in " + OutPath;
                        break;
                    }
                    if (sw.Elapsed - lastAlive > TimeSpan.FromSeconds(2))
                    {
                        lastAlive = sw.Elapsed;
                        if (GameProcess.Running) continue;
                        Withdraw();
                        reply.Problem = "the game exited while the commands ran.";
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (!reply.Delivered) Withdraw();
                throw;
            }

            reply.Raw = text;
            reply.Transcript = BridgeText.Parse(text, marker);
            return reply;
        }

        private void Withdraw()
        {
            try { if (File.Exists(InPath)) File.Delete(InPath); } catch (IOException) { }
        }

        private static long Length(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch (IOException) { return 0; }
        }

        private static string ReadFrom(string path, long offset)
        {
            try
            {
                if (!File.Exists(path)) return "";
                using FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (offset > fs.Length) return "";
                fs.Seek(offset, SeekOrigin.Begin);
                byte[] buf = new byte[fs.Length - offset];
                int read = 0;
                while (read < buf.Length)
                {
                    int n = fs.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                return Encoding.UTF8.GetString(buf, 0, read);
            }
            catch (IOException) { return ""; }
        }

        /// The newest "bridge on" banner's plugin version in out.txt.
        public string PluginVersionFromBanner()
        {
            try { return File.Exists(OutPath) ? BridgeText.BannerVersion(ForestPaths.ReadShared(OutPath)) : null; }
            catch (IOException) { return null; }
        }
    }
}
