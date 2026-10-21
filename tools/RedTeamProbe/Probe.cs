using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using UnityEngine;

// T-0414 red team probe: a stand-in for a cheat mod, driven over the bridge
// (call static:RedTeam.Probe <Method> ...). Dev only, never shipped.
namespace RedTeam
{
    [BepInPlugin("redteam.probe", "RedTeam Probe", "1.0.0")]
    public class Probe : BaseUnityPlugin
    {
        private static Probe _i;
        private static readonly Dictionary<string, Harmony> _harmonies = new Dictionary<string, Harmony>();
        private static Hook _hook;
        private static GameObject _flip, _ratio;

        private void Awake() { _i = this; Logger.LogInfo("RedTeam probe loaded"); }

        private static void Log(string s) { if (_i != null) _i.Logger.LogInfo("RedTeam: " + s); }

        private static MethodBase Target()
        {
            Type t = AccessTools.TypeByName("TheForest.Items.ItemDatabase");
            return t.GetMethod("ItemIdByName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
        }

        public static string Assemblies()
        {
            StringBuilder sb = new StringBuilder();
            int n = 0;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string loc;
                bool dyn = a is System.Reflection.Emit.AssemblyBuilder;
                try { loc = dyn ? "(dynamic)" : a.Location; } catch (Exception ex) { loc = "(throws " + ex.GetType().Name + ")"; }
                if (string.IsNullOrEmpty(loc)) loc = "(EMPTY)";
                sb.Append(a.GetName().Name).Append(" | ").Append(loc).Append('\n');
                n++;
            }
            Log("assemblies (" + n + "):\n" + sb);
            return n + " assemblies, see log";
        }

        public static string Patches()
        {
            StringBuilder sb = new StringBuilder();
            int n = 0;
            foreach (MethodBase m in Harmony.GetAllPatchedMethods())
            {
                Patches info = Harmony.GetPatchInfo(m);
                if (info == null) continue;
                List<Patch> all = new List<Patch>();
                all.AddRange(info.Prefixes); all.AddRange(info.Postfixes); all.AddRange(info.Transpilers); all.AddRange(info.Finalizers);
                foreach (Patch p in all)
                {
                    string asm = "?";
                    try { asm = p.PatchMethod.DeclaringType.Assembly.GetName().Name; } catch (Exception) { }
                    sb.Append(m.DeclaringType != null ? m.DeclaringType.Name : "?").Append('.').Append(m.Name)
                      .Append(" | owner ").Append(p.owner).Append(" | method in ").Append(asm).Append('\n');
                    n++;
                }
            }
            Log("patches (" + n + "):\n" + sb);
            return n + " patches, see log";
        }

        public static void Noop() { }

        public static string PatchAs(string id)
        {
            Harmony h = new Harmony(id);
            _harmonies[id] = h;
            h.Patch(Target(), postfix: new HarmonyMethod(typeof(Probe).GetMethod("Noop")));
            Log("patched ItemDatabase.ItemIdByName as '" + id + "'");
            return "patched as " + id;
        }

        public static string UnpatchAs(string id)
        {
            Harmony h;
            if (!_harmonies.TryGetValue(id, out h)) return "none";
            h.UnpatchSelf();
            _harmonies.Remove(id);
            return "unpatched " + id;
        }

        public static string LoadBytes()
        {
            string path = typeof(Probe).Assembly.Location;
            Assembly a = Assembly.Load(File.ReadAllBytes(path));
            string loc = a.Location;
            Log("loaded from bytes: " + a.FullName + " location '" + loc + "' dynamic " + (a is System.Reflection.Emit.AssemblyBuilder));
            return "loaded " + a.GetName().Name + " location '" + loc + "'";
        }

        public delegate int orig_ItemIdByName(string name);
        public static int HookFn(orig_ItemIdByName orig, string name) { return orig(name); }

        public static string Detour(bool on)
        {
            if (on && _hook == null) _hook = new Hook(Target(), typeof(Probe).GetMethod("HookFn"));
            if (!on && _hook != null) { _hook.Dispose(); _hook = null; }
            Log("MonoMod hook on ItemIdByName: " + on);
            return "hook " + on;
        }

        public static string Flip(bool on)
        {
            if (on && _flip == null)
            {
                _flip = new GameObject("RedTeamFlip");
                DontDestroyOnLoad(_flip);
                _flip.AddComponent<FlipEarly>();
                _flip.AddComponent<FlipLate>();
            }
            if (!on && _flip != null) { Destroy(_flip); _flip = null; Time.timeScale = 1f; }
            Log("timeScale flip (2 from LateUpdate, 1 again first thing in Update): " + on);
            return "flip " + on;
        }

