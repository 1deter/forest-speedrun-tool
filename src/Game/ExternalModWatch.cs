using System;
using System.Collections.Generic;
using BepInEx.Logging;
using ForestOverlay.Core;
using ForestOverlay.Data;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Watches for anything external beside the game (T-0308): the names
    // come from RunIntegrity.GatherMods (the run report's own lists - one
    // gatherer). Plugins load after ours, so the first look is on the
    // first Update (every plugin's Awake is done by then); after that it
    // looks again only when an assembly loaded or the patched-method or
    // detour count moved, and at most every few seconds. Sticky: a name once seen stays
    // for the session. A label, never a gate (rule 12).
    // ------------------------------------------------------------------
    public sealed class ExternalModWatch
    {
        private const float EverySeconds = 3f;

        private readonly ManualLogSource _log;
        private readonly PracticeState _practice;
        private readonly string _guid, _path;
        private readonly RunReport _scratch = new RunReport();
        private readonly List<string> _seen = new List<string>();
        private volatile bool _loaded = true;   // true: look at the first Tick
        private float _next;
        private readonly ModCounts _counts = new ModCounts();

        public ExternalModWatch(ManualLogSource log, PracticeState practice, string guid, string path)
        {
            _log = log;
            _practice = practice;
            _guid = guid;
            _path = path;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        }

        private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            _loaded = true;
        }

        /// Plugin.Update: cheap while nothing changed (a float compare).
        public void Tick(float now)
        {
            if (now < _next) return;
            _next = now + EverySeconds;
            // MonoMod hooks (T-0414 F6) and the assembly count too: a
            // one-shot Assembly.Load(bytes) raised no event (live, v0.24.398).
            bool loaded = _loaded;
            _loaded = false;
            if (!_counts.Due(RunIntegrity.PatchedCount(), RunIntegrity.DetourCount(), RunIntegrity.AssemblyCount(), loaded)) return;
            Look();
        }

        private void Look()
        {
            RunIntegrity.GatherMods(_scratch, _guid, _path);
            int before = _seen.Count;
            ExternalMods.Collect(_scratch.OtherPlugins, _scratch.OtherPatchers, _scratch.OtherCode, _scratch.ForeignPatches, _seen);
            if (_seen.Count == before) return;
            string names = ExternalMods.Join(_seen);
            _log.LogWarning("Mods: other code is running beside the game - this session is flagged (" + string.Join(", ", _seen.ToArray()) + ").");
            _practice.SetMods(names);
        }
    }
}
