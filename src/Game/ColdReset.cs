using System;
using System.Reflection;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The player's cold, put back as a load gives it on every restore in
    // place (T-0269, found by T-0248's field diff against a Full load).
    //
    // WHY (decompiled PlayerStats): the cold is outside the save -
    // `BodyTemp` (37 on a new player), `IsCold` (SetCold), `coldSwitch` /
    // `coldFloatBlend` (the shivering animator blend), the screen frost
    // (`FrostScript.coverage`, the main camera's `Frost`) and the frost
    // damage's timer / de-frost (`FrostDamageSettings`). A Full load builds
    // a new player with all of it at rest; a restore in place kept the
    // live values, so a runner frozen since the capture stayed frozen,
    // with frost on the screen and the next frost hit nearer. The game's
    // cold routine (UpdateStats: rain + dark, the north, a cave swim) sets
    // it again from the restored place, as after a Full load.
    //
    // WHAT: SetCold(false) - the game's own call (skin variation too) -
    // then the fields at a new player's values. Says what changed; ""
    // when the player was not cold.
    // ------------------------------------------------------------------
    public static class ColdReset
    {
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // A new player's body temperature (PlayerStats' initializer).
        private const float Normal = 37f;

        private static Type _local;

        public static string AsLoad()
        {
            try
            {
                if (_local == null) _local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
                if (_local == null) return "";
                FieldInfo sf = _local.GetField("Stats", Stat);
                object stats = sf != null ? sf.GetValue(null) : null;
                if (stats == null || !(stats as UnityEngine.Object)) return "";

                float temp = AsFloat(Get(stats, "BodyTemp"), Normal);
                bool cold = Get(stats, "IsCold") is bool && (bool)Get(stats, "IsCold");
                bool shiver = Get(stats, "coldSwitch") is bool && (bool)Get(stats, "coldSwitch");
                float blend = AsFloat(Get(stats, "coldFloatBlend"), 0f);
                object frost = Get(stats, "FrostScript");
                float coverage = frost != null && (frost as UnityEngine.Object) ? AsFloat(Get(frost, "coverage"), 0f) : 0f;
                object settings = Get(stats, "FrostDamageSettings");
                float timer = settings != null ? AsFloat(Get(settings, "CurrentTimer"), 0f) : 0f;
                bool defrost = settings != null && Get(settings, "DoDeFrost") is bool && (bool)Get(settings, "DoDeFrost");

                if (temp == Normal && !cold && !shiver && blend == 0f && coverage == 0f && timer == 0f && !defrost) return "";

                if (cold)
                {
                    MethodInfo m = stats.GetType().GetMethod("SetCold", Inst, null, new Type[] { typeof(bool) }, null);
                    if (m != null) m.Invoke(stats, new object[] { false });
                    else Set(stats, "IsCold", false);
                }
                Set(stats, "BodyTemp", Normal);
                Set(stats, "coldSwitch", false);
                Set(stats, "coldFloatBlend", 0f);
                Set(stats, "ShouldDoWetColdRoll", false);
                if (frost != null && (frost as UnityEngine.Object)) Set(frost, "coverage", 0f);
                if (settings != null)
                {
                    Set(settings, "CurrentTimer", 0f);
                    Set(settings, "DoDeFrost", false);
                    Set(settings, "TakingDamage", false);
                }

                string what = temp != Normal ? "body temperature " + temp.ToString("0.#") + " -> 37" : "";
                if (cold) what += (what.Length > 0 ? ", " : "") + "cold off";
                if (coverage > 0f) what += (what.Length > 0 ? ", " : "") + "frost " + coverage.ToString("0.00") + " -> 0";
                if (what.Length == 0) what = "cold reset";
                return what + " (as a load)";
            }
            catch (Exception ex) { return "cold: failed (" + (ex.InnerException ?? ex).Message + ")"; }
        }

        private static float AsFloat(object v, float otherwise)
        {
            return v is float ? (float)v : otherwise;
        }

        private static object Get(object o, string name)
        {
            Type t = o.GetType();
            FieldInfo f = t.GetField(name, Inst);
            if (f != null) return f.GetValue(o);
            PropertyInfo p = t.GetProperty(name, Inst);
            return p != null && p.GetIndexParameters().Length == 0 ? p.GetValue(o, null) : null;
        }

        private static void Set(object o, string name, object value)
        {
            Type t = o.GetType();
            FieldInfo f = t.GetField(name, Inst);
            if (f != null) { f.SetValue(o, value); return; }
            PropertyInfo p = t.GetProperty(name, Inst);
            if (p != null && p.CanWrite) p.SetValue(o, value, null);
        }
    }
}
