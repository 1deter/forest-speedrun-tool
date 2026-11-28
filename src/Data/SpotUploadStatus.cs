using System.Globalization;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // The one status line under Practice's Share row (T-0495, author
    // 2026-11-28): is the selected spot on the website, and when not, why.
    // A runner's own spot reaches the site only with an uploaded run, and
    // only a timed one (RunUploadModule.Enqueue), so "not on the website"
    // alone read as broken. Pure text; the module gathers the facts.
    // ------------------------------------------------------------------
    public static class SpotUploadStatus
    {
        /// onSite: the website's list entry for this spot, null when it is
        /// not listed. listKnown false: the list has not been read (yet).
        /// pending: queued upload files of this spot. savedRuns: the
        /// runner's own saved runs of it.
        public static string Text(bool hasId, bool timed, bool sendOn, bool tokenBad, bool listKnown,
                                  SiteSpot onSite, string runnerId, int pending, int savedRuns)
        {
            if (!hasId) return "Not on the website: this spot is not saved yet.";
            if (onSite != null)
            {
                string s = "On the website";
                if (SiteSpots.IsOwner(onSite, runnerId)) s += " (yours)";
                s += ", " + onSite.Runs.ToString(CultureInfo.InvariantCulture) + (onSite.Runs == 1 ? " run" : " runs");
                if (pending > 0) s += "; " + pending + " upload file" + (pending == 1 ? "" : "s") + " waiting";
                return s + ".";
            }

            string head = listKnown ? "Not on the website yet: " : "Could not check the website; if it is not there yet: ";
            if (!timed) return head + "this spot has no start and end, and only a timed spot's runs are uploaded. Set both in the editor.";
            if (!sendOn) return head + "sending to the website is off (switch it on in the Runs tab).";
            if (tokenBad) return head + "the website refused this install's token, so uploads are paused. The site's admin can reset it.";
            if (pending > 0) return head + pending + " upload file" + (pending == 1 ? "" : "s") + " waiting to send.";
            if (savedRuns > 0) return head + savedRuns + " saved run" + (savedRuns == 1 ? "" : "s") + " not uploaded yet. Press Upload saved runs.";
            return head + "no run of it has finished and uploaded yet. Finish a timed run on it and it goes up.";
        }
    }
}
