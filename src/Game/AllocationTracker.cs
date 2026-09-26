using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BepInEx.Logging;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Exact managed allocation, by type and by main-thread method (Next up
    // 6: fewer allocations = fewer ~90 ms GC pauses).
    //
    // WHY: GC.GetTotalMemory moves in 4 KB heap blocks and misses
    // allocation into free space, so the game profiler's heap column was a
    // noisy ranking. The game's mono.dll exports Mono's profiler API
    // (mono_profiler_install / _install_allocation / _set_events), and its
    // mono_profiler_install prepends to a list of profilers (read from its
    // machine code, 2026-09-26) - adding one is safe. Every allocation then
    // calls OnAlloc with the new object and its class.
    //
    // COVERAGE: arrays, strings and mono_object_new_specific always report
    // once the event is on. A plain `new T()` in a method the JIT compiled
    // BEFORE the event was on takes Mono's fast path, which never reports -
    // so `Diagnostics.AllocationTrackerAtStartup` installs it in Awake
    // (before the game's scene code is compiled; costs a callback per
    // allocation for the whole session). Installed later, the report says
    // "late" and undercounts plain objects.
    //
    // THE CALLBACK must never allocate (it would call itself) or throw:
    // fixed arrays, Interlocked, Marshal reads. It is compiled (called once)
    // before it is installed, so no JIT runs inside an allocation. Class
    // names are read only when a report is built, on the main thread.
    //
    // MainBytes: bytes allocated on the main thread while counting. The
    // game profiler reads it around each hooked call instead of the heap
    // size - exact allocation per method.
    //
    // Diagnostic only: it reads, never changes, what the game allocates.
    // ------------------------------------------------------------------
    public static class AllocationTracker
    {
        private const string Mono = "mono.dll";
        private const int EventAllocations = 1 << 7;   // MONO_PROFILE_ALLOCATIONS
        private const int Capacity = 1 << 13;          // classes; power of two
        private const int Top = 15;

        // MonoArray on 64-bit: vtable, sync, bounds, max_length, then data
        // (mono_array_new_specific writes the length at +0x18). MonoString:
        // vtable, sync, int length at +0x10, chars at +0x14. Checked
        // against a pinned array and string at install.
        private const int ArrayLengthOffset = 0x18;
        private const int ArrayDataOffset = 0x20;
        private const int StringLengthOffset = 0x10;
        private const int StringDataOffset = 0x14;

        private const byte KindFixed = 0, KindArray = 1, KindString = 2;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AllocFn(IntPtr profiler, IntPtr obj, IntPtr klass);

        [DllImport(Mono)] private static extern void mono_profiler_install(IntPtr profiler, IntPtr shutdown);
        [DllImport(Mono)] private static extern void mono_profiler_install_allocation(IntPtr callback);
        [DllImport(Mono)] private static extern void mono_profiler_set_events(int events);
        [DllImport(Mono)] private static extern int mono_class_get_rank(IntPtr klass);
        [DllImport(Mono)] private static extern int mono_class_instance_size(IntPtr klass);
        [DllImport(Mono)] private static extern int mono_array_element_size(IntPtr klass);
        [DllImport(Mono)] private static extern IntPtr mono_get_string_class();
        [DllImport(Mono)] private static extern IntPtr mono_class_get_type(IntPtr klass);
        [DllImport(Mono)] private static extern IntPtr mono_type_get_name(IntPtr type);
        [DllImport(Mono)] private static extern IntPtr mono_class_from_mono_type(IntPtr type);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);

        // The table: open addressing on the MonoClass pointer.
        private static IntPtr[] _keys;
        private static byte[] _kind;
        private static int[] _size;
        private static long[] _count;
        private static long[] _bytes;
        private static int _overflow;

        private static AllocFn _callback;      // kept: the runtime holds only its pointer
        private static IntPtr _stringClass;
        private static uint _mainThread;
        private static volatile bool _counting;
        private static bool _sizesOk;
        private static bool _attempted;

        private static long _mainCount;
        private static long _startTicks;
        private static readonly Dictionary<long, string> Names = new Dictionary<long, string>();

        /// Bytes allocated on the main thread since counting started.
        public static long MainBytes;

        public static bool Installed { get; private set; }
        /// Installed at startup: plain objects are counted too.
        public static bool Early { get; private set; }
        public static bool Counting { get { return _counting; } }
        public static string Status = "off";

        // ------------------------------------------------------------------
        /// Installs the profiler (once per launch; Mono has no uninstall).
        /// Must run on the main thread. Returns false with Status set on
        /// failure.
        public static bool Install(ManualLogSource log, bool early)
        {
            if (Installed) return true;
            if (_attempted) return false;   // a profiler cannot be removed: never twice
            _attempted = true;
            try
            {
                _keys = new IntPtr[Capacity];
                _kind = new byte[Capacity];
                _size = new int[Capacity];
                _count = new long[Capacity];
                _bytes = new long[Capacity];
                _mainThread = GetCurrentThreadId();
                _stringClass = mono_get_string_class();
                _sizesOk = CheckLayout(log);

                // Compile the callback and everything it calls now, outside
                // any allocation: a real class through each path.
                _counting = true;
                OnAlloc(IntPtr.Zero, IntPtr.Zero, ClassOf(typeof(object)));
                WarmUp();
                _counting = false;
                Clear();

                _callback = OnAlloc;
                IntPtr fn = Marshal.GetFunctionPointerForDelegate(_callback);
                IntPtr profiler = Marshal.AllocHGlobal(16);
                mono_profiler_install(profiler, IntPtr.Zero);
                mono_profiler_install_allocation(fn);
                mono_profiler_set_events(EventAllocations);
                string flag = EnableProfileAllocs();
                if (flag.Length > 0)
                {
                    Status = "installed, but allocations will not report: " + flag;
                    log.LogWarning("Allocation tracker: " + Status);
                    return false;
                }

                Installed = true;
                Early = early;
                Status = "installed" + (early ? " at startup" : " late (plain objects from code compiled earlier are not seen)") +
                         (_sizesOk ? "" : "; array / string sizes unknown (layout check failed) - counts only");
                log.LogInfo("Allocation tracker: " + Status + ".");
                return true;
            }
            catch (Exception ex)
            {
                _counting = false;
                Status = "could not install: " + ex.Message;
                log.LogWarning("Allocation tracker: " + Status);
                return false;
            }
        }

        // Mono's `profile_allocs` (object.c): the allocators report only
        // while it is set, and mono_class_get_allocation_ftn clears it for
        // good the first time the JIT compiles an allocation with the event
        // off - long before any plugin loads (seen 2026-09-26: 0 counted).
        // Its address is read from two allocators' `cmp [rip+X], 0` and
        // set back to 1; they must agree, or nothing is written.
        private static string EnableProfileAllocs()
        {
            IntPtr module = GetModuleHandle(Mono);
            if (module == IntPtr.Zero) return "mono.dll not found";
            long a = FlagIn(GetProcAddress(module, "mono_object_new_alloc_specific"));
            long b = FlagIn(GetProcAddress(module, "mono_array_new_specific"));
            if (a == 0 || a != b) return "the allocation flag was not found in this mono.dll (" + a.ToString("X") + " / " + b.ToString("X") + ")";
            Marshal.WriteInt32(new IntPtr(a), 1);
            return "";
        }

        // The first `cmp dword ptr [rip+disp32], 0` (83 3D d32 00) in the
        // function's first 0x200 bytes (object +0x5d, array +0xca).
        private static long FlagIn(IntPtr fn)
        {
            if (fn == IntPtr.Zero) return 0;
            byte[] code = new byte[0x200];
            Marshal.Copy(fn, code, 0, code.Length);
            for (int i = 0; i + 7 < code.Length; i++)
            {
                if (code[i] != 0x83 || code[i + 1] != 0x3D || code[i + 6] != 0x00) continue;
                int disp = BitConverter.ToInt32(code, i + 2);
                return fn.ToInt64() + i + 7 + disp;
            }
            return 0;
        }

        private static IntPtr ClassOf(Type t)
        {
            return mono_class_from_mono_type(t.TypeHandle.Value);
        }

        // Pins an array and a string and reads their lengths where the
        // callback will: a different runtime layout turns sizes off.
        private static bool CheckLayout(ManualLogSource log)
        {
            int[] arr = new int[37];
            string str = new string('x', 23);
            GCHandle ha = GCHandle.Alloc(arr, GCHandleType.Pinned);
            GCHandle hs = GCHandle.Alloc(str, GCHandleType.Pinned);
            try
            {
                IntPtr a = new IntPtr(ha.AddrOfPinnedObject().ToInt64() - ArrayDataOffset);
                IntPtr s = new IntPtr(hs.AddrOfPinnedObject().ToInt64() - StringDataOffset);
                int al = Marshal.ReadInt32(a, ArrayLengthOffset);
                int sl = Marshal.ReadInt32(s, StringLengthOffset);
                bool ok = al == arr.Length && sl == str.Length;
                if (!ok) log.LogWarning("Allocation tracker: layout check read array " + al + " (want 37), string " + sl + " (want 23).");
                return ok;
            }
            finally
            {
                ha.Free();
                hs.Free();
            }
        }

        private static void WarmUp()
        {
            int[] arr = new int[3];
            string str = new string('y', 5);
            GCHandle ha = GCHandle.Alloc(arr, GCHandleType.Pinned);
            GCHandle hs = GCHandle.Alloc(str, GCHandleType.Pinned);
            try
            {
                if (_sizesOk)
                {
                    OnAlloc(IntPtr.Zero, new IntPtr(ha.AddrOfPinnedObject().ToInt64() - ArrayDataOffset), ClassOf(typeof(int[])));
                    OnAlloc(IntPtr.Zero, new IntPtr(hs.AddrOfPinnedObject().ToInt64() - StringDataOffset), _stringClass);
                }
            }
            finally
            {
                ha.Free();
                hs.Free();
            }
        }

        // ------------------------------------------------------------------
        /// Starts a count from zero (installs late if not installed).
        public static void Start(ManualLogSource log)
        {
            if (!Installed && !Install(log, false)) return;
            Clear();
            _startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            _counting = true;
            Status = "counting" + (Early ? "" : " (late install: plain objects undercounted)");
        }

        /// Stops counting (the callback keeps running but returns at once).
        public static void Stop()
        {
            _counting = false;
            if (Installed) Status = "stopped";
        }

        private static void Clear()
        {
            for (int i = 0; i < Capacity; i++) { _count[i] = 0; _bytes[i] = 0; }
            _overflow = 0;
            _mainCount = 0;
            MainBytes = 0;
        }

        // ------------------------------------------------------------------
        // The callback. No allocation, no exceptions out.
        private static void OnAlloc(IntPtr profiler, IntPtr obj, IntPtr klass)
        {
            if (!_counting || klass == IntPtr.Zero) return;
            int slot = Slot(klass);
            if (slot < 0) { Interlocked.Increment(ref _overflow); return; }

            long size = _size[slot];
            if (obj != IntPtr.Zero && _sizesOk)
            {
                byte kind = _kind[slot];
                if (kind == KindArray) size = ArrayDataOffset + (long)Marshal.ReadInt32(obj, ArrayLengthOffset) * size;
                else if (kind == KindString) size = StringDataOffset + 2L * (Marshal.ReadInt32(obj, StringLengthOffset) + 1);
            }
            Interlocked.Increment(ref _count[slot]);
            Interlocked.Add(ref _bytes[slot], size);
            if (GetCurrentThreadId() == _mainThread)
            {
                MainBytes += size;
                _mainCount++;
            }
        }

        private static int Slot(IntPtr klass)
        {
            long k = klass.ToInt64();
            int h = (int)((k >> 4) ^ (k >> 17)) & (Capacity - 1);
            for (int probe = 0; probe < 64; probe++)
            {
                int i = (h + probe) & (Capacity - 1);
                IntPtr cur = _keys[i];
                if (cur == klass) return i;
                if (cur != IntPtr.Zero) continue;
                // Size first, then publish: another thread may read it at once.
                // Two new classes racing for one empty slot can overwrite
                // each other's size; the winner writes its own again after.
                Describe(i, klass);
                IntPtr was = Interlocked.CompareExchange(ref _keys[i], klass, IntPtr.Zero);
                if (was == IntPtr.Zero) { Describe(i, klass); return i; }
                if (was == klass) return i;
            }
            return -1;
        }

        private static void Describe(int i, IntPtr klass)
        {
            if (klass == _stringClass) { _kind[i] = KindString; _size[i] = StringDataOffset; return; }
            if (mono_class_get_rank(klass) > 0) { _kind[i] = KindArray; _size[i] = mono_array_element_size(klass); return; }
            _kind[i] = KindFixed;
            _size[i] = mono_class_instance_size(klass);
        }

        // ------------------------------------------------------------------
        /// The report (main thread): the rate, then the top types by bytes.
        /// Returns the lines; the caller logs them.
        public static List<string> Report()
        {
            List<string> lines = new List<string>();
            if (!Installed) { lines.Add("Allocations: not installed."); return lines; }
            double seconds = (System.Diagnostics.Stopwatch.GetTimestamp() - _startTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
            if (seconds <= 0.01) seconds = 0.01;

            long total = 0, objects = 0;
            List<int> used = new List<int>();
            for (int i = 0; i < Capacity; i++)
            {
                if (_keys[i] == IntPtr.Zero || _count[i] == 0) continue;
                used.Add(i);
                total += _bytes[i];
                objects += _count[i];
            }
            used.Sort(delegate(int a, int b) { return _bytes[b].CompareTo(_bytes[a]); });

            StringBuilder sb = new StringBuilder();
            sb.Append("Allocations (").Append(seconds.ToString("0")).Append(" s").Append(Early ? "" : ", late install")
              .Append("): ").Append(Kb(total / seconds)).Append(" KB/s, ").Append((objects / seconds).ToString("0")).Append(" objects/s; main thread ")
              .Append(Kb(MainBytes / seconds)).Append(" KB/s, ").Append((_mainCount / seconds).ToString("0")).Append(" objects/s; ")
              .Append(used.Count).Append(" types");
            if (_overflow > 0) sb.Append("; ").Append(_overflow).Append(" not counted (table full)");
            lines.Add(sb.ToString());

            sb.Length = 0;
            sb.Append("by type: ");
            for (int k = 0; k < used.Count && k < Top; k++)
            {
                int i = used[k];
                if (k > 0) sb.Append(", ");
                sb.Append(NameOf(_keys[i])).Append(' ').Append(Kb(_bytes[i] / seconds)).Append(" KB/s (")
                  .Append((_count[i] / seconds).ToString("0")).Append("/s)");
            }
            lines.Add(sb.ToString());
            return lines;
        }

        private static string NameOf(IntPtr klass)
        {
            string name;
            if (Names.TryGetValue(klass.ToInt64(), out name)) return name;
            try
            {
                // g_malloc'd; not freed (a few dozen names a session).
                IntPtr p = mono_type_get_name(mono_class_get_type(klass));
                name = p == IntPtr.Zero ? "?" : Marshal.PtrToStringAnsi(p);
            }
            catch (Exception) { name = "?"; }
            Names[klass.ToInt64()] = name;
            return name;
        }

        private static string Kb(double bytes)
        {
            double kb = bytes / 1024.0;
            return kb >= 10 ? kb.ToString("0") : kb.ToString("0.0");
        }
    }
}
