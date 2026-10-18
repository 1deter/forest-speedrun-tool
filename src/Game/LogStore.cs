using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Logs in the inventory - a GAMEPLAY MOD (runner sxczurass, shaped
    // with the author, 2026-09-27): picked-up logs are stored with a
    // counter up to a cap instead of carried in the arms; the hands stay
    // free. Off by default, practice-marked.
    //
    // THE GAME (IL): logs are not an inventory item. PlayerInventory's
    // AddItemNF / RemoveItemNF / AmountOfNF / OwnsNF short-circuit item 78
    // to LogControler (PlayerInventory.Logs): _logs is the count (at most
    // 2), Lift() adds one (a held model shown, the weapon put away,
    // animator bools set), PutDown(fake, drop, equipPrevious, preSpawned)
    // takes one (drop = a log spawned in front of the player; fake = spawn
    // without taking). Building and fires take logs through RemoveItem(78);
    // the log sled / holders (LogHolder, MultiHolder) and repairs
    // (RepairTool, BuildingRepair) read Logs.Amount / HasLogs and call
    // Lift / PutDown themselves. The save keeps _logs and re-Lifts that
    // many on load (LogControler.OnDeserialized).
    //
    // WHAT THIS DOES, while on: the stored count IS _logs (so the game's
    // save and our savestates carry it for free), but
    //   - Lift stores one (up to the cap; full = refused, the log stays),
    //     nothing shown in the arms;
    //   - PutDown takes from the store (a drop still spawns the log);
    //   - Amount / HasLogs answer "arms empty" to the rest of the game, so
    //     ropes, ziplines, sitting, death etc. never drop stored logs and
    //     weapons stay usable - and carrying costs no energy;
    //   - AmountOfNF / OwnsNF(78) answer the store (building, fires);
    //   - the holder / repair methods read the store instead (transpiled
    //     calls, so they alone see it);
    //   - the getter patch alone is not enough: the JIT inlines the 10-byte
    //     getters into callers compiled before it, so those callers (Equip,
    //     HideAllEquiped...) are transpiled to "arms empty" too (T-0431,
    //     LogStoreTables.Arms).
    // Not covered: putting a log on a zipline (needs a log in the arms).
    // Turning it off moves up to 2 back into the arms and drops the rest.
    // A save made with more than 2 stored and loaded with this off keeps 2
    // (the game's own cap).
    // ------------------------------------------------------------------
    public static class LogStore
    {
        public const int LogItemId = 78;

        private static ManualLogSource _log;
        private static Harmony _harmony;
        private static string _harmonyId;
        private static bool _patched;
        private static int _holderCalls;

        /// Largest stored count; set from the config.
        public static int Cap = 5;
        public static bool Active { get { return _patched; } }
        public static string Status = "off";

        // Reflection, resolved once.
        private static bool _resolved;
        private static Type _ctrlType;
        private static FieldInfo _logsField, _heldField;
        private static MethodInfo _lift, _putDown, _removeLog, _getAmount, _getHasLogs;
        private static PropertyInfo _logsProp;

        private static int _armsCalls, _patchMs;

        public static void Init(ManualLogSource log, string harmonyId)
        {
            _log = log;
            _harmonyId = harmonyId + ".logstore";
        }

        private static bool Resolve()
        {
            if (_resolved) return _ctrlType != null;
            _resolved = true;
            BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _ctrlType = GameBridge.FindGameType("TheForest.Items.Special.LogControler");
            Type inv = GameBridge.FindGameType("TheForest.Items.Inventory.PlayerInventory");
            if (_ctrlType == null || inv == null) { _ctrlType = null; return false; }
            _logsField = _ctrlType.GetField("_logs", inst);
            _heldField = _ctrlType.GetField("_logsHeld", inst);
            _lift = _ctrlType.GetMethod("Lift", inst, null, Type.EmptyTypes, null);
            _putDown = _ctrlType.GetMethod("PutDown", inst);
            _removeLog = _ctrlType.GetMethod("RemoveLog", inst);
            _getAmount = _ctrlType.GetMethod("get_Amount", inst);
            _getHasLogs = _ctrlType.GetMethod("get_HasLogs", inst);
            _logsProp = inv.GetProperty("Logs", inst);
            if (_logsField == null || _heldField == null || _lift == null || _putDown == null ||
                _removeLog == null || _getAmount == null || _getHasLogs == null || _logsProp == null)
            {
                _ctrlType = null;
                return false;
            }
            return true;
        }

        /// The player's LogControler, or null (title screen, loading).
        private static object Controller()
        {
            object inv = GameBridge.ReadStaticField("TheForest.Utils.LocalPlayer", "Inventory");
            if (inv as UnityEngine.Object == null) return null;
            try { return _logsProp.GetValue(inv, null) as UnityEngine.Object; }
            catch (Exception) { return null; }
        }

        private static int Logs(object ctrl) { return (int)_logsField.GetValue(ctrl); }
        private static void SetLogs(object ctrl, int n) { _logsField.SetValue(ctrl, n); }

        /// The stored count, or -1 with no player.
        public static int Stored()
        {
            if (!_patched || !Resolve()) return -1;
            object c = Controller();
            return c != null ? Logs(c) : -1;
        }

        // ------------------------------------------------------------------
        /// Called every frame with the setting: patches in or out, logs
        /// moved between the arms and the store once a player exists.
        public static void Maintain(bool want)
        {
            if (!want && !_patched) return;
            if (!Resolve()) { Status = "unavailable: LogControler not found (game updated?)"; return; }
            object ctrl = Controller();

            if (want && !_patched)
            {
                if (ctrl == null) { Status = "waiting for a game"; return; }
                int t0 = Environment.TickCount;
                string err = Patch();
                _patchMs = Environment.TickCount - t0;
                if (err != null) { Status = "failed: " + err; Unpatch(); return; }
                int held = PutHeldAway(ctrl);
                Status = "on";
                _log.LogInfo("Logs in the inventory: on, cap " + Cap + " - " + held + " log(s) moved from the arms, " +
                             Logs(ctrl) + " stored; " + _holderCalls + " holder / repair reads redirected, " + _armsCalls + " arm reads emptied; patched in " + _patchMs + " ms.");
            }
            else if (!want && _patched)
            {
                Unpatch();
                Status = "off";
                if (ctrl == null) return;
                int n = Logs(ctrl);
                SetLogs(ctrl, 0);
                int lifted = 0, dropped = 0;
                for (int i = 0; i < n; i++)
                {
                    if (lifted < 2 && (bool)_lift.Invoke(ctrl, null)) lifted++;
                    else { _putDown.Invoke(ctrl, new object[] { true, true, false, null }); dropped++; }
                }
                _log.LogInfo("Logs in the inventory: off - " + lifted + " log(s) back in the arms, " + dropped + " dropped.");
            }
            else if (_patched && ctrl != null)
            {
                // A restore or load can bring back held models: hide them.
                int held = PutHeldAway(ctrl);
                if (held > 0) _log.LogInfo("Logs in the inventory: " + held + " held log model(s) put away after a load; " + Logs(ctrl) + " stored.");
            }
        }

        /// Hides the held models and undoes what Lift did to the player,
        /// keeping the count. Returns how many were shown.
        private static int PutHeldAway(object ctrl)
        {
            GameObject[] held = _heldField.GetValue(ctrl) as GameObject[];
            if (held == null) return 0;
            int shown = 0;
            for (int i = 0; i < held.Length; i++) if (held[i] != null && held[i].activeSelf) shown++;
            if (shown == 0) return 0;
            int keep = Logs(ctrl);
            // RemoveLog hides _logsHeld[_logs] and, at 0, clears the
            // animator bools and re-equips the weapon put away.
            SetLogs(ctrl, shown);
            for (int i = 0; i < shown; i++) _removeLog.Invoke(ctrl, new object[] { true });
            SetLogs(ctrl, keep);
            return shown;
        }

        // ------------------------------------------------------------------
        private static string Patch()
        {
            _harmony = new Harmony(_harmonyId);
            _patched = true;
            Type me = typeof(LogStore);
            BindingFlags st = BindingFlags.Static | BindingFlags.NonPublic;
            _harmony.Patch(_lift, new HarmonyMethod(me.GetMethod("LiftPrefix", st)));
            _harmony.Patch(_putDown, new HarmonyMethod(me.GetMethod("PutDownPrefix", st)));
            _harmony.Patch(_getAmount, new HarmonyMethod(me.GetMethod("AmountPrefix", st)));
            _harmony.Patch(_getHasLogs, new HarmonyMethod(me.GetMethod("HasLogsPrefix", st)));

            Type inv = GameBridge.FindGameType("TheForest.Items.Inventory.PlayerInventory");
            BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo amountOf = inv.GetMethod("AmountOfNF", inst);
            MethodInfo owns = inv.GetMethod("OwnsNF", inst);
            if (amountOf == null || owns == null) return "PlayerInventory.AmountOfNF / OwnsNF not found";
            _harmony.Patch(amountOf, postfix: new HarmonyMethod(me.GetMethod("AmountOfPostfix", st)));
            _harmony.Patch(owns, postfix: new HarmonyMethod(me.GetMethod("OwnsPostfix", st)));

            _holderCalls = 0;
            _armsCalls = 0;
            List<string> missing = new List<string>();
            PatchTable(LogStoreTables.Holder, me.GetMethod("HolderTranspiler", st), missing);
            PatchTable(LogStoreTables.Arms, me.GetMethod("ArmsTranspiler", st), missing);
            if (missing.Count > 0)
                _log.LogWarning("Logs in the inventory: not found or nothing to rewrite, stored logs not handled there: " + string.Join(", ", missing.ToArray()));
            return null;
        }

        /// Transpiles every declared method (all overloads) of each table row.
        private static void PatchTable(string[][] table, MethodInfo transpiler, List<string> missing)
        {
            HarmonyMethod tr = new HarmonyMethod(transpiler);
            BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                               BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            bool arms = table == LogStoreTables.Arms;
            for (int i = 0; i < table.Length; i++)
            {
                string label = table[i][0] + "." + table[i][1];
                Type t = GameBridge.FindGameType(table[i][0]);
                if (t == null) { missing.Add(label); continue; }
                MethodInfo[] ms = t.GetMethods(all);
                bool any = false;
                for (int k = 0; k < ms.Length; k++)
                {
                    if (ms[k].Name != table[i][1] || ms[k].IsAbstract) continue;
                    int before = arms ? _armsCalls : _holderCalls;
                    try { _harmony.Patch(ms[k], transpiler: tr); }
                    catch (Exception ex) { _log.LogWarning("Logs in the inventory: patch " + label + ": " + ex.Message); continue; }
                    if ((arms ? _armsCalls : _holderCalls) > before) any = true;
                }
                if (!any) missing.Add(label);
            }
        }

        private static void Unpatch()
        {
            _patched = false;
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception ex) { _log.LogWarning("Logs in the inventory: unpatch: " + ex.Message); }
            _harmony = null;
        }

        public static void Shutdown() { Unpatch(); }

        // ------------------------------------------------------------------
        // Patches. The instance is the player's own LogControler.

        private static bool LiftPrefix(object __instance, ref bool __result)
        {
            try
            {
                int n = Logs(__instance);
                if (n >= Cap)
                {
                    __result = false;
                    Evidence("full at " + n + ", refused");
                    if (Full != null) Full(Cap);
                    return false;
                }
                SetLogs(__instance, n + 1);
                PlayWhoosh();
                Evidence(n + " -> " + (n + 1));
                __result = true;
                return false;
            }
            catch (Exception) { return true; }
        }

        private static bool PutDownPrefix(object __instance, ref bool fake, ref bool __result)
        {
            try
            {
                if (fake) return true;   // spawns a log without taking one: the game's own
                int n = Logs(__instance);
                if (n <= 0) { __result = false; return false; }
                SetLogs(__instance, n - 1);
                Evidence(n + " -> " + (n - 1));
                // The rest of PutDown with nothing taken: the drop (if any)
                // and the log-count sync.
                fake = true;
                return true;
            }
            catch (Exception) { return true; }
        }

        /// One line per change of the stored count, with who asked -
        /// pickups, building, holders, a load (gotcha 16). Rare events.
        private static void Evidence(string what)
        {
            try
            {
                System.Diagnostics.StackFrame[] frames = new System.Diagnostics.StackTrace(2, false).GetFrames();
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                int shown = 0;
                for (int i = 0; frames != null && i < frames.Length && shown < 4; i++)
                {
                    MethodBase mb = frames[i].GetMethod();
                    Type t = mb != null ? mb.DeclaringType : null;
                    if (t == null || t.Namespace == "HarmonyLib" || t == typeof(LogStore)) continue;
                    if (shown++ > 0) sb.Append(" <- ");
                    sb.Append(t.Name).Append('.').Append(mb.Name);
                }
                _log.LogInfo("Logs in the inventory: stored " + what + " (" + sb + ").");
            }
            catch (Exception) { }
        }

        /// A savestate's stored count, set after its restore (the game's
        /// own serialization of _logs is not relied on). -1 = not recorded.
        public static void Apply(int logs, string context)
        {
            if (logs < 0 || !_patched || !Resolve()) return;
            object c = Controller();
            if (c == null) return;
            int was = Logs(c);
            SetLogs(c, Math.Min(logs, Cap));
            PutHeldAway(c);
            _log.LogInfo(context + ": logs in the inventory " + was + " -> " + Logs(c) + " (as captured).");   // log: Savestate restore, Savestate after the load
        }

        private static bool AmountPrefix(ref int __result) { __result = 0; return false; }
        private static bool HasLogsPrefix(ref bool __result) { __result = false; return false; }

        private static void AmountOfPostfix(object __instance, int itemId, ref int __result)
        {
            if (itemId != LogItemId) return;
            try { object c = _logsProp.GetValue(__instance, null); if (c as UnityEngine.Object != null) __result = Logs(c); }
            catch (Exception) { }
        }

        private static void OwnsPostfix(object __instance, int itemId, ref bool __result)
        {
            if (itemId != LogItemId) return;
            try { object c = _logsProp.GetValue(__instance, null); if (c as UnityEngine.Object != null) __result = Logs(c) > 0; }
            catch (Exception) { }
        }

        // `callvirt LogControler::get_Amount / get_HasLogs` -> our readers,
        // which take the same instance off the stack.
        private static IEnumerable<CodeInstruction> HolderTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo amount = typeof(LogStore).GetMethod("HolderAmount");
            MethodInfo has = typeof(LogStore).GetMethod("HolderHasLogs");
            foreach (CodeInstruction ci in instructions)
            {
                if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && ci.operand is MethodInfo)
                {
                    MethodInfo target = (MethodInfo)ci.operand;
                    bool ours = target.DeclaringType == _ctrlType;
                    if (ours && target.Name == "get_Amount") { ci.opcode = OpCodes.Call; ci.operand = amount; _holderCalls++; }
                    else if (ours && target.Name == "get_HasLogs") { ci.opcode = OpCodes.Call; ci.operand = has; _holderCalls++; }
                }
                yield return ci;
            }
        }

        // The same rewrite to "arms empty": the readers the getter prefix
        // misses because the JIT inlined the getter into them (T-0431).
        private static IEnumerable<CodeInstruction> ArmsTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo amount = typeof(LogStore).GetMethod("ArmsAmount");
            MethodInfo has = typeof(LogStore).GetMethod("ArmsHasLogs");
            foreach (CodeInstruction ci in instructions)
            {
                if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && ci.operand is MethodInfo)
                {
                    MethodInfo target = (MethodInfo)ci.operand;
                    bool ours = target.DeclaringType == _ctrlType;
                    if (ours && target.Name == "get_Amount") { ci.opcode = OpCodes.Call; ci.operand = amount; _armsCalls++; }
                    else if (ours && target.Name == "get_HasLogs") { ci.opcode = OpCodes.Call; ci.operand = has; _armsCalls++; }
                }
                yield return ci;
            }
        }

        public static int ArmsAmount(object ctrl) { return 0; }
        public static bool ArmsHasLogs(object ctrl) { return false; }

        /// Holders compare the count with 2 (take while under 2, add while
        /// over 0): answered as 2 when the store is full, 1 when it has
        /// room and something in it, 0 when empty.
        public static int HolderAmount(object ctrl)
        {
            int n = Logs(ctrl);
            return n >= Cap ? 2 : n > 0 ? 1 : 0;
        }

        public static bool HolderHasLogs(object ctrl) { return Logs(ctrl) > 0; }

        /// Set by the module: the store refused a log (the cap).
        public static Action<int> Full;

        private static MethodInfo _whoosh;
        private static void PlayWhoosh()
        {
            try
            {
                object sfx = GameBridge.ReadStaticField("TheForest.Utils.LocalPlayer", "Sfx");
                if (sfx == null) return;
                if (_whoosh == null) _whoosh = sfx.GetType().GetMethod("PlayWhoosh", Type.EmptyTypes);
                if (_whoosh != null) _whoosh.Invoke(sfx, null);
            }
            catch (Exception) { }
        }
    }
}
