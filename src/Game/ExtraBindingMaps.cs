using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The runner's extra game bindings (T-0343) put into the game's own
    // Rewired maps. Every Rewired / TheForest name for them lives here.
    //
    // HOW (proved live, docs/game-notes.md *Adding a binding at runtime*): an
    // element map created on the player's LIVE controller map is a real
    // binding - every GetButton / GetAxis reader and the game's state
    // switching treat it like the game's own. Each binding goes into the
    // player's map of its controller (keyboard / mouse) in every category
    // where the game binds that action on keyboard or mouse, except Chat (1)
    // and Menu (2); Default (0) when there is none.
    //
    // WHY the hide: the game's own map work would see our elements -
    // RefreshMappings lets the last element per action win (the HUD hint for
    // Jump read "J"), LoadAllMaps replaces the maps (ours gone), SaveAllMaps
    // would write ours into the game's PlayerPrefs and the Controls menu
    // would list (and on a rebind delete) them. So a prefix takes ours out
    // and a finalizer puts them back around each of those, nested-safe.
    // A self-heal every 2 s re-applies them if anything else replaced the
    // maps. A TAS replay replaces the game's reads (TasInput) and a recording
    // holds what the game read, so both compose with these as with the
    // game's own bindings.
    // ------------------------------------------------------------------
    public static class ExtraBindingMaps
    {
        public sealed class ActionInfo
        {
            public int Id;
            public string Name;
            public bool Axis;
            public string Label;          // the descriptive name
            public string PositiveLabel;  // an axis's sides
            public string NegativeLabel;

            public string LabelFor(bool negative)
            {
                if (!Axis) return Label;
                return negative ? NegativeLabel : PositiveLabel;
            }
        }

        /// "ready" or why the bindings cannot be applied (shown in Settings).
        public const string WaitingStatus = "waiting for the game's controls";
        public static string Status = WaitingStatus;

        /// Set-up or applying failed: Status says why (Settings shows it in
        /// the warning colour, the log has its "Game controls:" line).
        public static bool Failed { get { return Status != "ready" && Status != WaitingStatus; } }

        /// Every game action, in Rewired's order; empty until Rewired is up.
        public static readonly List<ActionInfo> Actions = new List<ActionInfo>();

        /// Bumped each time the bindings are put into the maps (the module
        /// rebuilds its clash notes when it changes).
        public static int Version;

        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const int ChatCategory = 1, MenuCategory = 2, DefaultCategory = 0;
        private const float HealSeconds = 2f;

        private struct Kept
        {
            public object Map;
            public int Id;
            public bool Mouse;
        }

        private static readonly List<ExtraBinding> _wanted = new List<ExtraBinding>();
        private static readonly List<Kept> _kept = new List<Kept>();
        private static readonly Dictionary<ExtraBinding, string> _where = new Dictionary<ExtraBinding, string>();
        private static readonly Dictionary<string, ActionInfo> _byName = new Dictionary<string, ActionInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, ActionInfo> _byId = new Dictionary<int, ActionInfo>();
        private static readonly Dictionary<int, string> _categoryNames = new Dictionary<int, string>();
        private static readonly List<object> _scratchMaps = new List<object>();
        private static readonly List<object> _attachMaps = new List<object>();   // the player's maps at the last attach

        private static ManualLogSource _log;
        private static string _harmonyId;
        private static Harmony _harmony;
        private static bool _setup, _setupFailed, _busy, _pending, _pendingReport;
        private static int _depth, _keptExpected;
        private static float _nextTry, _nextHeal;

        // Reflection, found once.
        private static FieldInfo _playerField;
        private static Member _isReady, _controllers, _maps, _categoryId, _allMaps;
        private static Member _elId, _elActionId, _elKeyCode, _elModifiers, _elElement, _elRange, _elPole;
        private static MethodInfo _getAllMaps, _contains, _delete, _createKey, _createMouse;
        private static object _keyboardType, _mouseType, _poleNeg, _polePos, _typeAxis, _typeButton, _rangePos, _rangeNeg, _rangeFull, _noModifiers;

        public static bool Ready { get { return _setup && Actions.Count > 0; } }

        /// The bindings to put in; applied now when the game is ready (else on
        /// the next Tick). `harmonyId` is the plugin's; the hooks go in the
        /// first time there is something to hide. `report`: log the "applied"
        /// line (the start-up load; a runner's add / remove logs its own).
        public static void SetBindings(IList<ExtraBinding> list, bool report, ManualLogSource log, string harmonyId)
        {
            _log = log;
            _harmonyId = harmonyId;
            _wanted.Clear();
            if (list != null) for (int i = 0; i < list.Count; i++) _wanted.Add(list[i]);
            _pending = true;
            _pendingReport |= report;
            if (!_setup || _depth > 0 || _busy) return;
            try
            {
                if (PlayerNow() == null) return;
                ApplyPending();
            }
            catch (Exception ex) { Fail("could not apply the bindings (" + ex.Message + ")"); }
        }

        /// Each frame: cheap. Finds Rewired, makes the first attach, and
        /// re-applies every 2 s if the maps were replaced behind our hooks.
        public static void Tick(ManualLogSource log, string harmonyId)
        {
            _log = log;
            _harmonyId = harmonyId;
            float now = Time.realtimeSinceStartup;
            if (!_setup)
            {
                if (_setupFailed || now < _nextTry) return;
                _nextTry = now + 3f;
                try { Setup(); }
                catch (Exception ex) { Fail("cannot read the game's controls (" + ex.Message + ")"); }
                return;
            }
            if ((_wanted.Count == 0 && !_pending) || _depth > 0 || _busy || now < _nextHeal) return;
            _nextHeal = now + HealSeconds;
            try
            {
                object player = PlayerNow();
                if (player == null) return;
                if (_pending) { ApplyPending(); return; }
                if (!Intact(player))
                {
                    Detach();
                    Attach(true);   // logs: re-applied after something replaced the maps
                }
            }
            catch (Exception ex) { Fail("could not apply the bindings (" + ex.Message + ")"); }
        }

        private static void ApplyPending()
        {
            bool report = _pendingReport && _wanted.Count > 0;
            _pending = false;
            _pendingReport = false;
            Detach();
            Attach(report);
        }

        /// Where a binding went ("Default", "Default, Inventory"), or why it
        /// did not (starting "not "); null before the first attach.
        public static string Where(ExtraBinding b)
        {
            string s;
            return b != null && _where.TryGetValue(b, out s) ? s : null;
        }

        public static ActionInfo Find(string name)
        {
            ActionInfo a;
            return name != null && _byName.TryGetValue(name, out a) ? a : null;
        }

        /// The game's own actions on this input, by their descriptive names
        /// ("Crouch, Batch"), "" when none: the clash note. Read from the
        /// live maps (the runner's rebinds included), ours left out, Chat and
        /// Menu left out. Allocates: call it when something changed.
        public static string GameUses(ExtraBinding b)
        {
            if (!_setup || b == null) return "";
            try
            {
                object player = PlayerNow();
                if (player == null) return "";
                List<string> names = new List<string>();
                foreach (object map in Maps(player, b.IsMouse ? _mouseType : _keyboardType))
                {
                    int cat = (int)_categoryId.GetValue(map, null);
                    if (cat == ChatCategory || cat == MenuCategory) continue;
                    IEnumerable els = _allMaps.GetValue(map, null) as IEnumerable;
                    if (els == null) continue;
                    foreach (object el in els)
                    {
                        if (el == null || IsOurs((int)_elId.GetValue(el, null))) continue;
                        if (!Matches(el, b)) continue;
                        ActionInfo a;
                        if (!_byId.TryGetValue((int)_elActionId.GetValue(el, null), out a)) continue;
                        bool neg = a.Axis && _elPole.GetValue(el, null).ToString() == "Negative";
                        string n = a.LabelFor(neg);
                        if (!names.Contains(n)) names.Add(n);
                    }
                }
                return string.Join(", ", names.ToArray());
            }
            catch (Exception) { return ""; }
        }

        // --- setup -----------------------------------------------------------------------------

        private static void Fail(string why)
        {
            if (Status != why && _log != null) _log.LogWarning("Game controls: " + why);
            if (Status != why) Version++;
            Status = why;
        }

        /// A property or a field, read the same way: Rewired has both
        /// (Player.controllers and ControllerHelper.maps are fields, the rest
        /// properties - ILScan on Rewired_Core.dll, and live: v0.24.373 read
        /// controllers as a property and set-up failed).
        private sealed class Member
        {
            private readonly PropertyInfo _p;
            private readonly FieldInfo _f;
            public readonly Type ValueType;

            public Member(PropertyInfo p, FieldInfo f)
            {
                _p = p;
                _f = f;
                ValueType = p != null ? p.PropertyType : f.FieldType;
            }

            // Shaped like PropertyInfo.GetValue(target, index) so the call sites read the same.
            public object GetValue(object target, object[] unused)
            {
                return _p != null ? _p.GetValue(target, null) : _f.GetValue(target);
            }
        }

        private static Member Prop(Type t, string name)
        {
            if (t == null) throw new MissingMemberException("?", name);
            PropertyInfo p = t.GetProperty(name, Any);
            if (p != null && p.GetIndexParameters().Length == 0) return new Member(p, null);
            FieldInfo f = t.GetField(name, Any);
            if (f != null) return new Member(null, f);
            throw new MissingMemberException(t.Name, name);
        }

        private static void Setup()
        {
            Type input = GameBridge.FindGameType("TheForest.Utils.Input");
            _playerField = input != null ? input.GetField("player", Any) : null;
            if (_playerField == null) { _setupFailed = true; Fail("the game's input class changed"); return; }
            Assembly rw = _playerField.FieldType.Assembly;
            Type reInput = rw.GetType("Rewired.ReInput", false);
            Type mapType = rw.GetType("Rewired.ControllerMap", false);
            Type elType = rw.GetType("Rewired.ActionElementMap", false);
            Type ctrlType = rw.GetType("Rewired.ControllerType", false);
            Type pole = rw.GetType("Rewired.Pole", false);
            Type elKind = rw.GetType("Rewired.ControllerElementType", false);
            Type range = rw.GetType("Rewired.AxisRange", false);
            Type mods = rw.GetType("Rewired.ModifierKeyFlags", false);
            if (reInput == null || mapType == null || elType == null || ctrlType == null || pole == null
                || elKind == null || range == null || mods == null)
            { _setupFailed = true; Fail("Rewired changed"); return; }

            _isReady = Prop(reInput, "isReady");
            if (!(bool)_isReady.GetValue(null, null)) return;   // tried again in 3 s
            object mapping = Prop(reInput, "mapping").GetValue(null, null);
            IEnumerable actions = mapping != null ? Prop(mapping.GetType(), "Actions").GetValue(mapping, null) as IEnumerable : null;
            if (actions == null) return;

            _controllers = Prop(_playerField.FieldType, "controllers");
            _maps = Prop(_controllers.ValueType, "maps");
            _getAllMaps = _maps.ValueType.GetMethod("GetAllMaps", new[] { ctrlType });
            _categoryId = Prop(mapType, "categoryId");
            _allMaps = Prop(mapType, "AllMaps");
            _contains = mapType.GetMethod("ContainsElementMap", new[] { typeof(int) });
            _delete = mapType.GetMethod("DeleteElementMap", new[] { typeof(int) });
            Type outEl = elType.MakeByRefType();
            _createKey = mapType.GetMethod("CreateElementMap", new[] { typeof(int), pole, typeof(KeyCode), mods, outEl });
            _createMouse = mapType.GetMethod("CreateElementMap", new[] { typeof(int), pole, typeof(int), elKind, range, typeof(bool), outEl });
            _elId = Prop(elType, "id");
            _elActionId = Prop(elType, "actionId");
            _elKeyCode = Prop(elType, "keyCode");
            _elModifiers = Prop(elType, "modifierKeyFlags");
            _elElement = Prop(elType, "elementIdentifierId");
            _elRange = Prop(elType, "axisRange");
            _elPole = Prop(elType, "axisContribution");
            if (_getAllMaps == null || _contains == null || _delete == null || _createKey == null || _createMouse == null)
            { _setupFailed = true; Fail("Rewired's map calls changed"); return; }

            _keyboardType = Enum.Parse(ctrlType, "Keyboard");
            _mouseType = Enum.Parse(ctrlType, "Mouse");
            _polePos = Enum.Parse(pole, "Positive");
            _poleNeg = Enum.Parse(pole, "Negative");
            _typeAxis = Enum.Parse(elKind, "Axis");
            _typeButton = Enum.Parse(elKind, "Button");
            _rangePos = Enum.Parse(range, "Positive");
            _rangeNeg = Enum.Parse(range, "Negative");
            _rangeFull = Enum.Parse(range, "Full");
            _noModifiers = Enum.Parse(mods, "None");

            Actions.Clear();
            _byName.Clear();
            _byId.Clear();
            foreach (object a in actions)
            {
                if (a == null) continue;
                Type at = a.GetType();
                ActionInfo info = new ActionInfo();
                info.Id = (int)Prop(at, "id").GetValue(a, null);
                info.Name = Prop(at, "name").GetValue(a, null) as string;
                if (string.IsNullOrEmpty(info.Name)) continue;
                info.Axis = Prop(at, "type").GetValue(a, null).ToString() == "Axis";
                info.Label = Nice(Prop(at, "descriptiveName").GetValue(a, null) as string, info.Name);
                info.PositiveLabel = Nice(Prop(at, "positiveDescriptiveName").GetValue(a, null) as string, info.Label + " +");
                info.NegativeLabel = Nice(Prop(at, "negativeDescriptiveName").GetValue(a, null) as string, info.Label + " -");
                Actions.Add(info);
                if (!_byName.ContainsKey(info.Name)) _byName[info.Name] = info;
                _byId[info.Id] = info;
            }
            if (Actions.Count == 0) return;

            _categoryNames.Clear();
            IEnumerable cats = Prop(mapping.GetType(), "MapCategories").GetValue(mapping, null) as IEnumerable;
            if (cats != null)
                foreach (object c in cats)
                {
                    if (c == null) continue;
                    Type ct = c.GetType();
                    _categoryNames[(int)Prop(ct, "id").GetValue(c, null)] =
                        Nice(Prop(ct, "descriptiveName").GetValue(c, null) as string, Prop(ct, "name").GetValue(c, null) as string);
                }

            _setup = true;
            Status = "ready";
            Version++;
        }

        private static string Nice(string s, string fallback)
        {
            return string.IsNullOrEmpty(s) ? fallback : s;
        }

        private static void InstallHooks()
        {
            if (_harmony != null) return;
            _harmony = new Harmony(_harmonyId + ".gamecontrols");
            HarmonyMethod pre = new HarmonyMethod(typeof(ExtraBindingMaps).GetMethod("HidePrefix", BindingFlags.Static | BindingFlags.NonPublic));
            HarmonyMethod fin = new HarmonyMethod(typeof(ExtraBindingMaps).GetMethod("HideFinalizer", BindingFlags.Static | BindingFlags.NonPublic));
            Type mapping = GameBridge.FindGameType("TheForest.Utils.InputMapping");
            Type icons = GameBridge.FindGameType("TheForest.UI.InputMappingIcons");
            Type button = GameBridge.FindGameType("TheForest.Utils.InputMappingButton");
            int n = 0, of = 5;
            n += Hook(mapping, "LoadAllMaps", pre, fin);
            n += Hook(mapping, "SaveAllMaps", pre, fin);
            n += Hook(mapping, "ClearAllMaps", pre, fin);
            n += Hook(icons, "RefreshMappings", pre, fin);
            n += Hook(button, "GetElementMap", pre, fin);
            if (n < of && _log != null)
                _log.LogWarning("Game controls: only " + n + " of " + of + " of the game's map calls found - the game's Controls menu may list or save the extra bindings.");
        }

        private static int Hook(Type t, string name, HarmonyMethod pre, HarmonyMethod fin)
        {
            if (t == null) return 0;
            MethodInfo m = t.GetMethod(name, Any);
            if (m == null) return 0;
            _harmony.Patch(m, prefix: pre, finalizer: fin);
            return 1;
        }

        // Ours out before the game's own map work...
        private static void HidePrefix()
        {
            try
            {
                if (_depth++ == 0 && !_busy) Detach();
            }
            catch (Exception) { }
        }

        // ...and back after it, thrown or not (a finalizer runs either way).
        private static void HideFinalizer()
        {
            try
            {
                if (_depth > 0) _depth--;
                if (_depth == 0 && !_busy && _wanted.Count > 0 && _setup && PlayerNow() != null) Attach(false);
            }
            catch (Exception) { }
        }

        // --- attach / detach -------------------------------------------------------------------

        private static object PlayerNow()
        {
            if (_playerField == null) return null;
            if (_isReady != null && !(bool)_isReady.GetValue(null, null)) return null;
            return _playerField.GetValue(null);
        }

        private static IEnumerable Maps(object player, object controllerType)
        {
            object helper = _maps.GetValue(_controllers.GetValue(player, null), null);
            return _getAllMaps.Invoke(helper, new[] { controllerType }) as IEnumerable ?? new object[0];
        }

        private static bool IsOurs(int elementMapId)
        {
            for (int i = 0; i < _kept.Count; i++) if (_kept[i].Id == elementMapId) return true;
            return false;
        }

        private static bool Matches(object el, ExtraBinding b)
        {
            if (!b.IsMouse)
            {
                return (KeyCode)_elKeyCode.GetValue(el, null) == (KeyCode)Enum.Parse(typeof(KeyCode), b.Input)
                    && Convert.ToInt32(_elModifiers.GetValue(el, null)) == 0;
            }
            if ((int)_elElement.GetValue(el, null) != b.Element) return false;
            if (b.Kind != BindInput.Wheel) return true;
            string r = _elRange.GetValue(el, null).ToString();
            return r == "Full" || r == (b.WheelDown ? "Negative" : "Positive");
        }

        /// Takes every element we added out of the maps that still hold it.
        private static void Detach()
        {
            if (_kept.Count == 0) return;
            _busy = true;
            try
            {
                for (int i = 0; i < _kept.Count; i++)
                {
                    try
                    {
                        object[] arg = { _kept[i].Id };
                        if ((bool)_contains.Invoke(_kept[i].Map, arg)) _delete.Invoke(_kept[i].Map, arg);
                    }
                    catch (Exception) { }
                }
                _kept.Clear();
            }
            finally { _busy = false; }
        }

        /// Puts every wanted binding into the player's live maps. `report`:
        /// the first attach and a re-heal log one line; the hooks' re-attach
        /// around the game's map work does not.
        private static void Attach(bool report)
        {
            if (_wanted.Count == 0) { _where.Clear(); _keptExpected = 0; _attachMaps.Clear(); Version++; return; }
            object player = PlayerNow();
            if (player == null) return;
            InstallHooks();
            _busy = true;
            try
            {
                // Where the game binds each action (keyboard or mouse), ours out.
                Dictionary<int, List<int>> catsOf = new Dictionary<int, List<int>>();
                List<object> kb = new List<object>(), mouse = new List<object>();
                foreach (object m in Maps(player, _keyboardType)) if (m != null) kb.Add(m);
                foreach (object m in Maps(player, _mouseType)) if (m != null) mouse.Add(m);
                Collect(kb, catsOf);
                Collect(mouse, catsOf);
                _attachMaps.Clear();
                _attachMaps.AddRange(kb);
                _attachMaps.AddRange(mouse);

                _where.Clear();
                int applied = 0, failed = 0;
                for (int i = 0; i < _wanted.Count; i++)
                {
                    ExtraBinding b = _wanted[i];
                    ActionInfo a = Find(b.Action);
                    if (a == null) { _where[b] = "not one of the game's actions"; failed++; continue; }
                    List<int> cats;
                    if (!catsOf.TryGetValue(a.Id, out cats) || cats.Count == 0) cats = new List<int> { DefaultCategory };
                    string where = "";
                    List<object> maps = b.IsMouse ? mouse : kb;
                    for (int m = 0; m < maps.Count; m++)
                    {
                        int cat = (int)_categoryId.GetValue(maps[m], null);
                        if (!cats.Contains(cat)) continue;
                        int id;
                        if (!Create(maps[m], a.Id, b, out id)) continue;
                        Kept k = new Kept();
                        k.Map = maps[m];
                        k.Id = id;
                        k.Mouse = b.IsMouse;
                        _kept.Add(k);
                        string cn;
                        if (!_categoryNames.TryGetValue(cat, out cn)) cn = cat.ToString();
                        if (where.IndexOf(cn, StringComparison.Ordinal) < 0) where += (where.Length > 0 ? ", " : "") + cn;
                    }
                    if (where.Length == 0) { _where[b] = "not applied: the game has no map for it"; failed++; continue; }
                    _where[b] = where;
                    applied++;
                }
                _keptExpected = _kept.Count;
                if (Status != "ready") { Status = "ready"; Version++; }   // an earlier apply error is over
                Version++;
                if (report && _log != null)
                    _log.LogInfo("Game controls: " + applied + " extra binding" + (applied == 1 ? "" : "s") + " applied" +
                                 (failed > 0 ? " (" + failed + " not: see Settings > Game controls)" : "") + ".");
            }
            finally { _busy = false; }
        }

        private static void Collect(List<object> maps, Dictionary<int, List<int>> catsOf)
        {
            for (int m = 0; m < maps.Count; m++)
            {
                int cat = (int)_categoryId.GetValue(maps[m], null);
                if (cat == ChatCategory || cat == MenuCategory) continue;
                IEnumerable els = _allMaps.GetValue(maps[m], null) as IEnumerable;
                if (els == null) continue;
                foreach (object el in els)
                {
                    if (el == null) continue;
                    int action = (int)_elActionId.GetValue(el, null);
                    List<int> cats;
                    if (!catsOf.TryGetValue(action, out cats)) catsOf[action] = cats = new List<int>();
                    if (!cats.Contains(cat)) cats.Add(cat);
                }
            }
        }

        private static bool Create(object map, int actionId, ExtraBinding b, out int id)
        {
            id = -1;
            object[] args;
            MethodInfo call;
            object pole = b.Negative ? _poleNeg : _polePos;
            if (!b.IsMouse)
            {
                call = _createKey;
                args = new object[] { actionId, pole, Enum.Parse(typeof(KeyCode), b.Input), _noModifiers, null };
            }
            else if (b.Kind == BindInput.Wheel)
            {
                call = _createMouse;
                args = new object[] { actionId, pole, b.Element, _typeAxis, b.WheelDown ? _rangeNeg : _rangePos, false, null };
            }
            else
            {
                call = _createMouse;
                args = new object[] { actionId, pole, b.Element, _typeButton, _rangeFull, false, null };
            }
            if (!(bool)call.Invoke(map, args)) return false;
            object el = args[args.Length - 1];
            if (el == null) return false;
            id = (int)_elId.GetValue(el, null);
            return true;
        }

        /// The player still has the same keyboard / mouse maps as at the last
        /// attach (none replaced, none added: a map that was missing then may
        /// hold a binding now), and each kept element is still in its map.
        private static bool Intact(object player)
        {
            if (_kept.Count != _keptExpected) return false;
            _scratchMaps.Clear();
            foreach (object m in Maps(player, _keyboardType)) if (m != null) _scratchMaps.Add(m);
            foreach (object m in Maps(player, _mouseType)) if (m != null) _scratchMaps.Add(m);
            if (_scratchMaps.Count != _attachMaps.Count) return false;
            for (int i = 0; i < _scratchMaps.Count; i++)
                if (!ReferenceEquals(_scratchMaps[i], _attachMaps[i])) return false;
            for (int i = 0; i < _kept.Count; i++)
                if (!(bool)_contains.Invoke(_kept[i].Map, new object[] { _kept[i].Id })) return false;
            return true;
        }
    }
}
