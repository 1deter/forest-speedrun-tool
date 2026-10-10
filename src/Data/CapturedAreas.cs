using System;
using System.Collections.Generic;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Reads a savestate's `areas` header (Game/AreaReport.Describe() at
    // capture):
    //
    //   caves no, endgame yes, overlook no | scenes: ForestMain_v08,
    //   endgame_animPrefabs, endgame_streaming (loading) | streamed: ...
    //
    // and decides whether the capture had the endgame lab, so a restore
    // loads it (Game/EndgameLoader).
    //
    // The lab is endgame_streaming. endgame_animPrefabs alone is not the
    // lab: it stays loaded after a teleport out of a save loaded inside
    // (bridge, 2026-10-04, Slot 1: a surface capture read "endgame no |
    // ... endgame_animPrefabs", and its restore loaded the lab in the
    // background). It counts only with the endgame flag set - the vault
    // door's own load caught 3 s in (v0.24.82: endgame_animPrefabs, not
    // yet endgame_streaming), where the player has crossed LoadEndgame
    // (EnterEndgame) to reach the door.
    // ------------------------------------------------------------------
    public static class CapturedAreas
    {
        public const string StreamingScene = "endgame_streaming";
        public const string AnimScene = "endgame_animPrefabs";
        private const string Loading = " (loading)";

        /// The scenes listed in the line. A scene still loading at capture
        /// is included (without its suffix) only with includeLoading.
        public static HashSet<string> Scenes(string areas, bool includeLoading)
        {
            HashSet<string> set = new HashSet<string>();
            if (string.IsNullOrEmpty(areas)) return set;
            int at = areas.IndexOf("| scenes: ", StringComparison.Ordinal);
            if (at < 0) return set;
            at += "| scenes: ".Length;
            int end = areas.IndexOf(" |", at, StringComparison.Ordinal);
            string list = end < 0 ? areas.Substring(at) : areas.Substring(at, end - at);
            string[] parts = list.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i].Trim();
                if (s.Length == 0) continue;
                if (s.EndsWith(Loading, StringComparison.Ordinal))
                {
                    if (!includeLoading) continue;
                    s = s.Substring(0, s.Length - Loading.Length).Trim();
                }
                set.Add(s);
            }
            return set;
        }

        /// A flag of the line's first part ("caves no, endgame yes, ..."):
        /// true / false, null when absent or unreadable ("?").
        public static bool? Flag(string areas, string name)
        {
            if (string.IsNullOrEmpty(areas)) return null;
            int bar = areas.IndexOf(" |", StringComparison.Ordinal);
            string head = bar < 0 ? areas : areas.Substring(0, bar);
            string[] parts = head.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (!p.StartsWith(name + " ", StringComparison.Ordinal)) continue;
                string v = p.Substring(name.Length + 1).Trim();
                if (v == "yes") return true;
                if (v == "no") return false;
                return null;
            }
            return null;
        }

        /// An in-place restore should leave the endgame: the capture says
        /// "endgame no" and the live flag is set. LocalPlayer.IsInEndgame is
        /// not in the save, so a Quick load keeps the live value, and with it
        /// set CaveOptimizer keeps every cave's props scene unloaded (T-0075:
        /// a cave spot restarted from the lab had no body piles, ropes or
        /// planks). A missing or unreadable flag (old files) changes nothing,
        /// and neither does a capture that had the lab (endgame_streaming
        /// listed with the flag unset): the restore loads the lab for it,
        /// and leaving would turn the sun on inside it.
        public static bool ShouldLeaveEndgame(string areas, bool liveInEndgame)
        {
            return liveInEndgame && Flag(areas, "endgame") == false && !HadEndgame(areas);
        }

        /// The capture had the endgame lab (loaded, or its load under way).
        public static bool HadEndgame(string areas)
        {
            HashSet<string> scenes = Scenes(areas, true);
            if (scenes.Contains(StreamingScene)) return true;
            return scenes.Contains(AnimScene) && Flag(areas, "endgame") == true;
        }

        /// The scenes a Full load waits for: those loaded at capture, less
        /// the endgame's when the capture did not have the lab (a lingering
        /// endgame_animPrefabs is not loaded again, and the hold would wait
        /// its full 30 s for it).
        public static HashSet<string> ScenesToWaitFor(string areas)
        {
            HashSet<string> set = Scenes(areas, false);
            if (!HadEndgame(areas))
            {
                set.Remove(StreamingScene);
                set.Remove(AnimScene);
            }
            return set;
        }
    }
}
