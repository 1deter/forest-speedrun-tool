namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The terrain's "heaviest texture layer" at a cell (T-0203). The game's
    // TerrainHelper.GetProminantTextureIndex reads one alphamap cell with
    // terrainData.GetAlphamaps(x, z, 1, 1) - a new float[1,1,layers] every
    // call, from the player's every fixed step - and returns the layer with
    // the most weight. The answer for a cell only changes when the alphamap
    // is written, so it is kept per cell in a table (one sbyte a cell).
    //
    // Pure: the cell index arithmetic and the layer pick are the game's own
    // (same casts, same strict `>`, first maximum wins, -1 when every weight
    // is 0); the table belongs to one terrain data, resolution and layer
    // count, and starts over when any of them differs or on Clear().
    // ------------------------------------------------------------------
    public sealed class TerrainCells
    {
        /// "Not read yet" in the table.
        public const sbyte Unread = -2;

        /// A larger alphamap is not cached (the table would be big).
        public const int MaxResolution = 4096;

        /// Layers an sbyte can name.
        public const int MaxLayers = 127;

        private sbyte[] _cells;
        private object _owner;
        private int _res;
        private int _layers;

        /// The cell a world coordinate falls in: the game's
        /// `(int)((world - origin) / size * (float)res)`.
        public static int Index(float world, float origin, float size, int res)
        {
            return (int)((world - origin) / size * (float)res);
        }

        public static bool InRange(int x, int z, int res)
        {
            return x >= 0 && z >= 0 && x < res && z < res;
        }

        /// The layer with the most weight in the one cell `a[0, 0, i]`:
        /// strictly greater from 0, so the first maximum wins; -1 when no
        /// weight is above 0 (NaN is never above).
        public static int Heaviest(float[,,] a, int layers)
        {
            int best = -1;
            float bestWeight = 0f;
            for (int i = 0; i < layers; i++)
            {
                float w = a[0, 0, i];
                if (w > bestWeight) { best = i; bestWeight = w; }
            }
            return best;
        }

        /// Points the table at this terrain data / resolution / layers.
        /// Starts it over when any of them changed. False = not cacheable
        /// (the caller leaves the game's code to answer).
        public bool Bind(object owner, int res, int layers)
        {
            if (owner == null || res <= 0 || res > MaxResolution || layers <= 0 || layers > MaxLayers) return false;
            if (_cells != null && ReferenceEquals(owner, _owner) && res == _res && layers == _layers) return true;
            _owner = owner;
            _res = res;
            _layers = layers;
            _cells = new sbyte[res * res];
            Clear();
            return true;
        }

        /// The kept layer, `Unread` when the cell has not been read.
        public int Get(int x, int z)
        {
            return _cells[z * _res + x];
        }

        public void Set(int x, int z, int layer)
        {
            _cells[z * _res + x] = (sbyte)layer;
        }

        /// The alphamap was written: every cell is read again.
        public void Clear()
        {
            if (_cells == null) return;
            for (int i = 0; i < _cells.Length; i++) _cells[i] = Unread;
        }

        /// Drops the table (switch off).
        public void Release()
        {
            _cells = null;
            _owner = null;
            _res = 0;
            _layers = 0;
        }

        public bool Bound { get { return _cells != null; } }
    }
}
