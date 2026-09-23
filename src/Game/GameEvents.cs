using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Named game events: the endgame splits, told apart.
    //
    // WHY
    // The author's LiveSplit autosplitter splits on the rising edge of one
    // bool, LocalPlayer.AnimControl.endGameCutScene, which EVERY endgame
    // cutscene sets - so from outside the process the splits (vault door,
    // Timmy, Megan x2, gold keycard doors, game end) cannot be told apart.
    // The flag carries no identity; the call site does.
    //
    // HOW
    // Each cutscene is its own method on the player, started by an
    // activate* trigger with SendMessage("<routine>") (`ilscan strings`).
    // A read-only Harmony postfix on the method notes WHICH cutscene is
    // starting. The split itself fires on the flag's rising edge, polled
    // every frame - the same moment the autosplitter splits, so times stay
    // comparable with LiveSplit. That matters: several routines set the
    // flag only after their first yield, and the keycard doors only after
    // the walk-up to the keypad (playerOpenKeypadDoorAction
    // .lockPlayerParams), so firing at routine start would split early.
    //
    // On each rising edge:
    //   endgame-cutscene              always - exactly the old autosplitter
    //   <the pending hook's event>    e.g. timmy-pickup, megan-transform
    //   keycard-door-<itemId>         for keypad doors, plus the door's
    //                                 name in the log and the Runs tab
    //   red-elevator                  the keycard elevator (see below)
    //   game-end                      for either ending
    //
    // KEYCARD DOORS AND THE RED ELEVATOR share one player action. A keypad
    // door calls openKeypadDoor, which calls openDoorRoutine; the red
    // elevator (ElevatorSystem.Goto) sends "openDoorRoutine" directly. So
    // the postfix sits on openDoorRoutine, and a prefix on openKeypadDoor
    // marks the frame - no mark means the elevator. Confirmed in a real run:
    // the elevator set the flag with nothing pending when only
    // openKeypadDoor was hooked.
    //
    // If the flag cannot be read (a game update renamed it) hooks fire at
    // routine start instead, so events degrade to early rather than never.
    //
    // Adding an event is one line in Hooks. Event names are stable ids
    // used in segment files - renaming one breaks every segment using it.
    // ------------------------------------------------------------------
    public sealed class GameEvents
    {
        public struct Hook
        {
            public string Event;
            public string Label;
            public string Type;
            public string Method;
            /// True for routines that never set endGameCutScene: fire at
            /// routine start rather than wait for a flag that never comes.
            public bool Immediate;

            public Hook(string evt, string label, string type, string method, bool immediate)
            {
                Event = evt; Label = label; Type = type; Method = method; Immediate = immediate;
            }
        }

        public const string AnyCutscene = "endgame-cutscene";
        public const string GameEnd = "game-end";
        public const string KeycardDoor = "keycard-door";
        public const string RedElevator = "red-elevator";

        // Keypad doors named by the keycard they take. Confirmed in a real
        // run (author, 2026-09-22): the vault door is keypadDoor_animate
        // with the Keycard (210); the gold automatic door is
        // ElevatorCardReader with Keycard 2 (242). Keyed on the card, not
        // the object name - "keypadDoor_animate" is a prefab and may repeat.
        private static readonly int[] DoorCards = { 210, 242 };
        private static readonly string[] DoorNames = { "vault-door", "gold-door" };
        private static readonly string[] DoorLabels = { "Vault door (keycard)", "Gold keycard: automatic door" };

        /// Every nameable event in route order, for the segment editor's
        /// picker. keycard-door-<id> is also valid but open-ended.
        public static readonly string[] RouteOrder =
        {
            "vault-door", "timmy-pickup", "megan-transform", "megan-pickup", "megan-to-machine",
            "gold-door", RedElevator, GameEnd, "end-crash", "end-shutdown",
            AnyCutscene, KeycardDoor, "timmy-goodbye", "raft-out-of-world",
        };

        // In route order. Table in docs/game-notes.md. The Megan labels
        // were confirmed against a real endgame run - an earlier guess had
        // the two swapped.
        public static readonly Hook[] Hooks =
        {
            new Hook(KeycardDoor, "Keycard door (vault, gold automatic door)",
                     "playerOpenKeypadDoorAction", "openDoorRoutine", false),
            new Hook("timmy-pickup", "Finding Timmy (artifact)",
                     "TheForest.Player.Actions.PlayerPickupTimmyAction", "pickupTimmyRoutine", false),
            new Hook("megan-transform", "Approaching Megan (she transforms)",
                     "TheForest.Player.Actions.PlayerGirlTransformAction", "doGirlTransformRoutine", false),
            new Hook("megan-pickup", "Picking Megan up after the fight (no cutscene flag)",
                     "TheForest.Player.Actions.PlayerGirlPickupAction", "pickupGirlRoutine", true),
            new Hook("megan-to-machine", "Putting Megan in the artifact",
                     "TheForest.Player.Actions.PlayerGirlPickupAction", "girlToMachineRoutine", false),
            new Hook("end-crash", "Game end: plane crash",
                     "TheForest.Player.Actions.PlayerEndCrashAction", "doEndPlaneCrashRoutine", false),
            new Hook("end-shutdown", "Game end: artifact shut down",
                     "TheForest.Player.Actions.PlayerEndCrashAction", "doShutDownRoutine", false),
            new Hook("timmy-goodbye", "Goodbye Timmy",
                     "PlayerGoodbyeTimmyAction", "goodbyeTimmyRoutine", false),
            new Hook("raft-out-of-world", "Raft: out of world",
                     "RaftPush", "outOfWorldRoutine", false),
        };


        // A cutscene that has started but whose flag has not risen yet
        // expires after this long, so a stale identity is never pinned on
        // some unrelated later cutscene.
        private const float PendingTimeout = 30f;

        // Static: a Harmony postfix is a static method with no instance.
        // Keyed on "Type::Method" rather than the MethodBase: the instance
        // Harmony hands the postfix need not be the one we patched, and
        // reflection equality across them is not something to bet a split
        // on. The string is only built when a hook fires, which is rare.
        private static readonly Dictionary<string, int> ByMethod = new Dictionary<string, int>();
        private static readonly List<string> Names = new List<string>();
        private static readonly List<string> Stamps = new List<string>();
        private static ManualLogSource _log;

        private static string _pendingEvent;
        private static string _pendingDetail;
        private static int _pendingKeycard;
        private static float _pendingAt;
        private static bool _flagAvailable;

        private static FieldInfo _keycardIdField;
        private static FieldInfo _shortSequenceField;

        // Frame in which openKeypadDoor was entered, and whether it is
        // still on the stack - see KeypadPrefix.
        private static int _keypadFrame = -1;
        private static bool _inKeypad;

        private Harmony _harmony;

        // Flag polling.
        private FieldInfo _animControlField;
        private FieldInfo _endGameField;
        private bool _flagResolved;
        private bool _lastFlag;

        /// "8/9 hooks" or why none were installed. Shown in the Runs tab.
        public string Status { get; private set; }

        public int Installed { get; private set; }

        /// The endgame cutscene running now - its event, or AnyCutscene when
        /// no known routine started it - or null. With when it began
        /// (Time.time: the cutscene runs in game time) and how many have
        /// begun this session, for savestates taken during one.
        public string CutsceneRunning { get; private set; }
        public float CutsceneStartedAt { get; private set; }
        public int CutsceneStarts { get; private set; }

        public GameEvents(ManualLogSource log)
        {
            _log = log;
            Status = "not installed";
        }

        /// Events fired this session, oldest first. Consumers remember how
        /// many they have seen.
        public int Count { get { return Names.Count; } }
        public string NameAt(int i) { return Names[i]; }

        /// Wall-clock time of an event, "HH:mm:ss", for the UI.
        public string StampAt(int i) { return Stamps[i]; }

        /// The door behind the last keypad event, for the Runs tab.
        public static string LastDoor { get; private set; }

        public static string LabelFor(string evt)
        {
            for (int i = 0; i < Hooks.Length; i++)
                if (string.Equals(Hooks[i].Event, evt, StringComparison.OrdinalIgnoreCase)) return Hooks[i].Label;
            if (string.Equals(evt, AnyCutscene, StringComparison.OrdinalIgnoreCase))
                return "Any endgame cutscene (what the autosplitter used)";
            if (string.Equals(evt, GameEnd, StringComparison.OrdinalIgnoreCase))
                return "Game end (either ending)";
            if (string.Equals(evt, RedElevator, StringComparison.OrdinalIgnoreCase))
                return "Gold keycard: red elevator";
            for (int i = 0; i < DoorNames.Length; i++)
                if (string.Equals(evt, DoorNames[i], StringComparison.OrdinalIgnoreCase)) return DoorLabels[i];
            if (evt != null && evt.StartsWith(KeycardDoor + "-", StringComparison.OrdinalIgnoreCase))
                return "Keycard door opened with item " + evt.Substring(KeycardDoor.Length + 1);
            return null;
        }

        // ------------------------------------------------------------------
        public void Install(string harmonyId)
        {
            ResolveFlag();

            try
            {
                _harmony = new Harmony(harmonyId);
            }
            catch (Exception ex)
            {
                Status = "Harmony unavailable: " + ex.Message;
                _log.LogError("GameEvents: " + Status);
                return;
            }

            MethodInfo postfix = typeof(GameEvents).GetMethod("Postfix",
                BindingFlags.Static | BindingFlags.NonPublic);
            HarmonyMethod hm = new HarmonyMethod(postfix);

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                 BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (int i = 0; i < Hooks.Length; i++)
            {
                Hook h = Hooks[i];
                Type t = GameBridge.FindGameType(h.Type);
                MethodInfo target = null;

                // By name, whatever the parameters: several take a Transform
                // or Vector3 we do not care about.
                if (t != null)
                {
                    MethodInfo[] methods = t.GetMethods(flags);
                    for (int m = 0; m < methods.Length; m++)
                        if (methods[m].Name == h.Method) { target = methods[m]; break; }
                }

                if (target == null)
                {
                    _log.LogWarning("GameEvents: " + h.Type + "." + h.Method +
                                    " not found - '" + h.Event + "' will never fire.");
                    continue;
                }

                if (h.Event == KeycardDoor)
                {
                    _keycardIdField = t.GetField("_keycardId", flags);
                    _shortSequenceField = t.GetField("shortSequence", flags);
                    PatchKeypadMarker(t, flags);
                }

                try
                {
                    ByMethod[Key(target)] = i;
                    _harmony.Patch(target, null, hm);
                    Installed++;
                }
                catch (Exception ex)
                {
                    _log.LogWarning("GameEvents: could not hook " + h.Type + "." + h.Method + ": " + ex.Message);
                }
            }

            Status = Installed + "/" + Hooks.Length + " hooks" +
                     (_flagAvailable ? "" : ", NO cutscene flag - splitting at routine start");
            _log.LogInfo("GameEvents: " + Status + " installed.");
        }

        // A keypad door enters through openKeypadDoor; the red elevator does
        // not. Mark the call so the openDoorRoutine postfix can tell them
        // apart. Without the marker every keypad reads as the elevator, so
        // failure is logged loudly.
        private void PatchKeypadMarker(Type t, BindingFlags flags)
        {
            MethodInfo entry = t.GetMethod("openKeypadDoor", flags);
            if (entry == null)
            {
                _log.LogWarning("GameEvents: openKeypadDoor not found - keycard doors cannot be told from the red elevator.");
                return;
            }

            try
            {
                _harmony.Patch(entry,
                    new HarmonyMethod(typeof(GameEvents).GetMethod("KeypadPrefix", BindingFlags.Static | BindingFlags.NonPublic)),
                    new HarmonyMethod(typeof(GameEvents).GetMethod("KeypadPostfix", BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception ex)
            {
                _log.LogWarning("GameEvents: could not mark openKeypadDoor: " + ex.Message);
            }
        }

        private static void KeypadPrefix()
        {
            _inKeypad = true;
            _keypadFrame = Time.frameCount;
        }

        private static void KeypadPostfix()
        {
            _inKeypad = false;
        }

        private void ResolveFlag()
        {
            if (_flagResolved) return;
            _flagResolved = true;

            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type anim = GameBridge.FindGameType("playerAnimatorControl");
            if (local != null)
                _animControlField = local.GetField("AnimControl", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (anim != null)
                _endGameField = anim.GetField("endGameCutScene", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            _flagAvailable = _animControlField != null && _endGameField != null;
            if (!_flagAvailable)
                _log.LogWarning("GameEvents: LocalPlayer.AnimControl.endGameCutScene not found - " +
                                "endgame events will fire at routine start instead of on the flag.");
        }

        /// Once per frame, before modules tick: polls the cutscene flag and
        /// fires the split on its rising edge.
        public void Tick()
        {
            if (!_flagAvailable) return;

            bool flag = false;
            try
            {
                object anim = _animControlField.GetValue(null);
                // A destroyed component (save load) reads as not in a cutscene.
                UnityEngine.Object uo = anim as UnityEngine.Object;
                if (anim != null && uo != null) flag = (bool)_endGameField.GetValue(anim);
            }
            catch (Exception) { flag = false; }

            bool rising = flag && !_lastFlag;
            _lastFlag = flag;
            if (!flag) CutsceneRunning = null;
            if (!rising) return;

            bool known = _pendingEvent != null && Time.unscaledTime - _pendingAt <= PendingTimeout;
            CutsceneRunning = known ? _pendingEvent : AnyCutscene;
            CutsceneStartedAt = Time.time;
            CutsceneStarts++;

            Record(AnyCutscene, null);

            if (known)
                Fire(_pendingEvent, _pendingDetail, _pendingKeycard);
            else
                _log.LogInfo("Game event: endgame cutscene with no known routine pending.");

            _pendingEvent = null;
            _pendingDetail = null;
        }

        // Read-only: notes which cutscene started. Never throws into the game.
        private static void Postfix(object __instance, object[] __args, MethodBase __originalMethod)
        {
            try
            {
                int hook;
                if (__originalMethod == null || !ByMethod.TryGetValue(Key(__originalMethod), out hook)) return;

                string evt = Hooks[hook].Event;
                string detail = null;
                int keycard = 0;

                if (evt == KeycardDoor)
                {
                    // In the same frame and still inside openKeypadDoor:
                    // a door. Otherwise the elevator sent it directly. The
                    // frame check stops a marker left set by an exception
                    // from misfiling a later elevator.
                    bool viaKeypad = _inKeypad && _keypadFrame == Time.frameCount;
                    if (!viaKeypad) evt = RedElevator;

                    if (_keycardIdField != null && __instance != null)
                        keycard = (int)_keycardIdField.GetValue(__instance);

                    bool shortSeq = false;
                    if (_shortSequenceField != null && __instance != null)
                        shortSeq = (bool)_shortSequenceField.GetValue(__instance);

                    Transform pos = __args != null && __args.Length > 0 ? __args[0] as Transform : null;
                    detail = (viaKeypad ? "door '" : "elevator '") + PathOf(pos) + "', keycard " + keycard +
                             (shortSeq ? ", short sequence" : "");
                    LastDoor = detail;
                }

                if (Hooks[hook].Immediate || !_flagAvailable)
                {
                    Fire(evt, detail, keycard);
                    return;
                }

                _pendingEvent = evt;
                _pendingDetail = detail;
                _pendingKeycard = keycard;
                _pendingAt = Time.unscaledTime;
            }
            catch (Exception) { }
        }

        private static void Fire(string evt, string detail, int keycard)
        {
            Record(evt, detail);

            if (evt == KeycardDoor && keycard > 0)
            {
                Record(KeycardDoor + "-" + keycard, null);
                for (int i = 0; i < DoorCards.Length; i++)
                    if (DoorCards[i] == keycard) Record(DoorNames[i], null);
            }
            if (evt == "end-crash" || evt == "end-shutdown") Record(GameEnd, null);
        }

        private static void Record(string evt, string detail)
        {
            Names.Add(evt);
            Stamps.Add(DateTime.Now.ToString("HH:mm:ss"));
            if (_log != null)
                _log.LogInfo("Game event: " + evt + (detail != null ? " (" + detail + ")" : "") +
                             " frame " + Time.frameCount);
        }

        // Up to three levels - enough to tell doors apart in a log.
        private static string PathOf(Transform t)
        {
            if (t == null) return "?";
            string path = t.name;
            Transform p = t.parent;
            for (int i = 0; i < 2 && p != null; i++, p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private static string Key(MethodBase m)
        {
            return m.DeclaringType.FullName + "::" + m.Name;
        }

        public void Uninstall()
        {
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }
    }
}
