using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The A* path pool outlives a scene load (T-0034, proved live).
    // Pathfinding.PathPool.pool is a static Dictionary<Type, Stack<Path>>; a
    // pooled Path keeps its pathHandler (a PathNode per graph node), callback
    // (the old Seeker) and target nodes until the new world pops and Reset()s
    // it - so the old navmesh (~100-130 MB) stays until the new world has
    // needed as many paths at once, or for good in a quiet world.
    //
    // Fix: a prefix on AstarPath.Awake empties the pool. At that point the old
    // scene is gone and the new world has pooled nothing; GetPath makes a new
    // path when the stack is empty, so behaviour is unchanged. (Not on
    // OnDestroy: Seeker.OnDestroy re-pools old paths after the AstarPath.)
    // Memory only: not practice-only.
    // ------------------------------------------------------------------
    public sealed class PathPoolCleanup
    {
        /// Set by the owning module from its config switch.
        public static bool Enabled = true;

        public static int Dropped { get; private set; }

        private static ManualLogSource _log;
        private static FieldInfo _pool;   // static PathPool.pool
        private Harmony _harmony;

        public string Status { get; private set; }

        public PathPoolCleanup(ManualLogSource log)
        {
            _log = log;
            Status = "not installed";
        }

        public void Install(string harmonyId)
        {
            try
            {
                BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Type pp = GameBridge.FindGameType("Pathfinding.PathPool");
                Type astar = GameBridge.FindGameType("AstarPath");
                _pool = pp != null ? pp.GetField("pool", stat) : null;
                MethodInfo awake = astar != null ? astar.GetMethod("Awake", inst, null, Type.EmptyTypes, null) : null;
                if (_pool == null || awake == null)
                {
                    Status = "PathPool.pool / AstarPath.Awake not found";
                    _log.LogWarning("PathPoolCleanup: " + Status + ".");
                    return;
                }
                _harmony = new Harmony(harmonyId + ".pathpool");
                _harmony.Patch(awake, prefix: new HarmonyMethod(typeof(PathPoolCleanup).GetMethod("AwakePrefix", BindingFlags.Static | BindingFlags.NonPublic)));
                Status = "installed";
                _log.LogInfo("PathPoolCleanup: installed.");
            }
            catch (Exception ex)
            {
                Status = "install failed: " + ex.Message;
                _log.LogWarning("PathPoolCleanup: install failed: " + ex);
            }
        }

        public void Uninstall()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
        }

        private static void AwakePrefix()
        {
            if (!Enabled) return;
            try
            {
                IDictionary pool = _pool.GetValue(null) as IDictionary;
                if (pool == null) return;
                int n = 0;
                lock (pool)
                {
                    foreach (object stack in pool.Values)
                    {
                        ICollection c = stack as ICollection;
                        if (c != null) n += c.Count;
                    }
                    pool.Clear();
                }
                if (n > 0)
                {
                    Dropped += n;
                    _log.LogInfo("Pathfinding: dropped " + n + " pooled path(s) left by the previous world (" + Dropped + " this session).");
                }
            }
            catch (Exception ex) { _log.LogWarning("PathPoolCleanup: clear failed: " + ex.Message); }
        }
    }
}
