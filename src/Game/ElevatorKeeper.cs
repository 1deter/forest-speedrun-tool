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
    // Puts the endgame elevators back after a Quick load (runner maks: after
    // the red elevator ride a Quick load left the car at the overlook).
    //
    // WHY (bridge + IL, v0.24.40): TheForest.World.ElevatorSystem is not in
    // the save. Its ride (Goto) sets `_useCount` (limit 1), moves the
    // kinematic car (`_rb`, e.g. HellCorridor/Elevator_01a) to the other
    // stop in one step and ends with `_moving` false. After a Quick load the
    // car stayed at the overlook (y 705) with `_useCount` 1, so the ride
    // could not be taken again. Setting the car's position and `_useCount`
    // back by hand through the bridge made the elevator work again.
    //
    // WHAT: capture writes `elevators = path|useCount|x,y,z|ex,ey,ez;...`
    // for every loaded elevator; a Quick load moves each car back and sets
    // its use count. An elevator moving at restore time is left alone.
    // ------------------------------------------------------------------
    internal sealed class ElevatorKeeper
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly ManualLogSource _log;
        private Type _type;
        private FieldInfo _rb, _useCount, _moving;

        public ElevatorKeeper(ManualLogSource log) { _log = log; }

        private bool Bind()
        {
            if (_type != null) return _rb != null && _useCount != null;
            _type = GameBridge.FindGameType("TheForest.World.ElevatorSystem");
            if (_type == null) { _type = typeof(ElevatorKeeper); return false; }
            _rb = _type.GetField("_rb", Inst);
            _useCount = _type.GetField("_useCount", Inst);
            _moving = _type.GetField("_moving", Inst);
            if (_rb == null || _useCount == null) _log.LogWarning("ElevatorKeeper: ElevatorSystem fields not found - elevators are not kept.");
            return _rb != null && _useCount != null;
        }

        /// The header value at capture; "" when no elevator is loaded.
        public string Capture()
        {
            try
            {
                if (!Bind()) return "";
                StringBuilder sb = new StringBuilder();
                UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(_type);
                for (int i = 0; i < all.Length; i++)
                {
                    Component c = all[i] as Component;
                    Rigidbody rb = c != null ? _rb.GetValue(c) as Rigidbody : null;
                    if (rb == null) continue;
                    Vector3 p = rb.transform.position, e = rb.transform.eulerAngles;
                    if (sb.Length > 0) sb.Append(';');
                    sb.Append(ObjectProbe.PathOf(c.transform)).Append('|')
                      .Append((int)_useCount.GetValue(c)).Append('|')
                      .Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z)).Append('|')
                      .Append(F(e.x)).Append(',').Append(F(e.y)).Append(',').Append(F(e.z));
                }
                return sb.ToString();
            }
            catch (Exception ex) { _log.LogWarning("ElevatorKeeper: capture failed: " + ex.Message); return ""; }
        }

        /// After a Quick load; returns the log note ("" when nothing to do).
        public string Restore(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                if (!Bind()) return "";
                Dictionary<string, Component> live = new Dictionary<string, Component>();
                UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(_type);
                for (int i = 0; i < all.Length; i++)
                {
                    Component c = all[i] as Component;
                    if (c != null) live[ObjectProbe.PathOf(c.transform)] = c;
                }

                int back = 0, same = 0, missing = 0, moving = 0;
                string[] entries = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < entries.Length; i++)
                {
                    string[] parts = entries[i].Split('|');
                    Vector3 pos, euler;
                    int count;
                    if (parts.Length != 4 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out count) ||
                        !Vec(parts[2], out pos) || !Vec(parts[3], out euler)) continue;
                    Component c;
                    if (!live.TryGetValue(parts[0], out c)) { missing++; continue; }
                    if (_moving != null && (bool)_moving.GetValue(c)) { moving++; continue; }
                    Rigidbody rb = _rb.GetValue(c) as Rigidbody;
                    if (rb == null) { missing++; continue; }
                    if ((rb.transform.position - pos).sqrMagnitude < 0.01f && (int)_useCount.GetValue(c) == count) { same++; continue; }
                    Quaternion rot = Quaternion.Euler(euler);
                    rb.transform.position = pos;
                    rb.transform.rotation = rot;
                    rb.position = pos;
                    rb.rotation = rot;
                    _useCount.SetValue(c, count);
                    back++;
                }
                if (back == 0 && moving == 0 && missing == 0) return "";
                return "elevators: " + back + " put back" +
                       (same > 0 ? ", " + same + " as at capture" : "") +
                       (moving > 0 ? ", " + moving + " left moving" : "") +
                       (missing > 0 ? ", " + missing + " not loaded" : "");
            }
            catch (Exception ex) { return "elevators: restore failed (" + ex.Message + ")"; }
        }

        private static string F(float f) { return f.ToString("0.###", CultureInfo.InvariantCulture); }

        private static bool Vec(string s, out Vector3 v)
        {
            v = Vector3.zero;
            string[] p = s.Split(',');
            float x, y, z;
            if (p.Length != 3 ||
                !float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return false;
            v = new Vector3(x, y, z);
            return true;
        }
    }
}
