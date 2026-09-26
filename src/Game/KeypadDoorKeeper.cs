using System;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Replays a keypad door's cutscene after a restore of a capture taken
    // during it (author, v0.24.81: "make sure the same case still works for
    // the gold / vault door"; the red elevator's is ElevatorKeeper).
    //
    // WHY (bridge + IL, v0.24.81): activateKeypadDoor (vault door
    // EndgameEntrance/keypadDoor_animate/doorTrigger, gold door
    // Sections/ArtifactRoom/ElevatorCardReader/Trigger, the yacht's panel)
    // starts the cutscene on "Take" (Update: `sequence.BeginStage(0)`, else
    // DoActorAnimation + DoEnvironmentAnimation): the player's
    // openKeypadDoor -> openDoorRoutine, and DoLateCompletion, which sets
    // `doorOpen` and animates the door open, with Finished 6 s later. None
    // of the running cutscene is in the save: a Quick load came back with
    // the door open, `doorOpen` set and nothing running (the log's "no
    // cutscene began"). The replay closes the door as Awake leaves it
    // ("Base Layer.closed", `doorOpen` false), stands the player at its
    // `playerPos` and presses it again; the cutscene fast-forward then
    // takes it to the captured moment.
    //
    // WHAT: capture writes `keypaddoor = <path of the activateKeypadDoor>`
    // (the door whose playerPos the running openDoorRoutine was given,
    // GameEvents.LastDoorPos); a restore of a cutscene capture replays it.
    // ------------------------------------------------------------------
    internal sealed class KeypadDoorKeeper
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly int ClosedHash = Animator.StringToHash("Base Layer.closed");
        private readonly ManualLogSource _log;
        private Type _type;
        private FieldInfo _playerPos, _doorOpen, _doorAnimator, _sequence;
        private MethodInfo _actor, _environment;

        public KeypadDoorKeeper(ManualLogSource log) { _log = log; }

        private bool Bind()
        {
            if (_type != null) return _playerPos != null;
            _type = GameBridge.FindGameType("activateKeypadDoor");
            if (_type == null) { _type = typeof(KeypadDoorKeeper); return false; }
            _playerPos = _type.GetField("playerPos", Inst);
            _doorOpen = _type.GetField("doorOpen", Inst);
            _doorAnimator = _type.GetField("doorAnimator", Inst);
            _sequence = _type.GetField("sequence", Inst);
            _actor = _type.GetMethod("DoActorAnimation", Inst, null, Type.EmptyTypes, null);
            _environment = _type.GetMethod("DoEnvironmentAnimation", Inst, null, Type.EmptyTypes, null);
            if (_playerPos == null) _log.LogWarning("KeypadDoorKeeper: activateKeypadDoor.playerPos not found - door cutscenes are not replayed.");
            return _playerPos != null;
        }

        /// The header value at capture: the door whose `playerPos` is
        /// `doorPos`, or "".
        public string Capture(Transform doorPos)
        {
            try
            {
                if (doorPos == null || !Bind()) return "";
                Component c = FindBy(delegate(Component d) { return ReferenceEquals(_playerPos.GetValue(d), doorPos); });
                return c != null ? ObjectProbe.PathOf(c.transform) : "";
            }
            catch (Exception ex) { _log.LogWarning("KeypadDoorKeeper: capture failed: " + ex.Message); return ""; }
        }

        public Component Find(string path)
        {
            if (string.IsNullOrEmpty(path) || !Bind()) return null;
            return FindBy(delegate(Component d) { return ObjectProbe.PathOf(d.transform) == path; });
        }

        /// Where the door's cutscene stands the player.
        public bool Stand(Component door, out Vector3 at)
        {
            Transform t = door != null && _playerPos != null ? _playerPos.GetValue(door) as Transform : null;
            at = t != null ? t.position : Vector3.zero;
            return t != null;
        }

        /// The door closed as Awake leaves it, then pressed as Update does.
        public string Replay(Component door)
        {
            if (door == null) return "the door is not loaded - not replayed";
            try
            {
                MonoBehaviour mb = door as MonoBehaviour;
                if (mb != null) mb.CancelInvoke();   // a pending Finished from an earlier opening
                if (_doorOpen != null) _doorOpen.SetValue(door, false);
                Animator anim = _doorAnimator != null ? _doorAnimator.GetValue(door) as Animator : null;
                if (anim != null)
                {
                    anim.enabled = true;
                    anim.SetBool("open", false);
                    anim.SetBool("close", false);
                    if (anim.HasState(0, ClosedHash)) anim.CrossFade(ClosedHash, 0f, 0, 1f);
                }

                object seq = _sequence != null ? _sequence.GetValue(door) : null;
                MethodInfo begin = seq as UnityEngine.Object != null
                    ? seq.GetType().GetMethod("BeginStage", Inst, null, new[] { typeof(int) }, null) : null;
                if (begin != null) begin.Invoke(seq, new object[] { 0 });
                else if (_actor != null && _environment != null)
                {
                    _actor.Invoke(door, null);
                    _environment.Invoke(door, null);
                }
                else return "no way to start it - not replayed";
                return "door '" + ObjectProbe.PathOf(door.transform) + "' closed and opened again";
            }
            catch (Exception ex) { return "replay failed (" + ex.Message + ")"; }
        }

        private delegate bool Match(Component door);

        private Component FindBy(Match match)
        {
            UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(_type);
            for (int i = 0; i < all.Length; i++)
            {
                Component c = all[i] as Component;
                if (c != null && match(c)) return c;
            }
            return null;
        }
    }
}
