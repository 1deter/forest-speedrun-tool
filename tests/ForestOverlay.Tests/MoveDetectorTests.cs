using System.Collections.Generic;
using System.Linq;
using ForestOverlay.Data;
using UnityEngine;
using Xunit;

namespace ForestOverlay.Tests
{
    // Banned-move detection: what must be reported, and above all what must
    // not (author, 2026-10-03: "tested somewhat vigorously to prevent false
    // flags"). Each test drives the detector the way the game would, frame
    // by frame, at a given frame rate.
    public class MoveDetectorTests
    {
        private sealed class Sim
        {
            public readonly MoveDetector D = new MoveDetector();
            public readonly List<MoveDetector.Move> Moves = new List<MoveDetector.Move>();
            public Vector3 Pos = new Vector3(100f, 50f, 100f);
            public Vector3 Vel = Vector3.zero;
            public bool Kinematic;
            public bool HasPlayer = true;
            public float Fps = 200f;

            private float Dt { get { return 1f / Fps; } }

            /// Frames with game time running, moving at Vel.
            public void Run(float seconds, bool knockback = false)
            {
                int n = (int)System.Math.Round(seconds * Fps);
                for (int i = 0; i < n; i++)
                {
                    if (knockback) D.KnockbackPush(false, false, true, Pos);
                    Pos = Pos + Vel * Dt;
                    D.Frame(Dt, Dt, HasPlayer, Kinematic, Pos, Vel);
                    Take();
                }
            }

            /// Frames with game time stopped (deltaTime 0); `knockback` = the
            /// coroutine still pushing each frame.
            public void Stop(float seconds, bool knockback, bool pauseMenu = true)
            {
                int n = (int)System.Math.Round(seconds * Fps);
                for (int i = 0; i < n; i++)
                {
                    if (knockback) D.KnockbackPush(true, pauseMenu, true, Pos);
                    D.Frame(0f, Dt, HasPlayer, Kinematic, Pos, Vel);
                    Take();
                }
            }

            public void Take()
            {
                Moves.AddRange(D.Ready);
                D.Ready.Clear();
            }

            public void Flush() { D.Flush(); Take(); }
        }

        // --- what must not be reported ---------------------------------------

