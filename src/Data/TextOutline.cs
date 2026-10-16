namespace ForestOverlay.Data
{
    /// The dark outline behind HUD text (T-0335): the game's bottom-left
    /// messages are NGUI labels with effectStyle Outline8, black, distance 1
    /// (bridge 2026-10-16) - the text drawn eight times around itself, one
    /// NGUI unit away, then once on top.
    public static class TextOutline
    {
        /// NGUI's UIRoot is manual-height 720: one unit is Screen.height / 720 px.
        public const float GameUnitsHigh = 720f;

        /// The eight neighbours (Outline8), as unit steps.
        public static readonly float[] Dx = { -1f, 0f, 1f, -1f, 1f, -1f, 0f, 1f };
        public static readonly float[] Dy = { -1f, -1f, -1f, 0f, 0f, 1f, 1f, 1f };

        /// One game unit in real pixels at this screen height.
        public static float UnitPx(float screenHeight)
        {
            return screenHeight > 0f ? screenHeight / GameUnitsHigh : 1f;
        }

        /// The offset in the units a draw is made in, when the GUI matrix
        /// scales those by guiScale (real px per drawn unit): the game's
        /// outline width, so the ring is as thick as the game's at any
        /// resolution and HUD scale.
        public static float Distance(float screenHeight, float guiScale)
        {
            return UnitPx(screenHeight) / (guiScale > 0.01f ? guiScale : 1f);
        }
    }
}
