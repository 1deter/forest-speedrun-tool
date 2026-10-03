using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Puts the endgame's sliding doors back after a Quick load (the lab's
    // automatic doors and the elevator cars' doors).
    //
    // WHY (bridge, v0.24.225): TheForest.World.AutomatedDoorSystem is not in
    // the save - its open amount (`_alpha`), `_state` and `_locked` stay as
    // they were before the restore. After a red elevator ride (the doors
    // open at the top), a Quick load back into the car before the ride kept
    // the door open; the ride's Lock() only sets the flag (the leaves move
    // in Update, which a finished door has switched off), so the "locked"
    // car stayed open for the whole 25 s and the player walked out - a
    // different game from a real run (maks's Quick load reports).
    //
    // WHAT: capture writes `doors = path|alpha|state|locked;...` for every
    // loaded door; a Quick load sets the three back, stops a door mid-move
    // (enabled false - Update finishes what the fields say otherwise) and
    // places the leaves as the game's UpdateDoors does.
    // ------------------------------------------------------------------
    internal sealed class SlidingDoorKeeper
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly ManualLogSource _log;
        private Type _type;
        private FieldInfo _alpha, _state, _locked;
        private MethodInfo _update;

        public SlidingDoorKeeper(ManualLogSource log) { _log = log; }

        private bool Bind()
        {
            if (_type != null) return _alpha != null;
            _type = GameBridge.FindGameType("TheForest.World.AutomatedDoorSystem");
            if (_type == null) { _type = typeof(SlidingDoorKeeper); return false; }
            _alpha = _type.GetField("_alpha", Inst);
            _state = _type.GetField("_state", Inst);
            _locked = _type.GetField("_locked", Inst);
            _update = _type.GetMethod("UpdateDoors", Inst, null, Type.EmptyTypes, null);
            if (_alpha == null || _state == null || _locked == null || _update == null)
            {
                _log.LogWarning("SlidingDoorKeeper: AutomatedDoorSystem fields not found - sliding doors are not kept.");
                _alpha = null;
            }
            return _alpha != null;
        }

        /// The header value at capture; "" when no door is loaded.
        public string Capture()
        {
            try
            {
                if (!Bind()) return "";
                StringBuilder sb = new StringBuilder();
                UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(_type);
                for (int i = 0; i < all.Length; i++)
                {
                    Component c = all[i] as Component;
                    if (c == null || !c.gameObject.scene.IsValid()) continue;
                    if (sb.Length > 0) sb.Append(';');
                    sb.Append(ObjectProbe.PathOf(c.transform)).Append('|')
                      .Append(((float)_alpha.GetValue(c)).ToString("0.###", CultureInfo.InvariantCulture)).Append('|')
                      .Append(Convert.ToInt32(_state.GetValue(c))).Append('|')
                      .Append((bool)_locked.GetValue(c) ? 1 : 0);
                }
                return sb.ToString();
            }
            catch (Exception ex) { _log.LogWarning("SlidingDoorKeeper: capture failed: " + ex.Message); return ""; }
        }

        /// After a Quick load; the log note ("" when nothing changed).
        public string Restore(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                if (!Bind()) return "";
                Dictionary<string, Component> live = new Dictionary<string, Component>();
                UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(_type);
                for (int i = 0; i < all.Length; i++)
                {
                    Component c = all[i] as Component;
                    if (c != null && c.gameObject.scene.IsValid()) live[ObjectProbe.PathOf(c.transform)] = c;
                }

                int changed = 0, same = 0, missing = 0;
                List<string> moved = new List<string>();
                foreach (string entry in value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] p = entry.Split('|');
                    float alpha;
                    int state, locked;
                    if (p.Length < 4 ||
                        !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out alpha) ||
                        !int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out state) ||
                        !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out locked)) continue;
                    Component c;
                    if (!live.TryGetValue(p[0], out c)) { missing++; continue; }
                    MonoBehaviour mb = c as MonoBehaviour;
                    float was = (float)_alpha.GetValue(c);
                    bool sameNow = Mathf.Abs(was - alpha) < 0.001f && Convert.ToInt32(_state.GetValue(c)) == state &&
                                   (bool)_locked.GetValue(c) == (locked != 0) && (mb == null || !mb.enabled);
                    if (sameNow) { same++; continue; }
                    _alpha.SetValue(c, alpha);
                    _state.SetValue(c, Enum.ToObject(_state.FieldType, state));
                    _locked.SetValue(c, locked != 0);
                    if (mb != null) mb.enabled = false;
                    _update.Invoke(c, null);
                    changed++;
                    if (moved.Count < 3 && Mathf.Abs(was - alpha) >= 0.001f)
                        moved.Add(c.transform.parent != null && c.transform.parent.parent != null
                            ? c.transform.parent.parent.name + " " + (alpha > 0.5f ? "open" : "shut")
                            : c.name);
                }
                if (changed == 0 && missing == 0) return "";
                return "sliding doors: " + changed + " put back" +
                       (moved.Count > 0 ? " (" + string.Join(", ", moved.ToArray()) + ")" : "") +
                       (same > 0 ? ", " + same + " as at capture" : "") +
                       (missing > 0 ? ", " + missing + " not loaded" : "");
            }
            catch (Exception ex) { return "sliding doors: restore failed (" + ex.Message + ")"; }
        }
    }
}
