using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ForestOverlay.Data
{
    // ------------------------------------------------------------------
    // Cannibals at a savestate capture, and where they go after an
    // in-place restore (fix list 2; author: "ideally in the same position
    // and state"). Pure, so it is tested; the game side is
    // Game/EnemyKeeper.
    //
    // One entry per live cannibal, in the savestate's `enemies` header:
    //
    //   <family>:<kind>@<x>,<y>,<z>/<yaw>/<health>[/s]
    //   0:mutant_male/0/L@524.1,54.4,0.2/90/130/s
    //
    // `kind` is Game/EnemyKeeper's (prefab, mutantTypeSetup flags, /L for
    // the family's leader); a trailing /s: asleep at capture (v0.24.19).
    // v0.24.16 files used enemyType names ("regularMale").
    //
    // `family` numbers the cannibals' spawners at capture (one family =
    // one spawnMutants). The game rebuilds families on its own after a
    // restore, at spawn points of its choosing; seen live (2026-09-24),
    // moving one of its cannibals with spawnMutants.fixMutantPosition
    // gives a normal cannibal where it was put. So a restore does not
    // spawn anything: it matches the captured families to the live ones
    // of the same make-up and moves those.
    // ------------------------------------------------------------------
    public struct EnemyRecord
    {
        public int Family;
        public string Type;
        public Vector3 Position;
        public float Yaw;
        public int Health;
        public bool Asleep;

        public string Encode()
        {
            return Family.ToString(CultureInfo.InvariantCulture) + ":" + Type + "@" +
                   F(Position.x) + "," + F(Position.y) + "," + F(Position.z) + "/" +
                   F(Yaw) + "/" + Health.ToString(CultureInfo.InvariantCulture) + (Asleep ? "/s" : "");
        }

        private static string F(float f)
        {
            return f.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static bool TryDecode(string text, out EnemyRecord r)
        {
            r = new EnemyRecord();
            if (string.IsNullOrEmpty(text)) return false;
            int colon = text.IndexOf(':');
            int at = text.IndexOf('@');
            if (colon <= 0 || at <= colon + 1) return false;
            if (!int.TryParse(text.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Family)) return false;
            r.Type = text.Substring(colon + 1, at - colon - 1);

            string[] parts = text.Substring(at + 1).Split('/');
            if (parts.Length == 4)
            {
                if (parts[3] != "s") return false;
                r.Asleep = true;
            }
            else if (parts.Length != 3) return false;
            Vector3 p;
            if (!BridgeCommand.TryParseVector3(parts[0], out p)) return false;
            r.Position = p;
            if (!BridgeCommand.TryParseFloat(parts[1], out r.Yaw)) return false;
            return int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out r.Health);
        }

        /// A live cannibal as the matcher sees it.
        public struct Live
        {
            public int Family;
            public string Type;
        }

        /// For each captured record, the index of the live cannibal to move
        /// there, or -1. Whole families first: a captured family takes an
        /// unused live family with the same types (largest families first),
        /// member for member, so a leader keeps its followers. What is left
        /// takes any unused live cannibal of its type. `wholeFamilies` counts
        /// the families matched whole.
        public static int[] Match(IList<EnemyRecord> captured, IList<Live> live, out int wholeFamilies)
        {
            int[] result = new int[captured.Count];
            for (int i = 0; i < result.Length; i++) result[i] = -1;
            bool[] used = new bool[live.Count];
            wholeFamilies = 0;

            Dictionary<int, List<int>> capFam = Group(captured.Count, delegate(int i) { return captured[i].Family; });
            Dictionary<int, List<int>> liveFam = Group(live.Count, delegate(int i) { return live[i].Family; });

            List<int> capKeys = new List<int>(capFam.Keys);
            capKeys.Sort(delegate(int a, int b)
            {
                int bySize = capFam[b].Count.CompareTo(capFam[a].Count);
                return bySize != 0 ? bySize : a.CompareTo(b);
            });
            List<int> liveKeys = new List<int>(liveFam.Keys);
            liveKeys.Sort();
            HashSet<int> liveTaken = new HashSet<int>();

            for (int k = 0; k < capKeys.Count; k++)
            {
                List<int> members = capFam[capKeys[k]];
                string want = Makeup(members, delegate(int i) { return captured[i].Type; });
                for (int l = 0; l < liveKeys.Count; l++)
                {
                    if (liveTaken.Contains(liveKeys[l])) continue;
                    List<int> lm = liveFam[liveKeys[l]];
                    if (Makeup(lm, delegate(int i) { return live[i].Type; }) != want) continue;

                    liveTaken.Add(liveKeys[l]);
                    wholeFamilies++;
                    for (int m = 0; m < members.Count; m++)
                    {
                        for (int n = 0; n < lm.Count; n++)
                        {
                            if (used[lm[n]] || live[lm[n]].Type != captured[members[m]].Type) continue;
                            used[lm[n]] = true;
                            result[members[m]] = lm[n];
                            break;
                        }
                    }
                    break;
                }
            }

            // The rest, by type alone, from families not matched whole.
            for (int i = 0; i < captured.Count; i++)
            {
                if (result[i] >= 0) continue;
                for (int n = 0; n < live.Count; n++)
                {
                    if (used[n] || liveTaken.Contains(live[n].Family) || live[n].Type != captured[i].Type) continue;
                    used[n] = true;
                    result[i] = n;
                    break;
                }
            }
            return result;
        }

        private delegate int KeyOf(int i);
        private delegate string TypeOf(int i);

        private static Dictionary<int, List<int>> Group(int count, KeyOf key)
        {
            Dictionary<int, List<int>> groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < count; i++)
            {
                List<int> g;
                if (!groups.TryGetValue(key(i), out g)) groups[key(i)] = g = new List<int>();
                g.Add(i);
            }
            return groups;
        }

        private static string Makeup(List<int> members, TypeOf type)
        {
            List<string> types = new List<string>();
            for (int i = 0; i < members.Count; i++) types.Add(type(members[i]));
            types.Sort(StringComparer.Ordinal);
            return string.Join(",", types.ToArray());
        }
    }

    // ------------------------------------------------------------------
    // A family at capture: its spawner (spawnMutants) - where it stood,
    // which of mutantController's kind lists held it, and its settings
    // (the amount_* counts, leader, pale, paintedTribe, sleepingSpawn, ...
    // - every Int32 / Boolean / Single field but the runtime state). The
    // game rolls a family's make-up at random (setupRegularSpawn: 1-3
    // males, 0-2 females, a leader sometimes), so a restore cannot ask it
    // for "the same family": it builds one from these settings. Seen live
    // (2026-09-24): after a restore the game put a skinny pair where a
    // regular leader pair had been - the kind lives in the spawner.
    //
    //   <index>|<x>,<y>,<z>|<yaw>|<list>|<name>=<value>,...
    //   0|522.9,56.74,-10.7|0|allRegularSpawns|amount_male=2,leader=True,...
    // ------------------------------------------------------------------
    public sealed class FamilyRecord
    {
        public int Index;
        public Vector3 Position;
        public float Yaw;
        public string List = "";
        public readonly List<KeyValuePair<string, string>> Settings = new List<KeyValuePair<string, string>>();

        public string Encode()
        {
            string[] kv = new string[Settings.Count];
            for (int i = 0; i < Settings.Count; i++) kv[i] = Settings[i].Key + "=" + Settings[i].Value;
            return Index.ToString(CultureInfo.InvariantCulture) + "|" +
                   F(Position.x) + "," + F(Position.y) + "," + F(Position.z) + "|" + F(Yaw) + "|" +
                   List + "|" + string.Join(",", kv);
        }

        private static string F(float f)
        {
            return f.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static bool TryDecode(string text, out FamilyRecord r)
        {
            r = null;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split('|');
            if (parts.Length != 5) return false;

            FamilyRecord f = new FamilyRecord();
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out f.Index)) return false;
            Vector3 p;
            if (!BridgeCommand.TryParseVector3(parts[1], out p)) return false;
            f.Position = p;
            if (!BridgeCommand.TryParseFloat(parts[2], out f.Yaw)) return false;
            f.List = parts[3];

            string[] kv = parts[4].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < kv.Length; i++)
            {
                int eq = kv[i].IndexOf('=');
                if (eq <= 0) return false;
                f.Settings.Add(new KeyValuePair<string, string>(kv[i].Substring(0, eq), kv[i].Substring(eq + 1)));
            }
            r = f;
            return true;
        }
    }
}