        public static string Ratio(bool on)
        {
            if (on && _ratio == null) { _ratio = new GameObject("RedTeamRatio"); DontDestroyOnLoad(_ratio); _ratio.AddComponent<RatioWatch>(); }
            if (!on && _ratio != null) { Destroy(_ratio); _ratio = null; }
            return "ratio " + on;
        }

        internal static void LogLine(string s) { Log(s); }

        // MonoMod's detour registry against Harmony's patched methods.
        public static string Detours()
        {
            FieldInfo f = typeof(Detour).GetField("_DetourMap", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            System.Collections.IDictionary map = (System.Collections.IDictionary)f.GetValue(null);
            HashSet<MethodBase> harmony = new HashSet<MethodBase>(Harmony.GetAllPatchedMethods());
            StringBuilder sb = new StringBuilder();
            int extra = 0, multi = 0;
            foreach (System.Collections.DictionaryEntry e in map)
            {
                MethodBase m = (MethodBase)e.Key;
                int count = ((System.Collections.ICollection)e.Value).Count;
                bool h = harmony.Contains(m);
                if (!h || count > 1)
                {
                    sb.Append(m.DeclaringType != null ? m.DeclaringType.FullName : "?").Append('.').Append(m.Name)
                      .Append(" | detours ").Append(count).Append(" | harmony ").Append(h);
                    foreach (object d in (System.Collections.IEnumerable)e.Value)
                    {
                        FieldInfo to = d.GetType().GetField("Target", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        object tv = to != null ? to.GetValue(d) : null;
                        MethodBase tm = tv as MethodBase;
                        sb.Append(" | to ").Append(tm != null ? (tm.DeclaringType != null ? tm.DeclaringType.FullName + "." : "") + tm.Name + " in " + SafeAsm(tm) : "?");
                    }
                    sb.Append('\n');
                    if (!h) extra++;
                    if (count > 1) multi++;
                }
            }
            Log("detours: " + map.Count + " methods in MonoMod's map, " + harmony.Count + " Harmony-patched; not Harmony's " + extra + ", more than one detour " + multi + ":\n" + sb);
            return map.Count + " / " + harmony.Count + " / extra " + extra + " / multi " + multi;
        }

        private static string SafeAsm(MethodBase m)
        {
            try { return m.DeclaringType == null ? "(dynamic)" : m.DeclaringType.Assembly.GetName().Name; } catch (Exception) { return "?"; }
        }

        public static string LoadManaged()
        {
            string path = Path.Combine(Paths.ManagedPath, "RedTeamExtra.dll");
            Assembly a = Assembly.LoadFrom(path);
            Log("loaded from Managed: " + a.FullName + " at " + a.Location);
            return "loaded " + a.Location;
        }
    }

    [DefaultExecutionOrder(-30000)]
    public class FlipEarly : MonoBehaviour { private void Update() { Time.timeScale = 1f; } }

    public class FlipLate : MonoBehaviour { private void LateUpdate() { Time.timeScale = 2f; } }

    // Every frame, late: deltaTime / unscaledDeltaTime and the timeScale read now; one line per 5 s.
    [DefaultExecutionOrder(30000)]
    public class RatioWatch : MonoBehaviour
    {
        private float _until, _min = float.MaxValue, _max, _sumGame, _sumReal;
        private int _frames, _off, _scaleSeen1Ratio2;
        private void Update()
        {
            float dt = Time.deltaTime, ud = Time.unscaledDeltaTime, ts = Time.timeScale;
            if (ud > 0f)
            {
                float r = dt / ud;
                if (r < _min) _min = r;
                if (r > _max) _max = r;
                // vanilla: dt == ud * scale-at-frame-start; count frames where it is not ud * the scale read now
                if (Mathf.Abs(dt - ud * ts) > 0.0005f + 0.01f * ud) _off++;
                if (ts == 1f && r > 1.05f) _scaleSeen1Ratio2++;
            }
            _sumGame += dt; _sumReal += ud; _frames++;
            if (Time.unscaledTime >= _until)
            {
                if (_frames > 0)
                    Probe.LogLine("ratio: " + _frames + " frames, deltaTime/unscaledDeltaTime min " + _min.ToString("0.0000") + " max " +
                                  _max.ToString("0.0000") + ", frames where dt != ud*timeScale(now) " + _off +
                                  ", frames timeScale 1 but ratio > 1.05: " + _scaleSeen1Ratio2 + ", game " + _sumGame.ToString("0.000") +
                                  " s vs real " + _sumReal.ToString("0.000") + " s, captureFramerate " + Time.captureFramerate +
                                  ", maximumDeltaTime " + Time.maximumDeltaTime);
                _until = Time.unscaledTime + 5f;
                _min = float.MaxValue; _max = 0f; _sumGame = 0f; _sumReal = 0f; _frames = 0; _off = 0; _scaleSeen1Ratio2 = 0;
            }
        }
    }
}
