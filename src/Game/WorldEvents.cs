using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
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

        /// The ASL's velocity start: overall speed above this.
        public const float MovingSpeed = 0.15f;
        /// Still for this long before `moving` can fire again.
        private const float StillTime = 0.25f;

        /// CaveNames order (Cave01 = 0, NotInCaves = -1), with the ASL's
        /// labels. The enum is read by name, so this is display only.
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
            if (scene != null) _finishLoad = scene.GetField("FinishGameLoad", any);

            List<string> missing = new List<string>();
            if (_areaInfo == null || _cave == null || _caveIndex.Count != CaveIds.Length) missing.Add("caves");
            if (_clothing == null || _worn == null) missing.Add("clothing");
            if (_manifest == null || _found == null) missing.Add("passengers");
            if (_delayedDown == null) missing.Add(HoldInteract);
            _status = missing.Count == 0 ? "caves, clothing, passengers, starts"
                                         : "missing: " + string.Join(", ", missing.ToArray());
            _log.LogInfo("WorldEvents: " + _status + ".");
        }

        /// Once a frame, after GameEvents.Tick, before modules tick.
        public void Tick(PlayerRef player)
        {
            Resolve();
            if (PlayerRef.AtTitleScreen) { Forget(); return; }
            if (_finishLoad != null)
            {
                bool loaded;
                try { loaded = (bool)_finishLoad.GetValue(null); }
                catch (Exception) { loaded = true; }
                if (!loaded) { Forget(); return; }
            }

            try { PollCave(); } catch (Exception) { }
            try { PollClothing(); } catch (Exception) { }
            try { PollPassengers(); } catch (Exception) { }
            try { PollHold(); } catch (Exception) { }
            PollMoving(player);
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
            if (was != null)
            {
                GameEvents.RecordWorld(CaveExit + "-" + was, null, true);
                GameEvents.RecordWorld(CaveExit, was, true);
            }
            if (now != null)
            {
                GameEvents.RecordWorld(CaveEnter + "-" + now, null, true);
                GameEvents.RecordWorld(CaveEnter, now, true);
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
            GameEvents.RecordWorld(Passenger, found.ToString(), true);
        }

        private void PollHold()
        {
            if (_delayedDown == null) return;
            bool down = (bool)_delayedDown.GetValue(null);
            if (down && !_lastDelayed) GameEvents.RecordWorld(HoldInteract, null, false);
            _lastDelayed = down;
        }

        private void PollMoving(PlayerRef player)
        {
            if (player == null || !player.Found) return;
            float now = Time.unscaledTime;
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
            return null;
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
