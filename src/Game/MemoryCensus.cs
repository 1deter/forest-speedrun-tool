using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Who is keeping the old world alive? A census for the load leak.
    //
    // Runner logs (2026-09-23): every load restore kept ~103 MB of Mono
    // heap (340 -> 2293 MB over 20), and a trip through the title screen
    // gave most of it back. The title scene's ClearStaticVars (IL) clears
    // statics the game scene's copy does not - so the suspects are STATIC
    // references that outlive a same-scene reload.
    //
    // What a leak looks like in Mono under Unity: a static field reaches,
    // through lists, dictionaries, delegates or plain objects, a Unity
    // object that has been destroyed. The native side is gone, the C#
    // wrapper and everything its fields reference stay. So this walks every
    // static reference field of the game's assemblies (and this plugin's),
    // counts destroyed-but-referenced Unity objects per root, and stops at
    // LIVE Unity objects (they belong to the scene; walking into them would
    // walk the whole scene). Compared census to census, the root whose dead
    // count grows by a load's worth each time is the leak.
    //
    // v0.23.2, after v0.23.0/0.23.1 found object counts flat while the heap
    // grew ~122 MB a load: counting objects hides one huge array, so every
    // root now carries an estimated SIZE (arrays and strings exactly, other
    // objects by field count); the live DontDestroyOnLoad objects - which
    // outlive every load - are walked as roots too; more of the game's
    // assemblies are read; and the process's OS thread count is logged
    // (a leaked worker thread is a root no walk can see - gotcha 24).
    //
    // Also counts every Unity object by type (Resources.FindObjectsOfTypeAll
    // - once per load, never on a timer: gotcha 11) for native growth.
    //
    // Reading a static field runs its type's static constructor if it has
    // not run yet. Only the game's own assemblies are read, multiplayer
    // families are skipped, and a type whose initialiser throws is logged
    // and left alone.
    // ------------------------------------------------------------------
    public sealed class MemoryCensus
    {
        private const int MaxDepth = 7;
        private const int RootNodeCap = 150000;
        private const int TotalNodeCap = 1500000;
        private const int TopN = 8;
        private const long MB = 1024 * 1024;
        private const string DdolPrefix = "[DDOL] ";

        private static readonly string[] Assemblies =
        {
            "Assembly-CSharp", "Assembly-CSharp-firstpass", "Assembly-UnityScript", "Assembly-UnityScript-firstpass",
            "PlayMaker", "TheForest.Commons", "ForestOverlay"
        };
        private static readonly string[] SkipPrefixes = { "Bolt", "Coop", "Steam", "UdpKit", "Photon", "Mp", "MP" };

        private sealed class RefEq : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object o) { return RuntimeHelpers.GetHashCode(o); }
        }

        private struct Root
        {
            public string Name;
            public FieldInfo Field;
        }

        private sealed class RootStat
        {
            public int Nodes;
            public int Dead;
            public int Alive;
            public long Bytes;
            public bool Capped;
            public Dictionary<string, int> DeadTypes;
        }

        private struct Item
        {
            public object O;
            public int Depth;
            public Item(object o, int depth) { O = o; Depth = depth; }
        }

        private readonly ManualLogSource _log;
        private List<Root> _roots;
        private readonly Dictionary<Type, FieldInfo[]> _fields = new Dictionary<Type, FieldInfo[]>();
        private readonly Dictionary<Type, int> _objSize = new Dictionary<Type, int>();
        private readonly Dictionary<Type, int> _elemSize = new Dictionary<Type, int>();
        private readonly List<Item> _stack = new List<Item>();
        // Collections that changed while walked (another thread's).
        private int _changed;

        private Dictionary<string, RootStat> _last;
        private Dictionary<Type, int> _lastTypes;
        private int _lastDead;
        private long _lastNodes;
        private long _lastBytes;
        private long _lastDdolBytes;
        private int _lastUnity;
        private long _lastHeap;
        private int _lastThreads;

        public int Runs { get; private set; }

        public MemoryCensus(ManualLogSource log)
        {
            _log = log;
        }

        // ------------------------------------------------------------------
        /// Logs the census and returns a one-line summary for the UI.
        public string Run(string label)
        {
            Stopwatch sw = Stopwatch.StartNew();
            Runs++;
            _changed = 0;

            long heap = GC.GetTotalMemory(true);
            int threads = CountThreads();
            if (_roots == null) FindRoots();

            HashSet<object> seen = new HashSet<object>(new RefEq());
            Dictionary<string, RootStat> stats = new Dictionary<string, RootStat>();
            int budget = TotalNodeCap;
            int dead = 0, alive = 0, failed = 0;
            long nodes = 0, bytes = 0;

            for (int i = 0; i < _roots.Count && budget > 0; i++)
            {
                object value;
                try { value = _roots[i].Field.GetValue(null); }
                catch (Exception ex)
                {
                    failed++;
                    if (failed <= 3) _log.LogWarning("Memory census: skipped " + _roots[i].Name + " (" + (ex.InnerException ?? ex).GetType().Name + ").");
                    continue;
                }
                if (value == null) continue;

                RootStat st = new RootStat();
                Walk(value, st, seen, ref budget, false);
                if (st.Nodes == 0) continue;

                stats[_roots[i].Name] = st;
                nodes += st.Nodes;
                bytes += st.Bytes;
                dead += st.Dead;
                alive += st.Alive;
            }

            UnityEngine.Object[] all = null;
            try { all = Resources.FindObjectsOfTypeAll(typeof(UnityEngine.Object)); }
            catch (Exception ex) { _log.LogWarning("Memory census: object count failed: " + ex.Message); }

            // Live DontDestroyOnLoad objects outlive every load, so their
            // fields are roots like statics. One root per type.
            int ddol = 0;
            long ddolNodes = 0, ddolBytes = 0;
            if (all != null)
            {
                for (int i = 0; i < all.Length && budget > 0; i++)
                {
                    MonoBehaviour mb = all[i] as MonoBehaviour;
                    if (mb == null || !IsDdol(mb)) continue;
                    ddol++;

                    string name = DdolPrefix + mb.GetType().Name;
                    RootStat st;
                    if (!stats.TryGetValue(name, out st)) { st = new RootStat(); stats[name] = st; }
                    int n0 = st.Nodes;
                    long b0 = st.Bytes;
                    int d0 = st.Dead;
                    Walk(mb, st, seen, ref budget, true);
                    ddolNodes += st.Nodes - n0;
                    ddolBytes += st.Bytes - b0;
                    dead += st.Dead - d0;
                }
            }

            // Native side: every Unity object, by type.
            Dictionary<Type, int> types = new Dictionary<Type, int>();
            int unity = 0;
            if (all != null)
            {
                unity = all.Length;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    Type t = all[i].GetType();
                    int n;
                    types.TryGetValue(t, out n);
                    types[t] = n + 1;
                }
            }

            sw.Stop();
            string summary = Report(label, heap, threads, nodes, bytes, dead, alive, ddol, ddolNodes, ddolBytes,
                                    unity, stats, types, budget <= 0, sw.ElapsedMilliseconds);

            _last = stats;
            _lastTypes = types;
            _lastDead = dead;
            _lastNodes = nodes;
            _lastBytes = bytes;
            _lastDdolBytes = ddolBytes;
            _lastUnity = unity;
            _lastHeap = heap;
            _lastThreads = threads;
            return summary;
        }

        private static bool IsDdol(MonoBehaviour mb)
        {
            try
            {
                string asm = mb.GetType().Assembly.GetName().Name;
                if (asm.StartsWith("ForestOverlay", StringComparison.Ordinal) || asm.StartsWith("BepInEx", StringComparison.Ordinal) ||
                    asm.IndexOf("Harmony", StringComparison.Ordinal) >= 0)
                    return false;   // ours: already walked through our statics
                return mb.gameObject.scene.name == "DontDestroyOnLoad";
            }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------
        private void FindRoots()
        {
            _roots = new List<Root>();
            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();

            for (int a = 0; a < loaded.Length; a++)
            {
                string asm = loaded[a].GetName().Name;
                if (Array.IndexOf(Assemblies, asm) < 0) continue;

                Type[] types;
                try { types = loaded[a].GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }

                for (int t = 0; t < types.Length; t++)
                {
                    Type type = types[t];
                    if (type == null || type.ContainsGenericParameters || type.Name.IndexOf('<') >= 0) continue;
                    if (Skipped(type)) continue;

                    FieldInfo[] fields;
                    try { fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
                    catch (Exception) { continue; }

                    for (int f = 0; f < fields.Length; f++)
                    {
                        FieldInfo fi = fields[f];
                        if (fi.IsLiteral) continue;
                        Type ft = fi.FieldType;
                        if (ft.IsValueType || ft.IsPointer) continue;

                        Root r;
                        r.Name = type.Name + "." + fi.Name;
                        r.Field = fi;
                        _roots.Add(r);
                    }
                }
            }

            _log.LogInfo("Memory census: " + _roots.Count + " static reference fields to walk.");
        }

        private static bool Skipped(Type type)
        {
            string name = type.FullName ?? type.Name;
            string ns = type.Namespace ?? "";
            for (int i = 0; i < SkipPrefixes.Length; i++)
                if (name.StartsWith(SkipPrefixes[i], StringComparison.Ordinal) ||
                    type.Name.StartsWith(SkipPrefixes[i], StringComparison.Ordinal) ||
                    ns.StartsWith(SkipPrefixes[i], StringComparison.Ordinal))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------
        /// intoLive: start is a live Unity object whose own fields are the
        /// root (a DontDestroyOnLoad object); otherwise live ones end the walk.
        private void Walk(object start, RootStat st, HashSet<object> seen, ref int budget, bool intoLive)
        {
            _stack.Clear();
            if (intoLive)
            {
                if (!seen.Add(start)) return;
                st.Nodes++;
                st.Bytes += ObjectSize(start.GetType());
                PushFields(start, start.GetType(), 1);
            }
            else _stack.Add(new Item(start, 0));

            while (_stack.Count > 0)
            {
                Item it = _stack[_stack.Count - 1];
                _stack.RemoveAt(_stack.Count - 1);

                object o = it.O;
                if (o == null) continue;

                string s = o as string;
                if (s != null)
                {
                    if (seen.Add(o)) st.Bytes += 20 + 2L * s.Length;
                    continue;
                }

                Type t = o.GetType();
                if (t.IsValueType || o is MemberInfo || o is Assembly || o is Module) continue;
                if (!seen.Add(o)) continue;

                st.Nodes++;
                if (--budget <= 0 || st.Nodes >= RootNodeCap) { st.Capped = true; return; }

                if (o is UnityEngine.Object)
                {
                    // The overloaded == asks the native side: true once destroyed.
                    if ((UnityEngine.Object)o != null) { st.Alive++; continue; }
                    st.Dead++;
                    st.Bytes += ObjectSize(t);
                    if (st.DeadTypes == null) st.DeadTypes = new Dictionary<string, int>();
                    int n;
                    st.DeadTypes.TryGetValue(t.Name, out n);
                    st.DeadTypes[t.Name] = n + 1;
                    // A destroyed object's C# fields are what it keeps alive.
                    if (it.Depth < MaxDepth) PushFields(o, t, it.Depth + 1);
                    continue;
                }

                Array arr = o as Array;
                if (arr != null)
                {
                    Type et = t.GetElementType();
                    st.Bytes += 16 + arr.LongLength * ElementSize(et);
                    if (et.IsValueType || it.Depth >= MaxDepth) continue;
                    foreach (object e in arr) Push(e, it.Depth + 1);
                    continue;
                }

                st.Bytes += ObjectSize(t);
                if (it.Depth >= MaxDepth) continue;
                int next = it.Depth + 1;

                Delegate d = o as Delegate;
                if (d != null)
                {
                    Delegate[] list = d.GetInvocationList();
                    for (int i = 0; i < list.Length; i++) Push(list[i].Target, next);
                    continue;
                }

                // Only the framework's own collections are enumerated: a
                // game type's IEnumerable could be a generator with side
                // effects. Anything else is walked by its fields.
                if (IsFrameworkCollection(t))
                {
                    IDictionary dict = o as IDictionary;
                    if (dict != null)
                    {
                        st.Bytes += 24L * dict.Count;   // buckets + entries, not reached by enumerating
                        // Another thread (the game's workers) can change it
                        // mid-walk: skip it and count it, never fail the census.
                        try { foreach (DictionaryEntry e in dict) { Push(e.Key, next); Push(e.Value, next); } }
                        catch (InvalidOperationException) { _changed++; }
                        continue;
                    }
                    IEnumerable en = o as IEnumerable;
                    if (en != null)
                    {
                        ICollection c = o as ICollection;
                        if (c != null) st.Bytes += 8L * c.Count;   // the backing array
                        try { foreach (object e in en) Push(e, next); }
                        catch (InvalidOperationException) { _changed++; }
                        continue;
                    }
                }

                PushFields(o, t, next);
            }
        }

        private void Push(object o, int depth)
        {
            if (o != null) _stack.Add(new Item(o, depth));
        }

        private void PushFields(object o, Type t, int depth)
        {
            FieldInfo[] fields = FieldsOf(t);
            for (int i = 0; i < fields.Length; i++)
            {
                object v;
                try { v = fields[i].GetValue(o); }
                catch (Exception) { continue; }
                Push(v, depth);
            }
        }

        private static bool IsFrameworkCollection(Type t)
        {
            string ns = t.Namespace;
            return ns != null && ns.StartsWith("System.Collections", StringComparison.Ordinal);
        }

        // Reference-type instance fields (strings included, for their size),
        // base classes included, up to the Unity base types (whose own
        // fields are native bookkeeping).
        private FieldInfo[] FieldsOf(Type t)
        {
            FieldInfo[] cached;
            if (_fields.TryGetValue(t, out cached)) return cached;

            List<FieldInfo> list = new List<FieldInfo>();
            for (Type c = t; c != null && c != typeof(object) && c != typeof(UnityEngine.Object) &&
                              c != typeof(MonoBehaviour) && c != typeof(Behaviour) && c != typeof(Component); c = c.BaseType)
            {
                FieldInfo[] fs = c.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int i = 0; i < fs.Length; i++)
                {
                    Type ft = fs[i].FieldType;
                    if (ft.IsValueType || ft.IsPointer) continue;
                    list.Add(fs[i]);
                }
            }

            cached = list.ToArray();
            _fields[t] = cached;
            return cached;
        }

        // An estimate: a header plus 8 bytes a field. Close enough to tell a
        // 100 MB root from a small one.
        private int ObjectSize(Type t)
        {
            int size;
            if (_objSize.TryGetValue(t, out size)) return size;
            size = 16;
            for (Type c = t; c != null && c != typeof(object); c = c.BaseType)
            {
                try { size += 8 * c.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Length; }
                catch (Exception) { }
            }
            _objSize[t] = size;
            return size;
        }

        private int ElementSize(Type et)
        {
            if (!et.IsValueType) return 8;
            int size;
            if (_elemSize.TryGetValue(et, out size)) return size;
            try { size = Marshal.SizeOf(et); }
            catch (Exception) { size = 8 * Math.Max(1, et.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length); }
            _elemSize[et] = size;
            return size;
        }

        // ------------------------------------------------------------------
        // OS threads of this process (Toolhelp snapshot). A pathfinder or any
        // worker left running by an old world shows up here. -1 if unknown.
        [StructLayout(LayoutKind.Sequential)]
        private struct ThreadEntry32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ThreadID;
            public uint th32OwnerProcessID;
            public int tpBasePri;
            public int tpDeltaPri;
            public uint dwFlags;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
        [DllImport("kernel32.dll")] private static extern bool Thread32First(IntPtr snapshot, ref ThreadEntry32 entry);
        [DllImport("kernel32.dll")] private static extern bool Thread32Next(IntPtr snapshot, ref ThreadEntry32 entry);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();

        private static int CountThreads()
        {
            try
            {
                IntPtr snap = CreateToolhelp32Snapshot(4 /* TH32CS_SNAPTHREAD */, 0);
                if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return -1;
                try
                {
                    uint pid = GetCurrentProcessId();
                    ThreadEntry32 e = new ThreadEntry32();
                    e.dwSize = (uint)Marshal.SizeOf(typeof(ThreadEntry32));
                    int n = 0;
                    for (bool ok = Thread32First(snap, ref e); ok; ok = Thread32Next(snap, ref e))
                        if (e.th32OwnerProcessID == pid) n++;
                    return n;
                }
                finally { CloseHandle(snap); }
            }
            catch (Exception) { return -1; }
        }

        // ------------------------------------------------------------------
        private string Report(string label, long heap, int threads, long nodes, long bytes, int dead, int alive,
                              int ddol, long ddolNodes, long ddolBytes, int unity,
                              Dictionary<string, RootStat> stats, Dictionary<Type, int> types, bool capped, long ms)
        {
            bool first = _last == null;
            StringBuilder sb = new StringBuilder();
            sb.Append("Memory census ").Append(Runs).Append(" after ").Append(label)
              .Append(" (").Append(ms).Append(" ms): Mono heap ").Append(heap / MB).Append(" MB");
            if (!first) sb.Append(" (").Append(Signed((heap - _lastHeap) / MB)).Append(")");
            if (threads >= 0)
            {
                sb.Append(" | threads ").Append(threads);
                if (!first && _lastThreads >= 0) sb.Append(" (").Append(Signed(threads - _lastThreads)).Append(")");
            }
            sb.Append(" | statics reach ").Append(nodes).Append(" objects");
            if (!first) sb.Append(" (").Append(Signed(nodes - _lastNodes)).Append(")");
            sb.Append(", ~").Append(Mb(bytes)).Append(" MB");
            if (!first) sb.Append(" (").Append(SignedMb(bytes - _lastBytes)).Append(")");
            sb.Append(", destroyed Unity objects still referenced ").Append(dead);
            if (!first) sb.Append(" (").Append(Signed(dead - _lastDead)).Append(")");
            sb.Append(", live ").Append(alive);
            sb.Append(" | DontDestroyOnLoad: ").Append(ddol).Append(" objects reach ").Append(ddolNodes)
              .Append(", ~").Append(Mb(ddolBytes)).Append(" MB");
            if (!first) sb.Append(" (").Append(SignedMb(ddolBytes - _lastDdolBytes)).Append(")");
            sb.Append(" | Unity objects ").Append(unity);
            if (!first) sb.Append(" (").Append(Signed(unity - _lastUnity)).Append(")");
            if (capped) sb.Append(" | walk CAPPED at ").Append(TotalNodeCap);
            if (_changed > 0) sb.Append(" | ").Append(_changed).Append(" collection(s) changed while walked, skipped");
            string summary = sb.ToString();
            _log.LogInfo(summary);

            // Holding destroyed objects, most first.
            List<KeyValuePair<string, RootStat>> byDead = new List<KeyValuePair<string, RootStat>>(stats);
            byDead.Sort(delegate(KeyValuePair<string, RootStat> a, KeyValuePair<string, RootStat> b) { return b.Value.Dead.CompareTo(a.Value.Dead); });
            sb.Length = 0;
            for (int i = 0; i < byDead.Count && i < TopN && byDead[i].Value.Dead > 0; i++)
            {
                RootStat was = Previous(byDead[i].Key);
                sb.Append(i == 0 ? "  Holding destroyed objects: " : ", ").Append(byDead[i].Key).Append(' ').Append(byDead[i].Value.Dead);
                if (!first) sb.Append(" (").Append(Signed(byDead[i].Value.Dead - (was != null ? was.Dead : 0))).Append(')');
                if (byDead[i].Value.Capped) sb.Append(" [capped]");
            }
            if (sb.Length > 0) _log.LogInfo(sb.ToString());
            else _log.LogInfo("  Holding destroyed objects: none.");

            // What the top holders' destroyed objects are.
            sb.Length = 0;
            for (int i = 0; i < byDead.Count && i < 4 && byDead[i].Value.Dead > 0; i++)
            {
                sb.Append(i == 0 ? "  Destroyed, by type: " : " | ").Append(byDead[i].Key).Append(": ");
                AppendTop(sb, byDead[i].Value.DeadTypes, 4);
            }
            if (sb.Length > 0) _log.LogInfo(sb.ToString());

            // Largest roots by estimated size.
            List<KeyValuePair<string, RootStat>> bySize = new List<KeyValuePair<string, RootStat>>(stats);
            bySize.Sort(delegate(KeyValuePair<string, RootStat> a, KeyValuePair<string, RootStat> b) { return b.Value.Bytes.CompareTo(a.Value.Bytes); });
            sb.Length = 0;
            for (int i = 0; i < bySize.Count && i < TopN; i++)
            {
                sb.Append(i == 0 ? "  Largest roots: " : ", ").Append(bySize[i].Key).Append(" ~").Append(Mb(bySize[i].Value.Bytes)).Append(" MB");
                if (bySize[i].Value.Capped) sb.Append(" [capped]");
            }
            if (sb.Length > 0) _log.LogInfo(sb.ToString());

            // Growth since the last census, by objects reached and by size.
            if (!first)
            {
                List<KeyValuePair<string, int>> grown = new List<KeyValuePair<string, int>>();
                List<KeyValuePair<string, long>> grownSize = new List<KeyValuePair<string, long>>();
                foreach (KeyValuePair<string, RootStat> kv in stats)
                {
                    RootStat was = Previous(kv.Key);
                    int d = kv.Value.Nodes - (was != null ? was.Nodes : 0);
                    if (d > 0) grown.Add(new KeyValuePair<string, int>(kv.Key, d));
                    long db = kv.Value.Bytes - (was != null ? was.Bytes : 0);
                    if (db >= 64 * 1024) grownSize.Add(new KeyValuePair<string, long>(kv.Key, db));
                }
                grown.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
                sb.Length = 0;
                for (int i = 0; i < grown.Count && i < TopN; i++)
                    sb.Append(i == 0 ? "  Grown since census " + (Runs - 1) + ": " : ", ")
                      .Append(grown[i].Key).Append(" +").Append(grown[i].Value).Append(" (").Append(stats[grown[i].Key].Nodes).Append(')');
                _log.LogInfo(sb.Length > 0 ? sb.ToString() : "  Grown since census " + (Runs - 1) + ": nothing.");

                grownSize.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b) { return b.Value.CompareTo(a.Value); });
                sb.Length = 0;
                for (int i = 0; i < grownSize.Count && i < TopN; i++)
                    sb.Append(i == 0 ? "  Grown in size: " : ", ").Append(grownSize[i].Key).Append(' ').Append(SignedMb(grownSize[i].Value))
                      .Append(" (").Append(Mb(stats[grownSize[i].Key].Bytes)).Append(" MB)");
                _log.LogInfo(sb.Length > 0 ? sb.ToString() : "  Grown in size: nothing over 64 KB.");

                List<KeyValuePair<Type, int>> typeGrowth = new List<KeyValuePair<Type, int>>();
                foreach (KeyValuePair<Type, int> kv in types)
                {
                    int was;
                    _lastTypes.TryGetValue(kv.Key, out was);
                    if (kv.Value > was) typeGrowth.Add(new KeyValuePair<Type, int>(kv.Key, kv.Value - was));
                }
                typeGrowth.Sort(delegate(KeyValuePair<Type, int> a, KeyValuePair<Type, int> b) { return b.Value.CompareTo(a.Value); });
                sb.Length = 0;
                for (int i = 0; i < typeGrowth.Count && i < TopN; i++)
                    sb.Append(i == 0 ? "  Unity objects grown: " : ", ").Append(typeGrowth[i].Key.Name).Append(" +").Append(typeGrowth[i].Value)
                      .Append(" (").Append(types[typeGrowth[i].Key]).Append(')');
                _log.LogInfo(sb.Length > 0 ? sb.ToString() : "  Unity objects grown: none.");
            }

            return summary;
        }

        private static void AppendTop(StringBuilder sb, Dictionary<string, int> counts, int max)
        {
            if (counts == null) return;
            List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>(counts);
            list.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
            for (int i = 0; i < list.Count && i < max; i++)
                sb.Append(i == 0 ? "" : ", ").Append(list[i].Key).Append(' ').Append(list[i].Value);
        }

        private RootStat Previous(string root)
        {
            RootStat r;
            return _last != null && _last.TryGetValue(root, out r) ? r : null;
        }

        private static string Signed(long v)
        {
            return (v >= 0 ? "+" : "") + v;
        }

        private static string Mb(long bytes)
        {
            return (bytes / (double)MB).ToString("0.0");
        }

        private static string SignedMb(long bytes)
        {
            return (bytes >= 0 ? "+" : "-") + Mb(Math.Abs(bytes)) + " MB";
        }
    }
}
