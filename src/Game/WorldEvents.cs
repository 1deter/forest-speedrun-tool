using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The rest of the author's LiveSplit autosplitter, as named events.
    //
    // WHY
    // GameEvents covers the endgame cutscenes. The autosplitter
    // (1deter/auto-splitters, The Forest.ASL) also splits on caves (enter /
    // exit, per cave), clothing put on, the found-passenger count, and
    // starts on a hold-to-interact press (the plane meal) or on moving.
    // Each becomes an event, so a segment can split on exactly what a
    // LiveSplit run splits on and the two compare.
    //
    // HOW
    // Polled once a frame, before modules tick, from the same fields the
    // ASL reads - so a split lands in the same frame:
    //   cave-enter-<cave> / cave-exit-<cave>   ActiveAreaInfo._currentCave
    //   cave-enter / cave-exit                 any cave (also fired)
    //   clothing-<id>                          an id new in
    //                                          PlayerClothing._wornClothingItems
    //   passenger-<n> / passenger              PassengerManifest
    //                                          ._foundPassengersIdsCount rose to n
    //   hold-interact                          Input.DelayedActionIsDown rose
    //                                          (the ASL's plane meal start)
    //   moving                                 speed > 0.15 m/s after standing
    //                                          still (the ASL's velocity start)
    //   first-input                            any button or movement after 0.25 s
    //                                          with none (the rules' "takes control";
    //                                          not while a cursor shows - menus, the
    //                                          overlay window) (v0.24.193)
    //   rope-grab / rope-leave                 a cave rope climb begins / ends
    //                                          (playerAnimatorControl.onRope)
    //   <ride>-start / <ride>-end, and         zipline, sled, glider, cliff climb
    //   ride-start / ride-end                  (RideModes.Current, read at 10 Hz
    //                                          only for 3 s after a ride's enter /
    //                                          exit method ran - AuditWatch marks
    //                                          it; not within 1.5 s of a placement)
    // The game's own event bus (built, crafted, eaten, kills, story ...) is
    // raised from Game/AuditWatch's one postfix; names in Data/BusEvents.
    // <cave> is the game's CaveNames value in lower case (cave01 ...
    // cave10, hellcave, snowcave, underwatercave, underwatercave2/3).
    // NotInCaves is the surface AND the endgame lab: the ASL never splits
    // on the lab, and neither does this.
    //
    // Unlike the ASL, a cave-to-cave change (no surface between) fires the
    // exit and the enter - a superset, the same frame.
    //
    // A new component (a save load) re-reads the state without firing, so
    // loading a save in a cave is not an "enter". Teleports and restores
    // can change the cave; a run arms after its spot is placed, so those
    // events are skipped (PracticeRunModule.ArmRun).
    //
    // moving and hold-interact are not logged (they would fill the log);
    // the rest log a `Game event:` line like the endgame ones.
    // ------------------------------------------------------------------
    public sealed class WorldEvents
    {
        public const string CaveEnter = "cave-enter";
        public const string CaveExit = "cave-exit";
        public const string Clothing = "clothing";
        public const string Passenger = "passenger";
        public const string HoldInteract = "hold-interact";
        public const string Moving = "moving";
        public const string FirstInput = "first-input";
        public const string RopeGrab = "rope-grab";
        public const string RopeLeave = "rope-leave";

        /// The ASL's velocity start: overall speed above this.
        public const float MovingSpeed = 0.15f;
        /// Still for this long before `moving` can fire again.
        private const float StillTime = 0.25f;

        /// CaveNames order (Cave01 = 0, NotInCaves = -1), with the ASL's
        /// labels. The enum is read by name, so this is display only.
        /// The cave ids, in the game's order (the editor's cave picker).
        public static string[] CaveIdList { get { return (string[])CaveIds.Clone(); } }

        /// "Cave 6 - Lawyer Cave" for "cave06"; null when unknown.
        public static string CaveLabel(string id)
        {
            int at = Array.IndexOf(CaveIds, id);
            return at >= 0 ? CaveLabels[at] : null;
        }

        private static readonly string[] CaveIds =
        {
            "cave01", "cave02", "cave03", "cave04", "cave05", "cave06", "cave07", "cave08", "cave09", "cave10",
            "hellcave", "snowcave", "underwatercave", "underwatercave2", "underwatercave3",
        };
        private static readonly string[] CaveLabels =
        {
            "Cave 1 - Dead Cave", "Cave 2 - Hanging Cave", "Cave 3 - Wet Cave", "Cave 4 - Baby Cave",
            "Cave 5 - Submerged Cave", "Cave 6 - Lawyer Cave", "Cave 7 - Chasm Cave", "Cave 8 - Sinkhole Cave",
            "Cave 9 - Ledge Cave", "Cave 10 - Waterfall Cave", "Hell Cave", "Snow Cave",
            "Underwater Cave 1 (blueprint cave)", "Underwater Cave 2 (blueprint cave)", "Underwater Cave 3 (blueprint cave)",
        };

        private readonly ManualLogSource _log;
        private bool _resolved;
        private string _status = "not resolved";

        private FieldInfo _areaInfo, _cave;               // LocalPlayer.ActiveAreaInfo._currentCave
        private FieldInfo _clothing, _worn;               // LocalPlayer.Clothing._wornClothingItems
        private FieldInfo _manifest, _found;              // LocalPlayer.PassengerManifest._foundPassengersIdsCount
        private FieldInfo _delayedDown;                   // Input.DelayedActionIsDown (static)
        private FieldInfo _finishLoad;                    // Scene.FinishGameLoad (static)

        // CaveNames value -> CaveIds index, built from the enum's names.
        private readonly Dictionary<int, int> _caveIndex = new Dictionary<int, int>();

        private UnityEngine.Object _areaOwner, _clothingOwner, _manifestOwner;
        private string _lastCave;                         // null = surface / unknown
        private readonly List<int> _lastWorn = new List<int>();
        private int _lastFound = -1;
        private bool _lastDelayed;
        private float _stillSince = -1f;
        private bool _movingFired = true;
        private Vector3 _lastPos;
        private bool _hasLastPos;
        private bool _jumped;                             // a placement this frame

        private MethodInfo _getAxis;                      // static Input.GetAxis(string)
        private FieldInfo _rewiredPlayer;                 // static Input.player (Rewired.Player)
        private MethodInfo _anyButton;                    // Player.GetAnyButton()
        // Bound once (the player object again if it changes): Invoke boxed
        // every frame's answers.
        private Func<string, float> _axis;
        private Func<bool> _any;
        private object _anyFor;
        private float _idleSince = -1f;
        private bool _inputFired = true;
        private int _rope = -1;                           // -1 unknown, 0 off, 1 on
        private string _ride;                             // null = not read since a load
        private bool _rideWasDirty;
        private float _nextRideRead, _lastJumpAt = -10f;

        private const float RideReadEvery = 0.1f;
        /// A ride change this soon after a placement is the placement's
        /// (Go / tp end a ride, a restore puts one back), not the runner's.
        private const float RideAfterJump = 1.5f;

        /// The game is loaded and in play (not the title screen, not a
        /// load) as of this frame's Tick - AuditWatch raises the bus's
        /// events only then.
        public static bool Live { get; private set; }

        /// A move this far in one frame is a placement (teleport, restore,
        /// restart), not running (v0.24.188).
        private const float JumpDistance = 2f;

        public WorldEvents(ManualLogSource log)
        {
            _log = log;
        }

        /// "caves, clothing, passengers, starts" or what is missing.
        public string Status { get { return _status; } }

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type area = GameBridge.FindGameType("TheForest.Player.ActiveAreaInfo");
            Type clothing = GameBridge.FindGameType("TheForest.Player.Clothing.PlayerClothing");
            Type input = GameBridge.FindGameType("TheForest.Utils.Input");
            Type scene = GameBridge.FindGameType("TheForest.Utils.Scene");

            if (local != null)
            {
                _areaInfo = local.GetField("ActiveAreaInfo", any);
                _clothing = local.GetField("Clothing", any);
                _manifest = local.GetField("PassengerManifest", any);
            }
            if (area != null) _cave = area.GetField("_currentCave", any);
            if (_cave != null && _cave.FieldType.IsEnum)
            {
                Array values = Enum.GetValues(_cave.FieldType);
                for (int i = 0; i < values.Length; i++)
                {
                    object v = values.GetValue(i);
                    int at = Array.IndexOf(CaveIds, v.ToString().ToLowerInvariant());
                    if (at >= 0) _caveIndex[Convert.ToInt32(v)] = at;
                }
            }
            if (clothing != null) _worn = clothing.GetField("_wornClothingItems", any);
            if (_manifest != null) _found = _manifest.FieldType.GetField("_foundPassengersIdsCount", any);
            if (input != null) _delayedDown = input.GetField("DelayedActionIsDown", any);
            if (input != null)
            {
                _getAxis = input.GetMethod("GetAxis", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
                _rewiredPlayer = input.GetField("player", any);
                if (_rewiredPlayer != null) _anyButton = _rewiredPlayer.FieldType.GetMethod("GetAnyButton", Type.EmptyTypes);
            }
            if (scene != null) _finishLoad = scene.GetField("FinishGameLoad", any);

            List<string> missing = new List<string>();
            if (_areaInfo == null || _cave == null || _caveIndex.Count != CaveIds.Length) missing.Add("caves");
            if (_clothing == null || _worn == null) missing.Add("clothing");
            if (_manifest == null || _found == null) missing.Add("passengers");
            if (_delayedDown == null) missing.Add(HoldInteract);
            if (_getAxis == null || _anyButton == null) missing.Add(FirstInput);
            _status = missing.Count == 0 ? "caves, clothing, passengers, starts"
                                         : "missing: " + string.Join(", ", missing.ToArray());
            _log.LogInfo("WorldEvents: " + _status + ".");
        }

        /// Once a frame, after GameEvents.Tick, before modules tick.
        public void Tick(PlayerRef player)
        {
            Resolve();
            Live = false;
            if (PlayerRef.AtTitleScreen) { Forget(); return; }
            if (_finishLoad != null)
            {
                bool loaded;
                try { loaded = (bool)_finishLoad.GetValue(null); }
                catch (Exception) { loaded = true; }
                if (!loaded) { Forget(); return; }
            }
            Live = true;

            CheckJump(player);
            try { PollCave(); } catch (Exception) { }
            try { PollClothing(); } catch (Exception) { }
            try { PollPassengers(); } catch (Exception) { }
            try { PollHold(); } catch (Exception) { }
            PollMoving(player);
            try { PollInput(); } catch (Exception) { }
            try { PollRope(); } catch (Exception) { }
            try { PollRide(); } catch (Exception) { }
        }

        // Title screen / loading: the next reading is a baseline.
        private void Forget()
        {
            _areaOwner = null;
            _clothingOwner = null;
            _manifestOwner = null;
            _lastDelayed = false;
            _movingFired = true;
            _stillSince = -1f;
            _hasLastPos = false;
            _inputFired = true;
            _idleSince = -1f;
            _rope = -1;
            _ride = null;
            _rideWasDirty = false;
        }

        private void PollCave()
        {
            if (_areaInfo == null || _cave == null) return;
            UnityEngine.Object owner = _areaInfo.GetValue(null) as UnityEngine.Object;
            if (owner == null) { _areaOwner = null; return; }

            object v = _cave.GetValue(owner);
            int at;
            string now = _caveIndex.TryGetValue(Convert.ToInt32(v), out at) ? CaveIds[at] : null;

            if (!ReferenceEquals(owner, _areaOwner))
            {
                _areaOwner = owner;
                _lastCave = now;
                return;
            }
            if (now == _lastCave) return;

            string was = _lastCave;
            _lastCave = now;
            // A teleport / restore changed it (v0.24.193: a Go sets the
            // spot's cave): not walking through a cave mouth.
            if (_jumped) return;
            if (was != null)
            {
                GameEvents.RecordWorld(CaveExit + "-" + was, null, true);
                GameEvents.RecordWorld(CaveExit, was, true, true);
            }
            if (now != null)
            {
                GameEvents.RecordWorld(CaveEnter + "-" + now, null, true);
                GameEvents.RecordWorld(CaveEnter, now, true, true);
            }
        }

        private void PollClothing()
        {
            if (_clothing == null || _worn == null) return;
            UnityEngine.Object owner = _clothing.GetValue(null) as UnityEngine.Object;
            if (owner == null) { _clothingOwner = null; return; }

            List<int> worn = _worn.GetValue(owner) as List<int>;
            if (worn == null) return;

            bool baseline = !ReferenceEquals(owner, _clothingOwner);
            _clothingOwner = owner;

            if (!baseline)
            {
                for (int i = 0; i < worn.Count; i++)
                {
                    int id = worn[i];
                    if (_lastWorn.Contains(id)) continue;
                    GameEvents.RecordWorld(Clothing + "-" + id, ClothingName(id), true);
                }
            }

            if (baseline || !SameItems(worn, _lastWorn))
            {
                _lastWorn.Clear();
                _lastWorn.AddRange(worn);
            }
        }

        private static bool SameItems(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private void PollPassengers()
        {
            if (_manifest == null || _found == null) return;
            UnityEngine.Object owner = _manifest.GetValue(null) as UnityEngine.Object;
            if (owner == null) { _manifestOwner = null; return; }

            int found = (int)_found.GetValue(owner);
            if (!ReferenceEquals(owner, _manifestOwner))
            {
                _manifestOwner = owner;
                _lastFound = found;
                return;
            }
            if (found == _lastFound) return;

            bool rose = found > _lastFound;
            _lastFound = found;
            if (!rose) return;
            GameEvents.RecordWorld(Passenger + "-" + found, null, true);
            GameEvents.RecordWorld(Passenger, found.ToString(), true, true);
        }

        private void PollHold()
        {
            if (_delayedDown == null) return;
            bool down = (bool)_delayedDown.GetValue(null);
            if (down && !_lastDelayed) GameEvents.RecordWorld(HoldInteract, null, false);
            _lastDelayed = down;
        }

        // A move this far in one frame is a placement; read before the polls.
        private void CheckJump(PlayerRef player)
        {
            _jumped = false;
            if (player == null || !player.Found) return;
            Vector3 pos = player.Transform.position;
            _jumped = _hasLastPos && (pos - _lastPos).sqrMagnitude > JumpDistance * JumpDistance;
            _lastPos = pos;
            _hasLastPos = true;
        }

        private void PollMoving(PlayerRef player)
        {
            if (player == null || !player.Found) return;
            float now = Time.unscaledTime;

            // A placement settles for a frame or two (bridge: -0.87 m/s
            // after a restart from a cave) - that is not the runner moving,
            // and it started a velocity-start run on its own restart. After
            // a jump the player must stand still again first.
            if (_jumped) { _movingFired = true; _stillSince = -1f; return; }

            if (player.Speed <= MovingSpeed)
            {
                if (_stillSince < 0f) _stillSince = now;
                if (now - _stillSince >= StillTime) _movingFired = false;
                return;
            }
            _stillSince = -1f;
            if (_movingFired) return;
            _movingFired = true;
            GameEvents.RecordWorld(Moving, null, false);
        }

        // The first button or movement after a pause: what the rules call
        // taking control. A cursor on screen (a menu, the overlay window)
        // means the input is not the game's - and the overlay's clicks
        // must not start a run.
        private void PollInput()
        {
            if (_getAxis == null || _anyButton == null) return;
            float now = Time.unscaledTime;
            if (_jumped || Cursor.visible) { _inputFired = true; _idleSince = -1f; return; }

            if (_axis == null) _axis = (Func<string, float>)Delegate.CreateDelegate(typeof(Func<string, float>), _getAxis);
            object p = _rewiredPlayer.GetValue(null);
            if (p != null && !ReferenceEquals(p, _anyFor))
            {
                _anyFor = p;
                _any = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), p, _anyButton);
            }
            bool active = p != null && _any != null && _any();
            if (!active) active = Mathf.Abs(_axis("Horizontal")) > 0.1f || Mathf.Abs(_axis("Vertical")) > 0.1f;
            if (!active)
            {
                if (_idleSince < 0f) _idleSince = now;
                if (now - _idleSince >= StillTime) _inputFired = false;
                return;
            }
            _idleSince = -1f;
            if (_inputFired) return;
            _inputFired = true;
            GameEvents.RecordWorld(FirstInput, null, false);
        }

        private void PollRope()
        {
            int on = RopeClimb.IsOnRope() ? 1 : 0;
            if (_rope >= 0 && on != _rope) GameEvents.RecordWorld(on == 1 ? RopeGrab : RopeLeave, null, true);
            _rope = on;
        }

        // Read once after a load, then only while a ride's enter / exit
        // method ran in the last 3 s (and once after): the zipline's flag
        // is set later, in its StickToZipLine routine.
        private void PollRide()
        {
            float now = Time.unscaledTime;
            if (_jumped) _lastJumpAt = now;
            bool dirty = now < AuditWatch.RideDirtyUntil;
            if (_ride != null)
            {
                if (!dirty && !_rideWasDirty) return;
                _rideWasDirty = dirty;
                if (dirty && now < _nextRideRead) return;
            }
            _nextRideRead = now + RideReadEvery;

            string ride = RideModes.Current() ?? "";
            if (_ride == null) { _ride = ride; return; }
            if (ride == _ride) return;
            string was = _ride;
            _ride = ride;
            if (now - _lastJumpAt < RideAfterJump || PlayerRef.JustPlaced) return;

            if (was.Length > 0)
            {
                GameEvents.RecordWorld(BusEvents.RideEvent(was, false), null, true);
                GameEvents.RecordWorld(BusEvents.RideEnd, was, false, true);
            }
            if (ride.Length > 0)
            {
                GameEvents.RecordWorld(BusEvents.RideEvent(ride, true), null, true);
                GameEvents.RecordWorld(BusEvents.RideStart, ride, false, true);
            }
        }

        // ------------------------------------------------------------------
        // Names for the segment editor.

        /// Every cave event, in cave order: enter, exit per cave; then any.
        public static string[] CaveEvents()
        {
            List<string> all = new List<string>();
            for (int i = 0; i < CaveIds.Length; i++)
            {
                all.Add(CaveEnter + "-" + CaveIds[i]);
                all.Add(CaveExit + "-" + CaveIds[i]);
            }
            all.Add(CaveEnter);
            all.Add(CaveExit);
            return all.ToArray();
        }

        public static string[] PassengerEvents()
        {
            string[] all = new string[45];
            all[0] = Passenger;
            for (int n = 1; n <= 44; n++) all[n] = Passenger + "-" + n;
            return all;
        }

        /// clothing-<id> for every item in the game's clothing database.
        public static string[] ClothingEvents()
        {
            List<string> all = new List<string>();
            try
            {
                Array items = ClothingItems();
                if (items != null)
                    for (int i = 0; i < items.Length; i++)
                    {
                        int id = ClothingId(items.GetValue(i));
                        if (id > 0) all.Add(Clothing + "-" + id);
                    }
            }
            catch (Exception) { }
            return all.ToArray();
        }

        public static string LabelFor(string evt)
        {
            if (evt == null) return null;
            string e = evt.ToLowerInvariant();

            if (e == CaveEnter) return "Entered any cave";
            if (e == CaveExit) return "Left any cave";
            for (int i = 0; i < CaveIds.Length; i++)
            {
                if (e == CaveEnter + "-" + CaveIds[i]) return "Entered " + CaveLabels[i];
                if (e == CaveExit + "-" + CaveIds[i]) return "Left " + CaveLabels[i];
            }

            if (e == Passenger) return "Found a passenger (any)";
            if (e.StartsWith(Passenger + "-"))
            {
                int n;
                if (int.TryParse(e.Substring(Passenger.Length + 1), out n) && n > 0)
                    return "Found passenger number " + n + " (the found count reaching " + n + ")";
            }

            if (e.StartsWith(Clothing + "-"))
            {
                int id;
                if (int.TryParse(e.Substring(Clothing.Length + 1), out id))
                {
                    string name = ClothingName(id);
                    return "Put on " + (name ?? "clothing item " + id);
                }
            }

            if (e == "autosplit") return "The next autosplit: whichever event in the segment's Autosplit list comes next (a LiveSplit import)";
            if (e == HoldInteract) return "Hold-to-interact pressed (the autosplitter's plane meal start)";
            if (e == Moving) return "Started moving (the autosplitter's velocity start)";
            if (e == FirstInput) return "First input - a button or movement after a moment idle (the rules' \"takes control\")";
            if (e == RopeGrab) return "Grabbed a cave rope";
            if (e == RopeLeave) return "Let go of a cave rope";
            return BusEvents.LabelFor(e);
        }

        // The game's clothing database (a ScriptableObject, loaded with the
        // game - readable at the title screen too).
        private static FieldInfo _dbInstance, _dbItems, _itemId, _itemName;

        private static Array ClothingItems()
        {
            if (_dbItems == null)
            {
                Type db = GameBridge.FindGameType("TheForest.Player.Clothing.ClothingItemDatabase");
                if (db == null) return null;
                const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                _dbInstance = db.GetField("_instance", any);
                _dbItems = db.GetField("_items", any);
                if (_dbInstance == null || _dbItems == null) { _dbItems = null; return null; }
            }
            object inst = _dbInstance.GetValue(null);
            return inst == null ? null : _dbItems.GetValue(inst) as Array;
        }

        private static int ClothingId(object item)
        {
            if (item == null) return 0;
            if (_itemId == null)
            {
                const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _itemId = item.GetType().GetField("_id", any);
                _itemName = item.GetType().GetField("_displayName", any);
            }
            return _itemId == null ? 0 : (int)_itemId.GetValue(item);
        }

        /// "Red Beanie" from the game's "RED BEANIE", or null.
        public static string ClothingName(int id)
        {
            try
            {
                Array items = ClothingItems();
                if (items == null) return null;
                for (int i = 0; i < items.Length; i++)
                {
                    object item = items.GetValue(i);
                    if (ClothingId(item) != id || _itemName == null) continue;
                    string raw = _itemName.GetValue(item) as string;
                    return string.IsNullOrEmpty(raw) ? null : TitleCase(raw);
                }
            }
            catch (Exception) { }
            return null;
        }

        private static string TitleCase(string s)
        {
            char[] c = s.ToLowerInvariant().ToCharArray();
            bool start = true;
            for (int i = 0; i < c.Length; i++)
            {
                if (start && char.IsLetter(c[i])) c[i] = char.ToUpperInvariant(c[i]);
                start = c[i] == ' ' || c[i] == '-' || c[i] == '(';
            }
            return new string(c);
        }
    }
}
