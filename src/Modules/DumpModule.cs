using System;
using ForestOverlay.Core;
using UnityEngine;

namespace ForestOverlay.Modules
{
    // ------------------------------------------------------------------
    // Writes the analysis dumps. INFO-ONLY.
    //
    // Dumping walks every loaded type and the whole scene graph, which
    // takes long enough to stall a frame - so it is hotkey-driven only
    // and never runs on a timer.
    // ------------------------------------------------------------------
    public sealed class DumpModule : OverlayModule
    {
        public override string Id { get { return "dumps"; } }
        public override string DisplayName { get { return "Dumps"; } }

        private string _status = "";
        private float _clearAt;

        public override void RegisterHotkeys(HotkeyMap map)
        {
            map.Add("dump.write", KeyCode.F11, "Write dump files", WriteDumps);
        }

        public void WriteDumps()
        {
            _status = "dumping...";

            try
            {
                GameDumper.WriteTypeIndex(Ctx.Log);
                GameDumper.WriteSceneHierarchy(Ctx.Log);
                GameDumper.WritePlayerSnapshot(Ctx.Log, Ctx.Player.Transform);

                _status = "dumps -> " + GameDumper.DumpDirectory;
            }
            catch (Exception ex)
            {
                _status = "dump failed - see log";
                Ctx.Log.LogError("Dump failed: " + ex);
            }

            _clearAt = Time.unscaledTime + 8f;
        }

        public override void Tick()
        {
            if (_status.Length > 0 && _clearAt > 0f && Time.unscaledTime > _clearAt)
            {
                _status = "";
                _clearAt = 0f;
            }
        }

        public override void ContributeHud(HudBuilder hud)
        {
            if (_status.Length > 0) hud.Pair("Dump", _status);
        }
    }
}
