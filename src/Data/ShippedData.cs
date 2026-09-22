using System;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Logging;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Data files that ship inside the DLL.
    //
    // The 100% checklist and the community spot sets used to reach the
    // config folder only through scripts/deploy.ps1. Anyone who installed
    // the way the README says - drop ForestOverlay.dll into plugins - got
    // an empty 100% tab and no explanation. Embedding them means every
    // install path (manual, auto-update, a future patcher) carries the
    // data, because the data is the DLL.
    //
    // Shipped files are owned by the plugin and rewritten whenever they
    // differ from the embedded copy, exactly as deploy.ps1 always did.
    // Personal files (my-spots.txt, my-segments.txt, anything else a user
    // adds) have different names and are never touched.
    // ------------------------------------------------------------------
    public static class ShippedData
    {
        /// Resource names are "shipped/<folder>/<file>", set in the csproj.
        private const string Prefix = "shipped/";

        public static void Install(string configDirectory, ManualLogSource log)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string[] names = asm.GetManifestResourceNames();
            int written = 0, current = 0;

            for (int i = 0; i < names.Length; i++)
            {
                if (!names[i].StartsWith(Prefix, StringComparison.Ordinal)) continue;

                string relative = names[i].Substring(Prefix.Length).Replace('/', Path.DirectorySeparatorChar);
                string target = Path.Combine(configDirectory, relative);

                try
                {
                    string content;
                    using (Stream s = asm.GetManifestResourceStream(names[i]))
                    using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                        content = r.ReadToEnd();

                    if (File.Exists(target) && File.ReadAllText(target, Encoding.UTF8) == content)
                    {
                        current++;
                        continue;
                    }

                    string dir = Path.GetDirectoryName(target);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(target, content, new UTF8Encoding(false));
                    written++;
                }
                catch (Exception ex)
                {
                    log.LogWarning("Shipped data: could not write " + relative + ": " + ex.Message);
                }
            }

            log.LogInfo("Shipped data: " + written + " file(s) written, " + current + " already current.");
        }
    }
}
