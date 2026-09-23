using System.Collections.Generic;
using UnityEngine;

namespace ForestOverlay.Data
{
    public enum SplitEvent
    {
        None,
        /// The current checkpoint fired; `Next` has moved on.
        Split,
        /// The end fired with every checkpoint done.
        Finished,
        /// The end was reached while a checkpoint was still outstanding.
        /// The run carries on - reported so the runner knows why it did
        /// not finish.
        EndBlocked,
    }

    // ------------------------------------------------------------------
    // The order a running segment's triggers fire in: checkpoints one by
    // one, then the end.
    //
    // Two bugs lived here while this was inline in PracticeRunModule
    // (runner report, v0.22.6: "checkpoints in general are being
    // bypassed"):
    //
    //   * The end was evaluated whether or not the checkpoints had fired,
    //     so a run that missed one simply finished - an incomparable time,
    //     recorded as if it were whole. The end now waits for them.
    //
    //   * A checkpoint was primed only when it became the current one. If
    //     its condition already held then - the keycard already in the
    //     bag, already standing in its zone - the prime read "satisfied",
    //     no rising edge ever came, and every later split was dead. A
    //     trigger that becomes current by an earlier one firing now fires
    //     at once if it already holds.
    //
    // The one prime kept: a ZONE that is current when the run starts. A
    // loop route's end zone is its start zone, and you are standing at its
    // edge when the clock starts - firing there would finish in 0 s. Item
    // and event triggers have no such geometry and fire if they hold.
    //
    // Pure, so it is unit tested (SplitSequenceTests).
    // ------------------------------------------------------------------
    public sealed class SplitSequence
    {
        private IList<Trigger> _checkpoints;
        private Trigger _end;
        private TriggerState[] _states = new TriggerState[0];
        private TriggerState _endState;
        private TriggerState _endWatch;

        /// Index of the checkpoint that fires next; equal to Count once
        /// only the end remains.
        public int Next { get; private set; }

        public int Count { get { return _checkpoints != null ? _checkpoints.Count : 0; } }

        public bool OnlyEndLeft { get { return Next >= Count; } }

        /// The trigger the runner is heading for now.
        public Trigger Current { get { return OnlyEndLeft ? _end : _checkpoints[Next]; } }

        /// Call when the clock starts.
        public void Begin(IList<Trigger> checkpoints, Trigger end)
        {
            _checkpoints = checkpoints;
            _end = end;
            Next = 0;

            if (_states.Length != Count) _states = new TriggerState[Count];
            for (int i = 0; i < _states.Length; i++) TriggerEvaluator.Reset(ref _states[i]);
            TriggerEvaluator.Reset(ref _endState);
            TriggerEvaluator.Reset(ref _endWatch);

            if (Count > 0) MakeCurrent(_checkpoints[0], ref _states[0], true);
            else MakeCurrent(_end, ref _endState, true);
        }

        /// One evaluation pass; at most one thing happens per pass, so two
        /// splits due in the same frame land a frame apart.
        public SplitEvent Evaluate(Vector3 position, IItemCounts items, string firedEvent, IItemCounts baseline)
        {
            if (_checkpoints == null) return SplitEvent.None;

            if (!OnlyEndLeft)
            {
                if (TriggerEvaluator.Fired(_checkpoints[Next], ref _states[Next], position, items, firedEvent, baseline))
                {
                    Next++;
                    if (OnlyEndLeft) MakeCurrent(_end, ref _endState, false);
                    else MakeCurrent(_checkpoints[Next], ref _states[Next], false);
                    return SplitEvent.Split;
                }

                // Watched only to say why the run did not finish.
                if (TriggerEvaluator.Fired(_end, ref _endWatch, position, items, firedEvent, baseline))
                    return SplitEvent.EndBlocked;
                return SplitEvent.None;
            }

            return TriggerEvaluator.Fired(_end, ref _endState, position, items, firedEvent, baseline)
                ? SplitEvent.Finished
                : SplitEvent.None;
        }

        /// A manual split (F12): skip the current checkpoint.
        public void SkipCheckpoint()
        {
            if (OnlyEndLeft) return;
            Next++;
            if (OnlyEndLeft) MakeCurrent(_end, ref _endState, false);
            else MakeCurrent(_checkpoints[Next], ref _states[Next], false);
        }

        private static void MakeCurrent(Trigger t, ref TriggerState state, bool atRunStart)
        {
            if (atRunStart && t.Kind == TriggerKind.Zone) TriggerEvaluator.Reset(ref state);
            else TriggerEvaluator.ArmAsNext(ref state);
        }
    }
}
