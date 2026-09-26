using System;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // One line: everything that can keep the player from moving (runner
    // Tom, v0.24.112: "right after a Full load he couldn't move in the
    // spot"; not reproduced here - window open or closed, the controller
    // moved and every lock read free). Logged a few seconds after a Full
    // load, so the next report says which hold it was (gotcha 25).
    //
    // Read (all from IL / the bridge): FirstPersonCharacter.Locked /
    // MovementLocked / Grounded / clampInputVal (the hard-landing clamp),
    // the Rigidbody's kinematic flag, the inventory view (a menu or the
    // book), the rope / cliff / knocked-down / cutscene flags of
    // playerAnimatorControl, the Menu input state (our window's block or
    // the pause menu), timeScale and the action FSM's state.
    // ------------------------------------------------------------------
    public static class PlayerHold
    {
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static string Describe()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
                if (local == null) return "LocalPlayer not found";

                object fp = Static(local, "FpCharacter");
                if (fp != null)
                {
                    sb.Append("locked ").Append(YesNo(Get(fp, "Locked")))
                      .Append(", movement locked ").Append(YesNo(Get(fp, "MovementLocked")))
                      .Append(", grounded ").Append(YesNo(Get(fp, "Grounded")));
                    object clamp = Get(fp, "clampInputVal");
                    if (clamp is float && (float)clamp < 1f) sb.Append(", input clamped to ").Append(((float)clamp).ToString("0.00"));
                    Component c = fp as Component;
                    Rigidbody rb = c != null ? c.GetComponent<Rigidbody>() : null;
                    if (rb != null)
                        sb.Append(", kinematic ").Append(rb.isKinematic ? "yes" : "no")
                          .Append(", speed ").Append(rb.velocity.magnitude.ToString("0.0"));
                }
                else sb.Append("no FirstPersonCharacter");

                object inv = Static(local, "Inventory");
                if (inv != null) sb.Append(", view ").Append(Get(inv, "CurrentView"));

                object anim = Static(local, "AnimControl");
                if (anim != null)
                {
                    string[] flags = { "onRope", "cliffClimb", "knockedDown", "endGameCutScene", "introCutScene", "upsideDown", "onRaft" };
                    for (int i = 0; i < flags.Length; i++)
                        if (Get(anim, flags[i]) is bool && (bool)Get(anim, flags[i])) sb.Append(", ").Append(flags[i]);
                }

                Type input = GameBridge.FindGameType("TheForest.Utils.Input");
                Type state = GameBridge.FindGameType("TheForest.Utils.InputState");
                if (input != null && state != null)
                {
                    MethodInfo get = input.GetMethod("GetState", Stat, null, new[] { state }, null);
                    if (get != null)
                        sb.Append(", Menu input ").Append(YesNo(get.Invoke(null, new[] { Enum.Parse(state, "Menu") })));
                }

                sb.Append(", timeScale ").Append(Time.timeScale.ToString("0.##"));

                object setup = Static(local, "ScriptSetup");
                object pm = setup != null ? Get(setup, "pmControl") : null;
                if (pm != null) sb.Append(", action '").Append(Get(pm, "ActiveStateName")).Append('\'');
            }
            catch (Exception ex)
            {
                sb.Append(" (read failed: ").Append((ex.InnerException ?? ex).Message).Append(')');
            }
            return sb.ToString();
        }

        private static object Static(Type t, string name)
        {
            FieldInfo f = t.GetField(name, Stat);
            if (f != null) return f.GetValue(null);
            PropertyInfo p = t.GetProperty(name, Stat);
            return p != null ? p.GetValue(null, null) : null;
        }

        private static object Get(object o, string name)
        {
            Type t = o.GetType();
            FieldInfo f = t.GetField(name, Inst);
            if (f != null) return f.GetValue(o);
            PropertyInfo p = t.GetProperty(name, Inst);
            return p != null ? p.GetValue(o, null) : null;
        }

        private static string YesNo(object b)
        {
            return b is bool ? ((bool)b ? "yes" : "no") : "?";
        }
    }
}
