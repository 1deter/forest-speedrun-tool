namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Is the game's own death still playing? The decision behind
    // Game/DeathSequence, kept pure so it is tested (T-0248).
    //
    // Every sign is one only the single-player death chain sets (their
    // writers listed from the decompiled game, gotcha 102): `Dead`, the
    // hanging's `upsideDown`, the drag-away's two cannibal clones, the
    // inventory's Death view (the setter turns any view into Death while
    // `Dead`), the dead cameras, and three PlayerStats Invokes. Not signs:
    // CutSceneWake / CutSceneBlackToMorning (the intro's knock-out),
    // CheckArmsStart (also a new game's Start), ResetHit (every hit).
    // ------------------------------------------------------------------
    public static class DeathProgress
    {
        /// PlayerStats Invokes queued only by the death chain (FallDownDead
        /// and KnockOut: BlackScreen; BlackScreen and the drag-away:
        /// KillPlayer; a real death and KillMeFast: GameOver).
        public static readonly string[] Signs = { "BlackScreen", "KillPlayer", "GameOver" };

        /// Cancelled when a death is ended: the signs plus the wake-up's
        /// own (WakeFromKnockOut) and the multiplayer / VR ones FallDownDead
        /// queues.
        public static readonly string[] Cancelled =
        {
            "BlackScreen", "KillPlayer", "GameOver", "CheckArmsStart", "PlayWakeMusic",
            "resetInjuredBool", "disablePlayerControl", "EnableGhostMode",
        };

        /// What shows the death still playing, for the log; "" when nothing
        /// does. `pending` is the first sign Invoke still queued, or null.
        public static string Reason(bool dead, bool hanging, bool dragAway, bool deathView, string pending, bool deadCam)
        {
            if (dead) return "dead";
            if (hanging) return "hanging in the cave";
            if (dragAway) return "drag-away";
            if (deathView) return "death view";
            if (!string.IsNullOrEmpty(pending)) return pending + " pending";
            if (deadCam) return "dead cam";
            return "";
        }
    }
}
