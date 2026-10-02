using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The facts behind a run report (Data/RunReport): what code is
    // running beside the game.
    //
    //  - the game's Assembly-CSharp.dll, hashed once per session on a
    //    worker thread (8 MB, a few tens of ms - never on the main thread);
    //  - BepInEx's loaded plugins other than this one, and patchers other
    //    than our updater;
    //  - assemblies loaded from files outside the game's Managed folder,
    //    BepInEx's core and this plugin (dynamic ones - Harmony's own
    //    generated code - have no file and are skipped);
    //  - every Harmony patch in the process with its owner: anything not
    //    owned by ForestOverlay is another mod changing the game's code
    //    (a "Megan always uses one attack" mod would show here);
    //  - the game's own cheat switches (static Cheats fields).
    //
    // It cannot stop someone who rewrites this plugin to lie - nothing on
    // the runner's PC can (run mode design, author 2026-10-02). It makes
    // a dropped-in cheat DLL or an edited game file show.
    // ------------------------------------------------------------------
    public static class RunIntegrity
    {
        private static volatile string _gameHash = "";
        private static bool _hashStarted;

        /// SHA-256 of Assembly-CSharp.dll: "" until read, "error: ..." if not.
        public static string GameHash { get { return _gameHash; } }

        public static void StartHashing(ManualLogSource log)
        {
            if (_hashStarted) return;
            _hashStarted = true;
            string path = Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll");
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    _gameHash = Sha256(path);
                    log.LogInfo("Run mode: game code " + (Data.RunReport.IsKnownGame(_gameHash) ? "is the known Steam build" : "is NOT a known build") +
                                " (Assembly-CSharp " + _gameHash.Substring(0, 12) + ").");
                }
                catch (Exception ex)
                {
                    _gameHash = "error: " + ex.Message;
                    log.LogWarning("Run mode: could not hash the game's code: " + ex.Message);
                }
            });
        }

        public static string Sha256(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (System.Security.Cryptography.SHA256 sha = new System.Security.Cryptography.SHA256Managed())
            {
                byte[] hash = sha.ComputeHash(fs);
                System.Text.StringBuilder sb = new System.Text.StringBuilder(64);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// Fills the report's mods, code and patches (main thread; a few ms).
        public static void Gather(Data.RunReport r, string ownGuid, string ownPath)
        {
            r.GameHash = _gameHash;
            r.OtherPlugins.Clear();
            r.OtherPatchers.Clear();
            r.OtherCode.Clear();

            List<string> pluginFiles = new List<string>();
            try
            {
                foreach (PluginInfo p in Chainloader.PluginInfos.Values)
                {
                    if (p == null || p.Metadata == null) continue;
                    if (p.Location != null) pluginFiles.Add(Norm(p.Location));
                    if (p.Metadata.GUID == ownGuid) continue;
                    r.OtherPlugins.Add(p.Metadata.Name + " " + p.Metadata.Version + " (" + Path.GetFileName(p.Location ?? "?") + ")");
                }
            }
            catch (Exception ex) { r.OtherPlugins.Add("could not list the plugins: " + ex.Message); }

            try
            {
                if (Directory.Exists(Paths.PatcherPluginPath))
                    foreach (string f in Directory.GetFiles(Paths.PatcherPluginPath, "*.dll", SearchOption.AllDirectories))
                        if (!string.Equals(Path.GetFileName(f), "ForestOverlay.Updater.dll", StringComparison.OrdinalIgnoreCase))
                            r.OtherPatchers.Add(Path.GetFileName(f));
            }
            catch (Exception ex) { r.OtherPatchers.Add("could not list the patchers: " + ex.Message); }

            string managed = Norm(Paths.ManagedPath);
            string core = Norm(Path.Combine(Paths.BepInExRootPath, "core"));
            string own = Norm(ownPath);
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string loc;
                try
                {
                    if (a is System.Reflection.Emit.AssemblyBuilder) continue;
                    loc = a.Location;
                }
                catch (Exception) { continue; }
                if (string.IsNullOrEmpty(loc)) continue;
                loc = Norm(loc);
                if (loc.StartsWith(managed) || loc.StartsWith(core) || loc == own) continue;
                if (pluginFiles.Contains(loc)) continue;   // listed as a plugin already
                if (string.Equals(Path.GetFileName(loc), "ForestOverlay.Updater.dll", StringComparison.OrdinalIgnoreCase)) continue;
                r.OtherCode.Add(Path.GetFileName(loc));
            }

            GatherPatches(r, ownGuid);
        }

        /// The Harmony patches: ours counted, everyone else's listed.
        public static void GatherPatches(Data.RunReport r, string ownGuid)
        {
            r.ForeignPatches.Clear();
            r.OwnPatchedMethods = 0;
            try
            {
                foreach (MethodBase m in Harmony.GetAllPatchedMethods())
                {
                    // BepInEx patches .NET itself (Console, Assembly, Trace -
                    // seen live, v0.24.207): only the game's and Unity's code
                    // count.
                    if (IsRuntimeCode(m)) continue;
                    Patches info = Harmony.GetPatchInfo(m);
                    if (info == null) continue;
                    bool ours = false;
                    foreach (string owner in info.Owners)
                    {
                        if (owner != null && owner.StartsWith(ownGuid)) { ours = true; continue; }
                        string entry = (m.DeclaringType != null ? m.DeclaringType.Name + "." : "") + m.Name + " (by " + owner + ")";
                        if (!r.ForeignPatches.Contains(entry)) r.ForeignPatches.Add(entry);
                    }
                    if (ours) r.OwnPatchedMethods++;
                }
            }
            catch (Exception ex) { r.ForeignPatches.Add("could not list the patches: " + ex.Message); }
        }

        private static bool IsRuntimeCode(MethodBase m)
        {
            Type t = m.DeclaringType;
            if (t == null) return false;
            string asm = t.Assembly.GetName().Name;
            return asm == "mscorlib" || asm == "System" || asm.StartsWith("System.") || asm.StartsWith("Mono.");
        }

        /// How many methods are patched now - a cheap "did anything change".
        public static int PatchedCount()
        {
            try
            {
                int n = 0;
                foreach (MethodBase m in Harmony.GetAllPatchedMethods()) n++;
                return n;
            }
            catch (Exception) { return -1; }
        }

        // ------------------------------------------------------------------
        // The game's cheat switches (static fields of Cheats). Creative and
        // the peaceful / vegan modes are game settings, said by `started`.
        private static readonly string[] CheatFields = { "GodMode", "InfiniteEnergy", "NoSurvival", "UnlimitedHairspray", "DebugConsole" };
        private static FieldInfo[] _cheats;

        /// Adds the names of the cheats that are on now to `into` (once each).
        public static void ReadCheats(List<string> into)
        {
            if (_cheats == null)
            {
                _cheats = new FieldInfo[CheatFields.Length];
                Type t = GameBridge.FindGameType("Cheats");
                if (t != null)
                    for (int i = 0; i < CheatFields.Length; i++)
                        _cheats[i] = t.GetField(CheatFields[i], BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            for (int i = 0; i < _cheats.Length; i++)
            {
                if (_cheats[i] == null) continue;
                try
                {
                    if ((bool)_cheats[i].GetValue(null) && !into.Contains(CheatFields[i])) into.Add(CheatFields[i]);
                }
                catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------
        // GameSetup: how the game was started.
        private static PropertyInfo _isNew, _difficulty, _game, _mp;
        private static bool _setupResolved;

        private static void ResolveSetup()
        {
            if (_setupResolved) return;
            _setupResolved = true;
            Type t = GameBridge.FindGameType("TheForest.Utils.GameSetup");
            if (t == null) return;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            _isNew = t.GetProperty("IsNewGame", stat);
            _difficulty = t.GetProperty("Difficulty", stat);
            _game = t.GetProperty("Game", stat);
            _mp = t.GetProperty("IsMultiplayer", stat);
        }

        /// GameSetup.IsNewGame: the loaded game began as New Game.
        public static bool IsNewGame()
        {
            ResolveSetup();
            try { return _isNew != null && (bool)_isNew.GetValue(null, null); }
            catch (Exception) { return false; }
        }

        /// "Normal", "Hard, Creative", "Normal, multiplayer" ...
        public static string Describe()
        {
            ResolveSetup();
            List<string> parts = new List<string>();
            try
            {
                if (_difficulty != null) parts.Add(_difficulty.GetValue(null, null).ToString());
                if (_game != null)
                {
                    string g = _game.GetValue(null, null).ToString();
                    if (g != "Standard") parts.Add(g);
                }
                if (_mp != null && (bool)_mp.GetValue(null, null)) parts.Add("multiplayer");
            }
            catch (Exception) { }
            return string.Join(", ", parts.ToArray());
        }

        private static string Norm(string p)
        {
            try { return Path.GetFullPath(p).Replace('\\', '/').ToLowerInvariant(); }
            catch (Exception) { return (p ?? "").Replace('\\', '/').ToLowerInvariant(); }
        }
    }
}
