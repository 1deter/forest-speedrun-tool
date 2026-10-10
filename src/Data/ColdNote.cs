using System.Globalization;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The restore line's words for Game/ColdReset (T-0269), kept pure so
    // they are tested: what of the player's cold a Quick load set back to
    // a load's values. "" when the player was already as a load gives it.
    // ------------------------------------------------------------------
    public static class ColdNote
    {
        /// A new player's body temperature (PlayerStats' initializer).
        public const float Normal = 37f;

        public static bool AtRest(float temp, bool cold, bool shiver, float blend, float coverage, float timer, bool defrost)
        {
            return temp == Normal && !cold && !shiver && blend == 0f && coverage == 0f && timer == 0f && !defrost;
        }

        public static string Line(float temp, bool cold, bool shiver, float blend, float coverage, float timer, bool defrost)
        {
            if (AtRest(temp, cold, shiver, blend, coverage, timer, defrost)) return "";
            string what = temp != Normal ? "body temperature " + temp.ToString("0.#", CultureInfo.InvariantCulture) + " -> 37" : "";
            if (cold) what += (what.Length > 0 ? ", " : "") + "cold off";
            if (coverage > 0f) what += (what.Length > 0 ? ", " : "") + "frost " + coverage.ToString("0.00", CultureInfo.InvariantCulture) + " -> 0";
            if (what.Length == 0) what = "cold reset";
            return what + " (as a load)";
        }
    }
}
