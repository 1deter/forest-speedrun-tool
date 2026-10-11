using System;
using System.Reflection;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // ModAPI (the mod launcher, e.g. UltimateCheatmenu) rebuilds
    // Assembly-CSharp with parameters Mono 2.0 dies on when reflection
    // reads them (T-0246). The preloader patcher repairs them
    // (patcher/ModApiFix); this says whether it did this launch.
    // ------------------------------------------------------------------
    public static class ModApi
    {
        /// Same as patcher/ModApiFix.DomainKey (the plugin does not
        /// reference the patcher).
        private const string RepairedKey = "ForestOverlay.ModApiFix";

        /// Same as patcher/ModApiFix.Failed.
        private const int RepairFailed = -1;

        public enum Repair { NoModApi, Repaired, NeedsRestart, Failed }

        /// What the plugin does this launch: run, or stay off with a notice.
        public static Repair State()
        {
            return Decide(Present(), AppDomain.CurrentDomain.GetData(RepairedKey));
        }

        /// <paramref name="key"/>: the patcher's DomainKey value - null when
        /// it did not run this launch (just installed or updated), the
        /// repaired count, or Failed.
        public static Repair Decide(bool present, object key)
        {
            if (!present) return Repair.NoModApi;
            if (key == null) return Repair.NeedsRestart;
            if (key is int && (int)key == RepairFailed) return Repair.Failed;
            return Repair.Repaired;
        }

        /// The loaded Assembly-CSharp was rebuilt by ModAPI.
        public static bool Present()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                AssemblyName n;
                try { n = a.GetName(); } catch (Exception) { continue; }
                if (n.Name != "Assembly-CSharp") continue;
                return IsModApiBuild(a.GetReferencedAssemblies());
            }
            return false;
        }

        /// ModAPI's rebuild references its BaseModLib.
        public static bool IsModApiBuild(AssemblyName[] references)
        {
            foreach (AssemblyName r in references)
                if (r.Name == "BaseModLib") return true;
            return false;
        }
    }
}
