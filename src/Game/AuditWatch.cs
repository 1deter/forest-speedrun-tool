using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using ForestOverlay.Data;
using HarmonyLib;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The run audit log's game side (Data/RunAudit; run mode attempts
    // only): what the game tells about itself, caught with read-only
    // postfixes and queued for Modules/RunModeModule.Audit, which writes
    // the `event` lines.
    //
    // WHAT (IL, ilscan 2026-10-04):
    //   EventRegistry.Publish(object eventType, object eventParameter) -
    //     the game's own event bus (achievements, the survival book's
    //     tasks). Each TfEvent field is a plain `new object()`, so the
    //     event is told apart by reference. Publishers read in IL:
    //       BuiltStructure   Craft_Structure.Build (param: BuildingTypes)
    //       CraftedItem      CraftingCog.DoCraft (param: product item id)
    //       UsedItem         InventoryItemView.UseEdible (+ decaying view; item id)
    //       KilledEnemy      EnemyHealth.Die / dieExplode / DieTrap (param: the EnemyHealth)
    //       EnemyContact     PlayerStats.hitFromEnemy (param: EnemyType)
    //       Killed<Animal>   animalHealth.Die (param: the animal's GameObject)
    //       CutTree          TreeHealth.DoFallTree / DoFallTreeExplosion
    //       UsedBomb         Bomb.Explode
    //       Slept            PlayerStats.GoToSleep
    //       StoryProgress    e.g. activateTimmyPickup (param: GameStats.StoryElements)
    //       Enter/ExitEndgame  MainSceneSetupInit.Start, the endgame triggers, KillPlayer
    //       *Set             GameSetup / PlayerPreferences / Cheats (settings)
    //     WalkedSteps and the like are published often; the postfix
    //     returns on one bool when no attempt runs, else one dictionary
    //     lookup by reference.
    //   HudGui.TogglePauseMenu(bool on) - the pause menu's open / close
    //     (the ESC path, and the game's own closes).
    //   The rides' enter / exit methods (playerZipLineAction EnterZipLine /
    //     ExitZipLine, PlayerPushSledAction enterPushSled / exitPushSled /
    //     forceExitSled / forceDisableSled, PlayerHangGliderAction
    //     FlyWithGlider / StopFlyingGlider / DropGlider, PlayerClimbCliffAction
    //     enterClimbCliff / exitClimbCliffTop / exitClimbCliffGround /
    //     resetClimbCliff): only mark "a ride may have changed" for 3 s;
    //     the module then reads RideModes.Current() a few times a second
    //     (the zipline's flag is set later, in its StickToZipLine routine).
    //
    // Everything else the audit writes comes from what the plugin already
    // watches (GameEvents, WorldEvents, ItemCounter, DeathHooks).
    //
    // SEGMENT EVENTS (the same postfix - one hook on Publish): the bus's
    // events are also raised as named events through GameEvents.RecordWorld,
    // in or out of run mode, while the game is in play (WorldEvents.Live)
    // and not within 1.5 s of the overlay placing the player (a Go into
    // the vault sends the game's EnterEndgame - PlayerRef.JustPlaced):
    // built / built-<structure>, crafted / crafted-<item>, used /
    // used-<item>, kill-enemy, kill-animal / kill-<animal>, hit-by-enemy,
    // tree-cut, bomb, slept, story / story-<element>, endgame-area-enter /
    // -leave (names in Data/BusEvents). The specific name first, the
    // general one as its companion. One `Game event:` log line per
    // general name at most every 2 s (hits and trees come in bursts).
    // With no attempt running the postfix costs one dictionary lookup by
    // reference per publish.
    // ------------------------------------------------------------------
    public static class AuditWatch
    {
        public struct Raw
        {
            public string Kind;
            public string Detail;
            public Raw(string kind, string detail) { Kind = kind; Detail = detail; }
        }

        /// Set by run mode: an attempt is running in a loaded game. Off,
        /// every postfix returns at once.
        public static bool Recording;

        /// Caught since the module last took them (main thread).
        public static readonly List<Raw> Pending = new List<Raw>();

        /// Item names for crafted / used items (set by the module).
        public static Func<int, string> ItemName;

        /// Time.unscaledTime until which a ride may still be changing.
        public static float RideDirtyUntil = -1f;

        /// "watching ..." or what is missing - the Runs tab says it.
        public static string Status = "not installed";

        private const int MaxPending = 500;

        private enum Param { None, Item, Enum, Name, Fixed }

        // How the segment event's specific name is made (Data/BusEvents).
        private enum Seg { None, Plain, Item, Enum, Animal }

        private struct Entry
        {
            public string Field, Kind, Text;
            public Param Param;
            /// The general segment event, or null for none.
            public string Event;
            public Seg Seg;
            public Entry(string field, string kind, Param param, string text) : this(field, kind, param, text, null, Seg.None) { }
            public Entry(string field, string kind, Param param, string text, string evt, Seg seg)
            {
                Field = field; Kind = kind; Param = param; Text = text; Event = evt; Seg = seg;
            }
        }

        private static readonly Entry[] Table =
        {
            new Entry("BuiltStructure", RunAudit.Built, Param.Enum, null, BusEvents.Built, Seg.Enum),
            new Entry("CraftedItem", RunAudit.Crafted, Param.Item, null, BusEvents.Crafted, Seg.Item),
            new Entry("UsedItem", RunAudit.Used, Param.Item, null, BusEvents.Used, Seg.Item),
            new Entry("KilledEnemy", RunAudit.Kill, Param.Name, null, BusEvents.KillEnemy, Seg.Plain),
            new Entry("EnemyContact", RunAudit.Hit, Param.Enum, null, BusEvents.HitByEnemy, Seg.Plain),
            new Entry("KilledRabbit", RunAudit.Animal, Param.Fixed, "rabbit", BusEvents.KillAnimal, Seg.Animal),
            new Entry("KilledLizard", RunAudit.Animal, Param.Fixed, "lizard", BusEvents.KillAnimal, Seg.Animal),
            new Entry("KilledRaccoon", RunAudit.Animal, Param.Fixed, "raccoon", BusEvents.KillAnimal, Seg.Animal),
            new Entry("KilledDeer", RunAudit.Animal, Param.Fixed, "deer", BusEvents.KillAnimal, Seg.Animal),
            new Entry("KilledTurtle", RunAudit.Animal, Param.Fixed, "turtle", BusEvents.KillAnimal, Seg.Animal),
            new Entry("KilledBird", RunAudit.Animal, Param.Fixed, "bird", BusEvents.KillAnimal, Seg.Animal),
            new Entry("KilledShark", RunAudit.Animal, Param.Fixed, "shark", BusEvents.KillAnimal, Seg.Animal),
            new Entry("CutTree", RunAudit.Tree, Param.None, null, BusEvents.TreeCut, Seg.Plain),
            new Entry("UsedBomb", RunAudit.Bomb, Param.None, null, BusEvents.Bomb, Seg.Plain),
            new Entry("Slept", RunAudit.Sleep, Param.None, null, BusEvents.Slept, Seg.Plain),
            new Entry("StoryProgress", RunAudit.Story, Param.Enum, null, BusEvents.Story, Seg.Enum),
            new Entry("EnterEndgame", RunAudit.Endgame, Param.Fixed, "entered the endgame area", BusEvents.EndgameEnter, Seg.Plain),
            new Entry("ExitEndgame", RunAudit.Endgame, Param.Fixed, "left the endgame area", BusEvents.EndgameLeave, Seg.Plain),
            new Entry("CheatAllowedSet", RunAudit.Setting, Param.Enum, "cheats allowed"),
            new Entry("DifficultySet", RunAudit.Setting, Param.Enum, "difficulty"),
            new Entry("GameTypeSet", RunAudit.Setting, Param.Enum, "game type"),
            new Entry("AllowEnemiesSet", RunAudit.Setting, Param.Enum, "allow enemies"),
            new Entry("RegrowModeSet", RunAudit.Setting, Param.Enum, "tree regrowth"),
            new Entry("NoDestrutionModeSet", RunAudit.Setting, Param.Enum, "no destruction"),
            new Entry("RealisticPlayerDamageSet", RunAudit.Setting, Param.Enum, "realistic player damage"),
        };

        private static readonly Dictionary<object, int> ByEvent = new Dictionary<object, int>();
        // General segment event -> Time.unscaledTime it was last logged.
        private static readonly Dictionary<string, float> LoggedAt = new Dictionary<string, float>();
        private const float LogEvery = 2f;
        private static Harmony _harmony;
        private static ManualLogSource _log;

        public static void Install(ManualLogSource log, string harmonyId)
        {
            _log = log;
            List<string> watching = new List<string>();
            List<string> missing = new List<string>();
            try
            {
                _harmony = new Harmony(harmonyId + ".audit");
                const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // The game's event bus.
                Type registry = GameBridge.FindGameType("TheForest.Tools.EventRegistry");
                Type tfEvent = GameBridge.FindGameType("TheForest.Tools.TfEvent");
                MethodInfo publish = registry != null
                    ? registry.GetMethod("Publish", inst, null, new[] { typeof(object), typeof(object) }, null) : null;
                if (publish != null && tfEvent != null)
                {
                    for (int i = 0; i < Table.Length; i++)
                    {
                        FieldInfo f = tfEvent.GetField(Table[i].Field, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        object key = f != null ? f.GetValue(null) : null;
                        if (key != null && !ByEvent.ContainsKey(key)) ByEvent[key] = i;
                    }
                    _harmony.Patch(publish, postfix: Hook("PublishPostfix"));
                    watching.Add("the game's events (" + ByEvent.Count + "/" + Table.Length + ")");
                }
                else missing.Add("the game's events");

                // The pause menu.
                Type hud = GameBridge.FindGameType("HudGui");
                MethodInfo pause = hud != null ? hud.GetMethod("TogglePauseMenu", inst, null, new[] { typeof(bool) }, null) : null;
                if (pause != null) { _harmony.Patch(pause, postfix: Hook("PausePostfix")); watching.Add("the pause menu"); }
                else missing.Add("the pause menu");

                // The rides: only "may have changed".
                int rides = 0;
                HarmonyMethod touched = Hook("RidePostfix");
                rides += PatchNamed("playerZipLineAction", touched, "EnterZipLine", "ExitZipLine");
                rides += PatchNamed("TheForest.Player.Actions.PlayerPushSledAction", touched, "enterPushSled", "exitPushSled", "forceExitSled", "forceDisableSled");
                rides += PatchNamed("PlayerHangGliderAction", touched, "FlyWithGlider", "StopFlyingGlider", "DropGlider");
                rides += PatchNamed("TheForest.Player.Actions.PlayerClimbCliffAction", touched, "enterClimbCliff", "exitClimbCliffTop", "exitClimbCliffGround", "resetClimbCliff");
                if (rides > 0) watching.Add("rides (" + rides + " methods)");
                else missing.Add("rides");
            }
            catch (Exception e)
            {
                missing.Add("error: " + e.Message);
            }
            Status = (watching.Count > 0 ? "watching " + string.Join(", ", watching.ToArray()) : "watching nothing") +
                     (missing.Count > 0 ? "; not found: " + string.Join(", ", missing.ToArray()) : "");
            log.LogInfo("Run audit: " + Status + ".");
        }

        private static HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(typeof(AuditWatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));
        }

        private static int PatchNamed(string typeName, HarmonyMethod post, params string[] names)
        {
            Type t = GameBridge.FindGameType(typeName);
            if (t == null) return 0;
            int n = 0;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (Array.IndexOf(names, m.Name) < 0 || m.IsAbstract) continue;
                try { _harmony.Patch(m, postfix: post); n++; }
                catch (Exception e) { if (_log != null) _log.LogWarning("Run audit: " + t.Name + "." + m.Name + " not watched: " + e.Message); }
            }
            return n;
        }

        public static void Uninstall()
        {
            Recording = false;
            try { if (_harmony != null) _harmony.UnpatchSelf(); }
            catch (Exception) { }
        }

        // --- postfixes: read-only, never throw into the game ---------------------

        private static void PublishPostfix(object eventType, object eventParameter)
        {
            if (eventType == null) return;
            try
            {
                int at;
                if (!ByEvent.TryGetValue(eventType, out at)) return;
                Entry e = Table[at];
                if (Recording && Pending.Count < MaxPending) Pending.Add(new Raw(e.Kind, Describe(e, eventParameter)));
                if (e.Event != null && WorldEvents.Live && !PlayerRef.JustPlaced) RaiseSegmentEvent(e, eventParameter);
            }
            catch (Exception) { }
        }

        // The segment events: the specific name (if any), then the general
        // one as its companion - one occurrence.
        private static void RaiseSegmentEvent(Entry e, object p)
        {
            string specific = null, detail = null;
            switch (e.Seg)
            {
                case Seg.Item:
                    if (p is int)
                    {
                        int id = (int)p;
                        string name = ItemName != null ? ItemName(id) : null;
                        specific = BusEvents.ItemEvent(e.Event, id, name);
                        detail = string.IsNullOrEmpty(name) ? "item " + id : name;
                    }
                    break;
                case Seg.Enum:
                    if (p != null)
                    {
                        detail = p.ToString();
                        specific = BusEvents.Specific(e.Event, detail);
                    }
                    break;
                case Seg.Animal:
                    specific = BusEvents.KillOf(e.Text);
                    detail = e.Text;
                    break;
                default:
                    detail = e.Param == Param.Name ? Describe(e, p) : null;
                    break;
            }

            float now = Time.unscaledTime;
            float last;
            bool log = !LoggedAt.TryGetValue(e.Event, out last) || now - last >= LogEvery;
            if (log) LoggedAt[e.Event] = now;

            if (specific != null)
            {
                GameEvents.RecordWorld(specific, null, false);
                GameEvents.RecordWorld(e.Event, detail, log, true);
            }
            else GameEvents.RecordWorld(e.Event, detail, log);
        }

        // --- names for the segment editor's picker ------------------------------

        /// The BuildingTypes values (not None), for built-<structure>.
        public static string[] StructureNames() { return EnumNames("TheForest.Buildings.Creation.BuildingTypes"); }

        /// The GameStats.StoryElements values, for story-<element>.
        public static string[] StoryNames() { return EnumNames("TheForest.Utils.GameStats+StoryElements"); }

        private static string[] EnumNames(string typeName)
        {
            try
            {
                Type t = GameBridge.FindGameType(typeName);
                if (t == null || !t.IsEnum) return new string[0];
                List<string> names = new List<string>();
                foreach (string n in Enum.GetNames(t))
                    if (n != "None") names.Add(n);
                return names.ToArray();
            }
            catch (Exception) { return new string[0]; }
        }

        private static void PausePostfix(bool on)
        {
            if (!Recording || Pending.Count >= MaxPending) return;
            Pending.Add(new Raw(on ? RunAudit.PauseOpen : RunAudit.PauseClose, null));
        }

        private static void RidePostfix()
        {
            RideDirtyUntil = Time.unscaledTime + 3f;
        }

        private static string Describe(Entry e, object p)
        {
            switch (e.Param)
            {
                case Param.Fixed:
                    return e.Text;
                case Param.Item:
                {
                    if (!(p is int)) return null;
                    int id = (int)p;
                    string name = ItemName != null ? ItemName(id) : null;
                    return string.IsNullOrEmpty(name) ? "item " + id : name;
                }
                case Param.Enum:
                {
                    string v = p != null ? p.ToString() : null;
                    return e.Text != null ? e.Text + ": " + (v ?? "?") : v;
                }
                case Param.Name:
                {
                    Component c = p as Component;
                    GameObject go = c != null ? c.gameObject : p as GameObject;
                    if (go == null) return null;
                    string n = go.name;
                    return n.EndsWith("(Clone)", StringComparison.Ordinal) ? n.Substring(0, n.Length - 7) : n;
                }
                default:
                    return null;
            }
        }
    }
}
