namespace ForestOverlay.Data
{
    /// What the runner picked in the Deaths tab's "When I die" (config
    /// `[Deaths] OnDeath`). Automatic is the rules from before the choice
    /// existed, so nothing changes for anyone who never touches it.
    public enum DeathChoice
    {
        Automatic,
        ReloadSave,
        RestartSpot,
        ReviveAtSpot,
        GameDeath,
    }

    /// The kind of death, as Game/DeathHooks classifies it (kept pure here).
    public enum DeathCase
    {
        /// Any death after the first: the game shows the dead cam, then the menu.
        Real,
        /// The first death of a game: knocked out, wake up captured in a cave.
        Capture,
        /// A death in the Megan boss fight: the game wakes you up in the room.
        BossWake,
        /// Hard survival: the game deletes the save - nothing to reload.
        PermaDeath,
        /// Never touched.
        Multiplayer,
    }

    /// What a death will do.
    public enum DeathOutcome
    {
        /// Let the game die as usual.
        GameDeath,
        /// Reload the slot's save at once (the game's own load).
        ReloadSave,
        /// Health back, then the current spot's Restart (F7): its start state
        /// restored, Quick or Full load as the spot says; no start state =
        /// a teleport to the spot.
        RestartSpot,
        /// Health back and a teleport to the current spot (Go). The world is
        /// left as it is, start state or not.
        ReviveAtSpot,
    }

    /// Everything a death's decision reads, gathered by the Deaths module.
    public struct DeathSituation
    {
        public DeathCase Kind;

        public bool HasSpot;           // a current spot with a spawn point
        public string SpotName;
        public bool HasStartState;     // the current spot has one
        public bool FullLoad;          // ... restored with a load
        public bool PracticeOn;        // practice mode (F9)

        public bool ReloadOn;          // "Reload save on death"
        public bool ReloadOnCapture;   // "Also on the first death"
        public bool ReloadInBoss;      // "Also in the boss fight"
        public bool SlotKnown;         // a save slot to reload

        // Run mode (Core/RunMode: the run's category locks / forces).
        public bool ReviveLocked;      // "revive"
        public bool RestartLocked;     // "restart", and the spot is not the run's own
        public bool GoLocked;          // "go"
        public bool ReloadLocked;      // "reload"
        public bool ReloadForced;      // "reload" forced on for the run
    }

    public struct DeathDecision
    {
        public DeathOutcome Outcome;
        /// "<what happens> - <why>", for "Next death: ..." and the log.
        public string Text;
    }

    // ------------------------------------------------------------------
    // What a death does (pure; Modules/DeathModule acts on it).
    //
    // Automatic = the rules from before the choice existed:
    //   1. a current spot, the revive not locked by run mode, and practice
    //      mode on or a start state on the spot -> restart the spot. One
    //      change: a run whose category allows the revive but locks
    //      Restart (and the spot is not the run's own) no longer revives
    //      in place with a "locked" notice - it goes on to step 2;
    //   2. Reload save on death (with its first-death / boss-fight
    //      toggles; a run's category can force or lock it) -> reload;
    //   3. otherwise the game's own death.
    // An explicit choice does just that thing; when it cannot (no current
    // spot, locked by run mode, nothing to reload) step 2 then 3 decide,
    // and the text says why. Multiplayer is never touched; permadeath
    // never reloads (the save is gone).
    // ------------------------------------------------------------------
    public static class DeathPlan
    {
        public static DeathDecision Decide(DeathChoice choice, DeathSituation s)
        {
            if (s.Kind == DeathCase.Multiplayer)
                return Make(DeathOutcome.GameDeath, "the game's own death", "multiplayer is never changed");

            string instead = null;   // why the choice did not apply
            switch (choice)
            {
                case DeathChoice.Automatic:
                    if (s.HasSpot && !s.ReviveLocked && !s.RestartLocked && (s.PracticeOn || s.HasStartState))
                        return Restart(s, s.HasStartState ? "Automatic: it is the current spot and has a start state"
                                                          : "Automatic: practice mode is on and it is the current spot");
                    if (s.HasSpot && (s.ReviveLocked || s.RestartLocked) && (s.PracticeOn || s.HasStartState))
                        instead = "run mode locks the practice revive";
                    else if (s.HasSpot)
                        instead = "Automatic: practice mode is off and '" + Name(s) + "' has no start state";
                    break;

                case DeathChoice.RestartSpot:
                    if (!s.HasSpot) instead = "no current spot to restart (Go to or save one in Practice)";
                    else if (s.ReviveLocked || s.RestartLocked) instead = "run mode locks restarting a spot on death";
                    else return Restart(s, "you chose Restart the current spot" +
                                           (s.HasStartState ? "" : "; it has no start state, so only a teleport"));
                    break;

                case DeathChoice.ReviveAtSpot:
                    if (!s.HasSpot) instead = "no current spot to revive at (Go to or save one in Practice)";
                    else if (s.ReviveLocked || s.GoLocked) instead = "run mode locks the practice revive";
                    else return Make(DeathOutcome.ReviveAtSpot,
                                     "revives you at '" + Name(s) + "' (health back, teleport only" +
                                     (s.HasStartState ? ", start state not restored" : "") + ")",
                                     "you chose Revive at the current spot");
                    break;

                case DeathChoice.ReloadSave:
                    return Reload(s, "you chose Reload the save", null);

                case DeathChoice.GameDeath:
                    if (s.ReloadForced) return Reload(s, "the run's category forces Reload save on death (over your choice)", null);
                    return Game(s, "you chose The game's own death");
            }

            return Reload(s, null, instead);
        }

