using System.Collections.Generic;
using ForestOverlay.Core;
using ForestOverlay.Data;
using ForestOverlay.Game;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Banned-move detection (docs/run-mode.md *Banned moves: detection*;
    // author, 2026-10-03). Data/MoveDetector decides, Game/MoveWatch feeds
    // it the knockback's pushes, this feeds it every frame and takes what
    // it finds: one log line each (always - practice too, it is how the
    // detection is tested) and, during an attempt, a `move` line in the
    // attempt log, folded into the chain. A move is a lead for the
    // verifier, never a flag: the attempt stays valid, the site shows the
    // moves beside the category's banned moves.
    // ------------------------------------------------------------------
    public sealed partial class RunModeModule
    {
        private readonly MoveDetector _moves = new MoveDetector();
        private readonly List<string> _attemptMoves = new List<string>();   // this attempt's, for the Runs tab
        private bool _movesLoaded;

        private void InitMoves(ModuleContext ctx)
        {
            MoveWatch.Detector = _moves;
            MoveWatch.Install(ctx.Log, OverlayPlugin.PluginGuid);
        }

        // Every frame, from Tick.
        private void TickMoves(bool loaded)
        {
            // A load (or the title screen) drops a boost that never landed.
            if (loaded != _movesLoaded) { _moves.Reset(drop: true); _movesLoaded = loaded; }
            Rigidbody rb = loaded ? Ctx.Player.Rigidbody : null;
            bool has = loaded && rb != null && Ctx.Player.Transform != null;
            _moves.Frame(Time.deltaTime, Time.unscaledDeltaTime, has, has && rb.isKinematic,
                         has ? Ctx.Player.Transform.position : Vector3.zero, has ? rb.velocity : Vector3.zero);
            TakeMoves();
        }

        private void TakeMoves()
        {
            string dropped = _moves.TakeDropped();
            if (dropped.Length > 0) Ctx.Log.LogInfo("Move watch: " + dropped + ".");
            string small = _moves.TakeSmallLift();
            if (small.Length > 0) Ctx.Log.LogInfo("Move watch: a small lift, not reported: " + small + ".");
            if (_moves.Ready.Count == 0) return;
            for (int i = 0; i < _moves.Ready.Count; i++)
            {
                MoveDetector.Move m = _moves.Ready[i];
                bool folded = _attemptOpen && _chain != null && !_chain.Ended;
                Vector3 p = m.Position;
                Ctx.Log.LogInfo("Move seen: " + m.Kind + " at (" + p.x.ToString("0") + ", " + p.y.ToString("0") + ", " + p.z.ToString("0") +
                                ") - " + m.Detail + (folded ? " - in attempt " + _attemptId + "'s log" : " (no attempt running)") + ".");
                if (!folded) continue;
                _chain.Move(_clock.ElapsedMilliseconds, m.Kind, true, p.x, p.y, p.z, m.Detail);
                _attemptMoves.Add(KindLabel(m.Kind) + " at " + Clock(_clock.ElapsedMilliseconds) + ": " + m.Detail);
                _nextText = 0f;
            }
            _moves.Ready.Clear();
        }

        /// The attempt is about to end: what is still being measured goes in.
        private void FlushMoves()
        {
            _moves.Flush();
            TakeMoves();
        }

        /// A new attempt: nothing carries over.
        private void ResetMoves()
        {
            _moves.Reset(drop: true);
            _attemptMoves.Clear();
            _moves.TakeDropped();
        }

        public static string KindLabel(string kind)
        {
            switch (kind)
            {
                case MoveDetector.BombBoost: return "Bomb boost";
                case MoveDetector.HugeSpeedKind: return "Huge speed";
                case MoveDetector.CaveForceLoad: return "Cave state force load";
                case MoveDetector.FallDamageCancel: return "Fall damage cancel";
                case MoveDetector.LiftKind: return "Lift out of a structure";
                case MoveDetector.ClipKind: return "Clip through a solid";
                default: return kind;
            }
        }

        private static string Clock(long ms)
        {
            long s = ms / 1000;
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        // The Runs tab's lines about moves ("" = nothing to say).
        private string MovesText()
        {
            string watch = MoveWatch.Detector != null && MoveWatch.Status.StartsWith("watching") ? ""
                         : "Move detection is off: " + MoveWatch.Status + ".";
            if (MoveWatch.Detector != null && !MoveWatch.CaveStatus.StartsWith("watching"))
                watch = (watch.Length > 0 ? watch + "\n" : "") + "Cave entrances are not watched: " + MoveWatch.CaveStatus + ".";
            if (MoveWatch.Detector != null && !MoveWatch.LandStatus.StartsWith("watching"))
                watch = (watch.Length > 0 ? watch + "\n" : "") + "Landings are not watched: " + MoveWatch.LandStatus + ".";
            if (MoveWatch.Detector != null && !ClipWatch.Status.StartsWith("watching"))
                watch = (watch.Length > 0 ? watch + "\n" : "") + "Clips and lifts are not watched: " + ClipWatch.Status + ".";
            if (_attemptMoves.Count == 0) return watch;
            return (watch.Length > 0 ? watch + "\n" : "") +
                   "Moves seen in this attempt (in its log for the verifier - not flags, the attempt stays valid):\n- " +
                   string.Join("\n- ", _attemptMoves.ToArray());
        }
    }
}
