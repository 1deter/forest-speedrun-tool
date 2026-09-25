using System;
using System.Reflection;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The blueprint in the hands across a restore (runners sxczurass and
    // maks): a Quick load kept whatever blueprint was out, and a capture
    // taken with one out never brought it back.
    //
    // Build mode is not in the save: the held ghost is a plain clone under
    // player/.../Build/BuildingPlacerClose, and Create keeps CreateMode,
    // _currentBlueprint and _currentGhost. A Full load clears it with the
    // scene; a Quick load leaves it alone (bridge, v0.24.47).
    //
    // WHAT (IL): Create.CancelPlace - the game's own put-away (also called
    // by PlayerStats.KillPlayer): destroys the ghost, ClearReferences(true)
    // (placer off, HUD icons shut, RestoreEquipement, CanJump), CreateMode
    // off. Create.CreateBuilding(BuildingTypes) - what picking a page in
    // the book and the console's blueprint command call: instantiates the
    // ghost under the placer, CreateMode on, EquipPreviousUtility.
    // ------------------------------------------------------------------
    public static class BuildMode
    {
        private static FieldInfo _create;        // static LocalPlayer.Create
        private static FieldInfo _mode;          // Create.CreateMode
        private static FieldInfo _blueprint;     // Create._currentBlueprint
        private static FieldInfo _type;          // BuildingBlueprint._type
        private static MethodInfo _cancel;       // Create.CancelPlace()
        private static MethodInfo _build;        // Create.CreateBuilding(BuildingTypes)
        private static Type _types;              // BuildingTypes
        private static bool _resolved;

        /// The blueprint out now (a BuildingTypes name), "" for none.
        public static string Capture()
        {
            try
            {
                object create;
                if (!Live(out create)) return "";
                return Current(create);
            }
            catch (Exception) { return ""; }
        }

        /// Puts the blueprint away when one is out. Says what it did; ""
        /// when none was out.
        public static string PutAway()
        {
            try
            {
                object create;
                if (!Live(out create)) return "";
                string was = Current(create);
                if (!(bool)_mode.GetValue(create)) return "";
                _cancel.Invoke(create, null);
                return "put the blueprint" + (was.Length > 0 ? " (" + was + ")" : "") + " away";
            }
            catch (Exception ex)
            {
                return "putting the blueprint away failed: " + (ex.InnerException ?? ex).Message;
            }
        }

        /// Pulls out the captured blueprint; "" when `type` is empty.
        public static string PullOut(string type)
        {
            if (string.IsNullOrEmpty(type)) return "";
            try
            {
                object create;
                if (!Live(out create)) return "blueprint " + type + " not pulled out: no player";
                if (Current(create) == type) return "blueprint " + type + " already out";
                if ((bool)_mode.GetValue(create)) _cancel.Invoke(create, null);
                object value;
                try { value = Enum.Parse(_types, type, false); }
                catch (Exception) { return "blueprint " + type + " not pulled out: unknown to this game version"; }
                _build.Invoke(create, new object[] { value });
                return Current(create) == type ? "blueprint " + type + " pulled out"
                                               : "blueprint " + type + " not pulled out (the game refused it)";
            }
            catch (Exception ex)
            {
                return "pulling out blueprint " + type + " failed: " + (ex.InnerException ?? ex).Message;
            }
        }

        private static string Current(object create)
        {
            if (!(bool)_mode.GetValue(create)) return "";
            object bp = _blueprint.GetValue(create);
            return bp == null ? "" : Convert.ToString(_type.GetValue(bp));
        }

        private static bool Live(out object create)
        {
            create = null;
            if (!Resolve()) return false;
            create = _create.GetValue(null);
            // Unity's fake null: a destroyed Create after a load.
            UnityEngine.Object o = create as UnityEngine.Object;
            return create != null && !(o == null);
        }

        private static bool Resolve()
        {
            if (_resolved) return _build != null;
            _resolved = true;
            const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type create = GameBridge.FindGameType("TheForest.Buildings.Creation.Create");
            Type blueprint = GameBridge.FindGameType("TheForest.Buildings.Creation.BuildingBlueprint");
            _types = GameBridge.FindGameType("TheForest.Buildings.Creation.BuildingTypes");
            if (local == null || create == null || blueprint == null || _types == null) return false;

            _create = local.GetField("Create", stat);
            _mode = create.GetField("CreateMode", inst);
            _blueprint = create.GetField("_currentBlueprint", inst);
            _type = blueprint.GetField("_type", inst);
            _cancel = create.GetMethod("CancelPlace", inst, null, Type.EmptyTypes, null);
            MethodInfo build = create.GetMethod("CreateBuilding", inst, null, new[] { _types }, null);

            if (_create == null || _mode == null || _blueprint == null || _type == null || _cancel == null) return false;
            _build = build;
            return _build != null;
        }
    }
}
