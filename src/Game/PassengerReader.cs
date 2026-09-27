using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    public struct PassengerSeat
    {
        public string Seat;    // the manifest's name for it, e.g. "8C"
        public bool Found;
    }

    // ------------------------------------------------------------------
    // The passenger manifest (100% tab). INFO-ONLY.
    //
    // Confirmed from IL + bridge (2026-09-27):
    //   TheForest.Player.PassengerManifest, under the player's
    //   PlayerInventory._specialItems (player/ControllerObjects/SpecialItems)
    //     String[]    _displayName          43 seat names ("8C", "1A", ...)
    //     GameObject[] _foundGOs            43, one per seat
    //     List<Int32> _foundPassengersIds   saved with the game
    //     Int32       _itemId               197, the manifest item
    //   FoundPassenger(id): counts only while the player OWNS the manifest
    //   (Inventory.Owns(_itemId, true)), not upside down, and only ids with
    //   id - 1 < _foundGOs.Length; the seat is _displayName[id - 1].
    //
    // Where each passenger is: NOT from PassengerDatabase._passengers -
    // its scene paths are wrong (bridge: the five in Cave 6's main cavern
    // are ids 18, 14, 4, 20, 6; the database puts those in Caves 1 and 9).
    // ------------------------------------------------------------------
    public sealed class PassengerReader
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly ManualLogSource _log;
        private readonly List<PassengerSeat> _seats = new List<PassengerSeat>();

        private Type _type;
        private FieldInfo _found, _names, _gos, _itemId;
        private FieldInfo _inventory, _specialItems;
        private MethodInfo _owns;
        private bool _resolved;
        private Component _manifest;

        public IList<PassengerSeat> Seats { get { return _seats; } }
        public int FoundCount { get; private set; }
        /// True when the player carries the manifest - passengers only count then.
        public bool HasManifest { get; private set; }
        public string Status { get; private set; }

        public PassengerReader(ManualLogSource log)
        {
            _log = log;
            Status = "not read yet";
        }

        public void Refresh()
        {
            _seats.Clear();
            FoundCount = 0;
            HasManifest = false;
            if (!Resolve()) return;

            try
            {
                object inv = _inventory.GetValue(null);
                if (inv as UnityEngine.Object == null) { Status = "passengers: open a save first"; return; }

                // Fake-null after a load: found again under the new player
                // (a small subtree, not a scene scan).
                if (_manifest == null)
                {
                    GameObject special = _specialItems.GetValue(inv) as GameObject;
                    _manifest = special != null ? special.GetComponentInChildren(_type, true) : null;
                    if (_manifest == null) { Status = "passengers: no manifest on the player"; return; }
                }

                string[] names = _names.GetValue(_manifest) as string[];
                Array gos = _gos.GetValue(_manifest) as Array;
                IList found = _found.GetValue(_manifest) as IList;
                int seats = gos != null ? gos.Length : 0;
                if (names == null || seats == 0) { Status = "passengers: manifest has no seats"; return; }

                for (int i = 0; i < seats; i++)
                {
                    PassengerSeat s;
                    s.Seat = i < names.Length ? names[i] : "#" + (i + 1);
                    s.Found = false;
                    _seats.Add(s);
                }
                if (found != null)
                {
                    for (int i = 0; i < found.Count; i++)
                    {
                        int index = (int)found[i] - 1;
                        if (index < 0 || index >= _seats.Count || _seats[index].Found) continue;
                        PassengerSeat s = _seats[index];
                        s.Found = true;
                        _seats[index] = s;
                        FoundCount++;
                    }
                }

                // The game's order is arbitrary ("8C", "1A", "7B"...); row then letter.
                _seats.Sort(CompareSeats);

                int item = (int)_itemId.GetValue(_manifest);
                HasManifest = _owns != null && (bool)_owns.Invoke(inv, new object[] { item, true });
                Status = FoundCount + "/" + seats + " found";
            }
            catch (Exception ex)
            {
                Status = "passengers: read failed (" + (ex.InnerException ?? ex).Message + ")";
            }
        }

        private static int CompareSeats(PassengerSeat a, PassengerSeat b)
        {
            int ra = Row(a.Seat), rb = Row(b.Seat);
            if (ra != rb) return ra.CompareTo(rb);
            return string.CompareOrdinal(a.Seat, b.Seat);
        }

        // The leading number of a seat name ("11E" -> 11); names without one last.
        private static int Row(string seat)
        {
            int row = 0, i = 0;
            while (seat != null && i < seat.Length && seat[i] >= '0' && seat[i] <= '9') row = row * 10 + (seat[i++] - '0');
            return i == 0 ? int.MaxValue : row;
        }

        private bool Resolve()
        {
            if (_resolved) return _found != null;
            _resolved = true;

            _type = GameBridge.FindGameType("TheForest.Player.PassengerManifest");
            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type inv = GameBridge.FindGameType("TheForest.Items.Inventory.PlayerInventory");
            if (_type == null || local == null || inv == null) { Status = "passengers: PassengerManifest not in this game version"; return false; }

            _found = _type.GetField("_foundPassengersIds", Inst);
            _names = _type.GetField("_displayName", Inst);
            _gos = _type.GetField("_foundGOs", Inst);
            _itemId = _type.GetField("_itemId", Inst);
            _inventory = local.GetField("Inventory", Stat);
            _specialItems = inv.GetField("_specialItems", Inst);
            _owns = inv.GetMethod("Owns", Inst, null, new[] { typeof(int), typeof(bool) }, null);

            bool ok = _found != null && _names != null && _gos != null && _itemId != null && _inventory != null && _specialItems != null;
            _log.LogInfo("PassengerManifest bound: " + (ok ? "yes" : "NO") + ", Owns " + (_owns != null ? "yes" : "no") + ".");
            if (!ok) { _found = null; Status = "passengers: manifest fields not found"; }
            return ok;
        }
    }
}
