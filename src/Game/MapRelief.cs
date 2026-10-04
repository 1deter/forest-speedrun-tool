using System;
using System.IO;
using System.Threading;
using BepInEx.Logging;
using ForestOverlay.Data;
using UnityEngine;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // The Map tab's picture: a shaded relief of the island made once from
    // the live terrain (Terrain.activeTerrain = MainTerrain, 3500 x 3500 m
    // at (-1750, -1742.63), heightmap 2049 - docs/website.md), cached as
    // config/ForestOverlay/map/relief-<size x>x<size z>-<px>.png so later
    // launches only read a file.
    //
    // No hitch: the heights are read a few rows a frame on the main thread
    // (GetHeights is main-thread only), shaded, PNG-encoded and written on
    // a background thread, and a cache file is read and decoded on one
    // too. The main thread only uploads the finished pixels (one
    // LoadRawTextureData + Apply). Started on the first look at the tab.
    // ------------------------------------------------------------------
    public sealed class MapRelief
    {
        public const int Size = 1024;
        private const int RowsPerFrame = 32;

        // The terrain as dumped (docs/website.md), for a cache read at the
        // title screen, before any terrain exists.
        public const float DefaultMinX = -1750f, DefaultMinZ = -1742.63f, DefaultSize = 3500f;

        private enum Phase { Idle, WaitTerrain, Loading, Reading, Shading, Ready, Failed }

        private readonly ManualLogSource _log;
        private readonly string _folder;
        private Phase _phase = Phase.Idle;

        // Background results, handed over through these (volatile flag last).
        private volatile bool _workDone;
        private byte[] _workRgb;
        private string _workError;
        private bool _workFromCache;

        private float[] _heights;
        private int[] _srcCols;
        private int _row;
        private TerrainData _data;
        private int _res;
        private float _terrainY, _heightScale;
        private string _cachePath;
        private float _retryAt;
        private float _started;

        public Texture2D Texture { get; private set; }
        public float MinX = DefaultMinX, MinZ = DefaultMinZ, SizeX = DefaultSize, SizeZ = DefaultSize;

        /// Bumped whenever Status changes, so a caller rebuilds its text only then.
        public int Version { get; private set; }
        public string Status { get; private set; }
        public bool Ready { get { return _phase == Phase.Ready && Texture != null; } }
        /// 0..1 while the heights are read; 1 once read.
        public float Progress { get { return _phase == Phase.Reading ? (float)_row / Size : 1f; } }
        public bool Building { get { return _phase == Phase.Reading || _phase == Phase.Shading || _phase == Phase.Loading; } }

        public MapRelief(ManualLogSource log, string configDirectory)
        {
            _log = log;
            _folder = Path.Combine(configDirectory, "map");
            Status = "";
        }

        public static string CacheName(float sizeX, float sizeZ, int px)
        {
            return "relief-" + Mathf.RoundToInt(sizeX) + "x" + Mathf.RoundToInt(sizeZ) + "-" + px + ".png";
        }

        /// The terrain's height at (x, z), or NaN with no terrain.
        public static float TerrainY(float x, float z)
        {
            Terrain t = Terrain.activeTerrain;
            if (t == null) return float.NaN;
            return t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
        }

        /// Starts once (a no-op after); Tick does the rest.
        public void Start()
        {
            if (_phase != Phase.Idle) return;
            Begin();
        }

        private void Begin()
        {
            Terrain t = Terrain.activeTerrain;
            if (t != null && t.terrainData != null)
            {
                Vector3 pos = t.transform.position;
                Vector3 size = t.terrainData.size;
                MinX = pos.x; MinZ = pos.z; SizeX = size.x; SizeZ = size.z;
            }

            _cachePath = Path.Combine(_folder, CacheName(SizeX, SizeZ, Size));
            if (File.Exists(_cachePath)) { StartLoad(); return; }

            if (t == null || t.terrainData == null)
            {
                SetPhase(Phase.WaitTerrain, "No terrain loaded yet (title screen?) - the map is made from it the first time a game is loaded.");
                _retryAt = Time.realtimeSinceStartup + 1f;
                return;
            }
            StartRead(t);
        }

        public void Tick()
        {
            switch (_phase)
            {
                case Phase.WaitTerrain:
                    if (Time.realtimeSinceStartup >= _retryAt) Begin();
                    break;

                case Phase.Reading:
                    ReadRows();
                    break;

                case Phase.Loading:
                case Phase.Shading:
                    if (_workDone) FinishWork();
                    break;

                case Phase.Ready:
                    // A texture can be destroyed under us (it is
                    // DontUnloadUnusedAsset, but be safe): make it again.
                    if (Texture == null) { _phase = Phase.Idle; Begin(); }
                    break;
            }
        }

        // --- cache -------------------------------------------------------
        private void StartLoad()
        {
            SetPhase(Phase.Loading, "Loading the map...");
            _started = Time.realtimeSinceStartup;
            string path = _cachePath;
            Run(delegate
            {
                byte[] png = File.ReadAllBytes(path);
                int w, h;
                byte[] rgb;
                if (!ReliefImage.TryDecodePng(png, out w, out h, out rgb) || w != Size || h != Size)
                    throw new InvalidDataException("not a relief this version wrote");
                _workRgb = rgb;
            }, true);
        }

        // --- terrain -> heights (main thread, a few rows a frame) ---------
        private void StartRead(Terrain t)
        {
            _data = t.terrainData;
            _res = _data.heightmapResolution;
            _terrainY = t.transform.position.y;
            _heightScale = _data.size.y;
            _heights = new float[Size * Size];
            _srcCols = new int[Size];
            for (int i = 0; i < Size; i++) _srcCols[i] = Source(i, _res);
            _row = 0;
            _started = Time.realtimeSinceStartup;
            SetPhase(Phase.Reading, "Making the map from the terrain (once)...");
            _log.LogInfo("Map: making the relief from the terrain (" + _res + " heights, " +
                         SizeX + " x " + SizeZ + " m at " + MinX + ", " + MinZ + ").");
        }

        private static int Source(int i, int res)
        {
            int s = (int)Math.Round((i + 0.5) / Size * (res - 1));
            return s < 0 ? 0 : s > res - 1 ? res - 1 : s;
        }

        private void ReadRows()
        {
            if (_data == null) { Fail("the terrain went away while it was read (a load?)"); return; }
            try
            {
                int end = Math.Min(Size, _row + RowsPerFrame);
                for (; _row < end; _row++)
                {
                    float[,] line = _data.GetHeights(0, Source(_row, _res), _res, 1);
                    int o = _row * Size;
                    for (int x = 0; x < Size; x++)
                        _heights[o + x] = line[0, _srcCols[x]] * _heightScale + _terrainY;
                }
            }
            catch (Exception ex) { Fail("reading the terrain threw: " + ex.Message); return; }

            if (_row < Size) return;

            _data = null;
            SetPhase(Phase.Shading, "Shading the map...");
            float[] heights = _heights;
            _heights = null;
            float mppX = SizeX / Size, mppZ = SizeZ / Size;
            string path = _cachePath, folder = _folder;
            Run(delegate
            {
                byte[] rgb = new byte[Size * Size * 3];
                ReliefImage.Shade(heights, Size, Size, mppX, mppZ, ReliefImage.SeaLevel, rgb);
                _workRgb = rgb;
                try
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(path, ReliefImage.EncodePng(Size, Size, rgb));
                }
                catch (Exception ex) { _workError = "the cache was not written: " + ex.Message; }
            }, false);
        }

        // --- the background half -----------------------------------------
        private void Run(ThreadStart work, bool fromCache)
        {
            _workDone = false;
            _workRgb = null;
            _workError = null;
            _workFromCache = fromCache;
            Thread th = new Thread(delegate ()
            {
                try { work(); }
                catch (Exception ex) { _workRgb = null; _workError = ex.Message; }
                _workDone = true;
            });
            th.IsBackground = true;
            th.Name = "ForestOverlay map";
            th.Start();
        }

        private void FinishWork()
        {
            _workDone = false;
            byte[] rgb = _workRgb;
            _workRgb = null;
            float secs = Time.realtimeSinceStartup - _started;

            if (rgb == null)
            {
                if (_workFromCache)
                {
                    // A cache this build cannot read: make a fresh one.
                    _log.LogWarning("Map: the cached relief was not read (" + _workError + "); making it again.");
                    try { File.Delete(_cachePath); } catch (Exception) { }
                    Terrain t = Terrain.activeTerrain;
                    if (t != null && t.terrainData != null) StartRead(t);
                    else
                    {
                        SetPhase(Phase.WaitTerrain, "The cached map was unreadable - it is made again when a game is loaded.");
                        _retryAt = Time.realtimeSinceStartup + 1f;
                    }
                    return;
                }
                Fail(_workError ?? "no pixels");
                return;
            }

            try
            {
                Texture2D tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                tex.hideFlags = HideFlags.HideAndDontSave;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                tex.LoadRawTextureData(rgb);
                tex.Apply(false, true);
                if (Texture != null) UnityEngine.Object.Destroy(Texture);
                Texture = tex;
            }
            catch (Exception ex) { Fail("the texture upload threw: " + ex.Message); return; }

            string note = _workError != null ? " (" + _workError + ")" : "";
            SetPhase(Phase.Ready, _workFromCache ? "" : "Map made from the terrain" + note + ".");
            _log.LogInfo("Map: relief " + (_workFromCache ? "read from " : "made and cached as ") +
                         Path.GetFileName(_cachePath) + " in " + secs.ToString("0.00") + " s" + note + ".");
        }

        private void Fail(string why)
        {
            _data = null;
            _heights = null;
            SetPhase(Phase.Failed, "The map could not be made: " + why);
            _log.LogWarning("Map: " + why);
        }

        private void SetPhase(Phase p, string status)
        {
            _phase = p;
            Status = status;
            Version++;
        }

        public void Shutdown()
        {
            if (Texture != null) UnityEngine.Object.Destroy(Texture);
            Texture = null;
        }
    }
}
