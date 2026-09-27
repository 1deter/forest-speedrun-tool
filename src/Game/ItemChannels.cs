using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Inventory counts as run-state channels ("item:Soda" ...), recorded
    // beside PlayerStats in each .run's 5 Hz `v|` lines, so the website
    // can show "3 sodas at 1:00" (author, 2026-09-27).
    //
    // A fixed list of what runners carry and use, by the game's database
    // names (checked live with ItemIdByName; a name the game does not
    // know is skipped and logged). Counts come from the game's own
    // PlayerInventory.AmountOf(id, false) - the held item included
    // (checked: a Molotov stays 2 after Equip) - through a delegate bound
    // to the live inventory, so a sample is a call per item, no boxing.
    // ------------------------------------------------------------------
    public sealed class ItemChannels
    {
        /// Database names, in the order they are recorded.
        public static readonly string[] Items =
        {
            "Soda", "Booze", "EnergyMix", "Meds", "Aloe", "Coins", "Battery",
            "Stick", "Rock", "Log", "Rope", "Cloth", "Molotov", "BombTimed",
            "Dynamite", "Flare",
        };

        public const string Prefix = "item:";

        private readonly ManualLogSource _log;
        private string[] _channels = new string[0];
        private int[] _ids = new int[0];
        private bool _resolved;

        private object _inventory;
        private Func<int, bool, int> _amountOf;

        public ItemChannels(ManualLogSource log) { _log = log; }

        /// Empty until the item database answers (in game); fixed after.
        public string[] Channels { get { Resolve(); return _channels; } }

        private void Resolve()
        {
            if (_resolved) return;
            Type db = GameBridge.FindGameType("TheForest.Items.ItemDatabase");
            MethodInfo byName = db != null ? db.GetMethod("ItemIdByName", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null) : null;
            if (byName == null) return;

            var names = new List<string>();
            var ids = new List<int>();
            var missing = new List<string>();
            for (int i = 0; i < Items.Length; i++)
            {
                int id;
                try { id = (int)byName.Invoke(null, new object[] { Items[i] }); }
                catch (Exception) { return; }   // database not loaded yet
                if (id <= 0) { missing.Add(Items[i]); continue; }
                names.Add(Prefix + Items[i]);
                ids.Add(id);
            }
            if (ids.Count == 0) return;         // title screen: try again later

            _channels = names.ToArray();
            _ids = ids.ToArray();
            _resolved = true;
            _log.LogInfo("Run item channels: " + _ids.Length + " items" +
                         (missing.Count > 0 ? ", not in the game's database: " + string.Join(", ", missing.ToArray()) : "") + ".");
        }

        /// Writes the first `count` item counts into `into` from `offset`
        /// (count = the channels a run armed with). 0 when no inventory.
        public void Read(float[] into, int offset, int count)
        {
            Bind();
            for (int i = 0; i < count && i < _ids.Length; i++)
            {
                int n = 0;
                if (_amountOf != null)
                {
                    try { n = _amountOf(_ids[i], false); }
                    catch (Exception) { _amountOf = null; _inventory = null; }
                }
                into[offset + i] = n;
            }
        }

        /// The delegate follows the live inventory (a load replaces it).
        private void Bind()
        {
            object live = GameBridge.ReadStaticField("TheForest.Utils.LocalPlayer", "Inventory");
            if (live == null || (live as UnityEngine.Object) == null) { _amountOf = null; _inventory = null; return; }
            if (ReferenceEquals(live, _inventory) && _amountOf != null) return;

            _inventory = live;
            _amountOf = null;
            MethodInfo m = live.GetType().GetMethod("AmountOf", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int), typeof(bool) }, null);
            if (m == null) return;
            try { _amountOf = (Func<int, bool, int>)Delegate.CreateDelegate(typeof(Func<int, bool, int>), live, m); }
            catch (Exception e) { _log.LogWarning("Run item channels: AmountOf not bound - " + e.Message); }
        }
    }
}
