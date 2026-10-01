using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Creative-speed building in any mode (sxczurass, QA 2026-09-27;
    // v0.24.196): hold Build to keep adding resources to a blueprint, as
    // Creative does - speed only, every resource still comes out of the
    // inventory. A gameplay mod: practice.
    //
    // HOW (IL): Craft_Structure.Update adds an ingredient when
    //   GetBuildInputDown() || (Cheats.Creative && Input.GetButton("Build"))
    // and in Creative it also sets _nextAddItem = Time.time + 0.065 (the
    // pace of the hold). Those two Creative reads - the ones after the
    // GetBuildInputDown call - become "Creative || this switch". The earlier
    // read (the icon colour: owned or Creative) and AddIngredient's own one
    // (Creative takes nothing from the bag) are left alone, so nothing is
    // free.
    // ------------------------------------------------------------------
    public static class FastBuild
    {
        public static bool On;
        private static Harmony _harmony;
        private static int _replaced;

        /// "on" / why not, for the tab.
        public static string Status = "not installed";

        public static void Install(ManualLogSource log, string harmonyId)
        {
            try
            {
                Type cs = GameBridge.FindGameType("TheForest.Buildings.Creation.Craft_Structure");
                MethodInfo update = cs != null ? cs.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) : null;
                if (update == null) { Status = "Craft_Structure.Update not found"; log.LogWarning("FastBuild: " + Status + "."); return; }
                _harmony = new Harmony(harmonyId + ".fastbuild");
                _harmony.Patch(update, transpiler: new HarmonyMethod(typeof(FastBuild).GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic)));
                Status = _replaced == 2 ? "on" : "patched " + _replaced + " of 2 reads - the game's code differs";
                log.LogInfo("FastBuild: " + _replaced + " Creative read(s) in Craft_Structure.Update follow the switch.");
            }
            catch (Exception ex) { Status = "could not patch: " + ex.Message; log.LogWarning("FastBuild: " + ex); }
        }

        public static void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        /// The patched reads: the game's Creative flag, or the switch.
        public static bool OrOn(bool creative) { return creative || On; }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo orOn = typeof(FastBuild).GetMethod("OrOn");
            bool afterInput = false;
            _replaced = 0;
            foreach (CodeInstruction ci in instructions)
            {
                yield return ci;
                MethodInfo m = ci.operand as MethodInfo;
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && m != null && m.Name == "GetBuildInputDown") afterInput = true;
                FieldInfo f = ci.operand as FieldInfo;
                if (afterInput && ci.opcode == OpCodes.Ldsfld && f != null && f.Name == "Creative" && f.DeclaringType != null && f.DeclaringType.Name == "Cheats" && _replaced < 2)
                {
                    _replaced++;
                    yield return new CodeInstruction(OpCodes.Call, orOn);
                }
            }
        }
    }
}