        private static DeathDecision Restart(DeathSituation s, string why)
        {
            if (s.HasStartState)
                return Make(DeathOutcome.RestartSpot,
                            "restarts '" + Name(s) + "' (" + (s.FullLoad ? "Full" : "Quick") + " load of its start state)", why);
            return Make(DeathOutcome.RestartSpot,
                        "revives you at '" + Name(s) + "' (health back, teleport)", why);
        }

        // Step 2 and 3. `chosen` (the why) when the reload was picked
        // outright: the main toggle does not matter then, its first-death /
        // boss toggles do. `instead`: why the runner's choice did not apply.
        private static DeathDecision Reload(DeathSituation s, string chosen, string instead)
        {
            string lead = instead != null ? instead + "; " : "";
            bool want = s.ReloadForced || (!s.ReloadLocked && (chosen != null || s.ReloadOn));
            if (!want)
            {
                if (s.ReloadLocked && (chosen != null || s.ReloadOn))
                    return Game(s, lead + "run mode: the run's category locks Reload save on death");
                return Game(s, lead + "Reload save on death is off");
            }

            if (s.Kind == DeathCase.PermaDeath)
                return Game(s, lead + "permadeath: the game deletes the save, nothing to reload");
            if (s.Kind == DeathCase.Capture && !s.ReloadOnCapture)
                return Game(s, lead + "it is this game's first death and 'Also on the first death' is off");
            if (s.Kind == DeathCase.BossWake && !s.ReloadInBoss)
                return Game(s, lead + "you are in the boss fight and 'Also in the boss fight' is off");
            if (!s.SlotKnown)
                return Game(s, lead + "no save slot to reload");

            string why = chosen ?? (s.ReloadForced ? "the run's category forces Reload save on death"
                                                   : "Reload save on death is on");
            return Make(DeathOutcome.ReloadSave, "reloads your save (the game's own load)", lead + why);
        }

        private static DeathDecision Game(DeathSituation s, string why)
        {
            string what;
            switch (s.Kind)
            {
                case DeathCase.Capture: what = "the game's own death: you are captured (first death)"; break;
                case DeathCase.BossWake: what = "the game's own death: you wake up in the boss room"; break;
                case DeathCase.PermaDeath: what = "the game's own death (permadeath)"; break;
                default: what = "the game's own death (dead cam, then the menu)"; break;
            }
            return Make(DeathOutcome.GameDeath, what, why);
        }

        private static string Name(DeathSituation s)
        {
            return string.IsNullOrEmpty(s.SpotName) ? "?" : s.SpotName;
        }

        private static DeathDecision Make(DeathOutcome outcome, string what, string why)
        {
            DeathDecision d;
            d.Outcome = outcome;
            d.Text = what + " - " + why;
            return d;
        }
    }
}
