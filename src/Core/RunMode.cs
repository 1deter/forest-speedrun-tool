using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // Run mode (author, 2026-10-02: "practice-only getting used anyway on
    // accident could ruin a run").
    //
    // A Restart on a run spot (a spot with a run category), or Start run
    // mode by hand, begins a run attempt (Modules/RunModeModule; author,
    // 2026-10-02: runs start from preset category saves). While run mode is on, nothing that
    // writes the game can be used: every practice entry point asks
    // Refuse() first and does nothing when it answers true, and the
    // gameplay switches (god mode, item caps, ...) read Active and stay
    // off without their saved settings changing. Leaving is deliberate:
    // End run mode (two clicks, the Runs tab) or quitting the game; a
    // reset (title screen, Restart on the run's spot) is the next attempt.
    //
    // Flags are what makes an attempt not valid even so - the test bridge
    // on, a game cheat, a practice action that slipped past a lock. They
    // show on the HUD and in the attempt's report.
    // ------------------------------------------------------------------
    public sealed class RunMode
    {
        private readonly ManualLogSource _log;
        private readonly Notice _notice;
        private readonly PracticeState _practice;

        private readonly List<string> _flags = new List<string>();
        private readonly Dictionary<string, float> _refusedAt = new Dictionary<string, float>();

        public RunMode(ManualLogSource log, Notice notice, PracticeState practice)
        {
            _log = log;
            _notice = notice;
            _practice = practice;
            _practice.MarkedDuringRun = OnPracticeMark;
            Started = "";
            Label = "";
            EndedWhy = "";
        }

        /// True from a run start until End.
        public bool Active { get; private set; }

        /// Attempts started this session (the HUD's number).
        public int Attempt { get; private set; }

        /// What the attempt was started as, in full (the report's line).
        public string Started { get; private set; }

        /// Short, for the HUD: the category, or "Normal" for a run by hand.
        public string Label { get; private set; }

        /// Why the current attempt is not valid; empty when it is.
        public IList<string> Flags { get { return _flags; } }

        /// Why run mode is off, for the Runs tab ("" before the first run).
        public string EndedWhy { get; private set; }

        /// Called by RunModeModule when an attempt starts.
        public void Begin(string started, string label)
        {
            Attempt++;
            Active = true;
            Started = started ?? "";
            Label = label ?? "";
            EndedWhy = "";
            _flags.Clear();
            _refusedAt.Clear();
            Rebuild();
        }

        /// Run mode off until the next run start. `why` is said in the Runs tab.
        public void End(string why)
        {
            if (!Active) return;
            Active = false;
            EndedWhy = why ?? "";
            _log.LogInfo("Run mode: off (" + EndedWhy + ") - practice features unlocked.");
            Rebuild();
        }

        /// Adds a reason the attempt is not valid (once each).
        public void Flag(string why)
        {
            if (!Active || _flags.Contains(why)) return;
            _flags.Add(why);
            _log.LogWarning("Run mode: attempt " + Attempt + " flagged - " + why + ".");
            Rebuild();
        }

        /// The lock. True = run mode is on and `what` must not happen: the
        /// caller returns without acting. Says so on screen (at most every
        /// few seconds per action, so a held key does not spam) and logs it.
        public bool Refuse(string what)
        {
            if (!Active) return false;
            float last;
            if (!_refusedAt.TryGetValue(what, out last) || Time.unscaledTime - last > 3f)
            {
                _refusedAt[what] = Time.unscaledTime;
                _notice.Show("Run mode: " + what + " is locked during a run. End run mode in the Runs tab to practise.", 6f);
                _log.LogInfo("Run mode: refused " + what + ".");
            }
            return true;
        }

        /// Text for a refused action shown under its button.
        public string RefusedText(string what)
        {
            return what + " is locked during a run (End run mode in the Runs tab).";
        }

        // A Mark during a run means an entry point without a lock: the run
        // is not valid, and the log says which.
        private void OnPracticeMark(string reason)
        {
            if (!Active) return;
            // Every bridge command marks; one flag says it.
            if (reason.StartsWith("test bridge")) Flag("the test bridge is on");
            else Flag("practice action during the run: " + reason);
        }

        private void Rebuild()
        {
            if (!Active) { _practice.SetRunText(null, false); return; }
            string text = "RUN MODE - attempt " + Attempt + (Label.Length > 0 ? " (" + Label + ")" : "");
            // The first reason on the HUD; the Runs tab lists them all.
            if (_flags.Count > 0)
                text += " - NOT VALID: " + _flags[0] + (_flags.Count > 1 ? " (+" + (_flags.Count - 1) + " more, Runs tab)" : "");
            _practice.SetRunText(text, _flags.Count > 0);
        }
    }
}
