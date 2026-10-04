using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // ------------------------------------------------------------------
    // The trajectory preview's physics step against the numbers measured
    // live (game-notes *Speedrun tech*: the 55.43 m/s fall, the jump, the
    // bomb boost's pile-up) and HandleLanded's fall damage.
    // ------------------------------------------------------------------
    public class TrajectoryTests
    {
        private const float Dt = 1f / 60f;

        private static FlightModel Player()
        {
            return new FlightModel
            {
                Gravity = new Vector3(0f, -16f, 0f),
                ControllerGravity = 10f,
                Drag = 0f,
                SpeedCap = 55f,
                Step = Dt,
            };
        }

        [Fact]
        public void LongFall_LevelsOffAt55_43()
        {
            FlightModel m = Player();
            Vector3 p = Vector3.zero, v = Vector3.zero;
            for (int i = 0; i < 600; i++) Trajectory.Step(ref p, ref v, i, ref m);
            Assert.InRange(-v.y, 55.42f, 55.44f);
        }

        [Fact]
        public void Jump_Is3MetresHigh_AboutASecond()
        {
            FlightModel m = Player();
            Vector3 p = Vector3.zero, v = new Vector3(0f, 12.65f, 0f);
            float top = 0f;
            int i = 0;
            for (; i < 300; i++)
            {
                Trajectory.Step(ref p, ref v, i, ref m);
                if (p.y > top) top = p.y;
                if (p.y < 0f) break;
            }
            Assert.InRange(top, 2.9f, 3.2f);              // sqrt(2 * 8 * 10) up at 26 m/s^2: 3.08 m
            Assert.InRange((i + 1) * Dt, 0.9f, 1.05f);   // ~0.97 s
        }

        [Fact]
        public void Drag_SlowsLikePhysX()
        {
            FlightModel m = new FlightModel { Drag = 10f, Step = Dt };
            Vector3 p = Vector3.zero, v = new Vector3(6f, 0f, 0f);
            Trajectory.Step(ref p, ref v, 0, ref m);
            Assert.InRange(v.x, 6f * (1f - 10f / 60f) - 0.001f, 6f * (1f - 10f / 60f) + 0.001f);
        }

        [Fact]
        public void Knockback_NoCapNoExtraGravity_ThenHorizontalWiped()
        {
            FlightModel m = Player();
            m.KnockbackSteps = 40;
            m.FreeSteps = 10;
            Vector3 p = Vector3.zero, v = new Vector3(1896f, 0f, 0f);   // 237 paused frames x 8
            for (int i = 0; i < 10; i++) Trajectory.Step(ref p, ref v, i, ref m);
            Assert.Equal(1896f, v.x);                     // no 55 cap during the knockback
            Assert.InRange(v.y, -16f * 10 * Dt - 0.01f, -16f * 10 * Dt + 0.01f);   // -16 only
            Assert.InRange(p.x, 315f, 317f);              // 1896 x 10 / 60 (measured: 308 m for 1 s paused)
            Trajectory.Step(ref p, ref v, 10, ref m);
            Assert.Equal(0f, v.x);                        // the explode animation
            Assert.True(v.y < 0f);                        // vertical kept
        }

        [Fact]
        public void Knockback_PushesOnlyInItsPushSteps()
        {
            FlightModel m = new FlightModel { Step = Dt, KnockbackSteps = 30, FreeSteps = 10, PushSteps = 3, PushPerStep = new Vector3(0f, 0f, -32f) };
            Vector3 p = Vector3.zero, v = Vector3.zero;
            for (int i = 0; i < 8; i++) Trajectory.Step(ref p, ref v, i, ref m);
            Assert.Equal(-96f, v.z);
        }

        [Fact]
        public void AfterTheKnockback_TheCapIsBack()
        {
            FlightModel m = Player();
            m.KnockbackSteps = 2;
            m.FreeSteps = 2;
            Vector3 p = Vector3.zero, v = new Vector3(300f, 0f, 0f);
            for (int i = 0; i < 3; i++) Trajectory.Step(ref p, ref v, i, ref m);
            Assert.InRange(v.magnitude, 55f, 55.5f);
        }

        [Fact]
        public void Steps_Rounds()
        {
            Assert.Equal(10, Trajectory.Steps(0.163f, Dt));
            Assert.Equal(0, Trajectory.Steps(-1f, Dt));
            Assert.Equal(0, Trajectory.Steps(1f, 0f));
        }

        [Fact]
        public void FallDamage_MatchesHandleLanded()
        {
            Assert.Equal(0, Trajectory.FallDamage(28f, 2f));     // "> 28", not ">="
            Assert.Equal(52, Trajectory.FallDamage(40f, 2f));    // 0.9 x 40^2 / 27.5
            Assert.Equal(100, Trajectory.FallDamage(55.43f, 2f));
            Assert.Equal(0, Trajectory.FallDamage(50f, 0.5f));   // too short in the air
            Assert.Equal(1000, Trajectory.FallDamage(30f, 4f));  // fell too long
        }

        [Fact]
        public void BombBoost_MatchesTheMeasuredPileUp()
        {
            float speed = BombBoost.Speed(237);                  // 1 s paused at ~237 fps
            Assert.Equal(1896f, speed);
            float held = BombBoost.HeldFor(0f, false, Dt);
            Assert.InRange(BombBoost.Distance(speed, held), 300f, 320f);   // measured 308 m
            // Late: the explode animation already plays - one physics step.
            Assert.InRange(BombBoost.Distance(BombBoost.Speed(120), BombBoost.HeldFor(0.2f, true, Dt)), 15.9f, 16.1f);
            // Paused 0.15 s after the blast: one step left at most.
            Assert.Equal(Dt, BombBoost.HeldFor(0.2f, false, Dt));
            Assert.Equal(1920f, BombBoost.SpeedPerSecond(240f));
        }
    }
}
