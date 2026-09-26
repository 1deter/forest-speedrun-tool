using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Times the game's own scripts (Next up 6, author: "can patches make
    // the game itself faster?" - measure first). A debug switch in the
    // Debug views tab, never on by default and never kept across launches.
    //
    // WHY: the Perf line says how often a frame is slow and how fast the
    // heap grows, not whose code it is. Unity 5.6 has no profiler in a
    // release build, so the game's per-frame entry points are wrapped in
    // Harmony prefix / postfix pairs that read a Stopwatch and the managed
    // heap (ProfileTable has the caveats of the heap figure). Every 30 s
    // the top methods by time and by allocation go to the log (`Game
    // profile (30 s): ...`) and to the tab.
    //
    // WHAT IS HOOKED: the Unity messages (Update, LateUpdate, FixedUpdate,
    // OnGUI, the render and stay callbacks) of every script type that has a
    // live instance when it is switched on - through base classes, since
    // Unity calls an inherited Update - in the game's assemblies (not
    // Unity's, the runtime's or ours). Plus `Diagnostics.GameProfilerExtra`
    // ("Type::Method", "Type::*", or "*::Name" = that method on every game
    // type, e.g. "*::MoveNext" for the coroutines) to drill in. A hooked
    // method that runs another (StartCoroutine runs the first step at once)
    // counts it in both. Hooking and
    // unhooking run a few milliseconds a frame, so switching it costs no
    // freeze. A method that throws skips its postfix (not counted).
    //
    // Behaviour-preserving: the patches only read the clock and the heap
    // size. It is not practice-only; its own cost (a few hundred ns per
    // hooked call) is in the header line's calls/frame.
    // ------------------------------------------------------------------
    public sealed class GameProfiler
    {
        private const float Interval = 30f;
        private const double BudgetMs = 4.0;
        private const int Top = 12;

        private static readonly string[] Messages =
        {
            "Update", "LateUpdate", "FixedUpdate", "OnGUI",
            "OnWillRenderObject", "OnPreCull", "OnPreRender", "OnPostRender", "OnRenderObject", "OnRenderImage",
            "OnTriggerStay", "OnCollisionStay", "OnAnimatorMove", "OnAnimatorIK"
        };

        // Assemblies never hooked, by name prefix.
        private static readonly string[] Skip =
        {
            "mscorlib", "System", "Mono.", "UnityEngine", "BepInEx", "0Harmony", "HarmonyX", "MonoMod",
            "ForestOverlay", "I18N", "Boo.Lang", "UnityScript.Lang", "Newtonsoft", "RestSharp", "Ionic", "Pathfinding.",
            "SteamworksManaged", "udpkit", "Rewired_Windows"
        };

        private enum Phase { Off, Hooking, On, Unhooking }

        // The patches are static: Harmony calls them with no instance.
        private static readonly ProfileTable Table = new ProfileTable();
        private static readonly Dictionary<long, int> Slots = new Dictionary<long, int>(new LongComparer());
        private static int _unknown;

        private readonly ManualLogSource _log;
        private readonly string _harmonyId;
        private Harmony _harmony;
        private HarmonyMethod _prefix, _postfix;
        private Phase _phase = Phase.Off;
        private readonly List<MethodBase> _pending = new List<MethodBase>();
        private int _next;
        private readonly List<MethodBase> _hooked = new List<MethodBase>();
        private int _failed;
        private string _firstFailure = "";
        private float _phaseStart;
        private float _windowStart;
        private int _frames;

        public string Status { get; private set; }
        /// The last report, one line per entry (for the tab).
        public string LastReport { get; private set; }
        public bool Active { get { return _phase == Phase.Hooking || _phase == Phase.On; } }
        public bool Busy { get { return _phase == Phase.Hooking || _phase == Phase.Unhooking; } }

        public GameProfiler(ManualLogSource log, string harmonyId)
        {
            _log = log;
            _harmonyId = harmonyId + ".profiler";
            Status = "off";
            LastReport = "";
        }

        // ------------------------------------------------------------------
        /// Finds what to hook and starts hooking it over the next frames.
        public void Start(string extra)
        {
            if (_phase != Phase.Off) return;
            try
            {
                if (_harmony == null)
                {
                    _harmony = new Harmony(_harmonyId);
                    _prefix = new HarmonyMethod(typeof(GameProfiler).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic));
                    _postfix = new HarmonyMethod(typeof(GameProfiler).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic));
                }
                Stopwatch sw = Stopwatch.StartNew();
                _pending.Clear();
                _next = 0;
                _failed = 0;
                _firstFailure = "";
                int types;
                string missing;
                CollectTargets(extra, out types, out missing);
                _log.LogInfo("Game profiler: " + _pending.Count + " method(s) to hook on " + types + " live script type(s)" +
                             (missing.Length > 0 ? "; not found: " + missing : "") +
                             " (found in " + sw.ElapsedMilliseconds + " ms).");
                _phase = Phase.Hooking;
                _phaseStart = Time.realtimeSinceStartup;
                Status = "hooking 0 of " + _pending.Count + " methods...";
            }
            catch (Exception ex)
            {
                Status = "could not start: " + ex.Message;
                _log.LogWarning("Game profiler: " + Status);
            }
        }

        /// Unhooks everything over the next frames.
        public void Stop()
        {
            if (_phase == Phase.Off || _phase == Phase.Unhooking) return;
            _pending.Clear();
            _phase = Phase.Unhooking;
            _phaseStart = Time.realtimeSinceStartup;
            _next = 0;
            Status = "unhooking " + _hooked.Count + " methods...";
        }

        /// Plugin shutdown: everything off at once.
        public void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
            _hooked.Clear();
            _phase = Phase.Off;
        }

        /// Once a frame.
        public void Tick()
        {
            switch (_phase)
            {
                case Phase.Hooking: HookSome(); break;
                case Phase.Unhooking: UnhookSome(); break;
                case Phase.On: Count(); break;
            }
        }

        private void HookSome()
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (_next < _pending.Count && sw.Elapsed.TotalMilliseconds < BudgetMs)
            {
                MethodBase m = _pending[_next++];
                try
                {
                    Register(m);
                    _harmony.Patch(m, _prefix, _postfix);
                    _hooked.Add(m);
                }
                catch (Exception ex)
                {
                    _failed++;
                    if (_firstFailure.Length == 0) _firstFailure = Label(m) + ": " + (ex.InnerException ?? ex).Message;
                }
            }
            Status = "hooking " + _next + " of " + _pending.Count + " methods...";
            if (_next < _pending.Count) return;

            _log.LogInfo("Game profiler: hooked " + _hooked.Count + " method(s) in " +
                         (Time.realtimeSinceStartup - _phaseStart).ToString("0.0") + " s" +
                         (_failed > 0 ? ", " + _failed + " failed (first: " + _firstFailure + ")" : "") +
                         "; a report every " + Interval.ToString("0") + " s.");
            _pending.Clear();
            _phase = Phase.On;
            Table.Reset();
            _windowStart = Time.unscaledTime;
            _frames = 0;
            Status = "on: " + _hooked.Count + " methods hooked" + (_failed > 0 ? " (" + _failed + " failed)" : "") +
                     " - first report in " + Interval.ToString("0") + " s";
        }

        private void UnhookSome()
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (_next < _hooked.Count && sw.Elapsed.TotalMilliseconds < BudgetMs)
            {
                MethodBase m = _hooked[_next++];
                try { _harmony.Unpatch(m, HarmonyPatchType.All, _harmonyId); }
                catch (Exception) { }
            }
            Status = "unhooking " + _next + " of " + _hooked.Count + " methods...";
            if (_next < _hooked.Count) return;
            _log.LogInfo("Game profiler: off, " + _hooked.Count + " method(s) unhooked in " +
                         (Time.realtimeSinceStartup - _phaseStart).ToString("0.0") + " s.");
            _hooked.Clear();
            _phase = Phase.Off;
            Status = "off";
        }

        private void Count()
        {
            _frames++;
            float now = Time.unscaledTime;
            if (now - _windowStart < Interval) return;
            float seconds = now - _windowStart;
            List<string> lines = Table.Report(seconds, _frames, Stopwatch.Frequency, Top);
            if (_unknown > 0) lines.Add("unmatched calls: " + _unknown);
            for (int i = 0; i < lines.Count; i++) _log.LogInfo(i == 0 ? lines[i] : "  " + lines[i]);
            if (lines.Count > 0) LastReport = string.Join("\n", lines.ToArray());
            Table.Reset();
            _unknown = 0;
            _windowStart = now;
            _frames = 0;
            Status = "on: " + _hooked.Count + " methods hooked - report every " + Interval.ToString("0") + " s (log and below)";
        }

        // ------------------------------------------------------------------
        private void CollectTargets(string extra, out int typeCount, out string missing)
        {
            HashSet<MethodBase> seen = new HashSet<MethodBase>();
            HashSet<Type> types = new HashSet<Type>();
            UnityEngine.Object[] live = UnityEngine.Object.FindObjectsOfType(typeof(MonoBehaviour));
            for (int i = 0; i < live.Length; i++)
            {
                if (live[i] == null) continue;
                Type t = live[i].GetType();
                if (!types.Add(t) || !GameAssembly(t.Assembly)) continue;
                for (Type b = t; b != null && b != typeof(MonoBehaviour); b = b.BaseType)
                {
                    if (!GameAssembly(b.Assembly) || b.IsGenericType) continue;
                    MethodInfo[] ms = b.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    for (int j = 0; j < ms.Length; j++)
                        if (Array.IndexOf(Messages, ms[j].Name) >= 0 && !seen.Contains(ms[j]) && Hookable(ms[j]))
                        {
                            seen.Add(ms[j]);
                            _pending.Add(ms[j]);
                        }
                }
            }
            typeCount = 0;
            foreach (Type t in types) if (GameAssembly(t.Assembly)) typeCount++;

            missing = "";
            List<string[]> more = ProfileTable.ParseExtra(extra);
            for (int i = 0; i < more.Count; i++)
            {
                // "*::Name": that method on every game type (coroutines:
                // "*::MoveNext"); never "*::*".
                bool every = more[i][0] == "*";
                if (every && more[i][1] == "*") { missing += (missing.Length > 0 ? ", " : "") + "*::* (refused)"; continue; }
                List<Type> ts = every ? AllGameTypes() : new List<Type>();
                if (!every)
                {
                    Type one = FindType(more[i][0]);
                    if (one != null) ts.Add(one);
                }
                int found = 0;
                for (int k = 0; k < ts.Count; k++)
                {
                    if (ts[k].IsGenericType) continue;
                    MethodInfo[] ms;
                    try
                    {
                        ms = ts[k].GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                              BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    }
                    catch (Exception) { continue; }
                    for (int j = 0; j < ms.Length; j++)
                        if ((more[i][1] == "*" || ms[j].Name == more[i][1]) && Hookable(ms[j]))
                        {
                            found++;
                            if (seen.Add(ms[j])) _pending.Add(ms[j]);
                        }
                }
                if (found == 0) missing += (missing.Length > 0 ? ", " : "") + more[i][0] + "::" + more[i][1];
            }
        }

        private static bool GameAssembly(Assembly a)
        {
            string name;
            try { name = a.GetName().Name; }
            catch (Exception) { return false; }
            if (name == "UnityEngine.UI") return true;
            for (int i = 0; i < Skip.Length; i++)
                if (name.StartsWith(Skip[i], StringComparison.Ordinal)) return false;
            return true;
        }

        private static bool Hookable(MethodInfo m)
        {
            if (m.IsAbstract || m.IsGenericMethodDefinition || m.ContainsGenericParameters) return false;
            if (m.DeclaringType == null || m.DeclaringType.IsGenericType) return false;
            try
            {
                MethodBody body = m.GetMethodBody();
                // An empty body (`ret`) has nothing to time.
                if (body == null) return false;
                byte[] il = body.GetILAsByteArray();
                return il != null && il.Length > 2;
            }
            catch (Exception) { return false; }
        }

        private static List<Type> AllGameTypes()
        {
            List<Type> list = new List<Type>();
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < all.Length; a++)
            {
                if (!GameAssembly(all[a])) continue;
                Type[] ts;
                try { ts = all[a].GetTypes(); }
                catch (ReflectionTypeLoadException ex) { ts = ex.Types; }
                catch (Exception) { continue; }
                for (int i = 0; i < ts.Length; i++) if (ts[i] != null) list.Add(ts[i]);
            }
            return list;
        }

        private static Type FindType(string name)
        {
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < all.Length; a++)
            {
                if (!GameAssembly(all[a])) continue;
                try
                {
                    Type t = all[a].GetType(name, false);
                    if (t != null) return t;
                }
                catch (Exception) { }
            }
            for (int a = 0; a < all.Length; a++)
            {
                if (!GameAssembly(all[a])) continue;
                Type[] ts;
                try { ts = all[a].GetTypes(); }
                catch (ReflectionTypeLoadException ex) { ts = ex.Types; }
                catch (Exception) { continue; }
                for (int i = 0; i < ts.Length; i++)
                    if (ts[i] != null && ts[i].Name == name) return ts[i];
            }
            return null;
        }

        private static string Label(MethodBase m)
        {
            return (m.DeclaringType != null ? m.DeclaringType.Name : "?") + "." + m.Name;
        }

        private static void Register(MethodBase m)
        {
            long key = m.MethodHandle.Value.ToInt64();
            if (!Slots.ContainsKey(key)) Slots[key] = Table.Add(Label(m));
        }

        // ------------------------------------------------------------------
        // The patches. Nothing here allocates or throws into the game.
        private struct Sample
        {
            public long Ticks;
            public long Heap;
        }

        private static void Prefix(out Sample __state)
        {
            __state.Heap = GC.GetTotalMemory(false);
            __state.Ticks = Stopwatch.GetTimestamp();
        }

        private static void Postfix(MethodBase __originalMethod, Sample __state)
        {
            long ticks = Stopwatch.GetTimestamp() - __state.Ticks;
            long heap = GC.GetTotalMemory(false) - __state.Heap;
            try
            {
                int slot;
                if (__originalMethod != null && Slots.TryGetValue(__originalMethod.MethodHandle.Value.ToInt64(), out slot))
                    Table.Record(slot, ticks, heap);
                else _unknown++;
            }
            catch (Exception) { _unknown++; }
        }

        // Dictionary<long, ...> without a boxing default comparer on old Mono.
        private sealed class LongComparer : IEqualityComparer<long>
        {
            public bool Equals(long a, long b) { return a == b; }
            public int GetHashCode(long v) { return (int)v ^ (int)(v >> 32); }
        }
    }
}
