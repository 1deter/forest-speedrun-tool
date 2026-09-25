using System;
using System.Reflection;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The sun after a restore (Next: "time of day without cycling through
    // the night", author).
    //
    // WHY (IL, 2026-09-25; game-notes *Time of day and the sun*): the sun
    // follows TheForestAtmosphere.DelayedTimeOfDay, which trails TimeOfDay.
    // More than 5 degrees apart, CatchUpTimeOfDay eases it over 5 s through
    // LerpToTimeOfDay - after a restore to an earlier time, round through
    // the night. The game snaps it instead (DelayedTimeOfDay = TimeOfDay,
    // catch-up off) while LocalPlayer.Inventory is missing or disabled, or
    // when ForceSunRotationUpdate is set (sleeping sets it). Both restores
    // hit the snap in the bridge test (the sun in step 1 s later), so the
    // cycle was not reproduced: this is a guard with its log line.
    //
    // WHAT: after a restore, if the sun is still more than 5 degrees off
    // or catching up, set ForceSunRotationUpdate - the game's own flag;
    // its next Update snaps the sun. Returns "" when in step.
    // ------------------------------------------------------------------
    internal static class SunSync
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type _type;
        private static FieldInfo _instance, _time, _delayed, _catchUp, _force;

        public static string Check()
        {
            try
            {
                if (_type == null)
                {
                    _type = GameBridge.FindGameType("TheForestAtmosphere");
                    if (_type == null) return "";
                    _instance = _type.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    _time = _type.GetField("TimeOfDay", Inst);
                    _delayed = _type.GetField("DelayedTimeOfDay", Inst);
                    _catchUp = _type.GetField("CatchUpTimeOfDay", Inst);
                    _force = _type.GetField("ForceSunRotationUpdate", Inst);
                }
                if (_instance == null || _time == null || _delayed == null || _force == null) return "";
                object atm = _instance.GetValue(null);
                if (atm == null) return "";
                float time = (float)_time.GetValue(atm), sun = (float)_delayed.GetValue(atm);
                bool catching = _catchUp != null && (bool)_catchUp.GetValue(atm);
                float off = ((time - sun) % 360f + 360f) % 360f;   // how far the sun trails, forward
                if (!catching && (off <= 5f || off >= 355f)) return "";
                _force.SetValue(atm, true);
                return "sun: " + off.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                       " deg behind the time" + (catching ? ", catching up" : "") + " - snapped (the game's ForceSunRotationUpdate)";
            }
            catch (Exception ex) { return "sun: check failed (" + ex.Message + ")"; }
        }
    }
}
