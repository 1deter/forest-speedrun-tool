namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The ocean's spectrum condition key, kept (T-0203). Ceto's
    // WaveSpectrum.CreateConditions asks NewSpectrumConditionKey for a new
    // key object every Update only to compare it by value with the one it
    // has; the key is immutable and never stored. For the Unified and
    // Unified-Phillips spectra the key is a pure function of six inputs, so
    // while they are all equal the kept key is the answer.
    //
    // Floats compare with `==`: a NaN input never matches (the game's own
    // value comparison would also call a NaN key different), so it always
    // runs the game's code.
    // ------------------------------------------------------------------
    public sealed class OceanKeyMemo
    {
        private bool _has;
        private object _owner;
        private object _key;
        private int _size;
        private float _speed, _dir, _age;
        private int _type, _grids;

        /// SPECTRUM_TYPE: UNIFIED = 0, UNIFIED_PHILLIPS = 2. Phillips and
        /// custom spectra always run the game's code.
        public static bool Cacheable(int spectrumType)
        {
            return spectrumType == 0 || spectrumType == 2;
        }

        public bool Matches(object owner, int size, float speed, float dir, float age, int type, int grids)
        {
            return _has && Cacheable(type) && ReferenceEquals(owner, _owner) &&
                   type == _type && grids == _grids && size == _size &&
                   speed == _speed && dir == _dir && age == _age;
        }

        public object Key { get { return _key; } }

        public void Keep(object owner, object key, int size, float speed, float dir, float age, int type, int grids)
        {
            if (owner == null || key == null || !Cacheable(type)) { Clear(); return; }
            _owner = owner;
            _key = key;
            _size = size;
            _speed = speed;
            _dir = dir;
            _age = age;
            _type = type;
            _grids = grids;
            _has = true;
        }

        public void Clear()
        {
            _has = false;
            _owner = null;
            _key = null;
        }
    }
}
