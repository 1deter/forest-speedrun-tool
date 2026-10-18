using System;
using System.Reflection;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Two numbers the LiveSplit autosplitter's variable viewer shows
    // (1deter/auto-splitters, T-0366), read the way it reads them. INFO-ONLY.
    //   Passengers found  LocalPlayer.PassengerManifest._foundPassengersIdsCount
    //   Loads             LevelLoader.loadingCount (static; ILScan: written
    //                     by LevelLoader.Load's iterator, 2026-10-18)
    // Both answer -1 when the game has no such member or no player yet.
    // ------------------------------------------------------------------
    public sealed class AslValues
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private bool _resolved;
        private FieldInfo _manifest;
        private Func<object, int> _foundGet;
        private Func<int> _loadsGet;

        private void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            Type local = GameBridge.FindGameType("TheForest.Utils.LocalPlayer");
            Type loader = GameBridge.FindGameType("LevelLoader");
            if (local != null) _manifest = local.GetField("PassengerManifest", Any);
            if (_manifest != null) _foundGet = FastField.Instance<int>(_manifest.FieldType.GetField("_foundPassengersIdsCount", Any));
            if (loader != null) _loadsGet = FastField.Static<int>(loader.GetField("loadingCount", Any));
        }

        public int FoundPassengers()
        {
            Resolve();
            if (_manifest == null || _foundGet == null) return -1;
            try
            {
                UnityEngine.Object owner = _manifest.GetValue(null) as UnityEngine.Object;
                return owner == null ? -1 : _foundGet(owner);
            }
            catch (Exception) { return -1; }
        }

        public int Loads()
        {
            Resolve();
            if (_loadsGet == null) return -1;
            try { return _loadsGet(); }
            catch (Exception) { return -1; }
        }
    }
}
