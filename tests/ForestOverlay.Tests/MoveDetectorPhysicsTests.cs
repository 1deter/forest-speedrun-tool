using System.Collections.Generic;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // Lifts out of colliders and clips (MoveDetector.PhysicsStep / Clipped),
    // driven one physics step at a time the way Game/ClipWatch does (60 Hz).
    // What must be reported, and above all what must not.
    public class MoveDetectorPhysicsTests
    {
        private const float Dt = 1f / 60f;

        private sealed class Sim
        {
            public readonly MoveDetector D = new MoveDetector();
            public readonly List<MoveDetector.Move> Moves = new List<MoveDetector.Move>();
            public Vector3 Pos = new Vector3(100f, 50f, 100f);
            public Vector3 Vel = Vector3.zero;
            public bool Kinematic;
            public string Contact = "'Log Wall' (a BoxCollider 0.5 m thick)";
            public bool LastPlain;

            /// One physics step: the body moved by its velocity plus `push`
            /// (what the solver moved it without leaving any velocity).
            public void Step(Vector3 push)
            {
                Pos = Pos + Vel * Dt + push;
                LastPlain = D.PhysicsStep(Dt, true, Kinematic, Pos, Vel, Contact);
                Moves.AddRange(D.Ready);
                D.Ready.Clear();
            }

            public void Run(float seconds) { for (int i = 0; i < (int)System.Math.Round(seconds / Dt); i++) Step(Vector3.zero); }

            /// Gravity on, from the current velocity, until it is back to y0.
            public void Ballistic(float g = 25f)
            {
                float y0 = Pos.y;
                Step(Vector3.zero);
                while (Pos.y > y0 || Vel.y > 0f) { Vel = Vel + new Vector3(0f, -g * Dt, 0f); Step(Vector3.zero); if (Vel.y < -60f) break; }
                Vel = Vector3.zero;
            }
        }

        private static Sim Started()
        {
            var s = new Sim();
            s.Step(Vector3.zero);   // the first step only seeds
            s.Run(0.1f);
            return s;
        }

        [Fact]
        public void A_push_out_of_a_solid_with_no_speed_is_a_lift()
        {
            var s = Started();
            s.Step(new Vector3(0f, 0.8f, 0f));   // live: a box 0.8 m into the feet, velocity 0
            for (int i = 0; i < 6; i++) s.Step(new Vector3(0f, 0.2f, 0f));
            s.Run(0.5f);
            Assert.Single(s.Moves);
            var m = s.Moves[0];
            Assert.Equal(MoveDetector.LiftKind, m.Kind);
            Assert.InRange(m.Distance, 1.9f, 2.1f);
            Assert.Contains("Log Wall", m.Detail);
            Assert.Contains("2.0 m", m.Detail);
        }

        [Fact]
        public void A_lift_under_the_report_line_is_only_kept_for_the_log()
        {
            var s = Started();
            s.Step(new Vector3(0f, 0.5f, 0f));
            s.Run(0.5f);
            Assert.Empty(s.Moves);
            Assert.Contains("0.5 m", s.D.TakeSmallLift());
            Assert.Equal("", s.D.TakeSmallLift());
        }

        [Fact]
        public void Pushes_far_apart_are_separate_episodes()
        {
            var s = Started();
            for (int i = 0; i < 5; i++) { s.Step(new Vector3(0f, 0.3f, 0f)); s.Run(0.5f); }
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void A_jump_is_speed_not_a_lift()
        {
            var s = Started();
            s.Vel = new Vector3(3f, 9f, 0f);
            s.Ballistic();
            s.Run(0.5f);
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void Walking_up_a_slope_or_rising_in_water_is_speed()
        {
            var s = Started();
            s.Vel = new Vector3(6f, 4f, 0f);
            s.Run(3f);
            s.Vel = new Vector3(0f, 2f, 0f);   // buoyancy
            s.Run(3f);
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void The_speed_before_the_step_also_explains_a_rise()
        {
            // A landing on a ledge at the top of a jump: the step rose at the
            // old speed, the body's speed is already 0.
            var s = Started();
            s.Vel = new Vector3(0f, 9f, 0f);
            s.Step(Vector3.zero);
            s.Vel = Vector3.zero;
            s.Step(new Vector3(0f, 9f * Dt, 0f));
            s.Run(0.5f);
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void A_teleport_up_or_sideways_is_not_a_lift_and_not_a_plain_step()
        {
            var s = Started();
            s.Step(new Vector3(0f, 12f, 0f));
            Assert.False(s.LastPlain);
            s.Step(new Vector3(40f, 1.5f, 0f));
            Assert.False(s.LastPlain);
            s.Step(new Vector3(0f, -30f, 0f));
            Assert.False(s.LastPlain);
            s.Run(0.5f);
            Assert.True(s.LastPlain);
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void A_teleport_ends_a_lift_episode()
        {
            var s = Started();
            s.Step(new Vector3(0f, 0.8f, 0f));
            s.Step(new Vector3(0f, 0.8f, 0f));
            s.Step(new Vector3(300f, 0f, 0f));   // tp: the episode so far is judged
            s.Run(0.5f);
            Assert.Single(s.Moves);
            Assert.InRange(s.Moves[0].Distance, 1.5f, 1.7f);
        }

        [Fact]
        public void A_kinematic_ride_or_climb_never_lifts()
        {
            var s = Started();
            s.Kinematic = true;
            for (int i = 0; i < 60; i++) s.Step(new Vector3(0f, 0.3f, 0f));
            Assert.False(s.LastPlain);
            s.Kinematic = false;
            s.Run(0.5f);
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void A_knockback_upwards_is_speed()
        {
            var s = Started();
            s.Vel = new Vector3(0f, 100f, 80f);
            s.Ballistic();
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void A_sideways_push_out_of_a_wall_is_a_plain_step()
        {
            // A clip's last push (up to the capsule's width) must still be
            // judged by the clip check.
            var s = Started();
            s.Step(new Vector3(1.4f, 0f, 0f));
            Assert.True(s.LastPlain);
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void Flush_reports_a_lift_under_way_and_a_dropping_reset_forgets_it()
        {
            var s = Started();
            s.Step(new Vector3(0f, 1.2f, 0f));
            s.D.Flush();
            Assert.Single(s.D.Ready);

            var r = Started();
            r.Step(new Vector3(0f, 1.2f, 0f));
            r.D.Reset(drop: true);
            Assert.Empty(r.D.Ready);
        }

        [Fact]
        public void A_clip_is_reported_once_per_solid_while_it_repeats()
        {
            var d = new MoveDetector();
            Vector3 a = new Vector3(0f, 1f, 0f), b = new Vector3(1.2f, 1f, 0f);
            d.Clipped(a, b, "'door_leaf' (a BoxCollider 0.1 m thick)", 3, 10f);
            d.Clipped(b, a, "'door_leaf' (a BoxCollider 0.1 m thick)", 1, 11f);   // back and forth: the same clip
            d.Clipped(a, b, "'door_leaf' (a BoxCollider 0.1 m thick)", 1, 12.5f);
            Assert.Single(d.Ready);
            d.Clipped(a, b, "'panel' (a BoxCollider 0.2 m thick)", 0, 12.6f);
            d.Clipped(a, b, "'door_leaf' (a BoxCollider 0.1 m thick)", 0, 15f);
            Assert.Equal(3, d.Ready.Count);
            Assert.Equal(MoveDetector.ClipKind, d.Ready[0].Kind);
            Assert.Contains("door_leaf", d.Ready[0].Detail);
            Assert.Contains("3 physics steps inside", d.Ready[0].Detail);
            Assert.Contains("in one physics step", d.Ready[1].Detail);
        }

        [Fact]
        public void A_missing_player_ends_the_step_chain()
        {
            var s = Started();
            Assert.False(s.D.PhysicsStep(Dt, false, false, Vector3.zero, Vector3.zero, ""));
            s.Step(new Vector3(0f, 0f, 0f));
            Assert.False(s.LastPlain);   // seeds again
            s.Step(Vector3.zero);
            Assert.True(s.LastPlain);
        }
    }
}
