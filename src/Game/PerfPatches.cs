using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Patches that make the game allocate less (Next up 6). Every managed
    // allocation brings the next garbage collection closer, and each one
    // is a 90+ ms frame (the Boehm pause over a ~280 MB heap). Measured
    // with the allocation tracker (v0.24.91, Slot 1 surface, standing
    // still, ~200 fps): ~420 KB/s. These cut ~250 KB/s of it.
    //
    // Each one is behaviour-preserving (the game computes and draws the
    // same things), has its own switch (`[Performance]`, on by default,
    // Debug views tab), applies and removes live, and logs one line.
    //
    // 1. Overlay layout (ours): Unity runs an IMGUI layout pass - a new
    //    GUILayoutGroup, its list and a RectOffset - for every OnGUI
    //    behaviour with useGUILayout on, every frame. The overlay uses no
    //    GUILayout; only GUI.Window needs the pass, so it is on only while
    //    one of our windows is showing (Plugin.Update reads OverlayLayout).
    // 2. Post-processing layout: PostProcessingBehaviour.OnGUI acts on
    //    Repaint only (the Scion eye-adaptation switch, debug textures by
    //    GUI.DrawTexture) - its layout pass is pure waste, per camera.
    // 3. Ocean comparer: Ceto.ProjectedGrid.m_grids is a Dictionary keyed
    //    by the MESH_RESOLUTION enum with the default comparer, which on
    //    this Mono boxes the key on every lookup (~21 a frame: Update and
    //    every camera's OceanOnPostRender). Same dictionary, a comparer
    //    that compares the enum's int without boxing; hash = the value,
    //    as the default's, so even the iteration order is unchanged.
    // 4. Atmosphere arrays: TheForestAtmosphere.UpdateShaderParameters
    //    (per camera, per frame) allocates two Vector3[4] that
    //    Camera.CalculateFrustumCorners fills completely before they are
    //    read, and that never leave the method. A transpiler hands it two
    //    kept arrays instead.
    // ------------------------------------------------------------------
    public sealed class PerfPatches
    {
        /// Fix 1, read by Plugin.Update: only lay out our OnGUI while a
        /// window of ours is showing.
        public static bool OverlayLayout;

        private sealed class Fix
        {
            public string Key;
            public string Label;
            public ConfigEntry<bool> Cfg;
            public Func<string> Apply;    // "" = applied, else why not
            public Action Remove;
            public bool Applied;
            public string Status = "off";
        }

        private readonly ManualLogSource _log;
        private readonly Harmony _harmony;
        private readonly List<Fix> _fixes = new List<Fix>();

        public PerfPatches(ManualLogSource log, ConfigFile config, string harmonyId)
        {
            _log = log;
            _harmony = new Harmony(harmonyId + ".perf");

            Add(config, "OverlayLayoutOnlyForWindows", "Overlay: no GUI layout pass without a window",
                "Skip Unity's GUI layout pass for the overlay while none of its windows is open (saves ~40 KB/s of garbage).",
                delegate { OverlayLayout = true; return ""; }, delegate { OverlayLayout = false; });
            Add(config, "PostProcessingNoLayout", "Post-processing: no GUI layout pass",
                "Skip Unity's GUI layout pass for the game's post-processing, which never uses it (saves ~80-110 KB/s of garbage).",
                ApplyPostProcessing, RemovePostProcessing);
            Add(config, "OceanNoBoxing", "Ocean: look up grids without boxing",
                "Give the ocean's grid table a comparer that does not box its keys (saves ~80 KB/s of garbage).",
                ApplyOcean, RemoveOcean);
            Add(config, "AtmosphereReuseArrays", "Atmosphere: reuse two arrays per camera",
                "Let the atmosphere reuse the two small arrays it fills every frame per camera (saves ~30 KB/s of garbage).",
                ApplyAtmosphere, RemoveAtmosphere);

            for (int i = 0; i < _fixes.Count; i++)
                if (_fixes[i].Cfg.Value) Set(_fixes[i], true);
        }

        private void Add(ConfigFile config, string key, string label, string description, Func<string> apply, Action remove)
        {
            Fix f = new Fix();
            f.Key = key;
            f.Label = " " + label;   // the toggle's text, built once
            f.Cfg = config.Bind("Performance", key, true, description + " Behaviour-preserving; off = the game's own code.");
            f.Apply = apply;
            f.Remove = remove;
            _fixes.Add(f);
        }

        public int Count { get { return _fixes.Count; } }
        public string Label(int i) { return _fixes[i].Label; }
        public bool IsOn(int i) { return _fixes[i].Cfg.Value; }
        public string Status(int i) { return _fixes[i].Status; }

        /// The GUI switch (and the bridge): on / off, saved, applied live.
        public void Toggle(int i)
        {
            Fix f = _fixes[i];
            f.Cfg.Value = !f.Cfg.Value;
            Set(f, f.Cfg.Value);
        }

        /// Dev (bridge): every live script with an OnGUI that still gets
        /// Unity's layout pass (useGUILayout on, enabled) - what is left
        /// of the per-frame GUILayoutGroup garbage.
        public string ListLayoutUsers()
        {
            Dictionary<string, int> found = new Dictionary<string, int>();
            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(typeof(MonoBehaviour));
            for (int i = 0; i < all.Length; i++)
            {
                MonoBehaviour mb = all[i] as MonoBehaviour;
                if (mb == null || !mb.enabled || !mb.useGUILayout) continue;
                bool hasGui = false;
                for (Type t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                    if (t.GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                                    null, Type.EmptyTypes, null) != null) { hasGui = true; break; }
                if (!hasGui) continue;
                string key = mb.GetType().FullName + " on '" + mb.gameObject.name + "'";
                int n;
                found.TryGetValue(key, out n);
                found[key] = n + 1;
            }
            if (found.Count == 0) return "none";
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in found) parts.Add(kv.Key + (kv.Value > 1 ? " x" + kv.Value : ""));
            return string.Join("; ", parts.ToArray());
        }

        public void Shutdown()
        {
            for (int i = 0; i < _fixes.Count; i++) if (_fixes[i].Applied) Set(_fixes[i], false);
        }

        private void Set(Fix f, bool on)
        {
            if (on == f.Applied) return;
            try
            {
                if (on)
                {
                    string why = f.Apply();
                    f.Applied = why.Length == 0;
                    f.Status = f.Applied ? "on" : "not applied: " + why;
                }
                else
                {
                    f.Remove();
                    f.Applied = false;
                    f.Status = "off";
                }
            }
            catch (Exception ex)
            {
                f.Applied = false;
                f.Status = "failed: " + (ex.InnerException ?? ex).Message;
            }
            _log.LogInfo("Performance patch '" + f.Key + "': " + f.Status + ".");
        }

        private static Type GameType(string name)
        {
            return GameBridge.FindGameType(name);
        }

        // ------------------------------------------------------------------
        // 2. Post-processing
        private const string PostType = "UnityEngine.PostProcessing.PostProcessingBehaviour";
        private MethodInfo _postEnable;

        private string ApplyPostProcessing()
        {
            Type t = GameType(PostType);
            if (t == null) return PostType + " not found";
            _postEnable = t.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (_postEnable == null) return "OnEnable not found";
            _harmony.Patch(_postEnable, postfix: new HarmonyMethod(typeof(PerfPatches).GetMethod("PostEnablePostfix", BindingFlags.Static | BindingFlags.NonPublic)));
            SetLayout(t, false);
            return "";
        }

        private void RemovePostProcessing()
        {
            if (_postEnable != null) _harmony.Unpatch(_postEnable, HarmonyPatchType.Postfix, _harmony.Id);
            Type t = GameType(PostType);
            if (t != null) SetLayout(t, true);
        }

        private static void SetLayout(Type t, bool layout)
        {
            UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(t);
            for (int i = 0; i < all.Length; i++)
            {
                MonoBehaviour mb = all[i] as MonoBehaviour;
                if (mb != null) mb.useGUILayout = layout;
            }
        }

        private static void PostEnablePostfix(MonoBehaviour __instance)
        {
            try { if (__instance != null) __instance.useGUILayout = false; }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------
        // 3. Ocean
        private const string GridType = "Ceto.ProjectedGrid";
        private FieldInfo _grids;
        private MethodInfo _gridEnable;
        private static object _comparer;          // IEqualityComparer<MESH_RESOLUTION>
        private static FieldInfo _gridsStatic;

        private string ApplyOcean()
        {
            Type t = GameType(GridType);
            if (t == null) return GridType + " not found";
            _grids = t.GetField("m_grids", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (_grids == null || !_grids.FieldType.IsGenericType) return "m_grids not found";
            Type[] args = _grids.FieldType.GetGenericArguments();
            if (args.Length != 2 || !args[0].IsEnum || Enum.GetUnderlyingType(args[0]) != typeof(int)) return "m_grids is not keyed by an int enum";
            _comparer = Activator.CreateInstance(typeof(IntEnumComparer<>).MakeGenericType(args[0]));
            _gridsStatic = _grids;
            _gridEnable = t.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (_gridEnable == null) return "OnEnable not found";
            _harmony.Patch(_gridEnable, new HarmonyMethod(typeof(PerfPatches).GetMethod("GridEnablePrefix", BindingFlags.Static | BindingFlags.NonPublic)));
            SwapAll(t, _comparer);
            return "";
        }

        private void RemoveOcean()
        {
            if (_gridEnable != null) _harmony.Unpatch(_gridEnable, HarmonyPatchType.Prefix, _harmony.Id);
            Type t = GameType(GridType);
            if (t != null && _grids != null) SwapAll(t, null);
        }

        private void SwapAll(Type t, object comparer)
        {
            UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(t);
            for (int i = 0; i < all.Length; i++) Swap(all[i], comparer);
        }

        // Replaces the table with a copy under the given comparer (null =
        // the default). Contents and their order are kept.
        private static void Swap(object grid, object comparer)
        {
            IDictionary old = _gridsStatic.GetValue(grid) as IDictionary;
            if (old == null) return;
            PropertyInfo cmp = old.GetType().GetProperty("Comparer");
            object current = cmp != null ? cmp.GetValue(old, null) : null;
            if (comparer != null ? ReferenceEquals(current, comparer) : !(current is IIntEnumComparer)) return;
            Type dictType = old.GetType();
            object copy = comparer != null
                ? Activator.CreateInstance(dictType, new object[] { comparer })
                : Activator.CreateInstance(dictType);
            IDictionary dst = (IDictionary)copy;
            foreach (DictionaryEntry e in old) dst.Add(e.Key, e.Value);
            _gridsStatic.SetValue(grid, copy);
        }

        private static void GridEnablePrefix(object __instance)
        {
            try { if (_comparer != null) Swap(__instance, _comparer); }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------
        // 4. Atmosphere
        private const string AtmosType = "TheForestAtmosphere";
        private MethodInfo _atmosUpdate;
        private static int _atmosReplaced;
        public static readonly Vector3[] CornersA = new Vector3[4];
        public static readonly Vector3[] CornersB = new Vector3[4];

        private string ApplyAtmosphere()
        {
            Type t = GameType(AtmosType);
            if (t == null) return AtmosType + " not found";
            _atmosUpdate = t.GetMethod("UpdateShaderParameters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_atmosUpdate == null) return "UpdateShaderParameters not found";
            _atmosReplaced = 0;
            _harmony.Patch(_atmosUpdate, transpiler: new HarmonyMethod(typeof(PerfPatches).GetMethod("AtmosTranspiler", BindingFlags.Static | BindingFlags.NonPublic)));
            if (_atmosReplaced == 2) return "";
            // Not the code this was written for: the game's own code back.
            _harmony.Unpatch(_atmosUpdate, HarmonyPatchType.Transpiler, _harmony.Id);
            return "expected 2 new Vector3[4], found " + _atmosReplaced + " (game updated?)";
        }

        private void RemoveAtmosphere()
        {
            if (_atmosUpdate != null) _harmony.Unpatch(_atmosUpdate, HarmonyPatchType.Transpiler, _harmony.Id);
        }

        // `ldc.i4.4; newarr Vector3` -> `ldsfld CornersA` (then CornersB).
        private static IEnumerable<CodeInstruction> AtmosTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            FieldInfo[] kept = { typeof(PerfPatches).GetField("CornersA"), typeof(PerfPatches).GetField("CornersB") };
            int found = 0;
            for (int i = 0; i + 1 < list.Count; i++)
            {
                if (list[i].opcode != OpCodes.Ldc_I4_4 || list[i + 1].opcode != OpCodes.Newarr) continue;
                if (!ReferenceEquals(list[i + 1].operand, typeof(Vector3))) continue;
                if (found < kept.Length)
                {
                    // Keep the first instruction's labels on the load.
                    list[i].opcode = OpCodes.Ldsfld;
                    list[i].operand = kept[found];
                    list[i + 1].opcode = OpCodes.Nop;
                    list[i + 1].operand = null;
                }
                found++;
            }
            _atmosReplaced = found;
            return list;
        }
    }

    /// Marks our comparer, whatever its enum.
    public interface IIntEnumComparer { }

    /// Compares an int-based enum by value without boxing it. The IL for
    /// "return the argument" reads an enum as its int.
    public sealed class IntEnumComparer<T> : IEqualityComparer<T>, IIntEnumComparer
    {
        private readonly Func<T, int> _value;

        public IntEnumComparer()
        {
            DynamicMethod dm = new DynamicMethod("EnumValue", typeof(int), new[] { typeof(T) }, typeof(IntEnumComparer<T>).Module, true);
            ILGenerator il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);
            _value = (Func<T, int>)dm.CreateDelegate(typeof(Func<T, int>));
        }

        public bool Equals(T a, T b) { return _value(a) == _value(b); }
        public int GetHashCode(T v) { return _value(v); }
    }
}
