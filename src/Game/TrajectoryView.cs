using ForestOverlay.Core;
using System;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The trajectory preview (Debug views, Experimental, off by default;
    // Next up 6 "Speedrun tech research"). Read-only: it never writes the
    // game. Ten times a second (real time, so it keeps working in the
    // pause menu) it steps the player's flight from the live velocity with
    // Data/Trajectory, sweeping the player's capsule along each piece with
    // Physics.CapsuleCast until it touches something, and draws the path
    // with GL lines like the run lines (DrawTarget.ShouldDraw, gotcha 12).
    //
    // The live numbers (decompiled FirstPersonCharacter, `ilscan type
    // FirstPersonCharacter`): `gravity` (its extra push down, 10),
    // `maximumVelocity` (55), `Diving`, `jumpingTimer`, `Grounded`,
    // `swimming`; the rigidbody's drag; Physics.gravity; fixedDeltaTime.
    // It predicts "if you let go now": gravity on and the controller in
    // charge (a zipline / rope let go of), except during an explosion
    // knockback, which holds the controller off (game-notes *Bomb boost,
    // refined*): then the pushes MoveWatch saw piling up (the pause menu)
    // are added to the velocity, held for the free window left, and the
    // horizontal speed is wiped once the explode animation (layer 2,
    // tag "explode", playerAnimatorControl.Update) plays.
    //
    // One log line per flight (gotcha 16): the prediction made as it
    // started (the last one made in the pause menu, for a boost) against
    // where the player really came down - the test harness.
    // ------------------------------------------------------------------
    public sealed class TrajectoryView : MonoBehaviour
    {
        public const int MaxSteps = 900;          // 15 s at 60 Hz
        private const int CastEvery = 3;          // a sweep per 0.05 s of flight
        private const float Interval = 0.1f;      // 10 Hz, real time
        private const float MaxDrop = 2000f;
        private const float OnGround = 0.12f;     // a predicted flight shorter than this is standing
        private const float MinLoggedFlight = 0.4f;

        public static ManualLogSource Log;

        /// Simulate and draw (the module turns it off in run mode).
        public bool Show;

        public string Summary = "";
        public string BoostText = "";
        public string HudFlight = "";
        public string HudBoost = "";

        private readonly Vector3[] _points = new Vector3[MaxSteps / CastEvery + 2];
        private int _count;
        private bool _hasHit;
        private Vector3 _hitPoint;
        private Color _lineColour, _hitColour;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private Material _material;
        private float _next;
        private float _fps = 60f;

        // The game's members, resolved once.
        private static bool _resolved;
        private static Type _fpcType;
        private static FieldInfo _gravity, _maxVel, _diving, _jumpTimer;
        private static PropertyInfo _grounded, _swimming;
        private static FieldInfo _lpAnimator;     // static LocalPlayer.Animator
        private static readonly int ExplodeHash = Animator.StringToHash("explode");

        // Per player (re-resolved when the transform changes - a load).
        private Transform _for;
        private Component _fpc;
        private CapsuleCollider _capsule;
        private int _maskLayer = -1, _mask;

        // The flight being checked against its prediction.
        private bool _flight;
        private float _flightAt;                  // game time it started
        private Vector3 _flightFrom, _predAt;
        private float _predTime, _predSpeed;
        private bool _predHit;
        private float _calm;

        private static readonly Color PathColour = new Color(0.3f, 0.95f, 1f, 0.95f);
        private static readonly Color BoostColour = new Color(1f, 0.6f, 0.15f, 0.95f);
        private static readonly Color SafeColour = new Color(0.4f, 1f, 0.4f, 1f);
        private static readonly Color HurtColour = new Color(1f, 0.25f, 0.25f, 1f);

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _fpcType = GameBridge.FindGameType("FirstPersonCharacter");
            if (_fpcType != null)
            {
                _gravity = _fpcType.GetField("gravity", f);
                _maxVel = _fpcType.GetField("maximumVelocity", f);
                _diving = _fpcType.GetField("Diving", f);
                _jumpTimer = _fpcType.GetField("jumpingTimer", f);
                _grounded = _fpcType.GetProperty("Grounded", f);
                _swimming = _fpcType.GetProperty("swimming", f);
            }
            Type lp = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            if (lp != null) _lpAnimator = lp.GetField("Animator", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (Log != null)
                Log.LogInfo("Trajectory preview: " + (_fpcType == null ? "FirstPersonCharacter not found - the controller's gravity and cap are left out"
                    : "reading FirstPersonCharacter" + (_gravity == null || _maxVel == null ? " (gravity / maximumVelocity missing - left out)" : "")) +
                    (_lpAnimator == null ? "; LocalPlayer.Animator not found - the explode animation is not seen" : "") + ".");
        }

        public void Clear()
        {
            _count = 0;
            _hasHit = false;
            Summary = BoostText = HudFlight = HudBoost = "";
            _flight = false;
        }

        private void Update()
        {
            try
            {
                if (!Show) return;
                float dt = Time.unscaledDeltaTime;
                if (dt > 0f) _fps += (1f / dt - _fps) * 0.1f;
            }
            catch (Exception ex) { Lifecycle.Fail("TrajectoryView.Update", ex); }
        }

        /// From the module's Tick, every frame; works on its own throttle.
        public void Tick(Transform player, Rigidbody rb)
        {
            if (!Show) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            if (player == null || rb == null || PlayerRef.AtTitleScreen)
            {
                Clear();
                Summary = "No player (title screen or a load).";
                return;
            }
            Resolve();
            if (_for != player)
            {
                _for = player;
                _fpc = _fpcType != null ? player.GetComponent(_fpcType) : null;
                _capsule = player.GetComponent<CapsuleCollider>();
                _flight = false;
            }
            Refresh(player, rb);
        }

        private T Read<T>(FieldInfo f, T otherwise)
        {
            try { return f != null && _fpc != null ? (T)f.GetValue(_fpc) : otherwise; }
            catch (Exception) { return otherwise; }
        }

        private bool ReadProp(PropertyInfo p)
        {
            try { return p != null && _fpc != null && (bool)p.GetValue(_fpc, null); }
            catch (Exception) { return false; }
        }

        private bool ExplodeState()
        {
            try
            {
                Animator a = _lpAnimator != null ? _lpAnimator.GetValue(null) as Animator : null;
                return a != null && a.layerCount > 2 && a.GetCurrentAnimatorStateInfo(2).tagHash == ExplodeHash;
            }
            catch (Exception) { return false; }
        }

        private void Refresh(Transform player, Rigidbody rb)
        {
            if (Read(_diving, false) || ReadProp(_swimming))
            {
                _count = 0; _hasHit = false;
                Summary = "In water - no prediction (swimming has its own speed rules).";
                BoostText = HudFlight = HudBoost = "";
                _flight = false;
                return;
            }

            float step = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 1f / 60f;
            bool grounded = ReadProp(_grounded);
            bool paused = Time.timeScale <= 0f;
            float sinceBlast = Time.time - MoveWatch.KnockbackAt;
            bool knockback = MoveWatch.KnockbackOn && sinceBlast >= 0f && sinceBlast < 3f;
            bool explode = knockback && ExplodeState();
            Vector3 pending = Vector3.zero;
            int frames = 0;
            if (knockback) MoveWatch.Pending(out pending, out frames);

            FlightModel m = new FlightModel
            {
                Gravity = Physics.gravity,
                ControllerGravity = Read(_gravity, 0f),
                Drag = rb.drag,
                SpeedCap = Read(_maxVel, 0f),
                Step = step,
            };
            float held = 0f;
            if (knockback)
            {
                held = BombBoost.HeldFor(sinceBlast, explode, step);
                m.FreeSteps = Mathf.Max(1, Trajectory.Steps(held, step));
                m.KnockbackSteps = m.FreeSteps + Trajectory.Steps(BombBoost.ExplodeHold, step);
                m.PushSteps = explode ? 0 : Trajectory.Steps(BombBoost.PushWindow - sinceBlast, step);
                m.PushPerStep = player.forward * (-BombBoost.PushPerFrame * _fps * step);
            }

            Vector3 start = player.position;
            Vector3 v0 = rb.velocity + pending;
            Simulate(player, start, v0, ref m, out float flightTime, out Vector3 landVel, out Vector3 landAt, out bool wall);

            bool standing = !knockback && frames == 0 && (flightTime < OnGround || (grounded && v0.magnitude < 0.5f));
            float air = (grounded ? 0f : Read(_jumpTimer, 0f)) + flightTime;
            float down = -landVel.y;
            int steps = (int)(flightTime / step);
            bool duringKnockback = knockback && steps < m.KnockbackSteps;
            int damage = _hasHit && !wall && !duringKnockback ? Trajectory.FallDamage(down, air) : 0;
            _lineColour = knockback ? BoostColour : PathColour;
            _hitColour = damage > 0 ? HurtColour : SafeColour;

            if (standing)
            {
                _count = 0; _hasHit = false;
                Summary = "On the ground - the path shows once you are in the air (a jump, a fall, a let-go, a knockback).";
                HudFlight = "";
            }
            else
            {
                Vector3 d = landAt - start;
                float across = new Vector2(d.x, d.z).magnitude;
                string where = across.ToString("0.0") + " m away" + (d.y < -0.5f ? ", " + (-d.y).ToString("0.0") + " m lower" :
                                                                       d.y > 0.5f ? ", " + d.y.ToString("0.0") + " m higher" : "");
                if (!_hasHit)
                    Summary = "Nothing touched within " + flightTime.ToString("0.0") + " s (" + where + " when the path stops).";
                else if (wall)
                    Summary = "Hits a wall / steep face " + where + " after " + flightTime.ToString("0.00") + " s at " + landVel.magnitude.ToString("0.0") +
                              " m/s" + (knockback ? " - a boost stops or bends there (the knockback sweeps the capsule, CCD)." : ".");
                else
                    Summary = "Lands " + where + " after " + flightTime.ToString("0.00") + " s, " + down.ToString("0.0") + " m/s down - " +
                              (duringKnockback ? "fall damage not predicted (the knockback holds the controller off; untested)."
                               : damage >= Trajectory.FellTooLongDamage ? "fell too long: dead (over " + Trajectory.FellTooLongAir + " s in the air)."
                               : damage > 0 ? "fall damage " + damage + " (over " + Trajectory.FallDamageSpeed + " m/s)."
                               : down > Trajectory.FallDamageSpeed ? "no fall damage (under " + Trajectory.FallDamageMinAir + " s in the air)."
                               : "no fall damage (" + Trajectory.FallDamageSpeed + " m/s or less).");
                HudFlight = _hasHit ? across.ToString("0") + " m, " + flightTime.ToString("0.0") + " s, " + down.ToString("0") + " m/s down" +
                                      (damage > 0 ? " (" + (damage >= 1000 ? "dead" : "-" + damage) + ")" : "")
                                    : "no landing in " + flightTime.ToString("0") + " s";
            }

            // The bomb boost, while the knockback holds the player.
            if (!knockback) { BoostText = HudBoost = ""; }
            else
            {
                float speed = BombBoost.Speed(frames);
                float perSecond = BombBoost.SpeedPerSecond(_fps);
                if (paused && frames > 0)
                {
                    BoostText = "Bomb boost: " + frames + " frames piled up = " + speed.ToString("#,0") + " m/s on unpause (+ the " +
                                rb.velocity.magnitude.ToString("0") + " m/s you have). " +
                                (explode ? "Late: the explode animation already plays - the pile-up lasts one physics step (~" +
                                           BombBoost.Distance(speed, held).ToString("0") + " m). Pause right at the blast for the long boost."
                                         : "Held " + held.ToString("0.000") + " s -> ~" + BombBoost.Distance(speed, held).ToString("0") +
                                           " m if nothing is in the way. Each more second here at " + _fps.ToString("0") + " fps: +" +
                                           perSecond.ToString("#,0") + " m/s, +" + BombBoost.Distance(perSecond, held).ToString("0") + " m.") +
                                " (Measured model, game-notes *Bomb boost, refined*.)";
                    HudBoost = speed.ToString("0") + " m/s, ~" + BombBoost.Distance(speed, held).ToString("0") + " m" + (explode ? " (late)" : "");
                }
                else
                {
                    BoostText = "Knockback " + sinceBlast.ToString("0.00") + " s after the blast: " +
                                (explode ? "the explode animation plays - horizontal speed wiped every frame."
                                         : "free window " + Mathf.Max(0f, BombBoost.FreeWindow - sinceBlast).ToString("0.00") +
                                           " s left - the pause menu now piles up 8 m/s a frame (" + perSecond.ToString("#,0") + " m/s a second at " +
                                           _fps.ToString("0") + " fps).");
                    HudBoost = explode ? "knockback (late)" : "knockback - pause now";
                }
            }

            CheckFlight(player, rb, grounded, knockback, paused, standing, landAt, flightTime, down);
        }

        // Steps the flight, sweeping the capsule every CastEvery steps.
        private void Simulate(Transform player, Vector3 start, Vector3 v0, ref FlightModel m,
                              out float time, out Vector3 vel, out Vector3 at, out bool wall)
        {
            _count = 0;
            _hasHit = false;
            wall = false;
            _points[_count++] = start;

            Vector3 off1 = Vector3.zero, off2 = Vector3.zero;
            float radius = 0.3f;
            if (_capsule != null)
            {
                Vector3 centre = player.TransformPoint(_capsule.center) - start;
                radius = _capsule.radius * 0.95f;   // a hair smaller: not already touching the ground it stands on
                float half = Mathf.Max(0f, _capsule.height * 0.5f - _capsule.radius);
                Vector3 axis = _capsule.direction == 0 ? player.right : _capsule.direction == 2 ? player.forward : player.up;
                off1 = centre + axis * half;
                off2 = centre - axis * half;
            }
            int layer = player.gameObject.layer;
            if (layer != _maskLayer)
            {
                _maskLayer = layer;
                _mask = 0;
                for (int i = 0; i < 32; i++)
                    if (!Physics.GetIgnoreLayerCollision(layer, i)) _mask |= 1 << i;
                _mask &= ~(1 << layer);   // never the player's own colliders
            }

            Vector3 pos = start, segStart = start;
            vel = v0;
            time = 0f;
            at = start;
            Transform root = player.root;
            for (int i = 0; i < MaxSteps; i++)
            {
                Trajectory.Step(ref pos, ref vel, i, ref m);
                if ((i + 1) % CastEvery != 0 && i != MaxSteps - 1) continue;
                Vector3 seg = pos - segStart;
                float len = seg.magnitude;
                float segTime = ((i % CastEvery) + 1) * m.Step;
                if (len > 1e-4f)
                {
                    Vector3 dir = seg / len;
                    int n = Physics.CapsuleCastNonAlloc(segStart + off1, segStart + off2, radius, dir, _hits, len, _mask, QueryTriggerInteraction.Ignore);
                    if (n > _hits.Length) n = _hits.Length;
                    int best = -1;
                    for (int h = 0; h < n; h++)
                    {
                        if (_hits[h].distance <= 0f || _hits[h].collider == null) continue;   // overlapping at the start
                        if (_hits[h].collider.transform.root == root) continue;
                        if (best < 0 || _hits[h].distance < _hits[best].distance) best = h;
                    }
                    if (best >= 0)
                    {
                        float frac = _hits[best].distance / len;
                        at = segStart + dir * _hits[best].distance;
                        time += segTime * frac;
                        _points[_count++] = at;
                        _hasHit = true;
                        _hitPoint = _hits[best].point;
                        wall = _hits[best].normal.y < 0.1f;
                        return;
                    }
                }
                time += segTime;
                at = pos;
                if (_count < _points.Length) _points[_count++] = pos;
                segStart = pos;
                if (pos.y < start.y - MaxDrop) return;
            }
        }

        // One log line per flight: the prediction made as it started against
        // where the player came down.
        private void CheckFlight(Transform player, Rigidbody rb, bool grounded, bool knockback, bool paused, bool standing,
                                 Vector3 landAt, float flightTime, float down)
        {
            if (Log == null) return;
            bool newBoost = _flight && knockback && MoveWatch.KnockbackAt > _flightAt;
            // Small hops (walking over bumps) are not flights worth a line.
            bool worth = knockback || flightTime >= MinLoggedFlight;
            if ((!_flight || newBoost || paused) && !standing && _hasHit && (worth || _flight))
            {
                // Paused: keep the latest prediction (the one the unpause acts on).
                if (!_flight || newBoost) { _flightFrom = player.position; _flightAt = Time.time; }
                _flight = true;
                _predAt = landAt;
                _predTime = flightTime + (Time.time - _flightAt);
                _predSpeed = down;
                _predHit = true;
                _calm = 0f;
                return;
            }
            if (!_flight || paused) return;
            float since = Time.time - _flightAt;
            bool still = rb.velocity.magnitude < 0.3f;
            _calm = still ? _calm + Interval : 0f;
            bool landed = (grounded && !knockback && since > 0.1f) || _calm >= 0.3f;
            if (!landed && since < 30f) return;
            _flight = false;
            Vector3 p = player.position;
            Log.LogInfo("Trajectory: a flight from " + V(_flightFrom) + " came down at " + V(p) + " after " + since.ToString("0.00") +
                        " s; predicted " + (_predHit ? V(_predAt) + " after " + _predTime.ToString("0.00") + " s at " + _predSpeed.ToString("0.0") +
                        " m/s down" : "no landing") + " - " + Vector3.Distance(p, _predAt).ToString("0.0") + " m from the prediction" +
                        (since >= 30f ? " (gave up after 30 s)" : "") + ".");
        }

        private static string V(Vector3 p)
        {
            return "(" + p.x.ToString("0.0") + ", " + p.y.ToString("0.0") + ", " + p.z.ToString("0.0") + ")";
        }

        private void OnRenderObject()
        {
            try
            {
                if (!Show || _count < 2 || !DrawTarget.ShouldDraw()) return;
                if (_material == null)
                {
                    Shader shader = Shader.Find("Hidden/Internal-Colored");
                    if (shader == null) return;
                    _material = new Material(shader);
                    _material.hideFlags = HideFlags.HideAndDontSave;
                    _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    _material.SetInt("_ZWrite", 0);
                    _material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                }

                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                _material.SetPass(0);
                GL.PushMatrix();
                GL.Begin(GL.LINES);
                GL.Color(_lineColour);
                for (int i = 1; i < _count; i++)
                {
                    GL.Vertex(_points[i - 1]);
                    GL.Vertex(_points[i]);
                }
                int verts = 2 * (_count - 1);
                if (_hasHit)
                {
                    Vector3 p = _hitPoint;
                    const float h = 0.8f;
                    GL.Color(_hitColour);
                    GL.Vertex(p); GL.Vertex(new Vector3(p.x, p.y + 4f, p.z));
                    GL.Vertex(new Vector3(p.x - h, p.y, p.z)); GL.Vertex(new Vector3(p.x + h, p.y, p.z));
                    GL.Vertex(new Vector3(p.x, p.y, p.z - h)); GL.Vertex(new Vector3(p.x, p.y, p.z + h));
                    verts += 6;
                }
                GL.End();
                GL.PopMatrix();
                DrawTarget.Record(t0, verts);
            }
            catch (Exception ex) { Lifecycle.Fail("TrajectoryView.OnRenderObject", ex); }
        }

        private void OnDestroy()
        {
            try
            {
                if (_material != null) Destroy(_material);
            }
            catch (Exception ex) { Lifecycle.Fail("TrajectoryView.OnDestroy", ex); }
        }
    }
}
