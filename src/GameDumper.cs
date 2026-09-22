using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForestOverlay
{
    // ------------------------------------------------------------------
    // Writes offline-analysable dumps of the game's structure.
    //
    // The point: instead of reading class names off the screen and typing
    // them out, you press F11 and get files you can hand to someone (or
    // something) wholesale. Everything is tab-separated and one record per
    // line so it greps cleanly.
    //
    // Files land in <game root>/ForestOverlayDumps/.
    // ------------------------------------------------------------------
    public static class GameDumper
    {
        // Keeps the files a sane size. Raise if you need more.
        private const int MaxSceneLines = 40000;
        private const int MaxSceneDepth = 5;
        private const int MaxDetailTypes = 600;
        private const int MaxFieldsPerComponent = 120;
        private const int MaxValueLength = 90;

        public static string DumpDirectory
        {
            get
            {
                string dir = Path.Combine(Paths.GameRootPath, "ForestOverlayDumps");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        // ------------------------------------------------------------------
        // 1. Type index - every type in the game's own assemblies.
        //    Small enough to share whole; the map of what exists.
        // ------------------------------------------------------------------
        // ------------------------------------------------------------------
        // The item catalogue: every id and name the game knows.
        //
        // Needed because the 100% checklist is authored from names the
        // admins publish, which are not always what the game calls things.
        // Guessing at them produced a page of unresolved entries; this
        // makes the real list readable instead.
        // ------------------------------------------------------------------
        public static string WriteItemCatalogue(ManualLogSource log,
                                                IList<ForestOverlay.Game.ItemInfo> catalogue)
        {
            string path = Path.Combine(DumpDirectory, "items_" + Stamp() + ".txt");

            using (StreamWriter w = new StreamWriter(path, false, Encoding.UTF8))
            {
                w.WriteLine("# The Forest item catalogue");
                w.WriteLine("# generated " + DateTime.Now);
                w.WriteLine("# columns: id | name");
                w.WriteLine("# " + (catalogue == null ? 0 : catalogue.Count) + " items");
                w.WriteLine();

                if (catalogue != null)
                {
                    for (int i = 0; i < catalogue.Count; i++)
                        w.WriteLine(catalogue[i].Id + " | " + catalogue[i].Name);
                }
            }

            log.LogInfo("Item catalogue -> " + path);
            return path;
        }

        public static string WriteTypeIndex(ManualLogSource log)
        {
            string path = Path.Combine(DumpDirectory, "types_index_" + Stamp() + ".tsv");

            using (StreamWriter w = new StreamWriter(path, false))
            {
                w.WriteLine("# The Forest type index");
                w.WriteLine("# generated " + DateTime.Now);
                w.WriteLine("# columns: FullName\tBaseType\tIsMonoBehaviour\tFields\tProperties\tMethods");

                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                     BindingFlags.Public | BindingFlags.NonPublic |
                                     BindingFlags.DeclaredOnly;

                int written = 0;

                for (int a = 0; a < assemblies.Length; a++)
                {
                    string asmName;
                    try { asmName = assemblies[a].GetName().Name; }
                    catch (Exception) { continue; }
                    if (!asmName.StartsWith("Assembly-CSharp")) continue;

                    w.WriteLine("# assembly: " + asmName);

                    foreach (Type t in SafeGetTypes(assemblies[a], log))
                    {
                        if (t == null) continue;

                        int fields = 0, props = 0, methods = 0;
                        try { fields = t.GetFields(flags).Length; } catch (Exception) { }
                        try { props = t.GetProperties(flags).Length; } catch (Exception) { }
                        try { methods = t.GetMethods(flags).Length; } catch (Exception) { }

                        bool isMb = false;
                        try { isMb = typeof(MonoBehaviour).IsAssignableFrom(t); } catch (Exception) { }

                        string baseName = "-";
                        try { if (t.BaseType != null) baseName = t.BaseType.FullName; } catch (Exception) { }

                        w.WriteLine((t.FullName ?? t.Name) + "\t" + baseName + "\t" +
                                    (isMb ? "MB" : "-") + "\t" + fields + "\t" + props + "\t" + methods);
                        written++;
                    }
                }

                w.WriteLine("# total types: " + written);
            }

            log.LogInfo("Wrote type index -> " + path);
            return path;
        }

        // ------------------------------------------------------------------
        // 2. Detailed member listing for a subset of types (driven by the
        //    explorer's filter box, so you control the size).
        // ------------------------------------------------------------------
        public static string WriteTypeDetail(ManualLogSource log, List<Type> types, string label)
        {
            string safeLabel = MakeFilenameSafe(label);
            string path = Path.Combine(DumpDirectory, "types_detail_" + safeLabel + "_" + Stamp() + ".txt");

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                 BindingFlags.Public | BindingFlags.NonPublic |
                                 BindingFlags.DeclaredOnly;

            using (StreamWriter w = new StreamWriter(path, false))
            {
                w.WriteLine("# The Forest type detail");
                w.WriteLine("# filter: " + label);
                w.WriteLine("# generated " + DateTime.Now);
                w.WriteLine();

                int count = Math.Min(types.Count, MaxDetailTypes);
                if (types.Count > MaxDetailTypes)
                    w.WriteLine("# NOTE: truncated to " + MaxDetailTypes + " of " + types.Count +
                                " types - narrow the filter for the rest");

                for (int i = 0; i < count; i++)
                {
                    Type t = types[i];
                    if (t == null) continue;

                    string baseName = "-";
                    try { if (t.BaseType != null) baseName = t.BaseType.FullName; } catch (Exception) { }

                    w.WriteLine("=== " + (t.FullName ?? t.Name) + "  :  " + baseName + " ===");

                    try
                    {
                        FieldInfo[] fields = t.GetFields(flags);
                        for (int f = 0; f < fields.Length; f++)
                            w.WriteLine("  field  " + (fields[f].IsStatic ? "static " : "") +
                                        SafeTypeName(fields[f].FieldType) + " " + fields[f].Name);
                    }
                    catch (Exception ex) { w.WriteLine("  (fields unreadable: " + ex.Message + ")"); }

                    try
                    {
                        PropertyInfo[] props = t.GetProperties(flags);
                        for (int p = 0; p < props.Length; p++)
                            w.WriteLine("  prop   " + SafeTypeName(props[p].PropertyType) + " " + props[p].Name);
                    }
                    catch (Exception) { }

                    try
                    {
                        MethodInfo[] methods = t.GetMethods(flags);
                        for (int m = 0; m < methods.Length; m++)
                            w.WriteLine("  method " + SafeTypeName(methods[m].ReturnType) + " " +
                                        methods[m].Name + "(" + ParamList(methods[m]) + ")");
                    }
                    catch (Exception) { }

                    w.WriteLine();
                }
            }

            log.LogInfo("Wrote type detail -> " + path);
            return path;
        }

        // ------------------------------------------------------------------
        // 3. Scene hierarchy - what actually exists in the running game,
        //    with the components attached. Depth-limited because The
        //    Forest's world has an enormous number of objects.
        // ------------------------------------------------------------------
        public static string WriteSceneHierarchy(ManualLogSource log)
        {
            string path = Path.Combine(DumpDirectory, "scene_" + Stamp() + ".txt");
            int lines = 0;

            using (StreamWriter w = new StreamWriter(path, false))
            {
                w.WriteLine("# The Forest scene hierarchy");
                w.WriteLine("# generated " + DateTime.Now);
                w.WriteLine("# depth limit " + MaxSceneDepth + ", line cap " + MaxSceneLines);
                w.WriteLine();

                for (int s = 0; s < SceneManager.sceneCount; s++)
                {
                    Scene scene;
                    try { scene = SceneManager.GetSceneAt(s); }
                    catch (Exception) { continue; }

                    if (!scene.isLoaded) continue;

                    w.WriteLine("### SCENE: " + scene.name);
                    lines++;

                    GameObject[] roots;
                    try { roots = scene.GetRootGameObjects(); }
                    catch (Exception ex)
                    {
                        w.WriteLine("  (could not read roots: " + ex.Message + ")");
                        continue;
                    }

                    for (int r = 0; r < roots.Length && lines < MaxSceneLines; r++)
                        WriteGameObjectRecursive(w, roots[r].transform, 0, ref lines);

                    w.WriteLine();
                }

                if (lines >= MaxSceneLines)
                    w.WriteLine("# TRUNCATED at line cap");
            }

            log.LogInfo("Wrote scene hierarchy -> " + path);
            return path;
        }

        private static void WriteGameObjectRecursive(StreamWriter w, Transform t, int depth, ref int lines)
        {
            if (t == null || lines >= MaxSceneLines) return;

            string indent = new string(' ', depth * 2);
            string tag = "";
            try { tag = t.gameObject.tag; } catch (Exception) { }

            w.WriteLine(indent + t.name +
                        (string.IsNullOrEmpty(tag) || tag == "Untagged" ? "" : "  [tag:" + tag + "]") +
                        (t.gameObject.activeInHierarchy ? "" : "  (inactive)"));
            lines++;

            Component[] comps;
            try { comps = t.GetComponents(typeof(Component)); }
            catch (Exception) { return; }

            for (int c = 0; c < comps.Length && lines < MaxSceneLines; c++)
            {
                if (comps[c] == null)
                {
                    w.WriteLine(indent + "  - <missing script>");
                    lines++;
                    continue;
                }

                w.WriteLine(indent + "  - " + comps[c].GetType().FullName);
                lines++;
            }

            if (depth >= MaxSceneDepth)
            {
                if (t.childCount > 0)
                {
                    w.WriteLine(indent + "  ... " + t.childCount + " children (depth limit)");
                    lines++;
                }
                return;
            }

            for (int i = 0; i < t.childCount && lines < MaxSceneLines; i++)
                WriteGameObjectRecursive(w, t.GetChild(i), depth + 1, ref lines);
        }

        // ------------------------------------------------------------------
        // 4. Player snapshot - every component on the player subtree with
        //    live field values. This is the highest-value file for finding
        //    inventory counts, stats and movement data.
        // ------------------------------------------------------------------
        public static string WritePlayerSnapshot(ManualLogSource log, Transform player)
        {
            string path = Path.Combine(DumpDirectory, "player_" + Stamp() + ".txt");

            using (StreamWriter w = new StreamWriter(path, false))
            {
                w.WriteLine("# The Forest player snapshot");
                w.WriteLine("# generated " + DateTime.Now);
                w.WriteLine();

                if (player == null)
                {
                    w.WriteLine("NO PLAYER REFERENCE - load into a save first, then press F11 again.");
                    log.LogWarning("Player snapshot written with no player reference.");
                    return path;
                }

                w.WriteLine("root: " + player.name);
                w.WriteLine("position: " + player.position);
                w.WriteLine();

                WriteComponentsWithValues(w, player, 0);
            }

            log.LogInfo("Wrote player snapshot -> " + path);
            return path;
        }

        private static void WriteComponentsWithValues(StreamWriter w, Transform t, int depth)
        {
            if (t == null || depth > 3) return;

            string indent = new string(' ', depth * 2);
            w.WriteLine(indent + "## " + t.name);

            Component[] comps;
            try { comps = t.GetComponents(typeof(Component)); }
            catch (Exception) { return; }

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            for (int c = 0; c < comps.Length; c++)
            {
                if (comps[c] == null) continue;

                Type type = comps[c].GetType();

                // Skip engine components; their fields are documented already
                // and they bloat the file badly.
                if (type.Namespace != null && type.Namespace.StartsWith("UnityEngine"))
                {
                    w.WriteLine(indent + "  [" + type.Name + "] (engine, skipped)");
                    continue;
                }

                w.WriteLine(indent + "  [" + type.FullName + "]");

                FieldInfo[] fields;
                try { fields = type.GetFields(flags); }
                catch (Exception) { continue; }

                int limit = Math.Min(fields.Length, MaxFieldsPerComponent);
                for (int f = 0; f < limit; f++)
                {
                    string value;
                    try
                    {
                        object v = fields[f].GetValue(comps[c]);
                        value = v == null ? "null" : v.ToString();
                    }
                    catch (Exception ex) { value = "(" + ex.GetType().Name + ")"; }

                    if (value.Length > MaxValueLength)
                        value = value.Substring(0, MaxValueLength) + "...";

                    w.WriteLine(indent + "     " + SafeTypeName(fields[f].FieldType) + " " +
                                fields[f].Name + " = " + value);
                }

                if (fields.Length > limit)
                    w.WriteLine(indent + "     ... " + (fields.Length - limit) + " more fields");
            }

            for (int i = 0; i < t.childCount; i++)
                WriteComponentsWithValues(w, t.GetChild(i), depth + 1);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------
        private static IEnumerable<Type> SafeGetTypes(Assembly asm, ManualLogSource log)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                List<Type> partial = new List<Type>();
                if (ex.Types != null)
                    for (int i = 0; i < ex.Types.Length; i++)
                        if (ex.Types[i] != null) partial.Add(ex.Types[i]);
                types = partial.ToArray();
            }
            catch (Exception ex)
            {
                log.LogWarning("GetTypes failed: " + ex.Message);
                types = new Type[0];
            }
            return types;
        }

        private static string SafeTypeName(Type t)
        {
            if (t == null) return "?";
            try { return t.Name; }
            catch (Exception) { return "?"; }
        }

        private static string ParamList(MethodInfo m)
        {
            try
            {
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 0) return "";

                string s = "";
                for (int i = 0; i < ps.Length; i++)
                {
                    if (i > 0) s += ", ";
                    s += SafeTypeName(ps[i].ParameterType) + " " + ps[i].Name;
                }
                return s;
            }
            catch (Exception) { return "?"; }
        }

        private static string MakeFilenameSafe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "all";
            char[] bad = Path.GetInvalidFileNameChars();
            for (int i = 0; i < bad.Length; i++)
                s = s.Replace(bad[i], '_');
            s = s.Replace('.', '_').Replace(' ', '_');
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }
    }
}
