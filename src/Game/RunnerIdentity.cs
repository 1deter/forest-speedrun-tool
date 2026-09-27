using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace ForestOverlay.Game
{
    // ------------------------------------------------------------------
    // Who is running: the Steam name by default and a stable id for the
    // attempts they record (author, 2026-09-27: "steam name first", people
    // new to the tool would never fill a field in).
    //
    // The id is a HASH of the Steam id, never the id itself: a plain Steam
    // id is a link to the runner's profile, and attempt files are public
    // (.foseg in Discord, the repo, the website). One account, one hash, so
    // a renamed runner is still one runner.
    //
    // Reached by reflection like every game type: Steamworks.NET's
    // SteamFriends.GetPersonaName / SteamUser.GetSteamID (bridge,
    // 2026-09-27: both answer in game). No Steam = null; the caller keeps
    // a random id in the config instead.
    // ------------------------------------------------------------------
    public static class RunnerIdentity
    {
        /// The Steam persona name, or null.
        public static string SteamName()
        {
            object v = CallStatic("Steamworks.SteamFriends", "GetPersonaName");
            string s = v as string;
            return string.IsNullOrEmpty(s) ? null : s;
        }

        /// "r-" + 16 hex digits of a hash of the Steam id, or null.
        public static string SteamRunnerId()
        {
            object v = CallStatic("Steamworks.SteamUser", "GetSteamID");
            if (v == null) return null;
            string text = v.ToString();
            ulong id;
            if (!ulong.TryParse(text, out id) || id == 0) return null;
            return HashId("steam:" + text);
        }

        /// A fresh id for a copy without Steam.
        public static string RandomRunnerId()
        {
            return "r-" + Guid.NewGuid().ToString("N").Substring(0, 16);
        }

        public static string HashId(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("forestoverlay-runner|" + text));
                StringBuilder sb = new StringBuilder("r-", 18);
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static object CallStatic(string typeName, string method)
        {
            try
            {
                Type t = GameBridge.FindGameType(typeName);
                if (t == null) return null;
                MethodInfo m = t.GetMethod(method, BindingFlags.Static | BindingFlags.Public, null, Type.EmptyTypes, null);
                return m == null ? null : m.Invoke(null, null);
            }
            catch (Exception) { return null; }
        }
    }
}