        [Theory]
        [InlineData(30f)]
        [InlineData(60f)]
        [InlineData(144f)]
        [InlineData(240f)]
        [InlineData(400f)]
        public void A_plain_knockback_is_not_a_move_at_any_frame_rate(float fps)
        {
            var s = new Sim { Fps = fps };
            s.Run(1f);
            s.D.KnockbackStarted();
            // A knockback alone peaks at ~260 m/s (game-notes): far over the
            // huge speed, but it is the game's own knockback.
            s.Vel = new Vector3(264f, 20f, 0f);
            s.Run(0.5f, knockback: true);
            s.Vel = new Vector3(120f, -10f, 0f);
            s.Run(0.25f, knockback: true);
            s.Vel = new Vector3(40f, -5f, 0f);
            s.Run(0.5f);
            s.Vel = Vector3.zero;
            s.Run(3f);
            s.Flush();
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void Pausing_outside_a_knockback_is_not_a_move()
        {
            var s = new Sim { Fps = 240f, Vel = new Vector3(6f, 0f, 0f) };
            s.Run(2f);
            s.Stop(10f, knockback: false);
            s.Run(2f);
            s.Flush();
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void Pausing_after_the_knockback_ended_is_not_a_move()
        {
            var s = new Sim { Fps = 240f };
            s.D.KnockbackStarted();
            s.Vel = new Vector3(200f, 0f, 0f);
            s.Run(0.75f, knockback: true);
            s.Vel = Vector3.zero;
            s.Run(0.1f);             // the coroutine is over: no more pushes
            s.Stop(3f, knockback: false);
            s.Run(2f);
            s.Flush();
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void A_few_frames_of_stopped_time_are_below_the_threshold()
        {
            var s = new Sim { Fps = 240f };
            s.D.KnockbackStarted();
            s.Vel = new Vector3(100f, 0f, 0f);
            s.Run(0.05f, knockback: true);
            for (int i = 0; i < MoveDetector.MinPausedPushes - 1; i++)
            {
                s.D.KnockbackPush(true, true, true, s.Pos);
                s.D.Frame(0f, 1f / 240f, true, false, s.Pos, s.Vel);
            }
            s.Run(0.5f, knockback: true);
            s.Vel = Vector3.zero;
            s.Run(2f);
            s.Flush();
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void A_stop_the_player_never_comes_back_from_is_dropped()
        {
            // Killed by the blast, then Reload save on death: the load stops
            // game time while the old coroutine may still run.
            var s = new Sim { Fps = 200f };
            s.D.KnockbackStarted();
            s.Run(0.02f, knockback: true);
            s.Stop(1f, knockback: true, pauseMenu: false);
            s.HasPlayer = false;
            s.Run(0.5f);
            s.HasPlayer = true;
            s.Vel = new Vector3(0f, 0f, 0f);
            s.Run(2f);
            s.Flush();
            Assert.Empty(s.Moves);
            Assert.Contains("dropped", s.D.TakeDropped());
            Assert.Equal("", s.D.Dropped);
        }

        [Fact]
        public void A_reset_during_a_stop_drops_it()
        {
            var s = new Sim();
            s.D.KnockbackStarted();
            s.Run(0.02f, knockback: true);
            s.Stop(0.5f, knockback: true);
            s.D.Reset(drop: true);
            s.Run(2f);
            s.Flush();
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void An_attempt_ending_while_paused_drops_the_stop()
        {
            var s = new Sim();
            s.D.KnockbackStarted();
            s.Run(0.02f, knockback: true);
            s.Stop(0.5f, knockback: true);
            s.Flush();
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void A_teleport_is_not_huge_speed()
        {
            var s = new Sim { Fps = 144f };
            s.Run(1f);
            for (int i = 0; i < 20; i++)
            {
                s.Pos = s.Pos + new Vector3(3000f, 0f, 0f);   // a cave entrance, the elevator, a load spot
                s.Run(0.5f);
            }
            s.Flush();
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void A_velocity_without_movement_is_not_huge_speed()
        {
            var s = new Sim();
            s.Run(0.5f);
            for (int i = 0; i < 400; i++)
                s.D.Frame(0.005f, 0.005f, true, false, s.Pos, new Vector3(0f, 900f, 0f));
            s.Take();
            s.Flush();
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void A_kinematic_mover_is_not_huge_speed()
        {
            // Rides, cutscenes, the elevators: the game moves a kinematic body.
            var s = new Sim { Kinematic = true, Vel = new Vector3(0f, -400f, 0f) };
            s.Run(5f);
            s.Flush();
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void The_longest_fall_is_not_huge_speed()
        {
            // Gravity 16: the fall is lethal at 3.8 s (~61 m/s); even an
            // unbroken 10 s fall stays under the line.
            var s = new Sim { Fps = 120f };
            for (int i = 0; i < 1200; i++)
            {
                s.Vel = new Vector3(0f, -16f * (i / 120f), 0f);
                s.Run(1f / 120f);
            }
            s.Flush();
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void Speed_still_falling_off_after_a_knockback_is_excused_until_calm()
        {
            var s = new Sim { Fps = 240f };
            s.D.KnockbackStarted();
            s.Vel = new Vector3(260f, 30f, 0f);
            s.Run(0.75f, knockback: true);
            // Still flying fast for a while after the coroutine ended.
            s.Vel = new Vector3(230f, -20f, 0f);
            s.Run(0.8f);
            s.Vel = new Vector3(0f, 0f, 0f);
            s.Run(2f);
            s.Flush();
            Assert.Empty(s.Moves);
        }

        [Fact]
        public void Huge_speed_in_short_bursts_below_the_window_is_not_reported()
        {
            // One physics step at a high speed (a collision pop) is shorter
            // than the window: the distance never adds up.
            var s = new Sim { Fps = 60f };
            for (int i = 0; i < 10; i++)
            {
                s.Vel = new Vector3(400f, 0f, 0f);
                s.Run(1f / 60f);
                s.Vel = Vector3.zero;
                s.Run(1f);
            }
            s.Flush();
            Assert.Empty(s.Moves);
        }

        // --- what must be reported -------------------------------------------

        [Theory]
        [InlineData(60f, 0.5f)]
        [InlineData(144f, 1f)]
        [InlineData(240f, 1f)]
        [InlineData(240f, 3f)]
        public void A_bomb_boost_is_reported_once_with_its_numbers(float fps, float paused)
        {
            var s = new Sim { Fps = fps };
            s.Run(1f);
            s.D.KnockbackStarted();
            s.Vel = new Vector3(30f, 0f, 0f);
            s.Run(0.03f, knockback: true);
            s.Stop(paused, knockback: true);
            int frames = (int)System.Math.Round(paused * fps);
            s.Vel = new Vector3(8f * frames, 0f, 0f);   // all the piled push at once
            s.Run(0.17f, knockback: true);
            s.Vel = new Vector3(0f, -10f, 0f);
            s.Run(3f);
            s.Flush();

            var m = Assert.Single(s.Moves);
            Assert.Equal(MoveDetector.BombBoost, m.Kind);
            Assert.Equal(frames, m.PausedPushes);
            Assert.InRange(m.PausedSeconds, paused - 0.02f, paused + 0.02f);
            Assert.True(m.PauseMenu);
            Assert.InRange(m.SinceBlast, 0.02f, 0.04f);
            Assert.True(m.PeakSpeed >= 8f * frames - 1f);
            Assert.True(m.Distance > m.PeakSpeed * 0.15f);   // ~0.17 s at the piled speed
            Assert.Contains("the pause menu", m.Detail);
            Assert.Contains("frames of push piled up", m.Detail);
        }

        [Fact]
        public void Two_stops_in_one_knockback_are_two_boosts()
        {
            var s = new Sim { Fps = 200f };
            s.D.KnockbackStarted();
            s.Run(0.02f, knockback: true);
            s.Stop(0.5f, knockback: true);
            s.Run(0.1f, knockback: true);
            s.Stop(0.5f, knockback: true);
            s.Run(0.2f, knockback: true);
            s.Run(2f);
            s.Flush();
            Assert.Equal(2, s.Moves.Count);
            Assert.All(s.Moves, m => Assert.Equal(MoveDetector.BombBoost, m.Kind));
            Assert.All(s.Moves, m => Assert.Equal(100, m.PausedPushes));
        }

        [Fact]
        public void A_stop_that_is_not_the_pause_menu_says_so()
        {
            var s = new Sim { Fps = 200f };
            s.D.KnockbackStarted();
            s.Run(0.02f, knockback: true);
            s.Stop(0.2f, knockback: true, pauseMenu: false);
            s.Run(2f, knockback: false);
            s.Flush();
            var m = Assert.Single(s.Moves);
            Assert.False(m.PauseMenu);
            Assert.Contains("not the pause menu", m.Detail);
        }

        [Fact]
        public void An_attempt_ending_just_after_a_boost_still_reports_it()
        {
            var s = new Sim { Fps = 200f };
            s.D.KnockbackStarted();
            s.Run(0.02f, knockback: true);
            s.Stop(0.5f, knockback: true);
            s.Vel = new Vector3(800f, 0f, 0f);
            s.Run(0.1f, knockback: true);
            s.Flush();
            Assert.Equal(MoveDetector.BombBoost, Assert.Single(s.Moves).Kind);
        }

        [Fact]
        public void Huge_speed_without_a_knockback_is_reported_once_per_episode()
        {
            var s = new Sim { Fps = 144f };
            s.Run(1f);
            s.Vel = new Vector3(300f, 0f, 0f);
            s.Run(0.5f);
            s.Vel = new Vector3(5f, 0f, 0f);
            s.Run(3f);
            s.Vel = new Vector3(0f, 250f, 0f);
            s.Run(0.3f);
            s.Vel = Vector3.zero;
            s.Run(3f);
            s.Flush();
            Assert.Equal(2, s.Moves.Count);
            Assert.All(s.Moves, m => Assert.Equal(MoveDetector.HugeSpeedKind, m.Kind));
            Assert.InRange(s.Moves[0].Distance, 120f, 160f);
            Assert.InRange(s.Moves[0].PeakSpeed, 299f, 301f);
            Assert.Contains("300 m/s", s.Moves[0].Detail);
        }

        [Fact]
        public void Huge_speed_after_the_knockback_has_calmed_down_is_reported()
        {
            var s = new Sim { Fps = 200f };
            s.D.KnockbackStarted();
            s.Vel = new Vector3(200f, 0f, 0f);
            s.Run(0.75f, knockback: true);
            s.Vel = Vector3.zero;
            s.Run(1.5f);                      // calm
            s.Vel = new Vector3(0f, 400f, 0f); // something else launches the player
            s.Run(0.5f);
            s.Vel = Vector3.zero;
            s.Run(2f);
            s.Flush();
            Assert.Equal(MoveDetector.HugeSpeedKind, Assert.Single(s.Moves).Kind);
        }

        [Fact]
        public void Huge_speed_still_going_at_the_end_is_flushed()
        {
            var s = new Sim();
            s.Vel = new Vector3(500f, 0f, 0f);
            s.Run(0.5f);
            s.Flush();
            Assert.Equal(MoveDetector.HugeSpeedKind, Assert.Single(s.Moves).Kind);
        }
    }
}
