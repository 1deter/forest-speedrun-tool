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
    //
    // A plain teleport (Go) out of the endgame kept both the area and the
    // overlook flag the elevator ride set: the vault door cave looked wrong
    // and the Sahara cave's outside was invisible while its corridors
    // showed. Clearing both by hand fixed it (author, v0.24.42). So a
    // teleport landing outside every section's renderers leaves the active
    // area and clears the overlook flag; one landing inside a section is
    // left alone (the gates re-enter areas as you walk). The endgame flag
    // too (v0.24.61): a teleport from the lab to the surface kept
    // IsInEndgame and the surface was lit like a cave.
    // A red elevator ride under way is stopped first (v0.24.100): left
    // running, its end ~30 s later half-unloaded the cave teleported to.
    //
    // Except the vault entrance (v0.24.112): the tunnel between the
    // LoadEndgame trigger box (EndgameEntrance/LoadEndgame, tag
    // EndgameLoader) and the vault door is outside every section, but a
    // run has the flag set there - the box's forward crossing sends
    // EnterEndgame (LocalPlayer.SetInEndGame) - and the door's onDoorOpen
    // (PlayerInEndgameTester.DoPositionningTest) starts the endgame load
    // only with the flag set. A Go there cleared it (or, from the surface,
    // never set it), and the opened door led into an unloaded endgame
    // (bridge, 2026-09-26). So a destination on the box's forward side,
    // near the tunnel, keeps the flag or sets it as the crossing does.
    // Only there: a Go from the surface straight into a section (the lab)
    // still leaves the flag as it was (false) - if something in the
    // endgame misbehaves after a Go, look here first.
    // ------------------------------------------------------------------
    internal sealed class AreaKeeper
    {
        public const string None = "none";
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly ManualLogSource _log;
        private Type _type;
        private FieldInfo _active;
        private MethodInfo _onEnter, _onLeave;

        private readonly ElevatorKeeper _rides;

        public AreaKeeper(ManualLogSource log) { _log = log; _rides = new ElevatorKeeper(log); }

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

        /// Before a plain teleport to `dest`; returns the log note ("" when
        /// nothing to do). Cheap when no area is active and no overlook.
        public string ForTeleport(Vector3 dest)
        {
            // A ride under way first, wherever the teleport goes (ElevatorKeeper.StopRides).
            string ride = _rides.StopRides();
            string areas = Areas(dest);
            if (ride.Length == 0) return areas;
            return areas.Length > 0 ? ride + ", " + areas : ride;
        }

        private string Areas(Vector3 dest)
        {
            try
            {
                if (!Bind()) return "";
                Component live = Live();
                bool overlook = AreaReport.InOverlook();
                bool endgame = AreaReport.InEndgame();
                bool vault = InVaultEntrance(dest);
                if (live == null && !overlook && !endgame && !vault) return "";
                if (InsideASection(dest)) return "";

                string note = "";
                if (live != null)
                {
                    note = "area: left '" + ObjectProbe.PathOf(live.transform) + "'";
                    _onLeave.Invoke(live, new object[] { null });
                }
                if (overlook)
                {
                    string o = AreaReport.LeaveOverlook();
                    if (o.Length > 0) note += (note.Length > 0 ? ", " : "") + "overlook flag cleared";
                }
                if (vault)
                {
                    string e = endgame ? "endgame flag kept" : AreaReport.EnterEndgame();
                    if (e.Length > 0) note += (note.Length > 0 ? ", " : "") + e;
                    return note + " (vault entrance, past the LoadEndgame box)";
                }
                if (endgame)
                {
                    string e = AreaReport.LeaveEndgame();
                    if (e.Length > 0) note += (note.Length > 0 ? ", " : "") + e;
                }
                return note.Length > 0 ? note + " (outside the endgame sections)" : "";
            }
            catch (Exception ex)
            {
                while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
                return "area: teleport sync failed (" + ex.Message + ")";
            }
        }

        // The tunnel from the LoadEndgame box to the vault door, in the box's
        // frame (bridge, 2026-09-26): box at (147, -405.5, 1289.9), yaw
        // 283.1, size (60, 30, 12); the door's player spot is 103 m
        // forward, 19 m to the side, 22 m up. The door's side of the box
        // only; the approach behind it is outside, as in a run.
        private const float VaultAhead = 130f, VaultSide = 45f, VaultBelow = 25f, VaultAbove = 50f;
        private Transform _loadBox;

        private bool InVaultEntrance(Vector3 p)
        {
            if (_loadBox == null)
            {
                GameObject go = GameObject.FindWithTag("EndgameLoader");
                if (go == null) return false;
                _loadBox = go.transform;
            }
            Vector3 d = p - _loadBox.position;
            float ahead = Vector3.Dot(_loadBox.forward, d);
            float side = Vector3.Dot(_loadBox.right, d);
            return ahead > 0f && ahead < VaultAhead && Mathf.Abs(side) < VaultSide
                && d.y > -VaultBelow && d.y < VaultAbove;
        }

        private Type _members;
        private FieldInfo _renderers;

        private bool InsideASection(Vector3 p)
        {
            if (_members == null)
            {
                _members = GameBridge.FindGameType("TheForest.World.Areas.AreaMembers");
                if (_members != null) _renderers = _members.GetField("_renderers", Inst);
            }
            if (_members == null || _renderers == null) return true;   // unknown: leave it
            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(_members);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer[] rs = _renderers.GetValue(all[i]) as Renderer[];
                if (rs == null) continue;
                for (int j = 0; j < rs.Length; j++)
                {
                    if (rs[j] == null) continue;
                    Bounds b = rs[j].bounds;   // valid while disabled too
                    b.Expand(2f);
                    if (b.Contains(p)) return true;
                }
            }
            return false;
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
