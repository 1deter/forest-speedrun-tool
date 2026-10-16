namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The run line's colour by speed (T-0372): green (fast) - amber - red
    // (slow) over the range of the comparison run's recorded speeds, in a
    // few buckets (author, 2026-10-16: green = fast). Pure, tested.
    // ------------------------------------------------------------------
    public static class SpeedRamp
    {
        public const int Buckets = 16;

        /// 0 (slowest) .. Buckets - 1 (fastest) for a speed within [min, max];
        /// a flat range (no spread) is the middle.
        public static int Bucket(float speed, float min, float max)
        {
            if (!(max - min > 0.01f)) return Buckets / 2;
            float t = (speed - min) / (max - min);
            int b = (int)(t * Buckets);
            if (b < 0) b = 0;
            if (b > Buckets - 1) b = Buckets - 1;
            return b;
        }

        /// The bucket's colour, red - amber - green.
        public static void Colour(int bucket, out float r, out float g, out float b)
        {
            if (bucket < 0) bucket = 0;
            if (bucket > Buckets - 1) bucket = Buckets - 1;
            float t = bucket / (float)(Buckets - 1);
            b = 0.15f;
            if (t < 0.5f) { r = 0.95f; g = 0.25f + 0.65f * (t / 0.5f); }          // red -> amber
            else { r = 0.95f - 0.75f * ((t - 0.5f) / 0.5f); g = 0.9f; }           // amber -> green
        }
    }
}
