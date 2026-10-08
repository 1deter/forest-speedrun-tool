using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Banned-move detection (docs/run-mode.md *Banned moves: detection*;
    // author, 2026-10-03: "build detection"). Pure: Game/MoveWatch feeds
    // it what the game did, Modules/RunModeModule.Moves folds what it
    // finds into the attempt log as `move` lines. A move is evidence for a
    // verifier, never a flag that makes an attempt invalid.
    //
    // Bomb boost (exact). The explosion knockback is a coroutine
    // (playerHitReactions.enableExplodeCamera) that pushes the player 8 m/s
    // per rendered frame while its timer, counted in game time, runs (0.5 s,
    // then up to 0.25 s more). When game time stops (the pause menu), the
    // timer stops but the frames go on, and every push piles up until the
    // next physics step (game-notes *Bomb boost, refined*). So the boost is
    // exactly "pushes made while game time is stopped" - counted by the hook
    // on the coroutine itself, not guessed from a menu and a timing. One
    // stop of game time is one boost; it is reported after AfterSeconds of
    // game time with what it did (peak speed, distance). Fewer than
    // MinPausedPushes piled pushes (a few frames, < 30 m/s extra) are not
    // reported. A stop the player does not come back from (a load, the
    // player gone) did nothing and is dropped.
    //
    // Huge speed (exact as a number, says nothing about how). The player's
    // rigidbody speed AND the distance it really covered both at least
    // HugeSpeed over HugeWindow of game time. Needing both rules out
    // teleports (distance, no speed) and a stale velocity on a body that
    // is not moving (speed, no distance). A frame that moves the player
    // further than its speed can over a whole physics step is a teleport
    // and restarts the window (live, v0.24.227: a tp while the body still
    // held 300 m/s read as 509 m in 0.1 s). Not counted: a kinematic body
    // (rides, cutscenes, the game's own movers), and a knockback - from
    // its first push until the player has been slower than CalmSpeed for
    // CalmSeconds (a plain knockback reaches ~260 m/s, and a boost's speed
    // is the boost's own record). Speeds the game reaches by itself stay
    // under HugeSpeed: a sprint ~10 m/s, and a fall never passes 55.4 m/s
    // (live, 2026-10-03: from y 1500, the speed held at 55.43 for 16 s).
    //
    // Cave state force load (exact). A crawl / swim cave entrance
    // (playerEnterCaveAction.doCave) parents the player to the entrance,
    // sends InACave on a timer and lets the enter animation's root motion
    // carry them in; it lets go once the animation ends. When the animation
    // is cut short (the runners' smash in the air), the timer still sends
    // InACave and the player is let go at the mouth: in cave state - no
    // terrain collision, cave streaming - outside (game-notes *Cave state
    // force load*). So: an entry that ends in cave state with the player
    // not more than CaveDepth under the terrain (the game's own rule for
    // "in a cave", LocalPlayer.Goto). Live survey (2026-10-03, every crawl
    // and swim entrance in the game): a normal entry ends 7.5 m (Cave 2) to
    // 300 m under the terrain; the mouths sit 0-3 m above it.
    //
    // Fall damage cancel (high for "a fast landing took no damage"). The
    // game judges a landing (FirstPersonCharacter.HandleLanded, on the
    // Grounded rising edge) on prevVelocity: the vertical speed of the most
    // recent collision ENTER, written nowhere else (game-notes *Fall damage
    // and the slide cancel*). A landing that is not a new enter (contact
    // kept while sliding off a surface) or follows a slow enter is judged on
    // that small value. So: every gate the game applies passes (fall damage
    // allowed, over 0.75 s in the air, no shell / glider / plane crash /
    // landing already running, not swimming), the game's judged speed is
    // not over FallSpeed, and the player really fell faster than
    // FallReport within FallWindow before the landing. A long natural slide
    // down a steep slope reaches the ground the same way - the text says
    // what the game judged against what the player did, the verifier
    // decides. Live (2026-10-03): an 82 m drop onto the ground is judged at
    // 55 (the speed it hit at); a drop into the big lake at 1.3, swimming.
    //
    // Lift out of a structure (the log boost, the custom wall boost). PhysX
    // pushes a body out of a solid that appears or squeezes into it by moving
    // it, with no velocity left over (live, 2026-10-03: a box put 0.8 m into
    // the player's feet lifted them 0.8 m, velocity 0 throughout; game-notes
    // *Depenetration*). Jumps, knockbacks and swimming are velocity, rides
    // and climbs are kinematic - but ordinary geometry does it too: the
    // player's capsule is 4.6 m tall and walking into the yacht cabin's
    // bench lifted it 1.2 m in two steps (live, v0.24.232). The runners'
    // lifts both use a structure they built (a log wall, a custom wall), so:
    // per physics step, the rise beyond what the vertical speed (before or
    // after the step) allows, summed while it keeps coming (a gap of
    // LiftQuiet ends an episode); reported at LiftReport when the player
    // touched a player-built structure during it (`structure`), else only
    // logged (SmallLift). A step rising more than LiftMaxStep unexplained,
    // or moving further sideways than its speed allows, is a teleport; for
    // SettleSeconds after one (or after the player appears) nothing counts:
    // a teleport sets the player down overlapping whatever is there and the
    // physics pushes them out (live, v0.24.231: a tp into the yacht read as a
    // 1.2 m lift and a clip through its hull; tps beside a tree and into
    // Cave 6 as 0.5 m lifts). A step while the player touches something that
    // moved (`carried`: the yacht's hull bobs on a kinematic body, a raft, a
    // closing door) is not a lift either: the mover pushed them (live,
    // v0.24.231: walking on the yacht read as a 1.0 m lift).
    //
    // Clip through a solid. The capsule's centre ends up on the other side
    // of a solid collider the player had touched (Game/ClipWatch casts the
    // line from the last place the centre was clear of every solid to the
    // next one: the line must enter the collider through a front face - a
    // mesh face crossed from behind, as in the yacht's cabin, is not one;
    // ending up inside a rock counts, coming out of one does not) and only
    // within ClipSmashWindow of one of the runners' two ways in (author,
    // 2026-10-03): an axe ground smash (playerAnimatorControl
    // .doingGroundChop - the head collider following the head bone, the
    // clip's own mechanism; "always done with a smash and a crouch"; the
    // text says whether the player stood up from a crouch around it), or a
    // structure they built squeezing them ("building a stone/log wall when
    // pressed up against a thin texture ... phases you through", the
    // keycard cave clip in true any%). Others are only logged (Ungated); a collider only counts once the player has had a contact
    // with it, which leaves out every pair the game told the physics to
    // ignore - terrain in caves and at cave mouths, ropes, structures on
    // rafts). The same collider again within ClipRepeat is one clip.
    // ------------------------------------------------------------------
    public sealed class MoveDetector
    {
        public const string BombBoost = "bomb-boost";
        public const string HugeSpeedKind = "huge-speed";
        public const string CaveForceLoad = "cave-force-load";
        public const string FallDamageCancel = "fall-damage-cancel";
        public const string LiftKind = "lift";
        public const string ClipKind = "clip";

        public const float PushPerFrame = 8f;      // m/s, the coroutine's AddForce
        public const int MinPausedPushes = 4;      // 32 m/s piled up
        public const float AfterSeconds = 1f;      // game time measured after a boost
        public const float HugeSpeed = 200f;       // m/s
        public const float HugeWindow = 0.1f;      // game seconds per check
        public const float HugeEndSeconds = 1f;    // below HugeSpeed this long = the episode ended
        public const float CalmSpeed = 30f;
        public const float CalmSeconds = 1f;
        public const float MaxPhysicsStep = 0.05f;   // a frame can hold a whole physics step (1/60 s) and then some
        public const float CaveDepth = 3f;         // m under the terrain: LocalPlayer.Goto's "in a cave" (a normal entry ends >= 7.5)
        public const float FallSpeed = 28f;        // m/s: HandleLanded hurts over this
        public const float FallReport = 30f;       // m/s: our own measure must pass this (a margin over the game's line)
        public const float FallWindow = 0.25f;     // game seconds before the landing
        public const float FallAirTime = 0.75f;    // HandleLanded: no damage under this air time
        public const float FatalAirTime = 3.8f;    // HandleLanded: 1000 damage over this
        public const float LiftStepMin = 0.02f;    // m of unexplained rise in one step that counts
        public const float LiftReport = 1f;        // m in one episode
        public const float LiftQuiet = 0.25f;      // game seconds with no lift end an episode
        public const float LiftMaxStep = 4f;       // more unexplained rise in one step = a teleport
        public const float ClipRepeat = 2f;        // game seconds: the same collider again = the same clip
        public const float SettleSeconds = 0.5f;   // game seconds after a teleport when nothing counts
        public const float ClipSmashWindow = 1.5f; // game seconds after a ground smash a clip counts

        public sealed class Move
        {
            public string Kind;
            public Vector3 Position;   // where it started
            public string Detail;      // plain words, numbers included
            public float PeakSpeed;
            public float Distance;
            public int PausedPushes;   // bomb boost
            public float PausedSeconds;
            public bool PauseMenu;
            public float SinceBlast;   // game seconds from the knockback's start to the stop
            public float Seconds;      // huge speed: game seconds above HugeSpeed
        }

        /// Moves found and not yet taken (Drain).
        public readonly List<Move> Ready = new List<Move>();

        /// Why the last pending boost was dropped ("" none) - for the log.
        public string Dropped { get; private set; }

        /// Dropped, then cleared.
        public string TakeDropped() { string d = Dropped; Dropped = ""; return d; }

        // --- the knockback and a stop of game time inside it ---
        private bool _knockback;          // a knockback seen since the last reset
        private float _sinceBlast;        // game seconds since its first push
        private int _stopPushes;
        private float _stopReal;
        private bool _stopMenu;
        private float _stopAt;
        private Vector3 _stopPos;
        private bool _stopHasPos;

        // --- measuring a boost after game time runs again ---
        private Move _after;
        private float _afterLeft;
        private Vector3 _afterFrom;

        // --- huge speed ---
        private bool _excused;            // a knockback: until calm
        private float _calm;
        private bool _hasLast;
        private Vector3 _last;
        private float _lastSpeed;
        private float _wDt, _wDist, _wMax;
        private Move _huge;
        private float _hugeQuiet;

        // --- physics steps: lifts out of colliders ---
        private bool _stepHas;
        private Vector3 _stepPos, _stepVel;
        private Move _lift;
        private float _liftQuiet;
        private Vector3 _liftFrom;
        private float _settle;
        private string _liftStructure = "";
        private float _sinceSmash = 1000f, _sinceStand = 1000f, _sinceStructure = 1000f;
        private string _clipStructure = "";
        private bool _wasCrouching;

        // --- clips: the last one, to merge repeats ---
        private string _clipWhat = "";
        private float _clipAt = -100f;

        // --- the fastest fall over the last FallWindow (two half buckets) ---
        private float _fallCur, _fallPrev, _fallAge;

        /// The fastest downward speed over the last FallWindow of game time.
        public float RecentFall { get { return Mathf.Max(_fallCur, _fallPrev); } }

        public MoveDetector() { Dropped = ""; }

        /// Forget everything (a load, an attempt starting, our own teleport).
        /// A boost still being measured is reported with what it did so far
        /// unless `drop` (then it is dropped: it never happened in the game).
        public void Reset(bool drop)
        {
            if (_after != null && !drop) Finish();
            else if (_after != null || _stopPushes > 0) Drop("reset (a load or a teleport)");
            _after = null;
            _stopPushes = 0;
            _knockback = false;
            _excused = false;
            _calm = 0f;
            _hasLast = false;
            _wDt = _wDist = _wMax = 0f;
            if (_huge != null && !drop) EndHuge();
            _huge = null;
            _fallCur = _fallPrev = _fallAge = 0f;
            _stepHas = false;
            if (_lift != null && !drop) EndLift();
            _lift = null;
            _clipWhat = "";
        }

        /// The knockback's coroutine started (its first MoveNext).
        public void KnockbackStarted()
        {
            _knockback = true;
            _sinceBlast = 0f;
            _excused = true;
            _calm = 0f;
        }

        /// The coroutine pushed the player. `timeStopped`: game time was
        /// not running (deltaTime 0); `pauseMenu`: the pause menu was open.
        public void KnockbackPush(bool timeStopped, bool pauseMenu, bool hasPos, Vector3 pos)
        {
            if (!_knockback) KnockbackStarted();   // the hook came in mid-knockback
            _excused = true;
            _calm = 0f;
            if (!timeStopped) return;
            if (_stopPushes == 0)
            {
                if (_after != null) Finish();       // a second stop: the first boost ends here
                _stopAt = _sinceBlast;
                _stopReal = 0f;
                _stopMenu = false;
                _stopPos = pos;
                _stopHasPos = hasPos;
            }
            _stopPushes++;
            if (pauseMenu) _stopMenu = true;
        }

        /// Once a rendered frame. `gameDt` = Time.deltaTime (0 while game
        /// time is stopped), `realDt` = unscaled.
        public void Frame(float gameDt, float realDt, bool hasPlayer, bool kinematic, Vector3 pos, Vector3 vel)
        {
            if (!hasPlayer)
            {
                if (_after != null || _stopPushes > 0) Drop("the player was gone (a load)");
                _after = null;
                _stopPushes = 0;
                _hasLast = false;
                _wDt = _wDist = _wMax = 0f;
                _fallCur = _fallPrev = _fallAge = 0f;
                return;
            }
            if (gameDt <= 0f)
            {
                if (_stopPushes > 0) _stopReal += realDt;
                return;   // nothing moves while game time is stopped
            }

            if (_knockback) _sinceBlast += gameDt;
            float speed = vel.magnitude;

            // Game time runs again after a stop with piled-up pushes.
            if (_stopPushes > 0)
            {
                Move m = new Move();
                m.Kind = BombBoost;
                m.Position = _stopHasPos ? _stopPos : pos;
                m.PausedPushes = _stopPushes;
                m.PausedSeconds = _stopReal;
                m.PauseMenu = _stopMenu;
                m.SinceBlast = _stopAt;
                _stopPushes = 0;
                _after = m;
                _afterLeft = AfterSeconds;
                _afterFrom = pos;
            }
            if (_after != null)
            {
                if (!kinematic && speed > _after.PeakSpeed) _after.PeakSpeed = speed;
                float far = Vector3.Distance(_afterFrom, pos);
                if (far > _after.Distance) _after.Distance = far;
                _afterLeft -= gameDt;
                if (_afterLeft <= 0f) Finish();
            }

            Fall(gameDt, kinematic, vel.y);
            Huge(gameDt, kinematic, pos, speed);
        }

        /// The game's landing ran (a prefix on HandleLanded, before it
        /// judges). `judged` = its prevVelocity; `gameGates` = every other
        /// condition it hurts on (fall damage allowed, over FallAirTime in
        /// the air, no landing already running, no plane crash, not a shell
        /// ride or a glider over 32 m/s); `swimming` = in water.
        public void Landed(Vector3 pos, float judged, bool gameGates, float airSeconds, bool swimming, float fallNow)
        {
            float fell = Mathf.Max(RecentFall, fallNow);
            _fallCur = _fallPrev = _fallAge = 0f;
            if (!gameGates || swimming || airSeconds <= FallAirTime) return;
            if (judged > FallSpeed || fell <= FallReport) return;
            Move m = new Move();
            m.Kind = FallDamageCancel;
            m.Position = pos;
            m.PeakSpeed = fell;
            m.Seconds = airSeconds;
            bool fatal = airSeconds > FatalAirTime;
            int damage = (int)(0.9f * fell * fell / 27.5f);
            m.Detail = "a landing after " + airSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s in the air, falling at " +
                       Speed(fell) + " m/s, took no fall damage: the game judged it on an earlier contact at " +
                       Mathf.Max(0f, judged).ToString("0.0", CultureInfo.InvariantCulture) + " m/s (it hurts over " + Speed(FallSpeed) +
                       "); at the real speed it would have been " +
                       (fatal ? "fatal (over " + FatalAirTime.ToString("0.0", CultureInfo.InvariantCulture) + " s in the air)" : damage + " damage") +
                       " - a slide down a steep slope lands the same way";
            Ready.Add(m);
        }

        /// A crawl / swim cave entrance let the player go (doCave ended).
        /// `inCaves`: the game's cave state now; `underTerrain`: the terrain
        /// height above the player minus their height (negative = above
        /// it; ignored without `hasTerrain`); `fromStart`: metres from where
        /// the entry took hold of them; `seconds`: game seconds it held them.
        public void CaveEntryEnded(Vector3 pos, bool inCaves, bool hasTerrain, float underTerrain, float fromStart, float seconds)
        {
            if (!inCaves || !hasTerrain || float.IsNaN(underTerrain) || underTerrain > CaveDepth) return;
            Move m = new Move();
            m.Kind = CaveForceLoad;
            m.Position = pos;
            m.Distance = fromStart;
            m.Seconds = seconds;
            m.Detail = "a cave entrance put the player in cave state and let go of them " +
                       Mathf.Abs(underTerrain).ToString("0.0", CultureInfo.InvariantCulture) + " m " +
                       (underTerrain < 0f ? "above" : "under") + " the terrain (a normal entry ends 7 m or more under it), " +
                       fromStart.ToString("0.0", CultureInfo.InvariantCulture) + " m from where it took hold, after " +
                       seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
            Ready.Add(m);
        }

        /// The attempt ends: what is still being measured is reported.
        public void Flush()
        {
            if (_after != null) Finish();
            if (_stopPushes > 0) Drop("the attempt ended with game time stopped");
            _stopPushes = 0;
            if (_huge != null) EndHuge();
            if (_lift != null) EndLift();
        }

        /// One physics step (our FixedUpdate: `pos` = the capsule's centre
        /// after the last step, `vel` = the body's velocity). `contact` = the
        /// solid the player last touched (for the text). Returns true when it
        /// was a plain step (no teleport, not kinematic, not the first) - only
        /// then may a clip be judged across it.
        public bool PhysicsStep(float dt, bool hasPlayer, bool kinematic, Vector3 pos, Vector3 vel, string contact, bool carried = false,
                                string structure = "", bool smash = false, bool crouching = false)
        {
            if (dt > 0f)
            {
                _sinceSmash = smash ? 0f : _sinceSmash + dt;
                if (!string.IsNullOrEmpty(structure)) { _sinceStructure = 0f; _clipStructure = structure; }
                else _sinceStructure += dt;
                _sinceStand = _wasCrouching && !crouching ? 0f : _sinceStand + dt;
                _wasCrouching = crouching;
            }
            if (!hasPlayer || kinematic || dt <= 0f)
            {
                _stepHas = false;
                if (_lift != null) EndLift();
                return false;
            }
            if (!_stepHas)
            {
                _stepHas = true;
                _stepPos = pos;
                _stepVel = vel;
                _settle = SettleSeconds;
                return false;
            }
            Vector3 d = pos - _stepPos;
            float side = (float)System.Math.Sqrt(d.x * d.x + d.z * d.z);
            float sideCould = Mathf.Max(Flat(vel), Flat(_stepVel)) * dt * 2f + 2f;
            float up = Mathf.Max(0f, Mathf.Max(vel.y, _stepVel.y)) * dt;
            float down = Mathf.Max(0f, Mathf.Max(-vel.y, -_stepVel.y)) * dt * 2f + 1f;
            float rise = d.y - up;
            _stepPos = pos;
            _stepVel = vel;
            if (side > sideCould || rise > LiftMaxStep || -d.y > down)
            {
                // A teleport: not the physics'.
                if (_lift != null) EndLift();
                _settle = SettleSeconds;
                return false;
            }
            if (_settle > 0f)
            {
                // Set down by a teleport: being pushed out of what is there.
                _settle -= dt;
                return false;
            }
            if (rise > LiftStepMin && !carried)
            {
                if (_lift == null)
                {
                    _lift = new Move();
                    _lift.Kind = LiftKind;
                    _lift.Position = pos - d;
                    _liftFrom = pos - d;
                    _lift.Detail = "";
                    _liftStructure = "";
                }
                _lift.Distance += rise;
                _lift.Seconds += dt;
                _lift.PausedPushes++;   // steps that lifted
                if (!string.IsNullOrEmpty(contact)) _lift.Detail = contact;
                if (!string.IsNullOrEmpty(structure)) _liftStructure = structure;
                _liftQuiet = 0f;
            }
            else if (_lift != null)
            {
                if (!string.IsNullOrEmpty(structure)) _liftStructure = structure;
                _lift.Seconds += dt;
                _liftQuiet += dt;
                if (_liftQuiet >= LiftQuiet) EndLift();
            }
            if (_lift != null)
            {
                float far = Vector3.Distance(_liftFrom, pos);
                if (far > _lift.PeakSpeed) _lift.PeakSpeed = far;   // the farthest from where it began
            }
            return true;
        }

        /// A finished lift episode under LiftReport, for the log ("" none).
        public string SmallLift { get; private set; }

        /// SmallLift, then cleared.
        public string TakeSmallLift() { string s = SmallLift ?? ""; SmallLift = ""; return s; }

        /// The capsule's centre went from `from` to `to` across `what` (a
        /// solid the player had touched; its name, kind and thickness in
        /// words). `insideSteps`: physics steps the centre spent inside a
        /// solid on the way; `time`: game seconds now.
        public void Clipped(Vector3 from, Vector3 to, string what, int insideSteps, float time)
        {
            bool smashed = _sinceSmash <= ClipSmashWindow;
            bool squeezed = _sinceStructure <= ClipSmashWindow;
            if (!smashed && !squeezed)
            {
                Ungated = what + " (no axe ground smash and no structure they built touched in the last " +
                          ClipSmashWindow.ToString("0.0", CultureInfo.InvariantCulture) + " s)";
                return;
            }
            if (what == _clipWhat && time - _clipAt < ClipRepeat) { _clipAt = time; return; }
            _clipWhat = what;
            _clipAt = time;
            Move m = new Move();
            m.Kind = ClipKind;
            m.Position = from;
            m.Distance = Vector3.Distance(from, to);
            m.PausedPushes = insideSteps;
            m.Detail = "the player's body passed through " + what + ", a solid they had touched: " +
                       Meters2(m.Distance) + " m from where they were last clear of it" +
                       (insideSteps > 0 ? ", " + insideSteps.ToString(CultureInfo.InvariantCulture) + " physics step" + (insideSteps == 1 ? "" : "s") + " inside solids on the way"
                                        : " in one physics step") +
                       ", to (" + to.x.ToString("0.0", CultureInfo.InvariantCulture) + ", " + to.y.ToString("0.0", CultureInfo.InvariantCulture) + ", " +
                       to.z.ToString("0.0", CultureInfo.InvariantCulture) + ")" +
                       (smashed ? ", " + _sinceSmash.ToString("0.00", CultureInfo.InvariantCulture) + " s after an axe ground smash" +
                                  (_sinceStand <= ClipSmashWindow + 0.5f
                                       ? ", " + _sinceStand.ToString("0.00", CultureInfo.InvariantCulture) + " s after standing up from a crouch"
                                       : ", not standing up from a crouch")
                                : "") +
                       (squeezed ? (smashed ? ", and" : ",") + " touching a structure they built (" + _clipStructure + ")" : "");
            Ready.Add(m);
        }

        /// A crossing that did not count (no ground smash), for the log; "" none.
        public string Ungated { get; private set; }

        /// Ungated, then cleared.
        public string TakeUngated() { string s = Ungated ?? ""; Ungated = ""; return s; }

        // ------------------------------------------------------------------

        private void Fall(float gameDt, bool kinematic, float vy)
        {
            if (kinematic) { _fallCur = _fallPrev = _fallAge = 0f; return; }
            _fallAge += gameDt;
            if (_fallAge >= FallWindow * 0.5f) { _fallPrev = _fallCur; _fallCur = 0f; _fallAge = 0f; }
            if (-vy > _fallCur) _fallCur = -vy;
        }

        private void Huge(float gameDt, bool kinematic, Vector3 pos, float speed)
        {
            if (kinematic)
            {
                _hasLast = false;
                _wDt = _wDist = _wMax = 0f;
                return;
            }
            if (_excused)
            {
                if (speed < CalmSpeed) { _calm += gameDt; if (_calm >= CalmSeconds) _excused = false; }
                else _calm = 0f;
                _hasLast = false;
                _wDt = _wDist = _wMax = 0f;
                if (_huge != null) EndHuge();
                return;
            }
            if (!_hasLast) { _hasLast = true; _last = pos; _lastSpeed = speed; _wDt = _wDist = _wMax = 0f; return; }

            float step = Vector3.Distance(_last, pos);
            float could = Mathf.Max(speed, _lastSpeed) * Mathf.Max(gameDt, MaxPhysicsStep) * 2f + 2f;
            _last = pos;
            _lastSpeed = speed;
            if (step > could)
            {
                // A teleport: the distance is not the speed's.
                _wDt = _wDist = _wMax = 0f;
                return;
            }
            _wDt += gameDt;
            _wDist += step;
            if (speed > _wMax) _wMax = speed;
            if (_wDt < HugeWindow) return;

            bool hit = _wDist / _wDt >= HugeSpeed && _wMax >= HugeSpeed;
            if (hit)
            {
                if (_huge == null)
                {
                    _huge = new Move();
                    _huge.Kind = HugeSpeedKind;
                    _huge.Position = pos;
                }
                if (_wMax > _huge.PeakSpeed) _huge.PeakSpeed = _wMax;
                _huge.Distance += _wDist;
                _huge.Seconds += _wDt;
                _hugeQuiet = 0f;
            }
            else if (_huge != null)
            {
                _hugeQuiet += _wDt;
                if (_hugeQuiet >= HugeEndSeconds) EndHuge();
            }
            _wDt = _wDist = _wMax = 0f;
        }

        private void EndHuge()
        {
            Move m = _huge;
            _huge = null;
            m.Detail = "the player moved at " + Speed(m.PeakSpeed) + " m/s (over " + Speed(HugeSpeed) + "): " +
                       Meters(m.Distance) + " m in " + m.Seconds.ToString("0.0", CultureInfo.InvariantCulture) +
                       " s, not from an explosion knockback, a ride or a cutscene";
            Ready.Add(m);
        }

        private void EndLift()
        {
            Move m = _lift;
            _lift = null;
            string near = m.Detail;
            string built = _liftStructure;
            _liftStructure = "";
            string text = "lifted " + Meters2(m.Distance) + " m beyond what their speed allows over " +
                          m.PausedPushes.ToString(CultureInfo.InvariantCulture) + " physics step" + (m.PausedPushes == 1 ? "" : "s") + " (" +
                          m.Seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s)" +
                          (near.Length > 0 ? ", last touching " + near : "");
            if (m.Distance < LiftReport || built.Length == 0)
            {
                if (m.Distance >= LiftReport * 0.3f)
                    SmallLift = text + (built.Length == 0 ? " (no player-built structure touched)" : " (touching the structure " + built + ")");
                return;
            }
            m.Detail = "the physics pushed the player up out of a structure they built (" + built + "): " + text +
                       " - how a log boost or a custom wall boost lifts them";
            Ready.Add(m);
        }

        private static float Flat(Vector3 v) { return (float)System.Math.Sqrt(v.x * v.x + v.z * v.z); }

        private static string Meters2(float v) { return Mathf.Max(0f, v).ToString("0.0", CultureInfo.InvariantCulture); }

        private void Finish()
        {
            Move m = _after;
            _after = null;
            if (m.PausedPushes < MinPausedPushes) return;
            m.Detail = "game time stopped " + m.PausedSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s (" +
                       (m.PauseMenu ? "the pause menu" : "not the pause menu") + ") during an explosion knockback, " +
                       m.SinceBlast.ToString("0.00", CultureInfo.InvariantCulture) + " s after it started: " +
                       m.PausedPushes.ToString(CultureInfo.InvariantCulture) + " frames of push piled up (" +
                       Speed(m.PausedPushes * PushPerFrame) + " m/s); then " + Speed(m.PeakSpeed) + " m/s, " +
                       Meters(m.Distance) + " m in " + AfterSeconds.ToString("0", CultureInfo.InvariantCulture) + " s";
            Ready.Add(m);
        }

        private void Drop(string why)
        {
            int pushes = _after != null ? _after.PausedPushes : _stopPushes;
            if (pushes >= MinPausedPushes) Dropped = "a stop of game time during a knockback (" + pushes + " piled pushes) was dropped: " + why;
        }

        private static string Speed(float v) { return Mathf.Max(0f, v).ToString("#,0", CultureInfo.InvariantCulture); }
        private static string Meters(float v) { return Mathf.Max(0f, v).ToString("#,0", CultureInfo.InvariantCulture); }
    }
}
