using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using ForestOverlay.Data;
using HarmonyLib;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The game's forced garbage collection in a Quick load (T-0202).
    //
    // LevelSerializer.LoadNow runs LevelLoader.Load(0, Time.timeScale),
    // whose coroutine (LevelLoader/<Load>c__Iterator1.MoveNext, IL) calls
    // GC.Collect() unconditionally once every component is deserialized,
    // and again every 3000 objects; its other two calls need timeScale 0
    // (a load from the menu). Each is a full stop-the-world collection,
    // ~100 ms over the in-game heap - one per restore (bridge: GC x1 in
    // every restore line, 10 in a 10-restore loop with none from volume).
    //
    // With the `[Performance] RestoreCollectWhenDue` switch, a transpiler
    // sends those calls here: outside our own in-place restores (a Full
    // load, the game's own loads) it is GC.Collect() as before; inside one
    // it collects only when the garbage since the last collection reached
    // Data/GarbageBudget (64 MB) - so the pause stays inside a restore,
    // every few restores instead of every one. Nothing in the game reads
    // collection timing (no weak references or finalizers in its save
    // code - ilscan), so what the restore restores is unchanged.
    //
    // The baseline (heap in use just after the last collection, any
    // collection) is read once a frame by Tick: two runtime counters, no
    // allocation.
    // ------------------------------------------------------------------
    public static class LoaderCollect
    {
        private static int _seenCount = -1;
        private static long _heapAfter;
        private static MethodInfo _target;
        private static int _found;

        /// The switch is on and the patch in.
        public static bool Active { get; private set; }

        // What happened in the current restore (SavestateBridge reads it).
        private static int _skipped, _ran;
        private static long _lastGarbage;

        /// Once a frame: a collection since the last frame moves the baseline.
        public static void Tick()
        {
            int c = GC.CollectionCount(0);
            if (c == _seenCount) return;
            _seenCount = c;
            _heapAfter = GC.GetTotalMemory(false);
        }

        /// Called in place of the loader's GC.Collect() (transpiler).
        public static void Collect()
        {
            if (!Active || !SavestateBridge.InPlaceLoading)
            {
                GC.Collect();
                return;
            }
            Tick();
            long now = GC.GetTotalMemory(false);
            _lastGarbage = now - _heapAfter;
            if (!GarbageBudget.Due(now, _heapAfter, GarbageBudget.Bytes))
            {
                _skipped++;
                return;
            }
            GC.Collect();
            _ran++;
            Tick();
        }

        /// For the restore line: "" when the loader asked for no
        /// collection (or the switch is off), else what was done. Resets.
        public static string TakeNote()
        {
            if (_skipped + _ran == 0) return "";
            string note = "the game's forced GC " + (_ran > 0 ? "ran" : "skipped") +
                          " (" + StepBytes.Mb(_lastGarbage) + " MB of garbage since the last, " +
                          (_ran > 0 ? "budget " : "under ") + StepBytes.Mb(GarbageBudget.Bytes) + " MB" +
                          (_skipped + _ran > 1 ? "; " + _ran + " ran, " + _skipped + " skipped" : "") + ")";
            _skipped = 0;
            _ran = 0;
            return note;
        }

        // ------------------------------------------------------------------
        public static string Apply(Harmony harmony, ManualLogSource log)
        {
            Type loader = GameBridge.FindGameType("LevelLoader");
            if (loader == null) return "LevelLoader not found";
            _target = null;
            Type[] nested = loader.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public);
            for (int i = 0; i < nested.Length; i++)
            {
                if (!nested[i].Name.StartsWith("<Load>c__Iterator", StringComparison.Ordinal)) continue;
                MethodInfo m = nested[i].GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null || CollectCalls(m) == 0) continue;
                if (_target != null) return "more than one load coroutine collects (game updated?)";
                _target = m;
            }
            if (_target == null) return "the load coroutine's GC.Collect not found (game updated?)";
            _found = 0;
            harmony.Patch(_target, transpiler: new HarmonyMethod(typeof(LoaderCollect).GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic)));
            if (_found == 0)
            {
                harmony.Unpatch(_target, HarmonyPatchType.All, harmony.Id);
                return "no GC.Collect replaced (game updated?)";
            }
            Active = true;
            log.LogInfo("Performance: a Quick load's forced garbage collection runs only once " + StepBytes.Mb(GarbageBudget.Bytes) +
                        " MB of garbage built up (" + _found + " call(s) in the game's loader).");
            return "";
        }

        public static void Remove(Harmony harmony)
        {
            Active = false;
            if (_target != null) harmony.Unpatch(_target, HarmonyPatchType.All, harmony.Id);
        }

        // `call` (0x28) tokens in the IL that resolve to GC.Collect().
        private static int CollectCalls(MethodInfo m)
        {
            try
            {
                MethodBody body = m.GetMethodBody();
                if (body == null) return 0;
                byte[] il = body.GetILAsByteArray();
                int n = 0;
                for (int i = 0; i + 4 < il.Length; i++)
                {
                    if (il[i] != 0x28) continue;
                    MethodBase target;
                    try { target = m.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1)); }
                    catch (Exception) { continue; }
                    MethodInfo mi = target as MethodInfo;
                    if (mi != null && IsCollect(OpCodes.Call, mi)) n++;
                }
                return n;
            }
            catch (Exception) { return 0; }
        }

        private static bool IsCollect(OpCode op, object operand)
        {
            MethodInfo mi = operand as MethodInfo;
            return op == OpCodes.Call && mi != null && mi.DeclaringType == typeof(GC) && mi.Name == "Collect" &&
                   mi.GetParameters().Length == 0;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo ours = typeof(LoaderCollect).GetMethod("Collect", BindingFlags.Static | BindingFlags.Public);
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            int found = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!IsCollect(list[i].opcode, list[i].operand)) continue;
                list[i].operand = ours;   // same stack shape: no arguments, no result
                found++;
            }
            _found = found;
            return list;
        }
    }
}
