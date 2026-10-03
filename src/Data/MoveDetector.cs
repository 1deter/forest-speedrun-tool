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
    // ------------------------------------------------------------------
    public sealed class MoveDetector
    {
        public const string BombBoost = "bomb-boost";
        public const string HugeSpeedKind = "huge-speed";
        public const string CaveForceLoad = "cave-force-load";

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

            Huge(gameDt, kinematic, pos, speed);
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
        }

        // ------------------------------------------------------------------

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
