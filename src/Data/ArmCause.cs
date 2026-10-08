namespace ForestOverlay.Data
{
    /// Why a timed run is about to arm, handed from the entry point that
    /// placed the player (Go, F7, a start-state restore, the auto-restart)
    /// to the run module's arm, which may run later (a restore is async).
    /// Set at the entry point, taken once where the run arms.
    public sealed class ArmCause
    {
        public const string Default = "spot reached";

        private string _pending;

        /// The latest entry point wins.
        public void Set(string cause) { _pending = string.IsNullOrEmpty(cause) ? null : cause; }

        /// The pending cause, cleared by reading; Default when none.
        public string Take()
        {
            string c = _pending ?? Default;
            _pending = null;
            return c;
        }
    }
}
