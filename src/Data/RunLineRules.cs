namespace ForestOverlay.Data
{
    // When the run lines draw (T-0393): the pure rules.
    public static class RunLineRules
    {
        /// Whether your own current line is drawn: Run lines must be on, and
        /// then it shows with the setting on, while the replay camera plays,
        /// or when no run is going. Off while running by default.
        public static bool ShowOwn(bool runLines, bool ownLive, bool replayCamera, bool running)
        {
            return runLines && (ownLive || replayCamera || !running);
        }
    }
}
