using System;
using System.Reflection;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Cuts a player action in progress on a reset (runner maks: the plane
    // axe's swing played on through a restart).
    //
    // WHY NOT THE GAME'S RESET (bridge, 2026-09-24, `anim watch`):
    // playerAnimatorControl.resetAnimator fires "resetTrigger", which sends
    // the arms (upperBody) and full-body (fullBodyActions) layers to their
    // UNARMED idles with the full-body layer still at weight 1 - for a
    // frame the body stands in the unarmed pose under a camera placed for
    // the swing, and the author looked down into the neck (screenshot).
    // It also stays armed when nothing consumes it (at rest), and would
    // cut the next real swing.
    //
    // WHAT: the game tags its states - "idling", "held" (the armed idle),
    // "attacking", "smash", "block" (playerAnimatorControl.Start hashes
    // these names). While the arms layer rests in a held / idling state,
    // Track() remembers each action layer's state and which bool
    // parameters are off, for whatever is in the hands. Cancel() blends a
    // layer that is in an action (or off its rest) back to that rest in
    // 0.1 s, switches off the bools that are on now but were off at rest,
    // and clears the triggers. Nothing learned yet (just after a load):
    // the game's reset, and its trigger cleared two frames later.
    //
    // A swing's windup (stickHeavyAttackWindup) is tagged "held" like the
    // idle (bridge, v0.24.32: a reset 15 ms into a swing left it running).
    // So the rest is learned only once the arms have sat in one state for
    // RestSettle, and on a reset any arms state other than the learned
    // rest counts as an action - unless the hands changed since (a bool
    // on at rest, e.g. axeHeld, is now off), when only tagged actions do.
    //
    // The attack itself runs in the player's PlayMaker FSM (controlFSM):
    // cut in its windup, it waits in "stickAttack" for the animation that
    // never comes, 3 s, and eats clicks meanwhile (bridge, v0.24.33). So
    // after a cut the FSM is sent "toReset2" - the global event into
    // resetDelayLyr2, the state every attack ends in (spine layer back,
    // action bools off, then waitForInput). Not "toResetPlayer": that
    // runs the game's resetAnimator first (the neck view above).
    // ------------------------------------------------------------------
    public static class AnimReset
    {
        private static readonly int TagIdling = Animator.StringToHash("idling");
        private static readonly int TagHeld = Animator.StringToHash("held");
        private static readonly int TagAttacking = Animator.StringToHash("attacking");
        private static readonly int TagSmash = Animator.StringToHash("smash");
        private static readonly int TagBlock = Animator.StringToHash("block");
        private static readonly int ResetTriggerHash = Animator.StringToHash("resetTrigger");

        private const int Arms = 1;          // upperBody
        private const int FullBody = 2;      // fullBodyActions
        private const float Blend = 0.1f;
        private const float RestWeight = 0.05f;
        private const float RestSettle = 0.4f;   // a windup lasts ~0.2 s

        private static Animator _animator;
        private static AnimatorControllerParameter[] _params;   // cached: the property allocates
        private static bool[] _restBools;
        private static readonly int[] _restState = new int[3];
        private static bool _known;
        private static int _nextSnapshotFrame;
        private static int _settleState;
        private static float _settleSince;
        private static int _clearTriggerAt = -1;

        private const string FsmRest = "waitForInput";
        private const string FsmEndAttack = "toReset2";

        private static FieldInfo _animControl;     // static LocalPlayer.AnimControl
        private static FieldInfo _scriptSetup;     // static LocalPlayer.ScriptSetup
        private static FieldInfo _pmControl;       // playerScriptSetup.pmControl
        private static PropertyInfo _activeState;  // PlayMakerFSM.ActiveStateName
        private static MethodInfo _sendEvent;      // PlayMakerFSM.SendEvent(string)
        private static MethodInfo _resetAnimator;  // playerAnimatorControl.resetAnimator()
        private static bool _resolved;

        /// Every frame (PracticeModule.Tick). No allocation after the first
        /// call on an animator.
        public static void Track()
        {
            Animator an = AnimProbe.PlayerAnimator();
            if (an == null) return;
            if (!ReferenceEquals(an, _animator))
            {
                _animator = an;
                _params = an.parameters;
                _restBools = new bool[_params.Length];
                _known = false;
                _clearTriggerAt = -1;
            }

            if (_clearTriggerAt >= 0 && Time.frameCount >= _clearTriggerAt)
            {
                _clearTriggerAt = -1;
                an.ResetTrigger(ResetTriggerHash);
            }

            if (an.layerCount <= FullBody || an.IsInTransition(Arms) || an.IsInTransition(FullBody)) { _settleState = 0; return; }
            AnimatorStateInfo arms = an.GetCurrentAnimatorStateInfo(Arms);
            if (arms.tagHash != TagHeld && arms.tagHash != TagIdling) { _settleState = 0; return; }
            // A smash runs on the full-body layer alone, the arms still
            // "held" (bridge: v0.24.31 learned the smash as the rest). At
            // rest that layer is switched off (weight 0).
            if (an.GetLayerWeight(FullBody) > RestWeight) { _settleState = 0; return; }
            if (arms.fullPathHash != _settleState)
            {
                _settleState = arms.fullPathHash;
                _settleSince = Time.time;
                return;
            }
            if (Time.time - _settleSince < RestSettle) return;
            if (Time.frameCount < _nextSnapshotFrame) return;
            _nextSnapshotFrame = Time.frameCount + 10;

            _restState[Arms] = arms.fullPathHash;
            _restState[FullBody] = an.GetCurrentAnimatorStateInfo(FullBody).fullPathHash;
            for (int i = 0; i < _params.Length; i++)
                _restBools[i] = _params[i].type == AnimatorControllerParameterType.Bool && an.GetBool(_params[i].nameHash);
            _known = true;
        }

        /// Returns "" when it had nothing to report, "ended attack state
        /// 'x'" when it also ended the FSM's attack, else why not (callers
        /// add it to their log line).
        public static string Cancel()
        {
            try
            {
                Animator an = AnimProbe.PlayerAnimator();
                if (an == null) return "animation reset: no player animator";
                if (!ReferenceEquals(an, _animator) || !_known) return GameReset(an);

                // Same hands as at rest: every bool on then is still on.
                bool sameHands = true;
                bool cut = false;
                for (int i = 0; i < _params.Length && sameHands; i++)
                    if (_restBools[i] && !an.GetBool(_params[i].nameHash)) sameHands = false;

                for (int l = Arms; l <= FullBody; l++)
                {
                    AnimatorStateInfo s = an.IsInTransition(l) ? an.GetNextAnimatorStateInfo(l) : an.GetCurrentAnimatorStateInfo(l);
                    bool action = s.tagHash == TagAttacking || s.tagHash == TagSmash || s.tagHash == TagBlock;
                    bool resting = s.tagHash == TagHeld || s.tagHash == TagIdling;
                    bool offRest = s.fullPathHash != _restState[l];
                    // The full-body layer is only in use during an action
                    // (a smash: weight 1, its own tag).
                    bool fullBodyOn = l == FullBody && an.GetLayerWeight(FullBody) > RestWeight;
                    if (action || fullBodyOn || (offRest && (sameHands || !resting)))
                    {
                        an.CrossFadeInFixedTime(_restState[l], Blend, l, 0f);
                        cut = true;
                    }
                }
                for (int i = 0; i < _params.Length; i++)
                {
                    AnimatorControllerParameter p = _params[i];
                    if (p.type == AnimatorControllerParameterType.Trigger) an.ResetTrigger(p.nameHash);
                    else if (p.type == AnimatorControllerParameterType.Bool && !_restBools[i] && an.GetBool(p.nameHash))
                        an.SetBool(p.nameHash, false);
                }
                return cut ? EndAttack() : "";
            }
            catch (Exception ex)
            {
                return "animation reset failed: " + (ex.InnerException ?? ex).Message;
            }
        }

        // The cut animation's FSM side: out of the attack state the way
        // every attack ends. Says what it ended; "" when the FSM was at rest.
        private static string EndAttack()
        {
            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            if (_scriptSetup == null && local != null)
                _scriptSetup = local.GetField("ScriptSetup", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            object setup = _scriptSetup != null ? _scriptSetup.GetValue(null) : null;
            if (setup == null) return "attack state: no player setup";
            if (_pmControl == null)
                _pmControl = setup.GetType().GetField("pmControl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            UnityEngine.Object fsm = _pmControl != null ? _pmControl.GetValue(setup) as UnityEngine.Object : null;
            if (fsm == null) return "attack state: no control FSM";
            if (_sendEvent == null)
            {
                Type t = fsm.GetType();
                _activeState = t.GetProperty("ActiveStateName", BindingFlags.Instance | BindingFlags.Public);
                _sendEvent = t.GetMethod("SendEvent", BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(string) }, null);
            }
            if (_activeState == null || _sendEvent == null) return "attack state: not bound";
            string state = _activeState.GetValue(fsm, null) as string;
            if (state == FsmRest) return "";
            _sendEvent.Invoke(fsm, new object[] { FsmEndAttack });
            return "ended attack state '" + state + "'";
        }

        // Nothing learned for this animator yet: the game's own reset, and
        // its trigger cleared two frames on if nothing consumed it.
        private static string GameReset(Animator an)
        {
            if (!_resolved)
            {
                _resolved = true;
                Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
                Type ctrl = GameBridge.FindGameType("playerAnimatorControl");
                if (local != null) _animControl = local.GetField("AnimControl", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (ctrl != null) _resetAnimator = ctrl.GetMethod("resetAnimator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            }
            if (_animControl == null || _resetAnimator == null) return "animation reset: not bound";
            UnityEngine.Object c = _animControl.GetValue(null) as UnityEngine.Object;
            if (c == null) return "animation reset: no player animator";
            _resetAnimator.Invoke(c, null);
            _clearTriggerAt = Time.frameCount + 2;
            return "";
        }

        /// For the bridge: what Track has learned.
        public static string Describe()
        {
            if (!_known) return "rest not learned yet";
            return "rest: arms " + _restState[Arms] + ", full body " + _restState[FullBody];
        }
    }
}
