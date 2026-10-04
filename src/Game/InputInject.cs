using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using ForestOverlay.Data;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Game actions pressed / held from the test bridge (dev only).
    //
    // WHY: Jump / sprint / crouch tech needs real input at a known frame;
    // a FSM SendEvent skips the code that reads the button.
    //
    // HOW: the game reads its Rewired actions through the static
    // TheForest.Utils.Input (225 call sites, PlayMaker's GetButton* /
    // GetAxis actions included; only KeepAboveTerrain and the inventory's
    // combine timer ask Rewired directly - IL). Postfixes on GetButton,
    // GetButtonDown, GetButtonUp, GetAxis, GetAxisDown OR in what the
    // bridge holds (an injected axis replaces the real value); a
    // transpiler makes GetButtonAfterDelay / GetButtonPress /
    // IsPastButtonPress read their button through those same patched
    // statics instead of Rewired, so hold-to-take works too. Installed on
    // the first bridge input command or TAS record / replay, never otherwise; Seen counts every
    // name the game asks for from then on. Game/TasInput records and
    // replays through the same postfixes (after the bridge's injection:
    // a recording holds what the game read; a replay replaces it all).
    // ------------------------------------------------------------------
    public static class InputInject
    {
        public static readonly InjectedInputs State = new InjectedInputs();

        private static Harmony _harmony;
        private static string _error;
        private static int _rerouted;
        private static readonly Dictionary<string, int> _seenButtons = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _seenAxes = new Dictionary<string, int>();

        public static bool Installed { get { return _harmony != null && _error == null; } }

        /// Null when the patches are in, else why not.
        public static string Install(ManualLogSource log, string harmonyId)
        {
            if (_harmony != null) return _error;
            try
            {
                Type input = GameBridge.FindGameType("TheForest.Utils.Input");
                if (input == null) return _error = "TheForest.Utils.Input not found";
                _harmony = new Harmony(harmonyId + ".inputinject");
                int n = 0;
                n += Post(input, "GetButton", "ButtonPostfix");
                n += Post(input, "GetButtonDown", "DownPostfix");
                n += Post(input, "GetButtonUp", "UpPostfix");
                n += Post(input, "GetAxis", "AxisPostfix");
                n += Post(input, "GetAxisDown", "AxisDownPostfix");
                HarmonyMethod tr = new HarmonyMethod(typeof(InputInject).GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic));
                _rerouted = 0;
                foreach (string name in new[] { "GetButtonAfterDelay", "GetButtonPress", "IsPastButtonPress" })
                {
                    MethodInfo m = Static(input, name);
                    if (m != null) _harmony.Patch(m, transpiler: tr);
                }
                log.LogInfo("InputInject: " + n + " of 5 reads patched, " + _rerouted + " Rewired button read(s) in the delayed / tap " +
                            "reads rerouted through them - the bridge can press the game's actions.");
                if (n < 5) _error = "only " + n + " of 5 Input reads found";
            }
            catch (Exception ex)
            {
                _error = "could not patch: " + ex.Message;
                log.LogWarning("InputInject: " + ex);
            }
            return _error;
        }

        public static void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        /// Once a frame from the bridge.
        public static void Tick()
        {
            if (State.Any) State.Settle(Time.frameCount, Time.realtimeSinceStartup);
        }

        public static void ClearSeen()
        {
            _seenButtons.Clear();
            _seenAxes.Clear();
        }

        public static void DescribeSeen(List<string> o)
        {
            o.Add("buttons the game read: " + Join(_seenButtons));
            o.Add("axes the game read: " + Join(_seenAxes));
        }

        /// Every name the game has read since the patches went in.
        public static void SeenNames(List<string> buttons, List<string> axes)
        {
            buttons.Clear();
            axes.Clear();
            buttons.AddRange(_seenButtons.Keys);
            axes.AddRange(_seenAxes.Keys);
        }

        private static string Join(Dictionary<string, int> d)
        {
            if (d.Count == 0) return "none yet";
            List<string> keys = new List<string>(d.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < keys.Count; i++) keys[i] = keys[i] + " x" + d[keys[i]];
            return string.Join(", ", keys.ToArray());
        }

        private static MethodInfo Static(Type t, string name)
        {
            return t.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null)
                ?? FirstByName(t, name);
        }

        private static MethodInfo FirstByName(Type t, string name)
        {
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (m.Name == name) return m;
            return null;
        }

        private static int Post(Type input, string name, string postfix)
        {
            MethodInfo m = input.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            if (m == null) return 0;
            _harmony.Patch(m, postfix: new HarmonyMethod(typeof(InputInject).GetMethod(postfix, BindingFlags.Static | BindingFlags.NonPublic)));
            return 1;
        }

        private static void Count(Dictionary<string, int> d, string name)
        {
            if (name == null) return;
            int n;
            d.TryGetValue(name, out n);
            d[name] = n + 1;
        }

        // Parameter names follow the game's: GetButton*(string button), GetAxis*(string axis).
        private static void ButtonPostfix(string button, ref bool __result)
        {
            Count(_seenButtons, button);
            if (!__result && State.Any && State.Held(button, Time.frameCount, Time.realtimeSinceStartup)) __result = true;
            if (TasInput.Current != TasInput.Mode.Off) TasInput.Button(button, 0, ref __result);
        }

        private static void DownPostfix(string button, ref bool __result)
        {
            Count(_seenButtons, button);
            if (!__result && State.Any && State.Down(button, Time.frameCount, Time.realtimeSinceStartup)) __result = true;
            if (TasInput.Current != TasInput.Mode.Off) TasInput.Button(button, 1, ref __result);
        }

        private static void UpPostfix(string button, ref bool __result)
        {
            Count(_seenButtons, button);
            if (!__result && State.Any && State.Up(button, Time.frameCount, Time.realtimeSinceStartup)) __result = true;
            if (TasInput.Current != TasInput.Mode.Off) TasInput.Button(button, 2, ref __result);
        }

        private static void AxisPostfix(string axis, ref float __result)
        {
            Count(_seenAxes, axis);
            float v;
            if (State.Any && State.TryAxis(axis, Time.frameCount, Time.realtimeSinceStartup, out v)) __result = v;
            if (TasInput.Current != TasInput.Mode.Off) TasInput.Axis(axis, false, ref __result);
        }

        private static void AxisDownPostfix(string axis, ref float __result)
        {
            float v;
            if (State.Any && State.AxisDown(axis, Time.frameCount, Time.realtimeSinceStartup) &&
                State.TryAxis(axis, Time.frameCount, Time.realtimeSinceStartup, out v)) __result = v;
            if (TasInput.Current != TasInput.Mode.Off) TasInput.Axis(axis, true, ref __result);
        }

        // ldsfld Input::player; ldarg.0; callvirt Player::GetButton[Down|Up](string)
        //   -> nop; ldarg.0; call Input::GetButton[Down|Up](string)   (labels kept)
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            List<CodeInstruction> list = new List<CodeInstruction>(instructions);
            Type input = __originalMethod.DeclaringType;
            for (int i = 0; i + 2 < list.Count; i++)
            {
                FieldInfo f = list[i].operand as FieldInfo;
                MethodInfo m = list[i + 2].operand as MethodInfo;
                if (list[i].opcode != OpCodes.Ldsfld || f == null || f.Name != "player") continue;
                if (list[i + 1].opcode != OpCodes.Ldarg_0) continue;
                if (list[i + 2].opcode != OpCodes.Callvirt || m == null || m.DeclaringType == null || m.DeclaringType.Name != "Player") continue;
                if (m.Name != "GetButton" && m.Name != "GetButtonDown" && m.Name != "GetButtonUp") continue;
                MethodInfo ours = input.GetMethod(m.Name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
                if (ours == null) continue;
                list[i].opcode = OpCodes.Nop;
                list[i].operand = null;
                list[i + 2] = new CodeInstruction(OpCodes.Call, ours) { labels = list[i + 2].labels, blocks = list[i + 2].blocks };
                _rerouted++;
            }
            return list;
        }
    }
}
