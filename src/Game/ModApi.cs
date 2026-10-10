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

        /// The loaded Assembly-CSharp was rebuilt by ModAPI.
        public static bool Present()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                AssemblyName n;
                try { n = a.GetName(); } catch (Exception) { continue; }
                if (n.Name != "Assembly-CSharp") continue;
                foreach (AssemblyName r in a.GetReferencedAssemblies())
                    if (r.Name == "BaseModLib") return true;
                return false;
            }
            return false;
        }

        /// ModAPI is here but the patcher's repair did not run (the patcher
        /// was installed or updated during this launch): patching the game
        /// now would kill it.
        public static bool Unrepaired()
        {
            return Present() && AppDomain.CurrentDomain.GetData(RepairedKey) == null;
        }
    }
}
