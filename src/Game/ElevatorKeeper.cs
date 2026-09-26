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
    // its use count.
    //
    // A ride under way (runner maks, v0.24.52: "works half the time" - the
    // elevator gone, or the textures unloaded, sometimes 3-7 s later): Goto
    // waits 5 s for the keycard animation, then moves the car and the player
    // up in one step and waits `_duration`. v0.24.40 left a moving elevator
    // alone, so a restart in those 5 s had the pending ride lift the car
    // (and the player) after the restore. Now the ride is stopped
    // (StopAllCoroutines - Goto is the only coroutine ElevatorSystem runs)
    // and its end state applied: `_moving` false, the moving / idle
    // objects swapped back, motion blur back on, the ride's `_tracker_`
    // removed; then the car goes back as usual.
    //
    // A capture DURING the red elevator's keycard cutscene (runner maks,
    // v0.24.79: "elev boost", a start state 2.6 s in) came back with the
    // car at the bottom, its one use spent and nothing running: an
    // unpressable button. Goto starts the cutscene (openDoorRoutine) itself,
    // so the replay is the ride: the car back at the stop it left from, the
    // use given back, and the ride started as the button does (Update:
    // `_sequence.BeginStage(_sequenceStage)`, else GotoRemotePoint) - the
    // cutscene fast-forward then takes it to the captured moment. The car
    // moves 5 s (game time) after Goto starts, so the capture writes where
    // the ride left from as a fifth field; files without it (v0.24.78 and
    // older) assume the car had not moved yet when the cutscene was under
    // 3.5 s in (the walk-up to the card reader takes ~1 s of the 5).
    // ------------------------------------------------------------------
    internal sealed class ElevatorKeeper
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly ManualLogSource _log;
        private Type _type;
        private FieldInfo _rb, _useCount, _moving, _movingGos, _idleGos, _keycardAnim, _up, _down, _sequence, _stage;
        private MethodInfo _toggle, _remote;

        /// Goto's wait between the keycard animation starting and the car
        /// moving (YieldPresets.WaitFiveSeconds, game time).
        public const float KeycardWait = 5f;
        private const float OldFileMovedAfter = 3.5f;

        public ElevatorKeeper(ManualLogSource log) { _log = log; }

        private bool Bind()
        {
            if (_type != null) return _rb != null && _useCount != null;
            _type = GameBridge.FindGameType("TheForest.World.ElevatorSystem");
            if (_type == null) { _type = typeof(ElevatorKeeper); return false; }
            _rb = _type.GetField("_rb", Inst);
            _useCount = _type.GetField("_useCount", Inst);
            _moving = _type.GetField("_moving", Inst);
            _keycardAnim = _type.GetField("_playKeycardAnim", Inst);
            _up = _type.GetField("_upPosition", Inst);
            _down = _type.GetField("_downPosition", Inst);
            _sequence = _type.GetField("_sequence", Inst);
            _stage = _type.GetField("_sequenceStage", Inst);
            _remote = _type.GetMethod("GotoRemotePoint", Inst, null, Type.EmptyTypes, null);
            if (_rb == null || _useCount == null) _log.LogWarning("ElevatorKeeper: ElevatorSystem fields not found - elevators are not kept.");
            return _rb != null && _useCount != null;
        }

        /// The header value at capture; "" when no elevator is loaded.
        /// `rideAge`: game seconds since a keycard ride started (GameEvents),
        /// or negative when unknown.
        public string Capture(float rideAge)
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
                    // A keycard ride under way: the stop it left from.
                    if (Riding(c))
                    {
                        Vector3 from = rideAge >= KeycardWait ? OtherStop(c, p) : p;
                        sb.Append('|').Append(F(from.x)).Append(',').Append(F(from.y)).Append(',').Append(F(from.z));
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex) { _log.LogWarning("ElevatorKeeper: capture failed: " + ex.Message); return ""; }
        }

        /// After a Quick load; returns the log note ("" when nothing to do).
        /// `cutsceneAt` >= 0: the capture was that far into the red
        /// elevator's cutscene - a keycard ride under way is set up to be
        /// replayed (car at its start, use given back) and added to
        /// `replay`, for Replay once the player is placed. `ridesOnly`
        /// (after a Full load, which rebuilt the rest): only those.
        public string Restore(string value, float cutsceneAt, bool ridesOnly, List<Component> replay)
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

                int back = 0, same = 0, missing = 0, stopped = 0, rides = 0;
                string[] entries = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < entries.Length; i++)
                {
                    string[] parts = entries[i].Split('|');
                    Vector3 pos, euler;
                    int count;
                    if (parts.Length < 4 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out count) ||
                        !Vec(parts[2], out pos) || !Vec(parts[3], out euler)) continue;
                    Component c;
                    if (!live.TryGetValue(parts[0], out c)) { missing++; continue; }
                    Rigidbody rb = _rb.GetValue(c) as Rigidbody;
                    if (rb == null) { missing++; continue; }

                    Vector3 from = pos;
                    bool ride = cutsceneAt >= 0f && replay != null && KeycardAnim(c) && count > 0 &&
                                (parts.Length >= 5 ? Vec(parts[4], out from)
                                                   : OldFileRide(c, pos, cutsceneAt, out from));
                    if (!ride && ridesOnly) continue;
                    if (_moving != null && (bool)_moving.GetValue(c)) { StopRide(c, rb); stopped++; }
                    if (ride)
                    {
                        // Found live: the ride's `_tracker_` child and the
                        // moving objects are StopRide's; the car and the use
                        // count are set here, the ride itself after the
                        // player is placed (Replay).
                        Quaternion r0 = Quaternion.Euler(euler);
                        rb.transform.position = from;
                        rb.transform.rotation = r0;
                        rb.position = from;
                        rb.rotation = r0;
                        _useCount.SetValue(c, count - 1);
                        replay.Add(c);
                        rides++;
                        continue;
                    }
                    if ((rb.transform.position - pos).sqrMagnitude < 0.01f && (int)_useCount.GetValue(c) == count) { same++; continue; }
                    Quaternion rot = Quaternion.Euler(euler);
                    rb.transform.position = pos;
                    rb.transform.rotation = rot;
                    rb.position = pos;
                    rb.rotation = rot;
                    _useCount.SetValue(c, count);
                    back++;
                }
                if (back == 0 && stopped == 0 && missing == 0 && rides == 0) return "";
                return "elevators: " + back + " put back" +
                       (rides > 0 ? ", " + rides + " ride(s) under way at capture set to replay" : "") +
                       (same > 0 ? ", " + same + " as at capture" : "") +
                       (stopped > 0 ? ", " + stopped + " ride(s) stopped" : "") +
                       (missing > 0 ? ", " + missing + " not loaded" : "");
            }
            catch (Exception ex) { return "elevators: restore failed (" + ex.Message + ")"; }
        }

        /// Starts each ride as the elevator's own button does (Update on
        /// "Take"); the cutscene fast-forward takes it from there.
        public string Replay(List<Component> rides)
        {
            int started = 0;
            for (int i = 0; i < rides.Count; i++)
            {
                Component c = rides[i];
                if (c == null) continue;
                try
                {
                    object seq = _sequence != null ? _sequence.GetValue(c) : null;
                    UnityEngine.Object seqObj = seq as UnityEngine.Object;
                    MethodInfo begin = seqObj != null ? seq.GetType().GetMethod("BeginStage", Inst, null, new[] { typeof(int) }, null) : null;
                    if (begin != null) begin.Invoke(seq, new object[] { _stage != null ? (int)_stage.GetValue(c) : 0 });
                    else if (_remote != null) _remote.Invoke(c, null);
                    else continue;
                    started++;
                }
                catch (Exception ex) { _log.LogWarning("ElevatorKeeper: ride replay failed: " + ex.Message); }
            }
            return started + " of " + rides.Count + " ride(s) started again";
        }

        private bool KeycardAnim(Component c)
        {
            return _keycardAnim != null && (bool)_keycardAnim.GetValue(c);
        }

        private bool Riding(Component c)
        {
            return _moving != null && (bool)_moving.GetValue(c) && KeycardAnim(c);
        }

        /// The stop that is not `p` (the ride's other end).
        private Vector3 OtherStop(Component c, Vector3 p)
        {
            Transform up = _up != null ? _up.GetValue(c) as Transform : null;
            Transform down = _down != null ? _down.GetValue(c) as Transform : null;
            if (up == null || down == null) return p;
            return (up.position - p).sqrMagnitude > (down.position - p).sqrMagnitude ? up.position : down.position;
        }

        private bool OldFileRide(Component c, Vector3 captured, float cutsceneAt, out Vector3 from)
        {
            from = cutsceneAt < OldFileMovedAfter ? captured : OtherStop(c, captured);
            return true;
        }

        // Goto's last lines, without its UnityEvents (sound, door, keycard
        // light - left to the next ride).
        private void StopRide(Component c, Rigidbody rb)
        {
            MonoBehaviour mb = c as MonoBehaviour;
            if (mb != null) mb.StopAllCoroutines();
            _moving.SetValue(c, false);
            if (_toggle == null) _toggle = _type.GetMethod("ToggleGoArray", Inst);
            if (_toggle != null)
            {
                if (_movingGos == null) _movingGos = _type.GetField("_movingGos", Inst);
                if (_idleGos == null) _idleGos = _type.GetField("_idleGos", Inst);
                if (_movingGos != null) _toggle.Invoke(c, new object[] { _movingGos.GetValue(c), false });
                if (_idleGos != null) _toggle.Invoke(c, new object[] { _idleGos.GetValue(c), true });
            }
            Transform tracker = rb.transform.Find("_tracker_");
            if (tracker != null) UnityEngine.Object.Destroy(tracker.gameObject);
            try
            {
                // LocalPlayer.ImageEffectOptimizer.SkipMotionBlur = false
                Type lp = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
                FieldInfo f = lp != null ? lp.GetField("ImageEffectOptimizer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
                object opt = f != null ? f.GetValue(null) : null;
                PropertyInfo skip = opt != null ? opt.GetType().GetProperty("SkipMotionBlur", Inst) : null;
                if (skip != null) skip.SetValue(opt, false, null);
            }
            catch (Exception) { }
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
