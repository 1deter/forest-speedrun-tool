using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The trajectory preview's maths (Debug views, Experimental; drawn by
    // Game/TrajectoryView). Pure: the game side reads the live numbers into
    // a FlightModel, steps it here and sweeps the player's capsule along
    // each piece of path itself.
    //
    // One physics step, in the order the game runs it (decompiled
    // FirstPersonCharacter.FixedUpdate, then PhysX):
    //   1. FixedUpdate: in the air `HandleJumpSpeed` (on the ground
    //      `ClampVelocity`) clamps the speed to `maximumVelocity` (55);
    //      then it adds its own push down, `gravity * mass` as a force =
    //      `gravity` (10) m/s^2 - every step unless Diving / on a raft.
    //   2. PhysX: v += (Physics.gravity (-16) + forces / mass) * dt, then
    //      linear damping v *= 1 - min(1, drag * dt) (the rigidbody's drag:
    //      0, 10 on a sled), then position += v * dt.
    // So a fall levels off at 55 + 26 / 60 = 55.43 m/s - the number
    // measured live (game-notes *Speedrun tech*, *Player physics numbers*).
    // Air control (your input pulling the speed toward where you steer) is
    // NOT modelled: the preview assumes you keep holding the way you move.
    //
    // The explosion knockback (game-notes *Bomb boost, refined*, live):
    // the coroutine disables FirstPersonCharacter for the whole knockback
    // (no cap, no extra gravity: -16 only) and pushes 8 m/s against the
    // player's facing once per RENDERED frame. Pushes made while the pause
    // menu stops physics pile up and are all applied by the first physics
    // step after it closes. That speed is held ~10 physics steps (0.163 s
    // of game time from the blast, measured); then the explode animation
    // wipes the horizontal speed every frame (vertical kept) while it
    // plays. Pausing after the animation has started (the late regime) =
    // the pile-up for one physics step only.
    // ------------------------------------------------------------------
    public struct FlightModel
    {
        /// The rigidbody's own gravity (Physics.gravity; zero if it has none).
        public Vector3 Gravity;
        /// FirstPersonCharacter.gravity: its extra push down (m/s^2) while it runs.
        public float ControllerGravity;
        /// Rigidbody.drag.
        public float Drag;
        /// FirstPersonCharacter.maximumVelocity while it runs (0 = none).
        public float SpeedCap;
        /// Time.fixedDeltaTime.
        public float Step;

        /// Steps the knockback still holds the controller off (0 = no knockback).
        public int KnockbackSteps;
        /// Of those, the first ones before the explode animation wipes the horizontal speed.
        public int FreeSteps;
        /// Of the free ones, the steps the coroutine still pushes in.
        public int PushSteps;
        /// The coroutine's 8 m/s per frame, summed over one physics step's frames.
        public Vector3 PushPerStep;
    }

    public static class Trajectory
    {
        /// HandleLanded: damage only when the judged speed is over 28 m/s ...
        public const float FallDamageSpeed = 28f;
        /// ... after more than 0.75 s in the air ...
        public const float FallDamageMinAir = 0.75f;
        /// ... and 1000 (death) after more than 3.8 s.
        public const float FellTooLongAir = 3.8f;
        public const int FellTooLongDamage = 1000;

        /// Advances one physics step (step number `i` from the start of the
        /// prediction, which the knockback's phases count).
        public static void Step(ref Vector3 pos, ref Vector3 vel, int i, ref FlightModel m)
        {
            bool knockback = i < m.KnockbackSteps;
            if (knockback)
            {
                if (i < m.FreeSteps)
                {
                    if (i < m.PushSteps) vel = vel + m.PushPerStep;
                }
                else
                {
                    vel.x = 0f;   // the explode animation, every frame
                    vel.z = 0f;
                }
            }
            else if (m.SpeedCap > 0f)
            {
                float speed = vel.magnitude;
                if (speed > m.SpeedCap) vel = vel * (m.SpeedCap / speed);
            }

            Vector3 a = m.Gravity;
            if (!knockback) a.y -= m.ControllerGravity;
            vel = vel + a * m.Step;
            float damp = 1f - Mathf.Min(1f, Mathf.Max(0f, m.Drag) * m.Step);
            vel = vel * damp;
            pos = pos + vel * m.Step;
        }

        /// Whole physics steps in `seconds` (rounded; at least 0).
        public static int Steps(float seconds, float step)
        {
            if (step <= 0f || seconds <= 0f) return 0;
            return (int)(seconds / step + 0.5f);
        }

        /// The damage HandleLanded deals for a landing judged at `speed`
        /// (m/s, downwards) after `air` seconds in the air; 0 = none.
        public static int FallDamage(float speed, float air)
        {
            if (speed <= FallDamageSpeed || air <= FallDamageMinAir) return 0;
            if (air > FellTooLongAir) return FellTooLongDamage;
            return (int)(speed * 0.9f * (speed / 27.5f));
        }
    }

    // ------------------------------------------------------------------
    // The bomb boost's pile-up (game-notes *Bomb boost, refined*, measured
    // 2026-10-03 with real pauses at ~235 fps): speed after the menu
    // closes = 8 m/s x frames pushed (paused frames + the ones before),
    // held for the free window left (0.163 s - game time since the blast),
    // or one physics step once the explode animation plays. In the air
    // with nothing hit: distance = speed x time held (1.30-1.38 m per
    // paused frame measured; 8 x 0.163 = 1.30).
    // ------------------------------------------------------------------
    public static class BombBoost
    {
        /// enableExplodeCamera: AddForce(-forward * 8, VelocityChange) a frame.
        public const float PushPerFrame = 8f;
        /// The pile-up's speed is held this long after the blast (game time, measured).
        public const float FreeWindow = 0.163f;
        /// The free push ends when the explode animation starts, ~0.13 s after the blast (measured).
        public const float PushWindow = 0.13f;
        /// The explode animation after the window: 0.5 s + up to 0.25 s (horizontal wiped).
        public const float ExplodeHold = 0.5f;

        public static float Speed(int frames) { return PushPerFrame * Mathf.Max(0, frames); }

        /// Game time the boost's speed is still held: the free window left,
        /// or one physics step once the explode animation plays.
        public static float HeldFor(float sinceBlast, bool explodeState, float step)
        {
            if (explodeState) return step;
            return Mathf.Max(step, FreeWindow - Mathf.Max(0f, sinceBlast));
        }

        /// Distance in the air with nothing in the way.
        public static float Distance(float speed, float heldFor) { return speed * heldFor; }

        /// What one more second in the pause menu adds, in m/s, at `fps`.
        public static float SpeedPerSecond(float fps) { return PushPerFrame * Mathf.Max(0f, fps); }
    }
}
