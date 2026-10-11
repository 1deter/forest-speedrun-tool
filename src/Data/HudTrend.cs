using System.Globalization;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The trend colour on a changing HUD value (T-0023, like Momentum Mod's
    // speedometer): gaining / losing / steady. Optional per value (Edit HUD,
    // HudLayout's `trend`), colours in Settings -> HUD (author, 2026-10-07:
    // customisable, green = gain, red = loss, white = steady).
    //
    // The HUD ticks at 10 Hz; a value's rate of change against the last
    // sample is read through a dead-band (a value creeping by noise stays
    // steady) and a hold time (one sample the other way does not flip the
    // colour), so it never flickers frame to frame:
    //
    //   - steady -> gaining / losing: at once, on the first sample past
    //     the dead-band (a jump's speed shows on the jump);
    //   - gaining / losing -> steady: once no sample agreed for Hold;
    //   - gaining <-> losing: once the shown trend has held for Hold.
    //
    // A gap (the value not shown, a pause of the HUD tick) starts over
    // steady. Pure, allocation-free per sample: linked into the tests.
    // ------------------------------------------------------------------
    public enum TrendState { Steady = 0, Gaining = 1, Losing = 2 }

    public sealed class TrendTracker
    {
        /// Change per second below which a value reads as steady (u/s per
        /// second for the speeds). A first guess - to tune in game.
        public const float DefaultDeadBand = 0.5f;
        /// Seconds a trend is kept before it can turn.
        public const float DefaultHold = 0.3f;
        /// A longer gap between samples starts over (the rate means nothing).
        public const float MaxGap = 1f;

        public float DeadBand = DefaultDeadBand;
        public float Hold = DefaultHold;

        private bool _has;
        private float _last, _lastAt;
        private float _since;      // when the shown trend began
        private float _agreedAt;   // the last sample that agreed with it

        public TrendState State { get; private set; }

        public void Reset()
        {
            _has = false;
            State = TrendState.Steady;
        }

        /// One sample: `value` at `now` (seconds, rising). The trend to show.
        public TrendState Update(float value, float now)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || float.IsNaN(now) || float.IsInfinity(now))
            {
                Reset();
                return State;
            }
            if (!_has || now < _lastAt || now - _lastAt > MaxGap)
            {
                _has = true;
                _last = value;
                _lastAt = now;
                State = TrendState.Steady;
                _since = now;
                _agreedAt = now;
                return State;
            }
            float dt = now - _lastAt;
            if (dt <= 0f) return State;   // the same moment again: nothing new

            float rate = (value - _last) / dt;
            _last = value;
            _lastAt = now;
            TrendState seen = rate > DeadBand ? TrendState.Gaining
                            : rate < -DeadBand ? TrendState.Losing
                            : TrendState.Steady;

            if (seen == State) _agreedAt = now;
            else if (State == TrendState.Steady) Show(seen, now);
            else if (seen == TrendState.Steady) { if (now - _agreedAt >= Hold) Show(seen, now); }
            else if (now - _since >= Hold) Show(seen, now);
            return State;
        }

        private void Show(TrendState s, float now)
        {
            State = s;
            _since = now;
            _agreedAt = now;
        }
    }

    public static class HudTrend
    {
        /// The defaults (author, 2026-10-07), 0xRRGGBBAA.
        public const uint DefaultGain = 0x33DD55FF;
        public const uint DefaultLoss = 0xFF4444FF;

        // Steady has no colour of its own: it draws in the theme's text colour
        // (author, 2026-10-11).

        /// The swatches Settings offers for gaining and losing (0xRRGGBBAA):
        /// the two defaults first.
        public static readonly uint[] Presets =
        {
            0x33DD55FF, 0xFF4444FF, 0x3399FFFF, 0xFFDD33FF,
            0xFF9933FF, 0x33DDDDFF, 0xDD55DDFF, 0xFFFFFFFF
        };

        /// "#RRGGBB" (the # optional) as an opaque 0xRRGGBBAA; false for anything
        /// else. No alpha: a see-through value would be one that seems gone.
        public static bool TryParse(string text, out uint colour)
        {
            // By hand, no Trim / Substring: the Settings field asks every draw.
            colour = 0;
            if (text == null) return false;
            int start = 0, end = text.Length;
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            if (start < end && text[start] == '#') start++;
            if (end - start != 6) return false;
            uint v = 0;
            for (int i = start; i < end; i++)
            {
                int d = Hex(text[i]);
                if (d < 0) return false;
                v = (v << 4) | (uint)d;
            }
            colour = (v << 8) | 0xFFu;
            return true;
        }

        private static int Hex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        /// The stored colour, or the fallback for an unreadable one.
        public static uint ParseOr(string text, uint fallback)
        {
            uint c;
            return TryParse(text, out c) ? c : fallback;
        }

        /// "#RRGGBB" (the alpha is not kept).
        public static string Format(uint colour)
        {
            return "#" + (colour >> 8).ToString("X6", CultureInfo.InvariantCulture);
        }
    }
}
