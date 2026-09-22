using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace ForestOverlay.Game
{
    public struct BookEntry
    {
        public string Name;
        public bool Done;
        public int UnlockLevel;   // bestiary entries reveal progressively
    }

    // ------------------------------------------------------------------
    // Reads the survival book's TODO LIST. INFO-ONLY.
    //
    // The bestiary was read here originally and has been removed: it
    // is not part of the 100% requirement, and the game carries two
    // SurvivalBookBestiary components, so it also rendered as a pair of
    // identical enemy lists. The actual requirement is the todo list
    // plus a unique-item collection, and that list is admin-decided
    // data rather than something to hardcode - see Data/CollectionList.
    //
    // Confirmed from IL:
    //   TheForest.Player.SerializableSurvivalBookTodo - one TodoTask field
    //   per objective, each inheriting ACondition, which carries ._done.
    // ------------------------------------------------------------------
    public sealed class SurvivalBookReader
    {
        private readonly ManualLogSource _log;

        private readonly List<BookEntry> _todo = new List<BookEntry>();

        public IList<BookEntry> Todo { get { return _todo; } }
        public string Status { get; private set; }

        public int TodoDone { get; private set; }

        public SurvivalBookReader(ManualLogSource log)
        {
            _log = log;
            Status = "not read yet";
        }

        /// Re-reads everything. Cheap enough to call on a throttle, and
        /// re-reading is what keeps it correct across a save load.
        public void Refresh()
        {
            _todo.Clear();
            TodoDone = 0;

            ReadTodo();

            Status = _todo.Count > 0
                ? TodoDone + "/" + _todo.Count + " tasks done"
                : "survival book not loaded (open a save first)";
        }

        // ------------------------------------------------------------------
        private void ReadTodo()
        {
            // The serialisable version is the live one in current builds;
            // the older type is checked too rather than assuming.
            if (ReadTodoFrom("TheForest.Player.SerializableSurvivalBookTodo")) return;
            ReadTodoFrom("TheForest.Player.SurvivalBookTodo");
        }

        private bool ReadTodoFrom(string typeName)
        {
            Type type = GameBridge.FindGameType(typeName);
            if (type == null) return false;

            UnityEngine.Object[] found;
            try { found = Resources.FindObjectsOfTypeAll(type); }
            catch (Exception) { return false; }
            if (found == null || found.Length == 0) return false;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo[] fields = type.GetFields(flags);

            Component host = found[0] as Component;
            if (host == null) return false;

            for (int i = 0; i < fields.Length; i++)
            {
                // Each objective is its own TodoTask field. Identify them by
                // shape - something carrying _done - rather than by a
                // hardcoded list of names that a game update would break.
                FieldInfo doneField = FindField(fields[i].FieldType, "_done", flags);
                if (doneField == null) continue;

                object task;
                try { task = fields[i].GetValue(host); }
                catch (Exception) { continue; }
                if (task == null) continue;

                BookEntry entry;
                entry.Name = Tidy(fields[i].Name);
                entry.UnlockLevel = 0;

                try { entry.Done = (bool)doneField.GetValue(task); }
                catch (Exception) { continue; }

                _todo.Add(entry);
                if (entry.Done) TodoDone++;
            }

            return _todo.Count > 0;
        }

        // ------------------------------------------------------------------
        private static FieldInfo FindField(Type t, string name, BindingFlags flags)
        {
            while (t != null)
            {
                FieldInfo f = t.GetField(name, flags | BindingFlags.DeclaredOnly);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        /// Turns "_cave1" / "FoundBoar" / "cave_1_tab" into something
        /// readable, since every name source here is an identifier.
        private static string Tidy(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "unknown";

            System.Text.StringBuilder sb = new System.Text.StringBuilder(raw.Length + 4);
            bool lastWasSpace = true;

            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];

                if (c == '_' || c == '-')
                {
                    if (!lastWasSpace) { sb.Append(' '); lastWasSpace = true; }
                    continue;
                }

                // Split camelCase into words.
                if (!lastWasSpace && char.IsUpper(c) && i > 0 && !char.IsUpper(raw[i - 1]))
                {
                    sb.Append(' ');
                    sb.Append(c);
                    lastWasSpace = false;
                    continue;
                }

                sb.Append(sb.Length == 0 ? char.ToUpperInvariant(c) : c);
                lastWasSpace = false;
            }

            string text = sb.ToString().Trim();
            return text.Length == 0 ? "unknown" : text;
        }
    }
}
