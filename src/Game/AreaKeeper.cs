using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Puts the endgame's active area back after a Quick load (runner maks:
    // after the red elevator a Quick load streamed in the hallway's
    // textures, which were invisible at capture).
    //
    // WHY (bridge + IL, v0.24.41): the endgame's Sections/* carry
    // TheForest.World.Areas.Area + AreaMembers. Entering an area (OnEnter)
    // makes it the static Area.ActiveArea and Load()s it and its
    // neighbours (NeighbourTokens); leaving (OnLeave) unloads those whose
    // tokens reach 0, and Load / Unload switch the members' renderers,
    // lights and probes. In the "invisible section" no area is active.
    // None of that is in the save: after the elevator ride a Quick load
    // kept ActiveArea = ControlRoom (the overlook), which kept its
    // neighbour HellCorridor loaded. ControlRoom.OnLeave(null) by hand
    // through the bridge made the hallway invisible again, as at capture.
    //
    // WHAT: capture writes `activearea = <path>` or `none`; a Quick load
    // that finds another area active leaves it (captured `none`) or enters
    // the captured one (OnEnter leaves the current one itself).
    // ------------------------------------------------------------------
    internal sealed class AreaKeeper
    {
        public const string None = "none";
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly ManualLogSource _log;
        private Type _type;
        private FieldInfo _active;
        private MethodInfo _onEnter, _onLeave;

        public AreaKeeper(ManualLogSource log) { _log = log; }

        private bool Bind()
        {
            if (_type != null) return _active != null && _onEnter != null && _onLeave != null;
            _type = GameBridge.FindGameType("TheForest.World.Areas.Area");
            if (_type == null) { _type = typeof(AreaKeeper); return false; }
            _active = _type.GetField("ActiveArea", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            _onEnter = _type.GetMethod("OnEnter", Inst);
            _onLeave = _type.GetMethod("OnLeave", Inst);
            bool ok = _active != null && _onEnter != null && _onLeave != null;
            if (!ok) _log.LogWarning("AreaKeeper: Area members not found - the active area is not kept.");
            return ok;
        }

        private Component Live()
        {
            Component c = _active.GetValue(null) as Component;
            return c != null ? c : null;   // fake-null -> null
        }

        /// The header value at capture: the active area's path, `none`, or
        /// "" when areas are unknown (no endgame loaded, or not bound).
        public string Capture()
        {
            try
            {
                if (!Bind()) return "";
                if (UnityEngine.Object.FindObjectOfType(_type) == null) return "";
                Component live = Live();
                return live != null ? ObjectProbe.PathOf(live.transform) : None;
            }
            catch (Exception ex) { _log.LogWarning("AreaKeeper: capture failed: " + ex.Message); return ""; }
        }

        /// After a Quick load; returns the log note ("" when nothing to do).
        public string Restore(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                if (!Bind()) return "";
                Component live = Live();
                string livePath = live != null ? ObjectProbe.PathOf(live.transform) : None;
                if (livePath == value) return "";

                if (value == None)
                {
                    _onLeave.Invoke(live, new object[] { null });
                    return "area: left '" + livePath + "' (none active at capture)";
                }

                UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(_type);
                for (int i = 0; i < all.Length; i++)
                {
                    Component c = all[i] as Component;
                    if (c == null || ObjectProbe.PathOf(c.transform) != value) continue;
                    _onEnter.Invoke(c, new object[] { live });
                    return "area: entered '" + value + "' (was '" + livePath + "')";
                }
                return "area: '" + value + "' not loaded - left '" + livePath + "' as it is";
            }
            catch (Exception ex)
            {
                while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                return "area: restore failed (" + ex.Message + ")";
            }
        }
    }
}
