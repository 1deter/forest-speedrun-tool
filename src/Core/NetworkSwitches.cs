using BepInEx.Configuration;
using BepInEx.Logging;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // The two switches over every request the plugin makes by itself
    // (T-0326, author 2026-10-11: "a setting does exactly what it says";
    // one switch each rather than one per check - "separate toggles ...
    // just going to add more noise to the UI").
    //
    //   AtStartup off - no request at all at startup: the plugin's update
    //                   check, the community packs, the run categories.
    //   Refresh off   - nothing re-asked later by itself: the Import
    //                   view's packs and website spots, the Runs tab's
    //                   board / categories / deleted-attempt check, the
    //                   categories' poll, the board on an arm. Each view
    //                   offers a Check now click instead.
    //
    // Uploads (runs, attempts) keep their own Site switches: they send
    // what the runner already chose to send, they do not check anything.
    // ------------------------------------------------------------------
    public sealed class NetworkSwitches
    {
        public const string Section = "Network";

        public readonly ConfigEntry<bool> AtStartup;
        public readonly ConfigEntry<bool> Refresh;

        public NetworkSwitches(ConfigFile config, ManualLogSource log)
        {
            AtStartup = config.Bind(Section, "CheckAtStartup", true,
                "Ask the internet at startup: the plugin's update check, the community spots and the run categories. " +
                "Off = no request at all at startup (Updates > Check again still asks). Uploads follow their own Site switches.");
            Refresh = config.Bind(Section, "RefreshAutomatically", true,
                "Refresh the online lists by themselves: the Import view's community and website spots, the Runs tab's " +
                "other runners, categories and sent attempts. Off = only when you click Check now.");

            // The old Community.UpdateOnStartup (community packs only) was
            // this switch before T-0326: an Off carries over once.
            ConfigEntry<bool> old = config.Bind("Community", "UpdateOnStartup", true, "Replaced by Network.CheckAtStartup.");
            if (!old.Value && AtStartup.Value)
            {
                AtStartup.Value = false;
                log.LogInfo("Network: Community.UpdateOnStartup was off - Network.CheckAtStartup is off too.");
            }
            config.Remove(old.Definition);
            config.Save();

            log.LogInfo("Network: check at startup " + (AtStartup.Value ? "on" : "off") +
                        ", refresh automatically " + (Refresh.Value ? "on" : "off") + ".");
        }
    }
}
