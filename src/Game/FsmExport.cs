using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // A PlayMaker FSM as text (bridge `fsm`, dev only): variables, events,
    // global transitions, then every state with its transitions and its
    // actions' fields. A field bound to an FSM variable reads `{name}`,
    // else its value.
    //
    // WHY: the player's actions (pmControl, pmDamage, combat) are FSMs -
    // data the IL scan cannot see. Tech research and the knowledge bot
    // read them from these files.
    //
    // All by reflection (PlayMaker is not referenced): PlayMakerFSM.Fsm ->
    // Fsm.States (FsmState: Name, Transitions, Actions), Fsm.Variables,
    // Fsm.Events, Fsm.GlobalTransitions, Fsm.StartState.
    // ------------------------------------------------------------------
    public static class FsmExport
    {
        /// The PlayMakerFSM components under an object: the component
        /// itself, a Fsm's owner, or every one on a GameObject (and below
        /// it when `children`).
        public static List<Component> Collect(object target, bool children)
        {
            List<Component> list = new List<Component>();
            Component c = target as Component;
            if (c != null && c.GetType().Name == "PlayMakerFSM") { list.Add(c); return list; }
            if (target != null && target.GetType().Name == "Fsm")
            {
                Component owner = Get(target, "Owner") as Component;
                if (owner != null) list.Add(owner);
                return list;
            }
            GameObject go = target as GameObject ?? (c != null ? c.gameObject : null);
            Transform tr = target as Transform;
            if (tr != null) go = tr.gameObject;
            if (go == null) return list;
            Component[] all = children ? go.GetComponentsInChildren<Component>(true) : go.GetComponents<Component>();
            foreach (Component x in all)
                if (x != null && x.GetType().Name == "PlayMakerFSM") list.Add(x);
            return list;
        }

        public static string FsmName(Component pm)
        {
            object fsm = Get(pm, "Fsm");
            string n = fsm != null ? Get(fsm, "Name") as string : null;
            return string.IsNullOrEmpty(n) ? "FSM" : n;
        }

        /// The whole FSM as text; `states` and `actions` count what was written.
        public static string Write(Component pm, out int states, out int actions)
        {
            states = actions = 0;
            StringBuilder sb = new StringBuilder();
            object fsm = Get(pm, "Fsm");
            sb.Append("fsm ").Append(FsmName(pm)).Append("  on ").Append(ObjectProbe.PathOf(pm.transform)).Append('\n');
            if (fsm == null) { sb.Append("(no Fsm)\n"); return sb.ToString(); }
            sb.Append("start state: ").Append(Get(fsm, "StartState")).Append('\n');
            sb.Append("active state now: ").Append(Get(fsm, "ActiveStateName")).Append('\n');

            object vars = Get(fsm, "Variables");
            sb.Append("\n[variables]\n");
            if (vars != null)
            {
                foreach (string kind in new[] { "FloatVariables", "IntVariables", "BoolVariables", "StringVariables", "Vector2Variables",
                                                "Vector3Variables", "ColorVariables", "RectVariables", "QuaternionVariables",
                                                "GameObjectVariables", "ObjectVariables", "MaterialVariables", "TextureVariables",
                                                "ArrayVariables", "EnumVariables" })
                {
                    IEnumerable arr = Get(vars, kind) as IEnumerable;
                    if (arr == null) continue;
                    string k = kind.Substring(0, kind.Length - "Variables".Length).ToLowerInvariant();
                    foreach (object v in arr)
                        if (v != null) sb.Append("  ").Append(k).Append(' ').Append(Get(v, "Name")).Append(" = ").Append(Value(Get(v, "RawValue") ?? Get(v, "Value"), 0)).Append('\n');
                }
            }

            sb.Append("\n[events]\n  ");
            IEnumerable events = Get(fsm, "Events") as IEnumerable;
            bool first = true;
            if (events != null)
                foreach (object e in events)
                {
                    if (e == null) continue;
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(Get(e, "Name"));
                    if (true.Equals(Get(e, "IsGlobal"))) sb.Append(" (global)");
                }
            sb.Append('\n');

            sb.Append("\n[global transitions]\n");
            AppendTransitions(sb, Get(fsm, "GlobalTransitions") as IEnumerable);

            IEnumerable stateList = Get(fsm, "States") as IEnumerable;
            if (stateList != null)
                foreach (object st in stateList)
                {
                    if (st == null) continue;
                    states++;
                    sb.Append("\n[state ").Append(Get(st, "Name")).Append(']');
                    string desc = Get(st, "Description") as string;
                    if (!string.IsNullOrEmpty(desc)) sb.Append("  ").Append(OneLine(desc));
                    sb.Append('\n');
                    AppendTransitions(sb, Get(st, "Transitions") as IEnumerable);
                    IEnumerable acts = Get(st, "Actions") as IEnumerable;
                    if (acts == null) continue;
                    foreach (object act in acts)
                    {
                        if (act == null) continue;
                        actions++;
                        sb.Append("  action ").Append(act.GetType().Name);
                        if (false.Equals(Get(act, "Enabled"))) sb.Append(" (disabled)");
                        sb.Append('\n');
                        AppendFields(sb, act);
                    }
                }
            return sb.ToString();
        }

        private static void AppendTransitions(StringBuilder sb, IEnumerable list)
        {
            if (list == null) return;
            foreach (object t in list)
                if (t != null) sb.Append("  on ").Append(Get(t, "EventName")).Append(" -> ").Append(Get(t, "ToState")).Append('\n');
        }

        private static void AppendFields(StringBuilder sb, object act)
        {
            for (Type t = act.GetType(); t != null && t.Name != "FsmStateAction" && t != typeof(object); t = t.BaseType)
            {
                FieldInfo[] fs = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (FieldInfo f in fs)
                {
                    if (f.IsNotSerialized || f.Name.StartsWith("<")) continue;
                    if (!f.IsPublic && f.GetCustomAttributes(typeof(SerializeField), true).Length == 0) continue;
                    object v;
                    try { v = f.GetValue(act); } catch (Exception ex) { v = "(" + ex.GetType().Name + ")"; }
                    sb.Append("    ").Append(f.Name).Append(" = ").Append(Value(v, 0)).Append('\n');
                }
            }
        }

        /// A field's value in one line (Data/DumpText.FsmValue).
        private static string Value(object v, int depth) { return DumpText.FsmValue(v, depth, EngineValue); }

        /// The engine's values DumpText cannot see; null for anything else.
        private static string EngineValue(object v)
        {
            if (v is Vector3) return ObjectProbe.Vec((Vector3)v);
            UnityEngine.Object uo = v as UnityEngine.Object;
            if (uo != null) return uo.GetType().Name + " '" + uo.name + "'";
            if (v is UnityEngine.Object) return "null (destroyed)";
            if (v.GetType().Name == "FsmAnimationCurve")
            {
                AnimationCurve curve = Get(v, "curve") as AnimationCurve;
                if (curve == null) return "curve none";
                StringBuilder cs = new StringBuilder("curve");
                foreach (Keyframe k in curve.keys)
                    cs.Append(' ').Append(k.time.ToString("0.###", CultureInfo.InvariantCulture)).Append(':').Append(k.value.ToString("0.###", CultureInfo.InvariantCulture));
                return cs.ToString();
            }
            return null;
        }

        private static string OneLine(string s) { return DumpText.OneLine(s); }

        private static object Get(object o, string member) { return DumpText.Member(o, member); }
    }
}
