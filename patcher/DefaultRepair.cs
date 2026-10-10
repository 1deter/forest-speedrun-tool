using System;
using System.Collections.Generic;
using Mono.Cecil;

namespace ForestOverlay.Updater
{
    // ------------------------------------------------------------------
    // Puts back the default values ModAPI drops (T-0246). No BepInEx here,
    // so it is tested against an assembly built in memory.
    //
    // ModAPI rebuilds Assembly-CSharp: a method a mod replaces is kept as
    // __Name__Original and a new Name is written that calls the mod. The
    // new method's optional parameters keep the HasDefault flag but lose
    // their row in the Constant table (UltimateCheatmenu 2.3.6:
    // PlayerInventory.RemoveItem). The game never asks, so ModAPI alone
    // runs; reflection does (Harmony patching any method of that type),
    // and the game's Mono 2.0 then asserts "mono_class_from_mono_type:
    // implement me 0x00" and the process dies with nothing in the log.
    //
    // Each such parameter gets the default of the same parameter on
    // __Name__Original when it has one, else loses the HasDefault flag.
    // ------------------------------------------------------------------
    public static class DefaultRepair
    {
        /// The assembly ModAPI's rebuilt Assembly-CSharp references.
        public const string ModApiAssembly = "BaseModLib";

        public static bool ReferencesModApi(ModuleDefinition module)
        {
            foreach (AssemblyNameReference r in module.AssemblyReferences)
                if (r.Name == ModApiAssembly) return true;
            return false;
        }

        /// Repairs every HasDefault parameter without a constant; returns
        /// how many. `log` gets one line per parameter.
        public static int Repair(ModuleDefinition module, Action<string> log)
        {
            int repaired = 0;
            foreach (TypeDefinition t in AllTypes(module))
            {
                if (!t.HasMethods) continue;
                foreach (MethodDefinition m in t.Methods)
                {
                    if (!m.HasParameters) continue;
                    for (int i = 0; i < m.Parameters.Count; i++)
                    {
                        ParameterDefinition p = m.Parameters[i];
                        if (!p.HasDefault || p.HasConstant) continue;
                        object value;
                        string how;
                        if (TryOriginalDefault(t, m, i, out value))
                        {
                            p.Constant = value;
                            how = "default " + (value == null ? "null" : value.ToString()) + " from " + OriginalName(m.Name);
                        }
                        else
                        {
                            p.HasDefault = false;
                            how = "no default to copy, HasDefault cleared";
                        }
                        repaired++;
                        if (log != null) log(t.FullName + "::" + m.Name + " " + p.Name + ": " + how);
                    }
                }
            }
            return repaired;
        }

        public static string OriginalName(string name)
        {
            return "__" + name + "__Original";
        }

        private static bool TryOriginalDefault(TypeDefinition t, MethodDefinition m, int index, out object value)
        {
            value = null;
            string name = OriginalName(m.Name);
            foreach (MethodDefinition o in t.Methods)
            {
                if (o.Name != name || o.Parameters.Count != m.Parameters.Count) continue;
                bool same = true;
                for (int i = 0; i < o.Parameters.Count && same; i++)
                    same = o.Parameters[i].ParameterType.FullName == m.Parameters[i].ParameterType.FullName;
                if (!same) continue;
                ParameterDefinition p = o.Parameters[index];
                if (!p.HasConstant) return false;
                value = p.Constant;
                return true;
            }
            return false;
        }

        private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
        {
            Stack<TypeDefinition> todo = new Stack<TypeDefinition>();
            foreach (TypeDefinition t in module.Types) todo.Push(t);
            while (todo.Count > 0)
            {
                TypeDefinition t = todo.Pop();
                yield return t;
                if (t.HasNestedTypes) foreach (TypeDefinition n in t.NestedTypes) todo.Push(n);
            }
        }
    }
}
