using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The load leak's root (game-notes "The load leak"): the game's event
    // bus keeps every destroyed world's subscribers.
    //
    // TheForest.Tools.EventRegistry (IL) has static registries - System,
    // Game, Player, Enemy, Animal, Endgame, Achievements - each an
    // IDictionary<object, EventSubscription>, each subscription an
    // IList<SubscriberCallback> `_callbacks`. The only thing that empties
    // them is EventRegistry.Clear(), called from TitleScreen.Awake alone:
    // which is why a trip through the title screen gave the memory back.
    // A same-scene reload never clears them. Objects unsubscribe their named
    // handlers in OnDestroy, but not the lambdas they subscribed in Awake
    // (GameStats.<Awake>m__0..6 and others), so every load leaves callbacks
    // whose target is a destroyed MonoBehaviour - and a destroyed object's
    // C# fields keep the rest of the old world's managed side alive. The
    // census saw it as Achievements.Data (AchievementData.Registry) holding
    // a dead GameStats, AchievementsManager, PlayerInventory and
    // StoryCluesFolder per load, cut short by its depth limit.
    //
    // Fix: after each load, drop callbacks whose target is a destroyed
    // Unity object (or a compiler closure holding one) - what Clear() does
    // at the title, minus the live world's subscribers. Also the static
    // TreeHealth.OnTreeCutDown (a UnityEvent<Vector3>: a dead TreeLodGrid
    // or two a load), through UnityEventBase.RemoveListener.
    //
    // v0.23.4 skipped a subscription whose _publishingEventIndex was not -1
    // ("mid-publish"). But the EventSubscription constructor never sets it:
    // it starts at 0 and becomes -1 only after the first Publish. So every
    // event not yet published since the load (43 at the first load) was
    // skipped, and kept its dead callbacks until the author killed, built
    // or chopped. Prune runs from a module Tick, never inside a Publish, so
    // it no longer skips; it sets such an index to -1, the idle value
    // (Unsubscribe only adjusts the index when it is above -1).
    //
    // A dead subscriber would also run on every publish (a stat counted
    // once per past load). Memory only for us: not practice-only.
    // ------------------------------------------------------------------
    public sealed class StaleSubscribers
    {
        /// Set by the owning module from its config switch.
        public static bool Enabled = true;

        public static int Removed { get; private set; }

        private readonly ManualLogSource _log;
        private bool _bound;
        private FieldInfo[] _registries;     // static EventRegistry fields
        private FieldInfo _subscriptions;    // EventRegistry._eventSubscriptions
        private FieldInfo _callbacks;        // EventSubscription._callbacks
        private FieldInfo _publishing;       // EventSubscription._publishingEventIndex
        private FieldInfo _treeCutDown;      // TreeHealth.OnTreeCutDown
        private FieldInfo _unityCalls;       // UnityEventBase.m_Calls
        private FieldInfo _runtimeCalls;     // InvokableCallList.m_RuntimeCalls
        private MethodInfo _removeListener;  // UnityEventBase.RemoveListener(object, MethodInfo)

        public string Status { get; private set; }

        public StaleSubscribers(ManualLogSource log)
        {
            _log = log;
            Status = "not bound";
        }

        private void Bind()
        {
            _bound = true;
            try
            {
                BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

                Type reg = GameBridge.FindGameType("TheForest.Tools.EventRegistry");
                Type sub = GameBridge.FindGameType("TheForest.Tools.EventRegistry+EventSubscription");
                if (reg != null && sub != null)
                {
                    List<FieldInfo> regs = new List<FieldInfo>();
                    FieldInfo[] fs = reg.GetFields(stat);
                    for (int i = 0; i < fs.Length; i++)
                        if (fs[i].FieldType == reg) regs.Add(fs[i]);
                    _registries = regs.ToArray();
                    _subscriptions = reg.GetField("_eventSubscriptions", inst);
                    _callbacks = sub.GetField("_callbacks", inst);
                    _publishing = sub.GetField("_publishingEventIndex", inst);
                }

                Type tree = GameBridge.FindGameType("TreeHealth");
                _treeCutDown = tree != null ? tree.GetField("OnTreeCutDown", stat) : null;
                Type ueb = typeof(UnityEngine.Events.UnityEventBase);
                _unityCalls = ueb.GetField("m_Calls", inst);
                _runtimeCalls = _unityCalls != null ? _unityCalls.FieldType.GetField("m_RuntimeCalls", inst) : null;
                _removeListener = ueb.GetMethod("RemoveListener", inst, null, new Type[] { typeof(object), typeof(MethodInfo) }, null);
                if (_unityCalls == null || _runtimeCalls == null || _removeListener == null) _treeCutDown = null;

                bool registryOk = _registries != null && _registries.Length > 0 && _subscriptions != null && _callbacks != null;
                Status = (registryOk ? _registries.Length + " event registries" : "event registries NOT found") +
                         (_treeCutDown != null ? ", TreeHealth.OnTreeCutDown" : ", TreeHealth.OnTreeCutDown NOT found");
                _log.LogInfo("StaleSubscribers: " + Status + ".");
                if (!registryOk) _registries = null;
            }
            catch (Exception ex)
            {
                _registries = null;
                _treeCutDown = null;
                Status = "bind failed: " + ex.Message;
                _log.LogWarning("StaleSubscribers: bind failed: " + ex);
            }
        }

        // ------------------------------------------------------------------
        /// Drops dead subscribers; returns how many. Logs one line when any.
        public int Prune()
        {
            if (!Enabled) return 0;
            if (!_bound) Bind();

            int registry = 0, tree = 0;
            try { registry = PruneRegistries(); }
            catch (Exception ex) { _log.LogWarning("StaleSubscribers: registry prune failed: " + ex.Message); }
            try { tree = PruneTreeCutDown(); }
            catch (Exception ex) { _log.LogWarning("StaleSubscribers: OnTreeCutDown prune failed: " + ex.Message); }

            int n = registry + tree;
            if (n > 0)
            {
                Removed += n;
                _log.LogInfo("Events: removed " + registry + " event-registry subscription(s) and " + tree +
                             " tree-cut listener(s) left by destroyed objects (" + Removed + " this session).");
            }
            return n;
        }

        private int PruneRegistries()
        {
            if (_registries == null) return 0;
            int removed = 0;
            for (int r = 0; r < _registries.Length; r++)
            {
                object registry = _registries[r].GetValue(null);
                if (registry == null) continue;
                IDictionary subs = _subscriptions.GetValue(registry) as IDictionary;
                if (subs == null) continue;

                foreach (object subscription in subs.Values)
                {
                    if (subscription == null) continue;
                    IList list = _callbacks.GetValue(subscription) as IList;
                    if (list == null) continue;
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        Delegate d = list[i] as Delegate;
                        if (d != null && IsDead(d.Target)) { list.RemoveAt(i); removed++; }
                    }
                    // 0 = never published since it was created; -1 is idle.
                    if (_publishing != null && (int)_publishing.GetValue(subscription) != -1)
                        _publishing.SetValue(subscription, -1);
                }
            }
            return removed;
        }

        private int PruneTreeCutDown()
        {
            if (_treeCutDown == null) return 0;
            object evt = _treeCutDown.GetValue(null);
            if (evt == null) return 0;
            object calls = _unityCalls.GetValue(evt);
            IList runtime = calls != null ? _runtimeCalls.GetValue(calls) as IList : null;
            if (runtime == null) return 0;

            // Collect first: RemoveListener edits the list.
            List<Delegate> dead = new List<Delegate>();
            for (int i = 0; i < runtime.Count; i++)
            {
                Delegate d = CallDelegate(runtime[i]);
                if (d != null && IsDead(d.Target)) dead.Add(d);
            }
            for (int i = 0; i < dead.Count; i++)
                _removeListener.Invoke(evt, new object[] { dead[i].Target, dead[i].Method });
            return dead.Count;
        }

        // InvokableCall`1.Delegate (a field on the generic subclass).
        private static Delegate CallDelegate(object call)
        {
            if (call == null) return null;
            for (Type t = call.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                FieldInfo f = t.GetField("Delegate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(call) as Delegate;
            }
            return null;
        }

        // A destroyed Unity object, or a compiler closure (<>c__...) whose
        // captured fields hold one.
        private static bool IsDead(object target)
        {
            if (target == null) return false;   // static method: nothing held
            UnityEngine.Object u = target as UnityEngine.Object;
            if (!ReferenceEquals(u, null)) return u == null;

            Type t = target.GetType();
            if (t.Name.IndexOf('<') < 0) return false;
            FieldInfo[] fs = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fs.Length; i++)
            {
                if (!typeof(UnityEngine.Object).IsAssignableFrom(fs[i].FieldType)) continue;
                UnityEngine.Object held = fs[i].GetValue(target) as UnityEngine.Object;
                if (!ReferenceEquals(held, null) && held == null) return true;
            }
            return false;
        }
    }
}
