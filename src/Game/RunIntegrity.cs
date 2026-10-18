using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    //  - the game's Assembly-CSharp code as Mono loaded it (the in-memory
    //    image, T-0111), hashed once per session on a worker thread (8 MB,
    //    a few tens of ms - never on the main thread); the file on disk is
    //    hashed too and only noted when it differs;
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
                    string fileHash = "";
                    string fileErr = "";
                    try { fileHash = Sha256(path); }
                    catch (Exception ex) { fileErr = ex.Message; }
                    string why;
                    string loaded = LoadedImageHash(GameAssembly(), out why);
                    why = Data.RunReport.OneLine(why);
                    if (loaded == null && fileHash.Length == 0) throw new InvalidOperationException(why + "; " + fileErr);
                    string hash = loaded ?? fileHash;
                    // A changed game: every type hashed, so the site can name
                    // what changed (run mode phase 3). Before GameHash is
                    // set - the report copies both once it is.
                    if (!Data.RunReport.IsKnownGame(hash))
                    {
                        try
                        {
                            Stopwatch sw = Stopwatch.StartNew();
                            _typeHashes = HashTypes(GameAssembly());
                            log.LogInfo("Run mode: hashed " + _typeHashes.Count + " types of the game's code for the site to compare (" +
                                        sw.ElapsedMilliseconds + " ms).");
                        }
                        catch (Exception ex) { log.LogWarning("Run mode: could not hash the game's types: " + ex.Message); }
                    }
                    _gameFileHash = loaded != null && fileHash.Length > 0 && loaded != fileHash ? fileHash : "";
                    _gameFrom = loaded == null ? why : "";
                    _gameHash = hash;
                    log.LogInfo("Run mode: game code " + (Data.RunReport.IsKnownGame(_gameHash) ? "is the known Steam build" : "is NOT a known build") +
                                " (Assembly-CSharp " + _gameHash.Substring(0, 12) + ", " +
                                (loaded != null ? "loaded image" : "from the file: " + why) + ").");
                    if (_gameFileHash.Length > 0)
                        log.LogWarning("Run mode: the game's file on disk (" + fileHash.Substring(0, 12) +
                                       ") is not the code that was loaded (" + loaded.Substring(0, 12) + ").");
                }
                catch (Exception ex)
                {
                    _gameHash = "error: " + Data.RunReport.OneLine(ex.Message);
                    log.LogWarning("Run mode: could not hash the game's code: " + ex.Message);
                }
            });
        }

        private static volatile List<string> _typeHashes;
        private static volatile string _gameFileHash = "";
        private static volatile string _gameFrom = "";

        /// The file's hash when it differs from GameHash ("" otherwise).
        public static string GameFileHash { get { return _gameFileHash; } }

        /// Why GameHash is the file's, not the loaded image's ("" when it is the image's).
        public static string GameFrom { get { return _gameFrom; } }

        /// SHA-256 of the Assembly-CSharp image Mono holds in memory (T-0111):
        /// MonoImage* is the module's _impl; x64 layout raw_data at +16,
        /// raw_data_len (uint32) at +24 (read live, docs/run-mode.md).
        /// Null with why when it cannot be read; callers fall back to the file.
        public static string LoadedImageHash(Assembly asm, out string why)
        {
            why = "";
            try
            {
                if (IntPtr.Size != 8) { why = "32-bit game"; return null; }
                FieldInfo f = typeof(Module).GetField("_impl", BindingFlags.Instance | BindingFlags.NonPublic);
                if (f == null) { why = "no module handle"; return null; }
                object o = f.GetValue(asm.ManifestModule);
                IntPtr img = o is IntPtr ? (IntPtr)o : IntPtr.Zero;
                if (img == IntPtr.Zero) { why = "module handle is empty"; return null; }
                IntPtr raw = System.Runtime.InteropServices.Marshal.ReadIntPtr(img, 16);
                long len = (uint)System.Runtime.InteropServices.Marshal.ReadInt32(img, 24);
                if (raw == IntPtr.Zero) { why = "image address is empty"; return null; }
                if (len < 1024 * 1024 || len > 256L * 1024 * 1024) { why = "image length " + len + " is not plausible"; return null; }
                byte[] head = new byte[64];
                System.Runtime.InteropServices.Marshal.Copy(raw, head, 0, 64);
                long pe = Data.RunReport.PeOffset(head);
                byte[] sig = new byte[4];
                if (pe > 0 && pe + 4 <= len) System.Runtime.InteropServices.Marshal.Copy(new IntPtr(raw.ToInt64() + pe), sig, 0, 4);
                if (!Data.RunReport.PlausibleImage(head, len, sig)) { why = "image is not a PE file"; return null; }

                byte[] buf = new byte[65536];
                using (System.Security.Cryptography.SHA256 sha = new System.Security.Cryptography.SHA256Managed())
                {
                    for (long off = 0; off < len; off += buf.Length)
                    {
                        int n = (int)Math.Min(buf.Length, len - off);
                        System.Runtime.InteropServices.Marshal.Copy(new IntPtr(raw.ToInt64() + off), buf, 0, n);
                        sha.TransformBlock(buf, 0, n, null, 0);
                    }
                    sha.TransformFinalBlock(buf, 0, 0);
                    return Hex(sha.Hash, 32);
                }
            }
            catch (Exception ex) { why = ex.Message; return null; }
        }

        /// Bridge: `call static:ForestOverlay.Game.RunIntegrity CheckLoadedImage`
        /// -> "<loaded hash> / <file hash>" (main thread, ~50 ms).
        public static string CheckLoadedImage()
        {
            string why;
            string loaded = LoadedImageHash(GameAssembly(), out why);
            return (loaded ?? "none: " + why) + " / " + Sha256(Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll"));
        }

        /// Per-type hashes of the game's code when its file is not a known
        /// build (null otherwise, or until read).
        public static List<string> TypeHashes { get { return _typeHashes; } }

        public static Assembly GameAssembly()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "Assembly-CSharp") return a;
            throw new InvalidOperationException("Assembly-CSharp is not loaded");
        }

        /// "<top-level type> <16 hex>" for every top-level type, nested
        /// types folded in: SHA-256 over the sorted lines "<type>::<method>
        /// <IL hash>" of every declared method and constructor (and one
        /// "<type> type" line per type, so an added empty type shows).
        /// The site's table of the Steam build is made by this same code
        /// (WriteTypeHashes over the bridge), so both sides hash alike.
        public static List<string> HashTypes(Assembly asm)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }
            Dictionary<string, List<string>> byTop = new Dictionary<string, List<string>>();
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                     BindingFlags.Static | BindingFlags.DeclaredOnly;
            List<string> result = new List<string>();
            using (System.Security.Cryptography.SHA256 sha = new System.Security.Cryptography.SHA256Managed())
            {
                foreach (Type t in types)
                {
                    if (t == null) continue;
                    Type top = t;
                    while (top.DeclaringType != null) top = top.DeclaringType;
                    string key = top.FullName ?? top.Name;
                    string name = t.FullName ?? t.Name;
                    List<string> lines;
                    if (!byTop.TryGetValue(key, out lines)) byTop[key] = lines = new List<string>();
                    lines.Add(name + " type");
                    List<MethodBase> methods = new List<MethodBase>();
                    try
                    {
                        methods.AddRange(t.GetMethods(all));
                        methods.AddRange(t.GetConstructors(all));
                    }
                    catch (Exception) { lines.Add(name + " unreadable"); continue; }
                    foreach (MethodBase m in methods)
                    {
                        byte[] il = null;
                        try
                        {
                            MethodBody body = m.GetMethodBody();
                            if (body != null) il = body.GetILAsByteArray();
                        }
                        catch (Exception) { }
                        lines.Add(name + "::" + m.Name + " " + (il == null ? "-" : Hex(sha.ComputeHash(il), 8)));
                    }
                }
                List<string> keys = new List<string>(byTop.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    List<string> lines = byTop[key];
                    lines.Sort(StringComparer.Ordinal);
                    byte[] h = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines.ToArray())));
                    result.Add(key + " " + Hex(h, 8));
                }
            }
            return result;
        }

        /// The Steam build's table for the site (site/ForestSite/GameCode):
        /// `call static:ForestOverlay.Game.RunIntegrity WriteTypeHashes "<path>"`
        /// on a clean install. Main thread, a second or two.
        public static string WriteTypeHashes(string path)
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<string> list = HashTypes(GameAssembly());
            File.WriteAllText(path, "# Assembly-CSharp " + Sha256(Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll")) + "\n" +
                                    string.Join("\n", list.ToArray()) + "\n", new System.Text.UTF8Encoding(false));
            return list.Count + " types in " + sw.ElapsedMilliseconds + " ms -> " + path;
        }

        private static string Hex(byte[] b, int bytes)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(bytes * 2);
            for (int i = 0; i < bytes; i++) sb.Append(b[i].ToString("x2"));
            return sb.ToString();
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
            r.GameFileHash = _gameFileHash;
            r.GameFrom = _gameFrom;
            r.TypeHashes.Clear();
            List<string> types = _typeHashes;   // set before _gameHash
            if (r.GameHash.Length > 0 && types != null) r.TypeHashes.AddRange(types);
            GatherMods(r, ownGuid, ownPath);
        }

        /// The mods half of Gather, shared with Game/ExternalModWatch (T-0308):
        /// other plugins, patchers, loaded code and foreign patches.
        public static void GatherMods(Data.RunReport r, string ownGuid, string ownPath)
        {
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

        /// The Harmony patches: ours counted, BepInEx's own runtime fixes
        /// skipped (Data/PlatformPatches, T-0112), everyone else's listed -
        /// on any method, .NET's included.
        public static void GatherPatches(Data.RunReport r, string ownGuid)
        {
            r.ForeignPatches.Clear();
            r.OwnPatchedMethods = 0;
            try
            {
                string core = Path.Combine(Paths.BepInExRootPath, "core");
                foreach (MethodBase m in Harmony.GetAllPatchedMethods())
                {
                    Patches info = Harmony.GetPatchInfo(m);
                    if (info == null) continue;
                    bool ours = false;
                    ours |= SortPatches(info.Prefixes, m, r, ownGuid, core);
                    ours |= SortPatches(info.Postfixes, m, r, ownGuid, core);
                    ours |= SortPatches(info.Transpilers, m, r, ownGuid, core);
                    ours |= SortPatches(info.Finalizers, m, r, ownGuid, core);
                    ours |= SortPatches(info.ILManipulators, m, r, ownGuid, core);
                    if (ours) r.OwnPatchedMethods++;
                }
            }
            catch (Exception ex) { r.ForeignPatches.Add("could not list the patches: " + ex.Message); }
        }

        /// Lists the foreign patches of one kind on `m`; true if one is ours.
        private static bool SortPatches(IList<Patch> patches, MethodBase m, Data.RunReport r, string ownGuid, string core)
        {
            if (patches == null) return false;
            bool ours = false;
            for (int i = 0; i < patches.Count; i++)
            {
                Patch p = patches[i];
                if (p == null) continue;
                string owner = p.owner;
                if (owner != null && owner.StartsWith(ownGuid)) { ours = true; continue; }
                if (Data.PlatformPatches.IsBepInEx(owner, PatchFile(p), core)) continue;
                string entry = (m.DeclaringType != null ? m.DeclaringType.Name + "." : "") + m.Name + " (by " + owner + ")";
                if (!r.ForeignPatches.Contains(entry)) r.ForeignPatches.Add(entry);
            }
            return ours;
        }

        /// The file the patch method was loaded from ("" when it has none).
        private static string PatchFile(Patch p)
        {
            try
            {
                MethodInfo pm = p.PatchMethod;
                if (pm == null || pm.DeclaringType == null) return "";
                Assembly a = pm.DeclaringType.Assembly;
                if (a is System.Reflection.Emit.AssemblyBuilder) return "";
                return a.Location ?? "";
            }
            catch (Exception) { return ""; }
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
        /// Creative sets GodMode, InfiniteEnergy and NoSurvival itself
        /// (GameMode_Creative; seen live on Slot 1): the mode, not a cheat.
        public static void ReadCheats(List<string> into, bool creative)
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
                if (_cheats[i] == null || (creative && i < 3)) continue;
                try
                {
                    if ((bool)_cheats[i].GetValue(null) && !into.Contains(CheatFields[i])) into.Add(CheatFields[i]);
                }
                catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------
        // GameSetup: how the game was started.
        private static PropertyInfo _isNew, _difficulty, _game, _mp, _creative;
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
            _creative = t.GetProperty("IsCreativeGame", stat);
        }

        /// GameSetup.IsCreativeGame.
        public static bool IsCreative()
        {
            ResolveSetup();
            try { return _creative != null && (bool)_creative.GetValue(null, null); }
            catch (Exception) { return false; }
        }

        /// GameSetup.Difficulty ("Peaceful", "Normal", "Hard"; "" unread).
        public static string Difficulty()
        {
            ResolveSetup();
            try { return _difficulty != null ? _difficulty.GetValue(null, null).ToString() : ""; }
            catch (Exception) { return ""; }
        }

        /// GameSetup.IsMultiplayer.
        public static bool IsMultiplayer()
        {
            ResolveSetup();
            try { return _mp != null && (bool)_mp.GetValue(null, null); }
            catch (Exception) { return false; }
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
